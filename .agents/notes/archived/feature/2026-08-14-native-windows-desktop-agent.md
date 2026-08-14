# Agent Note: Native Windows desktop Agent

Status: implemented
Archived: 2026-08-14

English | [中文](2026-08-14-native-windows-desktop-agent.zh.md)

## Problem

The fork required a direct Windows x64 application rather than a command that starts a browser product. Reusing the browser frontend would retain its HTTP server, port lifecycle, and console launcher, while deleting terminal execution from the Agent would also remove core coding capabilities. The deliverable therefore needed to replace the product surface without reducing the model-facing runtime.

## Decision

**The product shell is a self-contained WPF `WinExe`.** It owns workspace selection, task tabs, prompt and response presentation, cancellation, permission decisions, model selection, and credential entry. The DeepSeek API key is stored with Windows Credential Manager; preferences and session data remain in per-user application directories.

**The Agent runtime remains a separate hidden process.** The application launches a bundled `node.exe` with `UseShellExecute=false`, `CreateNoWindow=true`, and redirected standard streams. ACP JSON-RPC supplies initialization, session creation, prompts, cancellation, streamed text updates, and permission requests without an HTTP listener. Process disposal closes stdin, waits for normal shutdown, then terminates the remaining process tree after a bounded timeout.

**The desktop composition retains coding capabilities.** It mounts the DeepSeek adapter, Windows sandbox and PowerShell executor, filesystem tools, skills, goals, jobs, compaction, subagents, workflows, todo tracking, and DeepSeek search. The removed `apps/web`, `apps/cli`, and `dsh-web-app` entries were product launch surfaces; `packages/web` remains the model-facing search capability.

**Distribution is a relocatable runtime plus a native shell.** `pnpm deploy --legacy` can omit direct workspace hoists and leave junctions into the checkout, so the build restores each missing direct package and replaces all links with copied files. A local .NET 8 SDK publishes the x64 self-contained shell, and Inno Setup creates one current-user installer. Both tools bootstrap into ignored artifact storage when absent.

## Alternatives considered

**Electron or an embedded browser view.** Rejected because it would keep a browser-rendered product and its frontend dependency graph rather than satisfy the native-shell requirement.

**One packed Node executable for the entire application.** Rejected because dynamic Cordis loading and native dependencies are already handled by a relocatable runtime directory; combining those files into the WPF executable would add a second packaging mechanism and make native-module loading harder to diagnose. The independent [single-file SDK runtime decision](../architecture/2026-07-10-single-file-executable-sdk-runtime-distribution.md) remains valid for its Python SDK distribution.

**Delete PowerShell execution with the CLI.** Rejected because “no command window” is a presentation requirement. PowerShell is the Windows coding executor and runs only as a child of the hidden Agent runtime.

## Consequences

Users install and launch DeepSeek Harness from the Start menu or desktop shortcut without Node.js, .NET, a browser, a port, or a terminal window. The first desktop release exposes text conversation and permission decisions while the ACP backend keeps the full model tool catalog. Persisted backend logs survive restarts, but the WPF shell does not yet reconstruct them into its sidebar. The existing [GUI layering and RPC decision](../architecture/2026-07-19-gui-layering-and-rpc-protocol.md) remains active for reusable client and Host packages; this fork replaces only the shipped product shell.
