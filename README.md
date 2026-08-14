# DeepSeek Harness · Windows 桌面版（WebView2）

把 **原封不动的 DeepSeek Harness Web 界面**打包成 Windows 桌面程序：**双击 exe 即可使用，无需打开终端或手动启动服务**。

- 界面、功能与源项目 harness Web 版 **100% 一致**（对话、Markdown、工具调用、Agent 预设、设置、API Key onboarding 等全在 Web 端）。
- 应用启动时自动拉起内置 Node 运行原版 harness Web 服务（监听本机随机端口），并用 **Edge WebView2** 在窗口内加载；关闭窗口自动结束后台进程。

## 下载与安装

前往 [Releases](https://github.com/warm-maple/deepseek-harness_exe/releases) 下载最新安装包：

| 文件 | 说明 |
|---|---|
| `DeepSeekHarnessWeb-Setup-<版本>-x64.exe` | 单文件安装器（推荐） |

安装后从开始菜单或桌面快捷方式启动 **DeepSeek Harness**，首次打开按原版方式配置 DeepSeek API Key 即可使用。

> 便携版也可直接使用：仓库 `.artifacts/desktop-web/payload/` 下的 `DeepSeekHarnessWeb.exe`（需连同 `runtime` 文件夹一起保留）。

### 系统要求

- Windows 10 / 11（x64）
- Edge WebView2 Runtime（Windows 10/11 一般已内置；缺失时会提示安装）

## 工作原理

```
双击 DeepSeekHarnessWeb.exe
  ├─ 1. 探测本机空闲端口
  ├─ 2. 启动内置 node：node_modules/@deepseek-ai/dsh/lib/bin.js web --port <端口>
  ├─ 3. 轮询 http://127.0.0.1:<端口> 就绪
  ├─ 4. WebView2 窗口加载该地址（原版 harness Web 界面）
  └─ 关闭窗口 → taskkill /T /F 结束整棵 Node 进程树
```

- 会话、凭据等持久化在 `%APPDATA%\DeepSeekHarness`（DSH_HOME）。
- Web 服务只监听 `127.0.0.1`，不对外暴露。

## 代码结构

```
apps/desktop-web/                 WebView2 壳（C# / WPF）
├── DeepSeekHarnessWeb.csproj     WinExe + Microsoft.Web.WebView2
├── MainWindow.xaml(.cs)          窗口 + 启动/关闭 Node 生命周期
├── app.ico                       应用图标
└── installer/DeepSeekHarnessWeb.iss   Inno Setup 安装脚本
```

## 重新构建

**1. 构建原版 Web 运行时**（在源项目完整源码目录，如 `E:\deepseek-harness_exe-master`）：

```powershell
pnpm install
pnpm run build:lib:host
pnpm run build:lib:client
pnpm run build:web
```

用 `distribution/web-runtime/package.json` 通过 `pnpm --filter dsh-web-runtime deploy --legacy --prod` 物化运行时（含补齐 peer 依赖、`apps/cli`、`apps/web/dist`、`node.exe`），并清理嵌套 `node_modules` 以规避安装器 260 字符路径限制。

**2. 发布壳并打包**：

```powershell
dotnet publish apps/desktop-web/DeepSeekHarnessWeb.csproj -c Release -r win-x64 --self-contained true -o .artifacts/desktop-web/payload
# 把上一步的 web 运行时复制为 .artifacts/desktop-web/payload/runtime
# 再用 ISCC 编译 apps/desktop-web/installer/DeepSeekHarnessWeb.iss 生成安装器
```

## 常见问题

- **首次启动较慢（5–20 秒）**：应用需要启动内置 Node 并加载 Web 前端，属正常现象。
- **SmartScreen 提示"未知发布者"**：安装包尚未代码签名，点击"更多信息 → 仍要运行"即可。
- **打开后是空白/错误页**：确认系统已安装 Edge WebView2 Runtime。

## 与源项目的关系

本仓库是 [deepseek-harness](https://github.com/deepseek-ai/deepseek-harness) 的 fork，仅新增 `apps/desktop-web` 桌面壳与打包脚本；**Web 界面与 Agent 功能全部来自原项目，未做改动**。源项目相关文档见 [README 原文](https://github.com/deepseek-ai/deepseek-harness)。
