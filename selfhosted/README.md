# draw.io (Self-Hosted)

Self-hosted, offline [draw.io](https://www.drawio.com) diagrams for Umbraco 17 and 18. Bundles the draw.io web app and serves it locally at `/draw/`—no external `diagrams.net` requests, virtual directories, or rewrite rules required.

![MIT license](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/license-mit.svg)
![.NET 10](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/dotnet-10.svg)
![Umbraco 17 and 18](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/umbraco-17-18.svg)

**Install this instead of `AurorisNet.Umbraco.Drawio`.** This package automatically installs and configures `AurorisNet.Umbraco.Drawio` as a NuGet dependency, giving you:
- The bundled offline draw.io editor served at `/draw/`
- The backoffice **Diagram** block (Block List / Block Grid) storing `.drawio`, editable PNG, and SVG in Media
- Standalone editor at `/draw/editor` and read-only viewer at `/draw/viewer`
- Automatic cleanup of unused diagram media

If you prefer hosted `diagrams.net` without bundling the draw.io web app, install [`AurorisNet.Umbraco.Drawio`](https://github.com/auroris/AurorisNet.Umbraco.Drawio#readme) instead.

## Install

```powershell
dotnet add package AurorisNet.Umbraco.Drawio.Selfhosted
```

No `Drawio` configuration is required. Build/publish copies the bundled web app to `DrawioDistribution`, and Umbraco serves it automatically at `/draw/`.

To supply your own draw.io build instead of the bundled copy, set `Drawio:WebRoot` to its folder and add `<DrawioBundle>false</DrawioBundle>` to your `.csproj`. To customize editor scripts, add `wwwroot/App_Plugins/AurorisNet.Drawio/PreConfig.js` or `PostConfig.js`, or register `IDrawioScriptContributor`.

## Documentation

- [Main README (settings and supported versions)](https://github.com/auroris/AurorisNet.Umbraco.Drawio#readme)
- [Usage, blocks, viewer, media retention, and customization](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/usage.md)
- [Optional IIS virtual directory](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/IIS-DEPLOYMENT.md)
- [Third-party notices](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/THIRD-PARTY-NOTICES.md)

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/LICENSE). Bundles draw.io under the Apache License 2.0 ([third-party notices](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/THIRD-PARTY-NOTICES.md)).
