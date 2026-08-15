# DeepSeek Harness Windows 桌面版（WebView2）改造记录

本文记录把源项目 harness 的 **Web 界面原封不动**打包成 Windows 桌面程序（双击即用、无需 cmd）的完整方案、构建方法与产物。

## 最终方案：WebView2 壳 + 内置原版 harness web

- 用 **Edge WebView2**（Windows 10/11 自带 Runtime）把原版 harness web 前端直接嵌进 WPF 壳。
- 双击 `DeepSeekHarnessWeb.exe` → 启动内置 Node 运行原版 `dsh web --port 0` → 从 stdout 接受其公告的 `http://127.0.0.1:<port>` → 轮询就绪 → WebView2 加载 → 关闭窗口时结束整棵 Node 进程树并等待根进程退出。
- **界面、功能 100% 是源项目原版 harness web**（消息、Markdown、工具、预设、设置、API Key onboarding 全在 web 端，WPF 只做壳）。
- 会话/凭据持久化到 `%APPDATA%\DeepSeekHarness`（DSH_HOME）。
- WebView2 用户数据固定在 `%LOCALAPPDATA%\DeepSeekHarness\WebView2`；安装器不会收录构建机生成的 `DeepSeekHarnessWeb.exe.WebView2` 缓存目录。

## 代码位置

- `apps/desktop-web/`：WebView2 壳（DeepSeekHarnessWeb.csproj + MainWindow + app.ico + installer/DeepSeekHarnessWeb.iss）。
- `apps/desktop/`（ACP 桌面版）已删除，只保留本版本。

## 运行时打包（原版 web 运行时，独立可运行）

在源项目 `E:\deepseek-harness_exe-master`（完整源码）执行：

```powershell
pnpm install
pnpm run build:lib:host
pnpm run build:lib:client
pnpm run build:web
```

`distribution/web-runtime/package.json`（166 个顶层 workspace 依赖）通过
`pnpm --filter dsh-web-runtime deploy --legacy --prod` 物化到 `.web-runtime`（约 300MB），
再补齐 peer 依赖包 lib（deploy 默认跳过 peers，37 个）、复制 `apps/cli`（@deepseek-ai/dsh）、
`apps/web/dist`（前端）、`node.exe`。为规避 Inno Setup 260 字符路径限制，打包前删除运行时内
嵌套 node_modules 与 .bin，使最长路径 ≤ 153 字符。

独立运行时启动原版 web 后返回 HTTP 200，boot manifest 完整；运行时提前退出或启动超时会显示退出码以及有界的 stdout 与 stderr 尾部日志。

## 构建与运行

- 便携版：`.artifacts/desktop-web/payload/DeepSeekHarnessWeb.exe`（连同 `runtime` 文件夹）。
- 单文件安装包：`.artifacts/desktop-web/DeepSeekHarnessWeb-Setup-0.2.0-x64.exe`（约 110MB，Inno Setup；默认装到 %LOCALAPPDATA%\Programs\DeepSeek Harness）。
- 应用图标：`apps/desktop-web/app-icon.png` 是源图，`generate-icon.ps1` 生成包含 16、24、32、48、64、128 与 256 像素条目的 `app.ico`；同一个 ICO 用于 exe 资源、WPF 窗口、安装器和快捷方式。
- 构建：`dotnet publish apps/desktop-web/DeepSeekHarnessWeb.csproj -c Release -r win-x64 --self-contained true -o .artifacts/desktop-web/payload`，再把 `.web-runtime` 复制为 `payload/runtime`；安装器用 ISCC 编译 `apps/desktop-web/installer/DeepSeekHarnessWeb.iss`。
- 验证：`dotnet test apps/desktop-web/tests/DeepSeekHarnessWeb.Tests.csproj -c Release` 检查运行时 URL 公告与 ICO 目录；`.github/workflows/desktop-web.yml` 在 Windows 上执行测试并发布外壳。

## 发布

GitHub Release `v0.2.0`：https://github.com/warm-maple/deepseek-harness_exe/releases/tag/v0.2.0
资产：`DeepSeekHarnessWeb-Setup-0.2.0-x64.exe`
