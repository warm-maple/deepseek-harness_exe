# DeepSeek Harness Web 桌面壳（WebView2）

本目录是 DeepSeek Harness **Windows 桌面版**的 WebView2 壳：把源项目原版 harness Web 界面原封不动地嵌进原生窗口，双击即用、无需终端。

## 文件

| 文件 | 职责 |
|---|---|
| DeepSeekHarnessWeb.csproj | Windows x64 自包含 WinExe，引用 Microsoft.Web.WebView2 |
| MainWindow.xaml / MainWindow.xaml.cs | 窗口；探测端口 → 启动内置 Node（原版 dsh web）→ WebView2 加载 → 关闭时结束进程树 |
| app.ico | 应用图标（用户提供 PNG 转多尺寸 ICO） |
| installer/DeepSeekHarnessWeb.iss | Inno Setup 单文件安装器脚本 |

## 使用

1. 从 GitHub Releases 下载 DeepSeekHarnessWeb-Setup-*-x64.exe 安装。
2. 启动 DeepSeek Harness，按原版 Web 界面的 onboarding 配置 API Key。

## 构建

```powershell
# 1. 发布壳
dotnet publish apps/desktop-web/DeepSeekHarnessWeb.csproj -c Release -r win-x64 --self-contained true -o .artifacts/desktop-web/payload

# 2. 把构建好的原版 Web 运行时复制为 payload/runtime（见 WINDOWS_DESKTOP_MODIFICATION.md）

# 3. 编译安装器
& .artifacts/tools/inno/ISCC.exe /DAppVersion=0.2.0 apps/desktop-web/installer/DeepSeekHarnessWeb.iss
```

## 运行时说明

- 内置 Node 运行 node_modules/@deepseek-ai/dsh/lib/bin.js web --port <随机端口>，Web 服务只监听 127.0.0.1。
- 数据（会话/凭据）持久化在 %APPDATA%\DeepSeekHarness。
- 运行时打包与裁剪细节见根目录 WINDOWS_DESKTOP_MODIFICATION.md。
