# @deepseek-ai/dsh-host-plugin-market

[English](README.md) | 中文

面向 out-of-tree profile 插件的市场 Remote。`PluginMarketGateway` 注册 `pluginMarket` 服务并发布四个生成的直连 Remote —— `pluginMarket/search`、`pluginMarket/state`、`pluginMarket/installPlugin`、`pluginMarket/uninstallPlugin` —— 让受信客户端（Web UI 的 插件 → 市场 标签页）无需命令行即可查询 npm registry 并管理当前 profile 的 out-of-tree 依赖。

搜索访问 registry（设置了 `npm_config_registry` 时优先，否则 `registry.npmjs.org`）：自由文本列出带 `dsh-plugin` 关键词的包；能解析为精确包名的查询直接取该包文档，因此在生态普遍采用关键词之前任何 npm 包都可以按名安装。结果会并入当前 profile 的安装状态。

安装用内置 npm CLI（同一个 `node` 可执行文件下的 `npm/bin/npm-cli.js`，绝不经过 shell）以 profile 目录为工作目录执行，然后按类型装配结果：声明 `dsh.bundle` 的包加入 `dsh.profile.bundles`；带其他 `dsh` 元数据的包在 profile 的 `cordis.patch.yml` 中获得一条受管理的 `- insert:` 行（由配置监视器热应用，行 id 为 `market:<名>`）；其余按普通库依赖安装。变更在服务上串行执行，并经过校验——可能被 npm 解析为旗标的说明符一律拒绝，shell 注入在结构上不可能。携带客户端半边的插件在重启后才会提供其浏览器 bundle。

## Model Experience

无。该 Host-only 市场不注册任何 prompt、tool、message 或 provider 请求。

#### KV Cache effect

无。该包从不组装模型输入。

## Known Limitations and Deferred Work

- **无版本选择 UI** —— 安装始终解析 `latest`；带显式版本范围的说明符可按名输入，但标签页暂无版本选择器。
- **浏览器半边需重启** —— 新装客户端插件的 bundle 在进程重启后才开始提供；Client 模块注册表的否定解析不会过期。
- **单一 registry** —— 每进程一个 registry（npm 环境配置）；按 profile 选择 registry 已推迟。
