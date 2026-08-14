# Windows desktop application

English | [中文](README.zh.md)

DeepSeek Harness Desktop is a native WPF shell for Windows x64. The installed application starts a bundled Node.js runtime without a console window and exchanges ACP JSON-RPC messages through redirected standard input and output. It opens no browser and listens on no network port.

## Use

1. Run `DeepSeekHarness-Setup-0.1.0-x64.exe`.
2. Open **Settings**, enter a DeepSeek API key, select a model and permission mode, then save.
3. Select a workspace, create a task, and send a prompt.
4. Approve or reject wider tool access in the application when workspace-write mode requests it. Use **Stop** to cancel the active turn.

The API key is stored in Windows Credential Manager. Non-secret preferences live under `%APPDATA%\DeepSeekHarness`; persisted Agent sessions live under `%LOCALAPPDATA%\DeepSeekHarness\sessions`.

## Agent capabilities

The desktop composition keeps the coding Agent runtime: PowerShell execution, sandboxed filesystem read/write/edit/search, workspace instructions, local skills, persistent goals and jobs, todo tracking, compaction, subagents, workflows, Ralph iterations, and DeepSeek web search. “No Web” refers to the removed browser product; the model-facing search tool remains available.

## Build

From the repository root on Windows x64:

```powershell
pnpm install
pnpm run build:windows
```

The build performs the Host package build, verifies the runtime dependency closure, publishes a self-contained WPF `WinExe`, deploys a relocatable ACP runtime, downloads the .NET 8 SDK and Inno Setup 6.7.3 when needed, and compiles a current-user installer.

Outputs:

- `.artifacts/desktop/payload/DeepSeekHarness.exe` — portable application payload; keep its `runtime` directory beside it.
- `.artifacts/desktop/DeepSeekHarness-Setup-<version>-x64.exe` — single-file installer.

Use `-SkipInstall`, `-SkipHostBuild`, or `-SkipInstaller` with `scripts/build-windows-desktop.ps1` only when the corresponding prerequisite or artifact is already current.

## Current limits

The native shell creates fresh ACP sessions and preserves their backend logs, but it does not yet reopen a previous conversation after the application exits. ACP text chunks are displayed live; rich tool cards, attachments, slash-command UI, and a graphical session-history browser are not implemented in this first desktop release. The underlying Agent tools remain available to the model. The installer is not code-signed, so Windows may show a publisher warning.
