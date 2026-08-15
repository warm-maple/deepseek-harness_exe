# Agent Note: WebView2 Windows desktop shell

Status: implemented

English | [中文](2026-08-14-webview2-windows-desktop-shell.zh.md)

## Problem

The Windows distribution needs a double-click application that presents the shipped Web product without an external browser or terminal. Its shell must allocate a loopback listener without racing other processes, distinguish a slow boot from an exited runtime, stop the child process tree before closing, and provide one valid icon to the executable, window, installer, and shortcuts.

## Decision

**The WPF executable is a WebView2 lifecycle shell.** `apps/desktop-web` starts the bundled `node.exe`, embeds the original DeepSeek Harness Web interface, keeps sessions and credentials under `%APPDATA%\DeepSeekHarness`, and owns runtime shutdown. WebView2 browser data lives under `%LOCALAPPDATA%\DeepSeekHarness\WebView2`, and the installer excludes any adjacent build-machine profile. The Node runtime retains the full Web composition; the shell does not duplicate its interface or Agent protocol.

**Node owns ephemeral port allocation.** The shell passes `dsh web --port 0` instead of selecting and releasing a port before Node binds. It accepts only the exact `dsh web: http://127.0.0.1:<port>` stdout announcement, then requires a successful HTTP response before navigation. Startup concurrently observes process exit and retains a bounded stdout and stderr tail so the error dialog reports the exit code and useful runtime evidence.

**Window shutdown reaches process quiescence.** Closing cancels startup work, terminates the bundled Node process tree, and waits for the root process to exit within a bounded interval. Cancellation owned by window shutdown does not open a startup-error dialog.

**One generated ICO serves every Windows icon consumer.** `app-icon.png` is the committed source artwork; `generate-icon.ps1` trims and fits it onto a square canvas and emits 16, 24, 32, 48, 64, 128, and 256 pixel entries. The project embeds that ICO as the executable icon and publishes the same file for the WPF window, Inno Setup, and shortcut references.

## Verification

`DeepSeekHarnessWeb.Tests` pins trusted runtime announcements, validates every ICO directory entry and payload offset, and requires the installer to exclude build-machine WebView2 data. The `Desktop Web shell` Windows workflow runs those tests and publishes the self-contained shell, while an assembled-distribution smoke confirms the announced loopback URL reaches the embedded Web interface.

## Alternatives considered

**Select a free port before launching Node.** Rejected because closing the probe listener before Node binds creates a time-of-check/time-of-use race; another process can take the port and make the first launch fail.

**Use a fixed port.** Rejected because another local service or a second shell instance can legitimately own it. Port zero gives allocation authority to the process that immediately binds the listener.

**Render a separate native ACP interface.** The fork previously shipped that design, recorded in the archived [native Windows desktop Agent decision](../../archived/feature/2026-08-14-native-windows-desktop-agent.md). It was replaced because maintaining a second product interface duplicated Web behavior and delayed access to features already present in the shipped Web client.

## Consequences

Users install and launch DeepSeek Harness without installing Node.js or .NET and without seeing a terminal or external browser. The application still uses an internal loopback HTTP server and requires the Edge WebView2 Runtime supplied by supported Windows installations.

Startup waits for an authoritative runtime announcement instead of guessing a port, and failures expose bounded diagnostics rather than requiring a blind second attempt. Updating the artwork requires regenerating and committing `app.ico`; the tests reject missing sizes, invalid offsets, and unsupported image headers.
