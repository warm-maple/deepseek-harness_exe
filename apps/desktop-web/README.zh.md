# DeepSeek Harness Web 桌面外壳（WebView2）

[English](README.md) | 中文

本目录包含原版 DeepSeek Harness Web 界面的 Windows 桌面外壳。自包含 WPF 可执行文件会在不显示控制台的情况下启动内置 Node 运行时，将界面嵌入 WebView2，并在窗口关闭时停止运行时。

## 文件

| 文件 | 职责 |
|---|---|
| `DeepSeekHarnessWeb.csproj` | 引用 Microsoft WebView2 的 Windows x64 自包含 `WinExe` |
| `MainWindow.xaml` / `MainWindow.xaml.cs` | 窗口、内置运行时启动、就绪检查、WebView2 导航与进程树关闭 |
| `app-icon.png` / `generate-icon.ps1` | 源图与确定性的 ImageMagick 转换 |
| `app.ico` | 生成的 16、24、32、48、64、128 与 256 像素 Windows 图标 |
| `tests/` | 启动公告信任检查与 ICO 目录验证 |
| `installer/DeepSeekHarnessWeb.iss` | Inno Setup 单文件安装器定义 |

## 使用

1. 从 GitHub Releases 下载并安装 `DeepSeekHarnessWeb-Setup-*-x64.exe`。
2. 启动 DeepSeek Harness，通过原版 Web 引导流程配置 API Key。

## 构建

```powershell
# Verify startup parsing and the committed icon.
dotnet test apps/desktop-web/tests/DeepSeekHarnessWeb.Tests.csproj -c Release

# Publish the shell.
dotnet publish apps/desktop-web/DeepSeekHarnessWeb.csproj -c Release -r win-x64 --self-contained true -o .artifacts/desktop-web/payload

# Copy the built original Web runtime to payload/runtime (see WINDOWS_DESKTOP_MODIFICATION.md), then compile the installer.
& .artifacts/tools/inno/ISCC.exe /DAppVersion=0.2.0 apps/desktop-web/installer/DeepSeekHarnessWeb.iss
```

修改源图后重新生成 `app.ico`：

```powershell
pwsh -File apps/desktop-web/generate-icon.ps1
```

## 运行时行为

- 外壳运行 `node_modules/@deepseek-ai/dsh/lib/bin.js web --port 0`。Node 负责分配临时端口；外壳只接受 `dsh web` 公告的精确 `http://127.0.0.1:<port>` URL，再探测其就绪状态。
- 运行时退出或启动超时会报告退出码以及有界的 stdout 与 stderr 尾部内容，不再隐藏底层故障。
- WebView2 将浏览器数据保存在 `%LOCALAPPDATA%\DeepSeekHarness\WebView2` 下；安装器会排除构建机生成的任何相邻 `DeepSeekHarnessWeb.exe.WebView2` 目录。
- 会话与凭据持久化在 `%APPDATA%\DeepSeekHarness` 下。
- 运行时打包与裁剪方法记录在 [Windows 桌面分发参考](../../WINDOWS_DESKTOP_MODIFICATION.md)中。
