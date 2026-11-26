# draw.io

Create, edit, and display [draw.io](https://www.drawio.com) diagrams in Umbraco 17 and 18.

![MIT license](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/license-mit.svg)
![.NET 10](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/dotnet-10.svg)
![Umbraco 17 and 18](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/umbraco-17-18.svg)

What you get:
- **Backoffice Diagram block** (Block List / Block Grid) with **Create diagram** and **Edit diagram**, storing the `.drawio` source, editable PNG, and SVG on one media item.
- **Standalone editor** at `/draw/editor` and **read-only viewer** at `/draw/viewer` (zoom, pages, layers, clickable shape links).
- **Automatic media cleanup** once no draft, published page, or kept version references a diagram.

## Install

Install **one** of these two packages (do not install both—the self-hosted package automatically pulls in the base package via NuGet):

```powershell
# Hosted editor (uses embed.diagrams.net / app.diagrams.net)
dotnet add package AurorisNet.Umbraco.Drawio

# Self-hosted offline editor (bundles draw.io at /draw/ and includes AurorisNet.Umbraco.Drawio)
dotnet add package AurorisNet.Umbraco.Drawio.Selfhosted
```

Umbraco discovers the package on startup and installs the **Diagram** element type, media type, data types, and **Media → diagrams** upload folder via a package migration. Add the **Diagram** block to any Block List or Block Grid to start using it.

With the hosted base package, browsers need access to `diagrams.net` and any CSP must allow frames from `https://embed.diagrams.net`.

## Settings

Optional configuration under the `Drawio` section:

| Setting | Default | Effect |
|---|---|---|
| `WebRoot` | empty | Local folder containing your own draw.io web app to serve at `/draw/`. |
| `RedirectMediaFiles` | `true` | Opens direct `.drawio` media links in the editor. |
| `SupersededImages` | `Delete` | Cleanup action for unreferenced diagram media: `Delete`, `RecycleBin`, or `Keep`. |
| `DefaultLibraries` | empty | Semicolon-separated shape libraries to open (e.g. `general;uml;flowchart`). |
| `LibraryGroup` | `Extra shapes` | Sidebar heading for custom `Libraries`. |
| `Libraries` | empty | Extra shape libraries (`Id`, `Title`, optional `Description`, same-site `Url`). |
| `Plugins` | empty | Self-hosted draw.io plugins (`Name`, same-site `Url`). |

## Supported versions

- Umbraco 18: <!-- umbraco-releases 18 -->18.0.0 to 18.3.2<!-- /umbraco-releases --> (.NET 10)
- Umbraco 17: <!-- umbraco-releases 17 -->17.0.0 to 17.8.2<!-- /umbraco-releases --> (.NET 10)

## Documentation

- [Usage, blocks, viewer, media retention, and customization](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/usage.md)
- [Optional IIS virtual directory](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/IIS-DEPLOYMENT.md)
- [Developing and releases](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/developing.md)
- [Third-party notices](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/THIRD-PARTY-NOTICES.md)

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/LICENSE). `AurorisNet.Umbraco.Drawio.Selfhosted` also bundles draw.io under Apache 2.0 ([third-party notices](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/THIRD-PARTY-NOTICES.md)).
