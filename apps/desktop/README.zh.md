# Windows 桌面应用

[English](README.md) | 中文

DeepSeek Harness Desktop 是面向 Windows x64 的原生 WPF 外壳。安装后的应用以无控制台窗口方式启动内置 Node.js 运行时，并通过重定向的标准输入输出交换 ACP JSON-RPC 消息。它不会打开浏览器，也不会监听网络端口。

## 使用

1. 运行 `DeepSeekHarness-Setup-0.1.0-x64.exe`。
2. 打开「设置」，输入 DeepSeek API Key，选择模型与权限模式，然后保存。
3. 选择工作区、新建任务并发送提示词。
4. 在 workspace-write 模式请求扩大工具访问范围时，可直接在应用中允许或拒绝。使用「停止」可取消当前轮次。

API Key 保存在 Windows 凭据管理器中。非敏感偏好保存在 `%APPDATA%\DeepSeekHarness`，持久化的 Agent 会话保存在 `%LOCALAPPDATA%\DeepSeekHarness\sessions`。

## Agent 能力

桌面组合保留编码 Agent 运行时：PowerShell 执行、受沙箱保护的文件读取／写入／编辑／搜索、工作区指令、本地技能、持久化目标与后台任务、待办事项、上下文压缩、子 Agent、工作流、Ralph 迭代和 DeepSeek 联网搜索。「不要 Web」指已删除浏览器产品；面向模型的搜索工具仍然可用。

## 构建

在 Windows x64 的仓库根目录运行：

```powershell
pnpm install
pnpm run build:windows
```

构建会完成 Host 包构建、验证运行时依赖闭包、发布自包含 WPF `WinExe`、部署可迁移的 ACP 运行时，并在需要时下载 .NET 8 SDK 与 Inno Setup 6.7.3，最后生成按当前用户安装的安装程序。

输出：

- `.artifacts/desktop/payload/DeepSeekHarness.exe`：便携应用目录；必须让同目录下的 `runtime` 文件夹保持在它旁边。
- `.artifacts/desktop/DeepSeekHarness-Setup-<version>-x64.exe`：单文件安装程序。

只有在对应前置步骤或产物仍然有效时，才对 `scripts/build-windows-desktop.ps1` 使用 `-SkipInstall`、`-SkipHostBuild` 或 `-SkipInstaller`。

## 当前限制

原生外壳会创建新的 ACP 会话并保留其后端日志，但应用退出后暂时不能重新打开之前的对话。ACP 文本分块会实时显示；第一版桌面应用尚未实现富工具卡片、附件、斜杠命令界面和图形化会话历史浏览器。底层 Agent 工具仍然可供模型使用。安装程序尚未进行代码签名，因此 Windows 可能显示发布者警告。
