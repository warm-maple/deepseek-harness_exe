# @deepseek-ai/dsh-client-ui-settings-plugin-market

[English](README.md) | 中文

Web「插件」设置区的市场标签页。注册 `settings.plugins.tab` 插槽的 `market` 条目：registry 搜索框（自由文本列出带 `dsh-plugin` 关键词的包；精确包名直接解析该包；`file:` 或绝对路径安装本地包）、带安装/卸载操作的结果卡片、以及带 Loader 挂载状态的 profile 已装依赖列表。所有操作经由 [`dsh-host-plugin-market`](../../host/plugin-market/README.md) 生成的 `pluginMarket` Remote；本包不持有视图状态以外的任何状态。

## Model Experience

无。该浏览器端 Settings 贡献不注册任何 prompt、tool、message 或 provider 请求。

#### KV Cache effect

无。该包从不组装模型输入。

## Known Limitations and Deferred Work

- **无版本选择器** —— 安装解析 `latest`；显式版本范围只能作为查询输入。
- **提示重启而非编排重启** —— 安装携带客户端半边的插件后，标签页提示重启应用，不代为触发。
