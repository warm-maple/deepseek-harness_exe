import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdtemp, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { PROTOCOL_VERSION } from '@agentclientprotocol/sdk'
import { SessionId } from '@deepseek-ai/dsh-session'
import { makeBridgeHarness, textResponse, type BridgeHarness } from './harness.ts'

/** Concatenate every streamed text delta in wire order. */
function messageText(harness: BridgeHarness): string {
  return harness.updates.flatMap(update => (
    update.sessionUpdate === 'agent_message_chunk' && update.content.type === 'text'
      ? [update.content.text]
      : []
  )).join('')
}

describe('automation-only ACP bridge', () => {
  let harness: BridgeHarness | undefined

  afterEach(async () => {
    await harness?.dispose()
    harness = undefined
  })

  it('advertises only fresh text sessions', async () => {
    harness = await makeBridgeHarness()
    const response = await harness.client.initialize({
      protocolVersion: PROTOCOL_VERSION,
      clientCapabilities: { _meta: { terminal_output: true } },
    })

    expect(response).toEqual({
      protocolVersion: PROTOCOL_VERSION,
      agentInfo: { name: 'deepseek-harness-acp', version: '0.0.1' },
      agentCapabilities: {
        promptCapabilities: { image: false, audio: false, embeddedContext: false },
        loadSession: true,
        listSessions: true,
      },
      authMethods: [],
    })
  })

  it('negotiates an unsupported version and accepts the required no-op authentication call', async () => {
    harness = await makeBridgeHarness()
    const response = await harness.client.initialize({ protocolVersion: 0, clientCapabilities: {} })
    expect(response.protocolVersion).toBe(PROTOCOL_VERSION)
    await expect(harness.client.authenticate({ methodId: 'unused' })).resolves.toEqual({})
  })

  it('creates a session, emits one committed answer, and settles the prompt', async () => {
    harness = await makeBridgeHarness({ script: [textResponse('hello there')] })
    await harness.client.initialize({ protocolVersion: PROTOCOL_VERSION, clientCapabilities: {} })
    const { sessionId } = await harness.client.newSession({ cwd: process.cwd(), mcpServers: [] })
    const result = await harness.client.prompt({
      sessionId,
      prompt: [{ type: 'text', text: 'say hello' }],
    })

    expect(result.stopReason).toBe('end_turn')
    // The bridge streams per-token text deltas; the aggregate is the answer.
    await vi.waitFor(() => { expect(messageText(harness!)).toBe('hello there') })
    expect(harness.updates.length).toBeGreaterThan(1)
    expect(harness.updates[0]).toMatchObject({
      sessionUpdate: 'agent_message_chunk',
      content: { type: 'text', text: 'h' },
    })
    expect(harness.updates[0]).toHaveProperty('messageId')
    expect(harness.ctx.agents.get(SessionId(sessionId))?.session.header.cwd).toBe(process.cwd())
    expect(harness.adapter.requests[0]?.messages.at(-1)?.content).toEqual([{ type: 'text', text: 'say hello' }])
  })

  it('leaves absent agent targets for request listeners to supply', async () => {
    harness = await makeBridgeHarness({ config: { provider: undefined, model: undefined } })
    await harness.client.initialize({ protocolVersion: PROTOCOL_VERSION, clientCapabilities: {} })
    const { sessionId } = await harness.client.newSession({ cwd: process.cwd(), mcpServers: [] })

    expect(harness.ctx.agents.get(SessionId(sessionId))?.options).toEqual({})
  })

  it('concatenates text blocks without exposing protocol framing to the model', async () => {
    harness = await makeBridgeHarness({ script: [textResponse('done')] })
    await harness.client.initialize({ protocolVersion: PROTOCOL_VERSION, clientCapabilities: {} })
    const { sessionId } = await harness.client.newSession({ cwd: process.cwd(), mcpServers: [] })
    await harness.client.prompt({
      sessionId,
      prompt: [
        { type: 'text', text: 'first' },
        { type: 'text', text: ' second' },
      ],
    })

    expect(harness.adapter.requests[0]?.messages.at(-1)?.content).toEqual([{ type: 'text', text: 'first second' }])
  })

  it('renders the deployment persona for an ACP-created agent', async () => {
    harness = await makeBridgeHarness({ persona: 'Automation persona for {{model}} in {{cwd}}.', script: [textResponse('ok')] })
    await harness.client.initialize({ protocolVersion: PROTOCOL_VERSION, clientCapabilities: {} })
    const { sessionId } = await harness.client.newSession({ cwd: process.cwd(), mcpServers: [] })
    await harness.client.prompt({ sessionId, prompt: [{ type: 'text', text: 'go' }] })
    expect(harness.adapter.requests[0]?.system).toContain(`Automation persona for mock in ${process.cwd()}.`)
  })

  it('requires one absolute workspace and no MCP servers', async () => {
    harness = await makeBridgeHarness()
    await harness.client.initialize({ protocolVersion: PROTOCOL_VERSION, clientCapabilities: {} })

    await expect(harness.client.newSession({ cwd: 'relative', mcpServers: [] })).rejects.toThrow(/absolute path/)
    await expect(harness.client.newSession({
      cwd: process.cwd(),
      mcpServers: [],
      additionalDirectories: ['/tmp/other'],
    })).rejects.toThrow(/additionalDirectories/)
    await expect(harness.client.newSession({
      cwd: process.cwd(),
      mcpServers: [{ name: 'fs', command: 'node', args: [], env: [] }],
    })).rejects.toThrow(/mcpServers/)

    await expect(harness.client.newSession({
      cwd: process.cwd(),
      mcpServers: [],
      additionalDirectories: [],
    })).resolves.toHaveProperty('sessionId')
  })

  it('rejects empty and beyond-baseline prompts before a turn starts', async () => {
    harness = await makeBridgeHarness()
    await harness.client.initialize({ protocolVersion: PROTOCOL_VERSION, clientCapabilities: {} })
    const { sessionId } = await harness.client.newSession({ cwd: process.cwd(), mcpServers: [] })

    await expect(harness.client.prompt({ sessionId, prompt: [{ type: 'text', text: '  ' }] }))
      .rejects.toThrow(/empty prompt/)
    await expect(harness.client.prompt({
      sessionId,
      prompt: [{ type: 'image', data: '', mimeType: 'image/png' }],
    })).rejects.toThrow(/only text and resource_link/)
    expect(harness.ctx.agents.get(SessionId(sessionId))?.session.events.some(event => event.type === 'turn/start')).toBe(false)
  })

  it('renders baseline resource links as textual references in the user message', async () => {
    harness = await makeBridgeHarness({ script: [textResponse('done')] })
    await harness.client.initialize({ protocolVersion: PROTOCOL_VERSION, clientCapabilities: {} })
    const { sessionId } = await harness.client.newSession({ cwd: process.cwd(), mcpServers: [] })
    await harness.client.prompt({
      sessionId,
      prompt: [
        { type: 'text', text: 'summarize' },
        { type: 'resource_link', name: 'notes.txt', uri: 'file:///tmp/notes.txt' },
      ],
    })
    expect(harness.adapter.requests[0]?.messages.at(-1)?.content).toEqual([{
      type: 'text',
      text: 'summarize\n[resource_link name="notes.txt" uri="file:///tmp/notes.txt"]\n',
    }])
  })


  it('lists and loads persisted sessions, replaying their history', async () => {
    const root = await mkdtemp(join(tmpdir(), 'acp-restore-'))
    harness = await makeBridgeHarness({
      script: [textResponse('restored answer'), textResponse('continued answer')],
      persistenceRoot: root,
    })
    await harness.client.initialize({ protocolVersion: PROTOCOL_VERSION, clientCapabilities: {} })
    const { sessionId } = await harness.client.newSession({ cwd: process.cwd(), mcpServers: [] })
    await harness.client.prompt({ sessionId, prompt: [{ type: 'text', text: 'restore me' }] })
    // The agent-loop composition auto-persists sessions when the JSONL
    // backend is present; wait for the write-behind flush before listing.

    // session/list surfaces the persisted session.
    let listed = await harness!.client.listSessions({})
    // The write-behind coordinator flushes shortly after the prompt settles.
    await vi.waitFor(async () => {
      listed = await harness!.client.listSessions({})
      expect(listed.sessions.map(item => item.sessionId)).toContain(sessionId)
    }, { timeout: 5000 })

    // session/load resumes it as a seeded agent and replays the history.
    harness!.updates.length = 0
    const loaded = await harness!.client.loadSession({ sessionId, cwd: process.cwd(), mcpServers: [] })
    expect(loaded).toHaveProperty('sessionId')
    expect(harness!.updates.some(update => update.sessionUpdate === 'user_message_chunk')).toBe(true)
    const text = harness!.updates.flatMap(update => (
      update.sessionUpdate === 'agent_message_chunk' && update.content.type === 'text'
        ? [update.content.text]
        : []
    )).join('')
    expect(text).toBe('restored answer')

    // The loaded session is a working live session.
    const resumed = loaded as unknown as { sessionId: string }
    const result = await harness!.client.prompt({ sessionId: resumed.sessionId, prompt: [{ type: 'text', text: 'continue' }] })
    expect(result.stopReason).toBe('end_turn')
    await rm(root, { recursive: true, force: true })
  })

  it('rejects prompts for unknown sessions and ignores unknown cancellation', async () => {
    harness = await makeBridgeHarness()
    await harness.client.initialize({ protocolVersion: PROTOCOL_VERSION, clientCapabilities: {} })
    await expect(harness.client.prompt({ sessionId: 'missing', prompt: [{ type: 'text', text: 'go' }] }))
      .rejects.toThrow(/unknown session/)
    await expect(harness.client.cancel({ sessionId: 'missing' })).resolves.toBeUndefined()
  })
})
