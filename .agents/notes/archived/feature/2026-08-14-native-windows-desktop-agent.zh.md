# Agent Note: 原生 Windows 桌面 Agent

Status: implemented
Archived: 2026-08-14

[English](2026-08-14-native-windows-desktop-agent.md) | 中文

## 问题

这个 fork 需要可以直接启动的 Windows x64 应用，而不是一条启动浏览器产品的命令。复用浏览器前端会保留 HTTP 服务、端口生命周期和控制台启动器；从 Agent 中删除终端执行又会一并移除核心编码能力。因此，交付物需要替换产品界面，同时不削减面向模型的运行时。

## 决策

**产品外壳使用自包含 WPF `WinExe`。** 它负责选择工作区、任务标签、提示词与回复展示、取消、权限决策、模型选择和凭据输入。DeepSeek API Key 使用 Windows 凭据管理器保存；偏好和会话数据继续存放在每用户应用目录。

**Agent 运行时仍是独立的隐藏进程。** 应用使用 `UseShellExecute=false`、`CreateNoWindow=true` 和重定向标准流启动内置 `node.exe`。ACP JSON-RPC 在没有 HTTP 监听器的情况下提供初始化、创建会话、提示词、取消、流式文本更新和权限请求。进程清理会先关闭标准输入并等待正常退出；经过有界超时后，再终止仍然存在的进程树。

**桌面组合保留编码能力。** 它挂载 DeepSeek 适配器、Windows 沙箱与 PowerShell 执行器、文件工具、技能、目标、后台任务、上下文压缩、子 Agent、工作流、待办跟踪和 DeepSeek 搜索。删除的 `apps/web`、`apps/cli` 与 `dsh-web-app` 是产品启动界面；`packages/web` 继续作为面向模型的搜索能力存在。

**分发产物由可迁移运行时与原生外壳组成。** `pnpm deploy --legacy` 可能漏掉直接工作区提升包，并留下指向源码检出目录的 junction，因此构建会补齐每个缺失的直接包，并把全部链接替换成复制的文件。本地 .NET 8 SDK 发布 x64 自包含外壳，Inno Setup 则生成一个按当前用户安装的安装程序。缺少这两个工具时，它们都会自动引导到已忽略的产物存储中。

## 曾考虑的替代方案

**Electron 或嵌入式浏览器视图。** 不采用：这会继续保留浏览器渲染产品及其前端依赖图，不能满足原生外壳要求。

**把整个应用打成一个 Node 可执行文件。** 不采用：动态 Cordis 加载与原生依赖已经由可迁移运行时目录处理；把这些文件继续并入 WPF 可执行文件会引入第二种打包机制，也让原生模块加载更难诊断。独立的[单文件 SDK 运行时决策](../architecture/2026-07-10-single-file-executable-sdk-runtime-distribution.md)对其 Python SDK 分发仍然有效。

**随 CLI 一起删除 PowerShell 执行。** 不采用：「不要命令行窗口」是展示要求。PowerShell 是 Windows 编码执行器，只会作为隐藏 Agent 运行时的子进程运行。

## 后果

用户可以从开始菜单或桌面快捷方式安装和启动 DeepSeek Harness，不需要 Node.js、.NET、浏览器、端口或终端窗口。第一版桌面应用展示文本对话与权限决策，同时 ACP 后端保留完整的模型工具目录。持久化后端日志会跨重启保留，但 WPF 外壳暂时不会把它们重新构造成侧栏历史。既有的[GUI 分层与 RPC 决策](../architecture/2026-07-19-gui-layering-and-rpc-protocol.md)仍适用于可复用的 client 与 Host 包；这个 fork 只替换已交付的产品外壳。
