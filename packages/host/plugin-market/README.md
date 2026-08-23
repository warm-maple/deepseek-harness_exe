# @deepseek-ai/dsh-host-plugin-market

English | [中文](README.zh.md)

Marketplace Remote for out-of-tree profile plugins. `PluginMarketGateway` registers the `pluginMarket` service and publishes four generated direct Remotes — `pluginMarket/search`, `pluginMarket/state`, `pluginMarket/installPlugin`, and `pluginMarket/uninstallPlugin` — that let a trusted client (the Web UI's Plugins → Marketplace tab) query the npm registry and manage the running profile's out-of-tree dependencies without a shell.

Search queries the registry (`npm_config_registry` when set, otherwise `registry.npmjs.org`): free text lists packages tagged with the `dsh-plugin` keyword, while a query that parses as an exact package name resolves that package document directly, so any npm package is reachable by name. Results merge the profile's current install state.

Install runs the bundled npm CLI (`npm/bin/npm-cli.js` under the same `node` executable, never a shell) with the profile directory as working directory, then composes the result by kind: a package declaring `dsh.bundle` joins `dsh.profile.bundles`; a package with any other `dsh` metadata gets a managed `- insert:` row in the profile `cordis.patch.yml` (hot-applied by the config watcher, row id `market:<name>`); everything else installs as a plain library dependency. Mutations are serialized on the service and validated — specifiers that could parse as npm flags are rejected, and shell interpolation is structurally impossible. Client-carrying plugins still need a restart for their browser bundle to be served.

## Model Experience

None, as this Host-only marketplace registers no prompt, tool, message, or provider request.

#### KV Cache effect

None; this package never assembles model input.

## Known Limitations and Deferred Work

- **No version pinning UI** — install always resolves `latest`; a specifier with an explicit range can be typed as a name-like query but the tab offers no version picker yet.
- **Restart for browser halves** — newly installed client plugins are served after the process restarts; the Client module registry's negative resolutions never expire.
- **Single registry** — one registry per process (npm's environment config); per-profile registry selection is deferred.
