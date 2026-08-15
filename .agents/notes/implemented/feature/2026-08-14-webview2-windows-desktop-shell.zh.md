# Agent Note: WebView2 Windows 桌面外壳

Status: implemented

[English](2026-08-14-webview2-windows-desktop-shell.md) | 中文

## 问题

Windows 分发需要一个双击即可启动的应用，在不打开外部浏览器或终端的情况下呈现已交付的 Web 产品。其外壳必须在不与其他进程竞争的情况下分配回环监听地址，区分启动缓慢与运行时退出，在关闭前停止子进程树，并为可执行文件、窗口、安装器和快捷方式提供同一个有效图标。

## 决策

**WPF 可执行文件是 WebView2 生命周期外壳。** `apps/desktop-web` 启动内置 `node.exe`、嵌入原版 DeepSeek Harness Web 界面、将会话与凭据保存在 `%APPDATA%\DeepSeekHarness` 下，并负责关闭运行时。WebView2 浏览器数据位于 `%LOCALAPPDATA%\DeepSeekHarness\WebView2` 下，安装器会排除任何相邻的构建机配置目录。Node 运行时保留完整 Web 组合；外壳不重复实现其界面或 agent 协议。

**Node 负责分配临时端口。** 外壳传入 `dsh web --port 0`，不再先选择端口并在 Node 绑定前释放。它只接受 stdout 中精确的 `dsh web: http://127.0.0.1:<port>` 公告，并在导航前要求 HTTP 请求成功。启动过程会同时观察进程退出，并保留有界的 stdout 与 stderr 尾部内容，使错误对话框可以报告退出码与有用的运行时证据。

**窗口关闭会让进程完全停稳。** 关闭操作会取消启动工作、终止内置 Node 进程树，并在有界时间内等待根进程退出。由窗口关闭发起的取消不会显示启动错误对话框。

**一个生成的 ICO 服务全部 Windows 图标使用方。** `app-icon.png` 是提交的源图；`generate-icon.ps1` 会裁掉多余边缘、将内容放入方形画布，再输出 16、24、32、48、64、128 与 256 像素条目。项目把该 ICO 嵌入为可执行文件图标，并发布同一个文件供 WPF 窗口、Inno Setup 与快捷方式引用。

## 验证

`DeepSeekHarnessWeb.Tests` 固定可信的运行时公告、验证每个 ICO 目录条目和载荷偏移，并要求安装器排除构建机 WebView2 数据。`Desktop Web shell` Windows 工作流会运行这些测试并发布自包含外壳；组装后分发的冒烟验证则确认公告的回环 URL 能够访问嵌入式 Web 界面。

## 曾考虑的替代方案

**在启动 Node 前选择空闲端口。** 不采用：探测监听器关闭后到 Node 绑定前存在检查与使用时序竞争；其他进程可以占用该端口，导致首次启动失败。

**使用固定端口。** 不采用：其他本地服务或第二个外壳实例都可能合理占用它。端口零把分配权交给立即绑定监听器的进程。

**渲染独立的原生 ACP 界面。** 这个 fork 曾交付该设计，并记录在已归档的[原生 Windows 桌面 Agent 决策](../../archived/feature/2026-08-14-native-windows-desktop-agent.md)中。该设计已被替换，因为维护第二套产品界面会重复 Web 行为，并推迟使用已交付 Web 客户端中现有的功能。

## 后果

用户无需安装 Node.js 或 .NET 即可安装并启动 DeepSeek Harness，也不会看到终端或外部浏览器。应用仍使用内部回环 HTTP 服务，并依赖受支持 Windows 安装中提供的 Edge WebView2 Runtime。

启动过程会等待权威的运行时公告，而不是猜测端口；故障会展示有界诊断信息，不再要求用户盲目重试。更新源图后需要重新生成并提交 `app.ico`；测试会拒绝缺失尺寸、无效偏移与不受支持的图像头。
