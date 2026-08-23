# @deepseek-ai/dsh-client-ui-settings-plugin-market

English | [中文](README.zh.md)

Marketplace tab in the Web Plugins settings section. Registers the `market` entry of the `settings.plugins.tab` slot: a registry search box (free text lists `dsh-plugin`-tagged packages; an exact package name resolves that package directly; a `file:` or absolute path installs a local package), result cards with install/uninstall actions, and the profile's installed-dependency list with Loader mount state. All actions go through the generated `pluginMarket` Remote of [`dsh-host-plugin-market`](../../host/plugin-market/README.md); this package owns no state beyond view state.

## Model Experience

None, as this browser-only Settings contribution registers no prompt, tool, message, or provider request.

#### KV Cache effect

None; this package never assembles model input.

## Known Limitations and Deferred Work

- **No version picker** — installs resolve `latest`; explicit ranges can only be typed as queries.
- **Restart notice, not orchestration** — after installing a client-carrying plugin the tab asks for an app restart instead of triggering one.
