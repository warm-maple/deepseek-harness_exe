# DeepSeek Harness Windows 桌面版（WebView2）改造记录

本文记录把源项目 harness 的 **Web 界面原封不动**打包成 Windows 桌面程序（双击即用、无需 cmd）的完整方案、构建方法与产物。当前版本 **0.4.0**，基于上游 `dsh-v0.1.1-rc.2`。

## 最终方案：WebView2 壳 + 内置原版 harness web

- 用 **Edge WebView2**（Windows 10/11 自带 Runtime）把原版 harness web 前端直接嵌进 WPF 壳。
- 双击 `DeepSeekHarnessWeb.exe` → 启动内置 Node 运行原版 `dsh web --port 0 --no-open` → 从 stdout 接受其公告的 `http://127.0.0.1:<port>` → 轮询就绪 → WebView2 加载 → 关闭窗口时结束整棵 Node 进程树并等待根进程退出。
- **界面、功能 100% 是源项目原版 harness web**（消息、Markdown、工具、预设、设置、API Key onboarding 全在 web 端，WPF 只做壳）。
- `--no-open` 是 0.3.0 新增：上游新版 `dsh web` 默认把 URL 交给系统浏览器，壳必须只保留 WebView2 一个界面。
- 会话/凭据持久化到 `%APPDATA%\DeepSeekHarness`（DSH_HOME），与旧版桌面端共用数据（实测旧会话完整可见）。
- WebView2 用户数据固定在 `%LOCALAPPDATA%\DeepSeekHarness\WebView2`；安装器不会收录构建机生成的 `DeepSeekHarnessWeb.exe.WebView2` 缓存目录。

## 代码位置

- `apps/desktop-web/`：WebView2 壳（DeepSeekHarnessWeb.csproj + MainWindow + app.ico + installer/DeepSeekHarnessWeb.iss）。

## 运行时打包（原版 web 运行时，独立可运行）

在源项目（完整源码）执行：

```powershell
pnpm install
pnpm run build:lib:host
pnpm run build:lib:client
pnpm run build:web
```

`distribution/web-runtime/package.json`（203 个顶层 workspace 依赖：上一版清单 + 上游新增包 +
web profile 浏览器插件 roster + `pnpm tsx scripts/verify-runtime-closure.ts --manifest
distribution/web-runtime/package.json` 报告的必需 peer）通过

```powershell
pnpm --filter dsh-web-runtime deploy --legacy --prod --config.node-linker=hoisted .web-runtime
```

物化到 `.web-runtime`。**`--config.node-linker=hoisted` 是 0.3.0 新增**：pnpm 11 默认产出 `.pnpm`
虚拟存储布局（junction + 超长路径），hoisted 才是旧版那样的扁平真实文件布局。物化后删除
`node_modules/.pnpm`（仅 lockfile 残留）与所有 `.bin`（shim），**保留**嵌套 `node_modules`
（hoisted 布局下的版本冲突解析，删除有破坏解析的风险）；最长路径 220 字符，满足 Inno Setup 260 限制。

前端 dist 无需再复制 `apps/web/dist`：新版 web runtime 通过
`require.resolve('@deepseek-ai/dsh-web-frontend/dist/index.html')` 从部署根 node_modules 定位，
清单里的 `@deepseek-ai/dsh-web-frontend` 依赖随 deploy 自动带上 dist。再复制 `node.exe` 即为
完整独立运行时。

## 构建与运行

- 便携版：`.artifacts/desktop-web/payload/DeepSeekHarnessWeb.exe`（连同 `runtime` 文件夹）。
- 单文件安装包：`.artifacts/desktop-web/DeepSeekHarnessWeb-Setup-0.4.0-x64.exe`（约 90MB，Inno Setup 7；默认装到 %LOCALAPPDATA%\Programs\DeepSeek Harness）。
- 应用图标：`apps/desktop-web/app-icon.png` 是源图，`generate-icon.ps1`（0.3.1 起纯 System.Drawing 实现，无 ImageMagick 依赖；中心方形裁切）生成包含 16、24、32、48、64、128 与 256 像素条目（PNG 内嵌）的 `app.ico`；同一个 ICO 用于 exe 资源、WPF 窗口、安装器和快捷方式。
- 升级清理（0.3.1 新增）：安装器在复制文件前清空 `{app}\runtime` 与遗留的 `DeepSeekHarnessWeb.exe.WebView2` 缓存，保证从 0.2.x 原地升级时不残留旧运行时文件；用户数据（`%APPDATA%\DeepSeekHarness`）不受影响。
- 构建：`dotnet publish apps/desktop-web/DeepSeekHarnessWeb.csproj -c Release -r win-x64 --self-contained true -o .artifacts/desktop-web/payload`，再把 `.web-runtime` 复制为 `payload/runtime`；安装器用 ISCC 编译 `apps/desktop-web/installer/DeepSeekHarnessWeb.iss`（源目录指向 payload）。
- 验证：`dotnet test apps/desktop-web/tests/DeepSeekHarnessWeb.Tests.csproj -c Release` 检查运行时 URL 公告与 ICO 目录。

## 发布

GitHub Release `v0.4.0`：资产 `DeepSeekHarnessWeb-Setup-0.4.0-x64.exe`。历史版本：`v0.2.0`–`v0.3.2`。

## 0.4.0：内置插件市场

- 新增 `packages/host/plugin-market`（`pluginMarket` Remote：`search`/`state`/`installPlugin`/`uninstallPlugin`，内置 npm 安装、`dsh.bundle`/插件行/库三类装配）与 `packages/client/ui-settings-plugin-market`（设置 → 插件 → 插件市场 标签页）。
- 搜索 `keywords:dsh-plugin` 社区约定；精确包名直连 registry 文档；`file:` 路径可装本地未发布插件。
- 安装/卸载串行化，写入 profile `package.json` 与受管理的 `cordis.patch.yml` 插入行（热生效；bundle 与浏览器半边需重启）。
