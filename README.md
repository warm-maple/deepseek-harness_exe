# DeepSeek Harness

English | [中文](README.zh.md)

DeepSeek Harness (`dsh`) is an open-source agent harness developed by [DeepSeek AI](https://deepseek.com).

It uses an architecture where **everything is a plugin**, and is powered by [Cordis](https://github.com/cordiverse/cordis), whose design is described in [_A Programming Paradigm for Spatiotemporal Composability_](https://github.com/cordiverse/paper).

## Developer preview

DeepSeek Harness is currently in _developer preview_ and is iterating rapidly. **THERE WILL BE COMPATIBILITY-BREAKING CHANGES.**

## Windows desktop app

This fork ships DeepSeek Harness as a native Windows x64 desktop Agent. It does not start a browser, HTTP server, or visible command window. The bundled background runtime communicates with the WPF application over ACP JSON-RPC stdio.

Install the current build with `.artifacts/desktop/DeepSeekHarness-Setup-0.1.0-x64.exe`. On first launch, open Settings, save a DeepSeek API key, choose a workspace, and start a task. The key is stored in Windows Credential Manager.

To rebuild the self-contained application and installer:

```powershell
pnpm install
pnpm run build:windows
```

The build downloads a local .NET 8 SDK and Inno Setup when they are unavailable, then writes the portable payload and installer under `.artifacts/desktop/`. See [the desktop application guide](apps/desktop/README.md).

## Community and support

- Feel free to submit feedback or bug reports through [GitHub Discussions](https://github.com/deepseek-ai/deepseek-harness/discussions).
- Add the [`dsh-plugin`](https://github.com/topics/dsh-plugin) topic to your plugin repository for discoverability.
- Join <a href="https://discord.gg/Ycq5dCaS4">DeepSeek Harness Discord community</a>.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Development

Start with the [development guide](docs/development.md) and [architecture documentation](docs/architecture.md).

For agents, follow [AGENTS.md](AGENTS.md).

## License

[MIT](LICENSE)

Third-party dependencies and their licenses are disclosed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
