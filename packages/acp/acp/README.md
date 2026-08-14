# @deepseek-ai/dsh-acp

English | [中文](README.zh.md)

Automation-only [Agent Client Protocol](https://agentclientprotocol.com) server over JSON-RPC stdio. Programmatic clients create fresh harness agents, send text prompts, collect live assistant text and reasoning deltas, observe tool calls and todo snapshots, resolve one-shot permission requests by policy, and cancel work. The primary in-repository client is [`dsh-subagent-acp`](../../subagent/subagent-acp/README.md).

This package is a transport adapter, not a UI integration or a capability seam. It does not expose editor navigation, transcript replay, commands, modes, configuration pickers, elicitation, reasoning, plans, titles, or tool presentation. Interactive rendering and human questions belong to the Web host and client modules.

## Plugin

`apply(ctx, config)` opens an `AgentSideConnection` on stdin/stdout and drives `ctx.agents`. Stdout is reserved for protocol frames.

| Config | Default | Meaning |
|---|---|---|
| `provider` | — | Initial provider route for every created agent. |
| `model` | — | Initial model for every created agent. |

Both fields are optional so another agent/request listener may supply the target. The runnable ACP composition requires both.

## Protocol contract

| Method | Behavior |
|---|---|
| `initialize` | Negotiates the supported version and advertises baseline-only prompts (no image, audio, or embedded-context capability). No session, editor, terminal, filesystem, or MCP capability is advertised. |
| `authenticate` | No-op because the server advertises no authentication methods. |
| `session/new` | Creates a fresh agent with an absolute primary `cwd`; empty `additionalDirectories` and `mcpServers` are accepted, non-empty values reject. |
| `session/prompt` | Concatenates text blocks, renders baseline resource links as bracketed textual references, rejects empty or beyond-baseline input, permits one in-flight request per session, and waits for the whole agent to become idle. Normal quiescence reports `end_turn`; explicit ACP cancellation, disposal, or a prompt whose admission was discarded (a turnless slot) reports `cancelled`. |
| `session/cancel` | Cancels only the addressed agent and settles its pending prompt as `cancelled`; unknown ids are no-ops. |
| `session/list` | Lists persisted top-level sessions (id, cwd, updatedAt) from the JSONL backend. Rejects with `Internal error` when the owning composition supplies no persistence. |
| `session/load` | Resumes a persisted session as a fresh agent seeded with its log (fork semantics: the original artifact stays untouched, the continued conversation gets a new id echoed back as a DSH extension field). Replays the history as `session/update` notifications under the requested id, then accepts prompts on the resumed session. |
| `session/update` | Emits `agent_message_chunk` deltas for streamed text, `agent_thought_chunk` deltas for reasoning, `tool_call`/`tool_call_update` for tool activity, and todo snapshots on the reserved `_meta` of an empty thought chunk. A committed `assistant/message` fallback covers blocks that never streamed and image placeholders. |
| `session/request_permission` | Offers one-shot allow/reject choices for bridge-owned approval requests carrying a tool call id. Clients may answer automatically. |

One connection may own several sessions. The bridge keys records by branded session id and checks exact agent identity before routing events or permission requests. Each session has an independent prompt slot, workspace, cancellation path, and disposer.

Live deltas give desktop shells web-like streaming while the fallback keeps text complete when an adapter assembles without per-token deltas. Reasoning and tool activity now travel on the wire so clients can render thinking blocks and tool cards; other presentation concerns (plans, titles, usage) remain in the session log.

## Lifecycle

Client disconnect and Cordis disposal share one memoized teardown. The bridge first rejects new sessions and prompts, settles pending prompts, then drains continuable descendants only below this connection's exact owned Agents before disposing those handles in parallel and awaiting every result before reporting any failure. Other frontends sharing the Context retain their continuable forests and admission. An ACP-only plugin reload therefore leaves no orphan agent.

ACP requires each prompt response to carry a `stopReason`, but the bridge does not claim a prompt-specific turn outcome. Committed assistant messages stream across the owned activity, and steering or injected work may contribute before idle. Token-limit turn endings therefore do not become prompt-level ACP stop reasons (they settle as `end_turn`); a model error on the correlated turn rejects the prompt immediately.

## Running

`pnpm --dir /path/to/deepseek-harness run demo:acp` boots the repository's automation server composition. A parent harness can spawn it through [`@deepseek-ai/dsh-subagent-acp`](../../subagent/subagent-acp/README.md); other ACP clients need only the core methods above.

## Model Experience

### Prompt text

#### What the model sees

`session/prompt` text blocks are concatenated verbatim into one user message; a baseline resource link appears in that message as a bracketed `[resource_link name=… uri=…]` reference the model may open with its own tools. Protocol metadata, client capabilities, permission choices, and session ids never enter the model request.

#### Token effect

Prompt tokens are data-dependent and remain in that session's history until compaction. Concurrent ACP sessions retain independent contexts.

#### KV Cache effect

Append-only; the new user message follows the reusable request prefix and does not invalidate prior cache entries.

### Permission decisions

#### What the model sees

Nothing directly. The owning tool records its allowed, rejected, cancelled, or unavailable outcome through the normal tool-result path.

#### Token effect

Only the owning tool result contributes tokens.

#### KV Cache effect

Append-only through the owning tool result.

## Known Limitations and Deferred Work

- **List and load only** — list/load resume persisted sessions via seeding; delete, in-place resume, and fork are unsupported.
- **Preset composition** — when `DSH_DESKTOP_PRESET` is set and the owning composition ships `dsh-agent-presets` with a preset roster, created agents join that preset through the factory `setup` hook (agent-plane tools/persona). Without either, agents run the plain host composition.
- **Baseline prompts and one workspace only** — images, audio, embedded resources, non-empty additional directories, and MCP servers reject; resource links flatten to textual references rather than fetched content.
- **No plans, titles, or usage** — only text, reasoning, tools, and todos cross the wire; plans, titles, and usage stay in the session log.
- **Connection-owned lifetime** — one connection releases all of its sessions; per-session close is not implemented.
