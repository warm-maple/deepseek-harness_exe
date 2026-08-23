# DeepSeek Harness Web desktop shell (WebView2)

English | [中文](README.zh.md)

This directory contains the Windows desktop shell for the original DeepSeek Harness Web interface. The self-contained WPF executable starts the bundled Node runtime without a console, embeds the interface in WebView2, and stops the runtime when the window closes.

## Files

| File | Responsibility |
|---|---|
| `DeepSeekHarnessWeb.csproj` | Windows x64 self-contained `WinExe` with Microsoft WebView2 |
| `MainWindow.xaml` / `MainWindow.xaml.cs` | Window, bundled runtime startup, readiness checks, WebView2 navigation, and process-tree shutdown |
| `app-icon.png` / `generate-icon.ps1` | Source artwork and deterministic ImageMagick conversion |
| `app.ico` | Generated 16, 24, 32, 48, 64, 128, and 256 pixel Windows icon |
| `tests/` | Startup-announcement trust checks and ICO directory validation |
| `installer/DeepSeekHarnessWeb.iss` | Inno Setup single-file installer definition |

## Use

1. Download and install `DeepSeekHarnessWeb-Setup-*-x64.exe` from GitHub Releases.
2. Start DeepSeek Harness and configure the API key through the original Web onboarding flow.

## Build

```powershell
# Verify startup parsing and the committed icon.
dotnet test apps/desktop-web/tests/DeepSeekHarnessWeb.Tests.csproj -c Release

# Publish the shell.
dotnet publish apps/desktop-web/DeepSeekHarnessWeb.csproj -c Release -r win-x64 --self-contained true -o .artifacts/desktop-web/payload

# Copy the built original Web runtime to payload/runtime (see WINDOWS_DESKTOP_MODIFICATION.md), then compile the installer.
& .artifacts/tools/inno/ISCC.exe /DAppVersion=0.2.0 apps/desktop-web/installer/DeepSeekHarnessWeb.iss
```

Regenerate `app.ico` after changing the source artwork:

```powershell
pwsh -File apps/desktop-web/generate-icon.ps1
```

## Runtime behavior

- The shell runs `node_modules/@deepseek-ai/dsh/lib/bin.js web --port 0`. Node owns the ephemeral port allocation, and the shell accepts only the exact `http://127.0.0.1:<port>` URL announced by `dsh web` before probing it for readiness.
- A runtime exit or startup timeout reports the exit code and a bounded tail of stdout and stderr instead of hiding the underlying failure.
- WebView2 stores its browser data under `%LOCALAPPDATA%\DeepSeekHarness\WebView2`; the installer excludes any adjacent `DeepSeekHarnessWeb.exe.WebView2` directory created on a build machine.
- Sessions and credentials persist under `%APPDATA%\DeepSeekHarness`.
- Runtime packaging and pruning are documented in [the Windows desktop distribution reference](../../WINDOWS_DESKTOP_MODIFICATION.md).
