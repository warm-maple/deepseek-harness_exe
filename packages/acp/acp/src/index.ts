/**
 * Automation-only Agent Client Protocol server over JSON-RPC stdio.
 *
 * The bridge exposes fresh harness sessions to trusted programmatic clients. It
 * carries prompt text, live assistant text and reasoning deltas, tool cards,
 * todo snapshots, cancellation, and one-shot permission decisions. Text and
 * reasoning deltas ride agent_message_chunk/agent_thought_chunk, tools ride
 * tool_call/tool_call_update, and todos ride the reserved _meta of a thought
 * chunk because ACP has no standard todo update type. SDK clients that ignore
 * unknown content continue to aggregate text unchanged.
 *
 * @module @deepseek-ai/dsh-acp
 */

import type { Context } from '@deepseek-ai/cordis'
import { randomUUID } from 'node:crypto'
import { isAbsolute } from 'node:path'
import { Readable, Writable } from 'node:stream'
import Schema from '@deepseek-ai/schemastery'
import { createUserMessage, errorChain } from '@deepseek-ai/dsh-llm'
import {
  AgentSideConnection,
  ndJsonStream,
  PROTOCOL_VERSION,
  RequestError,
  type Agent as AcpAgent,
  type AuthenticateRequest,
  type CancelNotification,
  type InitializeRequest,
  type InitializeResponse,
  type ListSessionsRequest,
  type ListSessionsResponse,
  type LoadSessionRequest,
  type LoadSessionResponse,
  type NewSessionRequest,
  type NewSessionResponse,
  type PromptRequest,
  type PromptResponse,
  type SessionNotification,
  type StopReason,
  type Stream,
  type ToolKind,
} from '@agentclientprotocol/sdk'
import type { Agent } from '@deepseek-ai/dsh-agent'
import { SessionId, type SessionEvent, type SessionHeader, type TurnEndReason } from '@deepseek-ai/dsh-session'
// Side-effect type import: declaration-merges the approval waterfall answered below.
import type {} from '@deepseek-ai/dsh-user-approval'
import { acpPromptToText, promptHasUnsupportedContent, turnEndToStopReason } from './codec.ts'

export const name = 'acp'
/** The bridge creates and owns agents; every other concern is carried by the agent composition. */
export const inject = ['agents']

/**
 * The single continuable-subagent teardown the bridge needs. Declared
 * structurally so this package does not depend on the subagent seam for one
 * shutdown hook; an absent service means nothing continuable was materialized.
 */
interface ContinuableDrain {
  /**
   * Close admission below exact host-owned parents, then dispose only their
   * continuable descendants child-first.
   */
  drainContinuableDescendants(parents: readonly Agent[]): Promise<void>
}

/** Preserve invalid-parameter detail in the SDK wire error message. */
function invalidParams(detail: string): RequestError {
  return RequestError.invalidParams(undefined, detail)
}

/** Preserve failed-turn detail; plain handler errors become a generic wire internal error. */
function internalError(detail: string): RequestError {
  return RequestError.internalError(undefined, detail)
}

/** Plugin config: the provider/model selection used for each ACP-created agent. */
export interface AcpConfig {
  /** Provider route for created agents. */
  provider?: string
  /** Model name for created agents. */
  model?: string
  /** Runtime-only transport override; production uses stdio. */
  stream?: Stream
}

export const Config: Schema<AcpConfig> = Schema.object({
  provider: Schema.string(),
  model: Schema.string(),
})

/** Per-session protocol state. */
interface SessionRecord {
  agent: Agent
  /** Exact owned-agent disposer; resolves after registry, loop, and session teardown. */
  dispose: () => Promise<void>
  /** In-flight prompt and its captured turn number for exact settlement. */
  inflight: {
    resolve: (reason: StopReason) => void
    reject: (error: Error) => void
    messageId: string
    turn: number | undefined
    /** The correlated turn's ending, set at turn/end and settled at whole-agent idle. */
    endReason: TurnEndReason | undefined
  } | undefined
}

/** Stable message id for one step's text and reasoning deltas. */
function stepMessageId(turn: number, step: number): string {
  return `msg:${turn}:${step}`
}

/**
 * Map a DSH tool name to the closest ACP tool kind for card styling.
 * @param name - the model-facing tool name.
 * @returns the ACP tool-kind category.
 */
function toolKindFor(name: string): ToolKind {
  const n = name.toLowerCase()
  if (n.includes('search') || n.startsWith('grep') || n.startsWith('find')) return 'search'
  if (n.startsWith('read') || n.startsWith('ls') || n.startsWith('cat')) return 'read'
  if (n.startsWith('edit') || n.startsWith('write') || n.startsWith('create') || n.startsWith('append') || n.includes('replace')) return 'edit'
  if (n.startsWith('delete') || n.startsWith('remove') || n.startsWith('rm')) return 'delete'
  if (n.startsWith('move') || n.startsWith('rename')) return 'move'
  if (n.startsWith('pwsh') || n.startsWith('bash') || n.startsWith('exec') || n.startsWith('run') || n.includes('terminal')) return 'execute'
  if (n.startsWith('web') || n.includes('fetch') || n.includes('http') || n.startsWith('search')) return 'fetch'
  if (n.includes('think')) return 'think'
  return 'other'
}

/** Best-effort parse of a model tool-arguments JSON string; raw text on failure. */
function parseArguments(raw: string): unknown {
  try {
    return JSON.parse(raw)
  } catch {
    return raw
  }
}

/** Join a tool result's text blocks into one display string. */
function toolResultText(content: readonly { type: string; text?: string }[]): string | undefined {
  const text = content.flatMap(block => (block.type === 'text' && block.text ? [block.text] : [])).join('\n')
  return text.length > 0 ? text : undefined
}

/**
 * Mount the automation-only ACP server.
 * @param ctx - Cordis context carrying the agent factory and session events.
 * @param config - Initial provider/model selection and optional test transport.
 */
export function apply(ctx: Context, config: AcpConfig): void {
  // ACP handlers execute outside this plugin's injection scope, so capture the
  // injected service during apply rather than reading it lazily in a callback.
  const agents = ctx.agents
  const logger = ctx.logger
  const sessions = new Map<SessionId, SessionRecord>()
  // Steps that already streamed text or reasoning deltas. The committed
  // assistant/message fallback skips blocks that were streamed so clients do
  // not double-render; entries are pruned when their turn ends.
  const streamedTextSteps = new Set<string>()
  const streamedReasoningSteps = new Set<string>()
  let closed = false
  let conn: AgentSideConnection

  /** Return the bridge-owned record for an agent, rejecting same-id impostors. */
  const ownedRecord = (agent: Agent): SessionRecord | undefined => {
    const record = sessions.get(agent.session.id)
    return record?.agent === agent ? record : undefined
  }

  const assertOpen = (): void => {
    if (closed) throw internalError('the ACP bridge has been disposed')
  }

  const requireSession = (sessionId: SessionId): SessionRecord => {
    const record = sessions.get(sessionId)
    if (record === undefined) throw invalidParams(`unknown session: ${sessionId}`)
    return record
  }

  /** Send a protocol update without letting a disconnected client fail an agent turn. */
  const notify = (notification: SessionNotification): void => {
    /* v8 ignore next 3 -- only a transport write failure reaches this guard. */
    void conn.sessionUpdate(notification).catch((error: unknown) => {
      logger.warn(`acp: session/update failed: ${String(error)}`)
    })
  }

  /**
   * Optional JSONL session persistence, composed by the owning app (acp-demo).
   * Absent when the bridge runs standalone, which makes session/list and
   * session/load unavailable.
   */
  const persistence = (): {
    list(): Promise<SessionHeader[]>
    load(id: SessionId): Promise<{ meta: SessionHeader; events: readonly SessionEvent[] }>
  } | undefined => ctx.get('sessionPersistence') as {
    list(): Promise<SessionHeader[]>
    load(id: SessionId): Promise<{ meta: SessionHeader; events: readonly SessionEvent[] }>
  } | undefined

  const settlePrompt = (record: SessionRecord, reason: StopReason): void => {
    const inflight = record.inflight
    if (inflight === undefined) return
    record.inflight = undefined
    inflight.resolve(reason)
  }

  /**
   * Agent preset selected by the desktop shell (DSH_DESKTOP_PRESET). Absent
   * when unset, in which case agents keep the plain host composition.
   */
  const desktopPreset = (): string | undefined => {
    const preset = process.env['DSH_DESKTOP_PRESET']
    return preset !== undefined && preset.length > 0 ? preset : undefined
  }

  /**
   * The factory setup that joins an agent to its selected preset. The
   * agent-presets service is optional (composed only by apps that ship a
   * preset roster); without it the agent runs the plain host composition.
   * @param agentCtx - the unpublished agent scope the preset mounts into.
   * @param presetId - the preset id to resolve and mount.
   */
  const joinPreset = async (agentCtx: Context, presetId: string): Promise<void> => {
    const presets = agentCtx.get('agentPresets') as {
      resolve(id: string | undefined): Promise<{ id: string }>
      mount(ctx: Context, id: string): Promise<unknown>
    } | undefined
    if (presets === undefined) return
    const resolved = await presets.resolve(presetId)
    await presets.mount(agentCtx, resolved.id)
  }

  /**
   * Replay one persisted session log as session/update notifications so the
   * client can render history before continuing. Text, reasoning, tool cards,
   * and todo snapshots use the same update vocabulary as the live path; the
   * notifications carry the requested session id so the client routes them to
   * the conversation being loaded.
   */
  const replayHistory = (notifySessionId: SessionId, events: readonly SessionEvent[]): void => {
    for (const event of events) {
      switch (event.type) {
        case 'user/message': {
          for (const block of event.data.content) {
            if (block.type === 'text' && block.text.length > 0) {
              notify({
                sessionId: notifySessionId,
                update: { sessionUpdate: 'user_message_chunk', content: { type: 'text', text: block.text } },
              })
            }
          }
          break
        }
        case 'assistant/message': {
          for (const block of event.data.message.content) {
            if (block.type === 'text' && block.text.length > 0) {
              notify({
                sessionId: notifySessionId,
                update: { sessionUpdate: 'agent_message_chunk', content: { type: 'text', text: block.text } },
              })
            } else if (block.type === 'reasoning' && block.text.length > 0) {
              notify({
                sessionId: notifySessionId,
                update: { sessionUpdate: 'agent_thought_chunk', content: { type: 'text', text: block.text } },
              })
            } else if (block.type === 'image') {
              notify({
                sessionId: notifySessionId,
                update: {
                  sessionUpdate: 'agent_message_chunk',
                  content: { type: 'text', text: `[image attachment ${block.attachment.attachmentId}]` },
                },
              })
            }
          }
          break
        }
        case 'tool/call': {
          notify({
            sessionId: notifySessionId,
            update: {
              sessionUpdate: 'tool_call',
              toolCallId: event.data.callId,
              title: event.data.name,
              kind: toolKindFor(event.data.name),
              status: 'in_progress',
              rawInput: parseArguments(event.data.arguments),
            },
          })
          break
        }
        case 'tool/result': {
          const block = event.data.message.content[0]
          const text = toolResultText(block.content)
          notify({
            sessionId: notifySessionId,
            update: {
              sessionUpdate: 'tool_call_update',
              toolCallId: block.toolCallId,
              status: block.isError === true ? 'failed' : 'completed',
              ...(text !== undefined ? { rawOutput: text } : {}),
            },
          })
          break
        }
        case 'todo/write': {
          notify({
            sessionId: notifySessionId,
            update: {
              sessionUpdate: 'agent_thought_chunk',
              content: { type: 'text', text: '' },
              _meta: { 'dsh:todos': event.data.todos },
            },
          })
          break
        }
        default:
          break
      }
    }
  }

  const rejectFromError = (
    inflight: NonNullable<SessionRecord['inflight']>,
    reason: Extract<TurnEndReason, { kind: 'error' }>,
  ): void => {
    inflight.reject(internalError(`turn failed: ${reason.error.message}`))
  }

  // Stream committed presentation: live text and reasoning deltas, tool cards,
  // and todo snapshots. Text and reasoning deltas ride the ACP content-chunk
  // update types with a per-step message id; tools ride tool_call/
  // tool_call_update; todos ride the reserved _meta of a thought chunk because
  // ACP has no standard todo update type. The committed assistant/message
  // fallback covers blocks that never streamed (defensive for adapters that
  // assemble without deltas) and image placeholders, which have no delta form.
  ctx.on('session/event', (session, event: SessionEvent) => {
    const record = sessions.get(session.header.id)
    if (record === undefined || record.agent.session !== session) return
    const sessionId = record.agent.session.id
    try {
      switch (event.type) {
        case 'assistant/chunk': {
          const { turn, step, chunk } = event.data
          const stepKey = `${sessionId}:${turn}:${step}`
          if (chunk.type === 'text-delta' && chunk.text.length > 0) {
            streamedTextSteps.add(stepKey)
            notify({
              sessionId,
              update: {
                sessionUpdate: 'agent_message_chunk',
                messageId: stepMessageId(turn, step),
                content: { type: 'text', text: chunk.text },
              },
            })
          } else if (chunk.type === 'reasoning-delta' && chunk.text.length > 0) {
            streamedReasoningSteps.add(stepKey)
            notify({
              sessionId,
              update: {
                sessionUpdate: 'agent_thought_chunk',
                messageId: stepMessageId(turn, step),
                content: { type: 'text', text: chunk.text },
              },
            })
          }
          break
        }
        case 'assistant/message': {
          const { turn, step, message } = event.data
          const stepKey = `${sessionId}:${turn}:${step}`
          const streamedText = streamedTextSteps.has(stepKey)
          const streamedReasoning = streamedReasoningSteps.has(stepKey)
          for (const block of message.content) {
            if (block.type === 'text' && block.text.length > 0 && !streamedText) {
              notify({
                sessionId,
                update: {
                  sessionUpdate: 'agent_message_chunk',
                  messageId: stepMessageId(turn, step),
                  content: { type: 'text', text: block.text },
                },
              })
            } else if (block.type === 'reasoning' && block.text.length > 0 && !streamedReasoning) {
              notify({
                sessionId,
                update: {
                  sessionUpdate: 'agent_thought_chunk',
                  messageId: stepMessageId(turn, step),
                  content: { type: 'text', text: block.text },
                },
              })
            } else if (block.type === 'image') {
              notify({
                sessionId,
                update: {
                  sessionUpdate: 'agent_message_chunk',
                  messageId: stepMessageId(turn, step),
                  content: {
                    type: 'text',
                    text: `[image attachment ${block.attachment.attachmentId}]`,
                  },
                },
              })
            }
          }
          break
        }
        case 'tool/call': {
          notify({
            sessionId,
            update: {
              sessionUpdate: 'tool_call',
              toolCallId: event.data.callId,
              title: event.data.name,
              kind: toolKindFor(event.data.name),
              status: 'in_progress',
              rawInput: parseArguments(event.data.arguments),
            },
          })
          break
        }
        case 'tool/result': {
          const block = event.data.message.content[0]
          const text = toolResultText(block.content)
          notify({
            sessionId,
            update: {
              sessionUpdate: 'tool_call_update',
              toolCallId: block.toolCallId,
              status: block.isError === true ? 'failed' : 'completed',
              ...(text !== undefined ? { rawOutput: text } : {}),
            },
          })
          break
        }
        case 'todo/write': {
          // DSH extension: todo snapshots travel on the reserved _meta of an
          // otherwise empty thought chunk so every ACP SDK client still parses
          // the frame while the desktop shell renders a todo card.
          notify({
            sessionId,
            update: {
              sessionUpdate: 'agent_thought_chunk',
              content: { type: 'text', text: '' },
              _meta: { 'dsh:todos': event.data.todos },
            },
          })
          break
        }
        default:
          break
      }
    } finally {
      const inflight = record.inflight
      if (inflight !== undefined && event.type === 'turn/end' && inflight.turn === event.data.turn) {
        // Prune both per-step streaming markers for this session's finished turn.
        const prefix = `${sessionId}:${event.data.turn}:`
        for (const key of streamedTextSteps) if (key.startsWith(prefix)) streamedTextSteps.delete(key)
        for (const key of streamedReasoningSteps) if (key.startsWith(prefix)) streamedReasoningSteps.delete(key)
        if (event.data.reason.kind === 'error') {
          // Model failures surface immediately as prompt errors; ordinary
          // endings wait for whole-agent idle below.
          record.inflight = undefined
          rejectFromError(inflight, event.data.reason)
        } else {
          inflight.endReason = event.data.reason
        }
      }
    }
  })

  ctx.on('agent/inbox/claimed', ({ agent, message, turn }) => {
    const record = ownedRecord(agent)
    const inflight = record?.inflight
    if (inflight !== undefined && inflight.messageId === message.id) inflight.turn = turn
  })

  ctx.on('agent/error', ({ agent, turn, error }) => {
    const record = ownedRecord(agent)
    const inflight = record?.inflight
    if (record === undefined || inflight === undefined || inflight.turn === turn) return
    record.inflight = undefined
    inflight.reject(internalError(`turn failed: ${errorChain(error)}`))
  })

  // Permission requests are a machine policy channel for ACP clients such as
  // dsh-subagent-acp. The bridge offers one-shot choices only and never infers a
  // durable grant from an unknown client response.
  ctx.on('approval/request', (request, next) => {
    const record = ownedRecord(request.agent)
    if (record === undefined || request.callId === undefined) return next()
    return conn.requestPermission({
      sessionId: record.agent.session.id,
      toolCall: { toolCallId: request.callId },
      options: [
        { optionId: 'allow-once', name: 'Allow once', kind: 'allow_once' },
        { optionId: 'reject-once', name: 'Reject', kind: 'reject_once' },
      ],
    }).then(({ outcome }) => {
      if (outcome.outcome === 'cancelled') return 'cancelled'
      return outcome.optionId === 'allow-once' ? 'allowed-once' : 'rejected'
    })
  })

  const makeAgent = (connection: AgentSideConnection): AcpAgent => {
    conn = connection
    return {
      initialize(_params: InitializeRequest): Promise<InitializeResponse> {
        // Single-version agent: the spec's "same version if supported, else
        // the latest supported" both resolve to this server's one version.
        return Promise.resolve({
          protocolVersion: PROTOCOL_VERSION,
          agentInfo: { name: 'deepseek-harness-acp', version: '0.0.1' },
          agentCapabilities: {
            promptCapabilities: { image: false, audio: false, embeddedContext: false },
            // Session restore is offered when the owning composition supplies
            // JSONL persistence; the handlers reject cleanly without it.
            loadSession: true,
            listSessions: true,
          },
          authMethods: [],
        })
      },

      authenticate(_params: AuthenticateRequest): Promise<void> {
        return Promise.resolve()
      },

      async newSession(params: NewSessionRequest): Promise<NewSessionResponse> {
        assertOpen()
        validateSessionParams(params)
        const sessionId = SessionId(randomUUID())
        // No preset composition: the ACP bundle keeps the model-facing rows in
        // the host plane, so this agent reads them from the global layer. A
        // deployment that configures a roster has to join one here first
        // (@deepseek-ai/dsh-agent-presets README, "Composing a child agent").
        const presetId = desktopPreset()
        const handle = await agents.create({
          sessionId,
          meta: { cwd: params.cwd, ...(presetId !== undefined ? { agentPreset: presetId } : {}) },
          agentOptions: agentOptions(config),
          ...(presetId !== undefined ? { setup: (agentCtx: Context) => joinPreset(agentCtx, presetId) } : {}),
        })
        /* v8 ignore next 4 -- a real stdio close can race an in-flight create. */
        if (closed) {
          await handle.dispose()
          throw internalError('connection closed during session/new')
        }
        sessions.set(sessionId, {
          agent: handle.agent,
          dispose: () => handle.dispose(),
          inflight: undefined,
        })
        return { sessionId }
      },

      async listSessions(_params: ListSessionsRequest): Promise<ListSessionsResponse> {
        assertOpen()
        const store = persistence()
        if (store === undefined) throw internalError('session persistence is not configured')
        const headers = await store.list()
        return {
          sessions: headers
            .filter(header => header.origin !== 'subagent')
            .map(header => ({
              sessionId: header.id,
              cwd: header.cwd ?? process.cwd(),
              ...(header.createdAt !== undefined ? { updatedAt: new Date(header.createdAt).toISOString() } : {}),
            })),
        }
      },

      async loadSession(params: LoadSessionRequest): Promise<LoadSessionResponse> {
        assertOpen()
        const store = persistence()
        if (store === undefined) throw internalError('session persistence is not configured')
        const persistedId = SessionId(params.sessionId)
        const loaded = await store.load(persistedId)
        // Resume as a fresh agent seeded with the persisted log (fork
        // semantics): the original artifact stays untouched and the continued
        // conversation gets a new id, which the response echoes back.
        const sessionId = SessionId(randomUUID())
        // The desktop's current mode wins over the restored session's original
        // preset so the shell behavior matches the user's active selection.
        const presetId = desktopPreset() ?? loaded.meta.agentPreset
        const handle = await agents.create({
          sessionId,
          meta: {
            ...(loaded.meta.cwd !== undefined ? { cwd: loaded.meta.cwd } : {}),
            ...(loaded.meta.parentSession !== undefined ? { parentSession: loaded.meta.parentSession } : {}),
            ...(loaded.meta.origin !== undefined ? { origin: loaded.meta.origin } : {}),
            ...(loaded.meta.delegationDepth !== undefined ? { delegationDepth: loaded.meta.delegationDepth } : {}),
            ...(presetId !== undefined ? { agentPreset: presetId } : {}),
            seedLength: loaded.events.length,
          },
          seed: [...loaded.events],
          agentOptions: agentOptions(config),
          ...(presetId !== undefined ? { setup: (agentCtx: Context) => joinPreset(agentCtx, presetId) } : {}),
        })
        /* v8 ignore next 4 -- a real stdio close can race an in-flight create. */
        if (closed) {
          await handle.dispose()
          throw internalError('connection closed during session/load')
        }
        const record: SessionRecord = {
          agent: handle.agent,
          dispose: () => handle.dispose(),
          inflight: undefined,
        }
        sessions.set(sessionId, record)
        // Replay history under the requested id so the client can render it
        // before the response resolves and route it to the loading conversation.
        replayHistory(persistedId, loaded.events)
        // DSH extension: the LoadSessionResponse schema has no session id
        // field, so the resumed id rides a top-level extra member the desktop
        // shell reads; SDK clients ignore unknown response fields.
        return { sessionId } as unknown as LoadSessionResponse
      },

      async prompt(params: PromptRequest): Promise<PromptResponse> {
        assertOpen()
        const record = requireSession(SessionId(params.sessionId))
        if (record.inflight !== undefined) {
          throw invalidParams('a prompt is already in flight for this session')
        }
        if (promptHasUnsupportedContent(params.prompt)) {
          throw invalidParams('only text and resource_link prompt content is supported')
        }
        const text = acpPromptToText(params.prompt)
        if (text.trim().length === 0) throw invalidParams('empty prompt')

        // Not driving a retired agent is this bridge's contract: an
        // agent-loop-only reload disposes the loop's agents while the bridge
        // record survives, so validate the record against the live registry
        // before sending — a disposed machine would accept the item silently.
        if (ctx.agents.get(record.agent.id) !== record.agent) {
          throw internalError('prompt was not queued: the agent was disposed outside the bridge')
        }
        const message = createUserMessage({ content: [{ type: 'text', text }], source: { kind: 'user' } })
        const stopReason = await new Promise<StopReason>((resolve, reject) => {
          // Arm the slot before followup() so a listener-driven synchronous
          // turn cannot slip past correlation; a synchronous followup()
          // failure (invalid input) must free the slot again or the session
          // would reject every later prompt as already in flight.
          const inflight: NonNullable<SessionRecord['inflight']> = {
            resolve, reject, messageId: message.id, turn: undefined, endReason: undefined,
          }
          record.inflight = inflight
          try {
            record.agent.followup(message)
            // The machine's send() contains listener failures and accepts
            // any typed input; this guards a future synchronous throw so the
            // slot cannot wedge.
            /* v8 ignore start -- future-proofing guard, see above */
          } catch (error: unknown) {
            record.inflight = undefined
            const detail = error instanceof Error ? error.message : String(error)
            throw internalError(`prompt was not queued: ${detail}`)
          }
          /* v8 ignore stop */
          // Settlement waits for whole-agent idle: a correlated turn/end arms
          // `endReason`, while a turnless slot (admission discarded the
          // prompt) stays cancelled. Other producers may run further turns
          // before quiescence; the prompt settles only when the agent stops.
          void record.agent.whenIdle().then(() => {
            if (record.inflight !== inflight) return
            record.inflight = undefined
            const end = inflight.endReason
            if (end === undefined) {
              inflight.resolve('cancelled')
            } else {
              // Token-limit and other non-terminal endings are not prompt-level
              // stop reasons (see README); only normal quiescence reports end_turn.
              inflight.resolve(end.kind === 'max-tokens' ? 'end_turn' : turnEndToStopReason(end))
            }
          })
        })
        return { stopReason }
      },

      cancel(params: CancelNotification): Promise<void> {
        const record = sessions.get(SessionId(params.sessionId))
        if (record === undefined) return Promise.resolve()
        record.agent.cancel({ kind: 'user' })
        settlePrompt(record, 'cancelled')
        return Promise.resolve()
      },
    }
  }

  /* v8 ignore next 4 -- production stdio wiring; tests inject config.stream. */
  const stream: Stream = config.stream ?? ndJsonStream(
    Writable.toWeb(process.stdout) as WritableStream<Uint8Array>,
    Readable.toWeb(process.stdin) as ReadableStream<Uint8Array>,
  )
  conn = new AgentSideConnection(makeAgent, stream)

  let quiescing: Promise<void> | undefined
  const quiesce = (): Promise<void> => {
    if (quiescing !== undefined) return quiescing
    closed = true
    const records = [...sessions.values()]
    sessions.clear()
    // Stop the bridge's own work before any await: a descendant drain can block
    // on persistence or scoped cleanup, and the top-level agents must not keep
    // running model and tool calls for its whole duration.
    for (const record of records) {
      record.agent.cancel({ kind: 'user' })
      settlePrompt(record, 'cancelled')
    }
    quiescing = (async () => {
      // Continuable subagents outlive the turn that started them, and their
      // Activations own descendant teardown. Drain only these sessions' forests
      // child-first BEFORE disposing the top-level agents, so no descendant is
      // left holding a runtime its owner already released and another frontend
      // sharing this Context remains live.
      // Read the one teardown method structurally: the bridge needs no other
      // part of the subagent seam, so it does not depend on that package.
      const subagents = ctx.get('subagents') as ContinuableDrain | undefined
      if (subagents !== undefined) {
        try {
          await subagents.drainContinuableDescendants(records.map(record => record.agent))
        } catch (error: unknown) {
          logger.warn(`acp: continuable subagent teardown failed: ${String(error)}`)
        }
      }
      const disposals = await Promise.allSettled(records.map(record => record.dispose()))
      const failures: unknown[] = []
      for (const result of disposals) {
        if (result.status === 'rejected') failures.push(result.reason as unknown)
      }
      if (failures.length > 0) {
        // The production consumer logs this AggregateError through `String`,
        // which renders only its message. Embed every per-session diagnostic,
        // including nested causes and aggregate members, in that message.
        const detail = failures.map(failure => errorChain(failure)).join('; ')
        throw new AggregateError(
          failures,
          `ACP agent teardown failed for ${failures.length} session(s): ${detail}`,
        )
      }
    })()
    return quiescing
  }

  /* v8 ignore start -- production transport rejection and teardown failure. */
  void conn.closed
    .catch((error: unknown) => {
      logger.warn(`acp: connection closed with an error: ${String(error)}`)
    })
    .then(quiesce)
    .catch((error: unknown) => {
      logger.warn(`acp: connection-close teardown failed: ${String(error)}`)
    })
  /* v8 ignore stop */

  ctx.effect(() => quiesce, 'acp.connection')
}

/**
 * Build per-agent options from plugin config without assigning absent optional fields.
 * @param config - ACP provider/model configuration.
 * @returns the configured fields only.
 */
function agentOptions(config: AcpConfig): { provider?: string; model?: string } {
  return {
    ...config.provider !== undefined ? { provider: config.provider } : {},
    ...config.model !== undefined ? { model: config.model } : {},
  }
}

/** Reject session features outside the automation contract. */
function validateSessionParams(params: NewSessionRequest): void {
  if (!isAbsolute(params.cwd)) throw invalidParams(`cwd must be an absolute path: ${params.cwd}`)
  if (params.additionalDirectories !== undefined && params.additionalDirectories.length > 0) {
    throw invalidParams('additionalDirectories is not supported')
  }
  if (params.mcpServers.length > 0) throw invalidParams('mcpServers is not supported')
}
