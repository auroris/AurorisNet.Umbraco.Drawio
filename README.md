# draw.io for Umbraco

Create, edit and view [draw.io](https://www.drawio.com) diagrams on an Umbraco website.

![MIT license](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/license-mit.svg)
![.NET 10](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/dotnet-10.svg)
![Umbraco 17 and 18](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/umbraco-17-18.svg)

The package adds three ways to work with diagrams:

- **A backoffice editor.** A **Diagram** block for Block List and Block Grid opens draw.io,
  and stores each diagram in the media library as a PNG that carries its editable drawing.
- **A standalone editor** at `/draw/editor`, which opens a diagram file from the website and
  downloads the result.
- **A read-only viewer** at `/draw/viewer`, which published Diagram blocks can link to.

The package also removes a diagram's media once no draft, published page or kept version
uses it.

| Package | Where the editor comes from |
|---|---|
| `AurorisNet.Umbraco.Drawio` | Hosted diagrams.net: `/draw/` opens `https://app.diagrams.net/`, and the embedded editor and viewer load `https://embed.diagrams.net/`. |
| `AurorisNet.Umbraco.Drawio.Selfhosted` | The website. The package depends on the same version of the base package, carries a draw.io distribution and serves it at `/draw/`. |

## Install

Add one of the two packages to a website that runs a supported Umbraco release:

```powershell
dotnet add package AurorisNet.Umbraco.Drawio
# Or, to serve the editor from the website:
dotnet add package AurorisNet.Umbraco.Drawio.Selfhosted
```

Each package registers itself through an Umbraco composer. A site that builds its services
by hand calls `builder.AddDrawio()`; a second call changes nothing. On the first start, a
package migration creates the Diagram element type and its data types, which
**Backoffice diagrams** below describes.

With the base package, browsers need access to diagrams.net, and a Content Security Policy
on the site must allow frames from `https://embed.diagrams.net`. The diagram is sent to that
frame in the browser. Media stays in the website's media library.

## Settings

The `Drawio` section is optional. The site checks it at startup and stops with a message
that names each invalid value.

| Setting | Default | What it does |
|---|---|---|
| `WebRoot` | empty | A folder that holds a draw.io web app, absolute or relative to the content root. The site serves it at `/draw/`, with either package. |
| `RedirectMediaFiles` | `true` | Redirects a request for `/media/…/*.drawio` to `/draw/editor?url=…`, so a link to the file opens it in the editor. |
| `SupersededImages` | `Delete` | What happens to a diagram's media once nothing refers to it: `Delete`, `RecycleBin` or `Keep`, as **Media retention** below describes. |
| `DefaultLibraries` | empty | The shape libraries the editor opens, separated by semicolons, such as `general;uml;flowchart`. Empty keeps draw.io's own set. |
| `LibraryGroup` | `Extra shapes` | The sidebar heading for `Libraries`. |
| `Libraries` | empty | Extra shape libraries the editor opens with the default ones. Each has an `Id`, a `Title`, an optional `Description` and a `Url`. |
| `Plugins` | empty | draw.io plugins for a self-hosted editor. Each has a `Name` and a `Url`. |

An `Id` or `Name` holds up to 64 letters, digits, `-` or `_`, and appears once. A `Url` is a
path on the website, such as `/App_Plugins/MySite/icons.xml`. The editor loads these files
into its own page, so the site refuses an address on another host.

```json
{
  "Drawio": {
    "DefaultLibraries": "general;uml;flowchart",
    "Libraries": [
      { "Id": "site-icons", "Title": "Site icons", "Url": "/App_Plugins/MySite/icons.xml" }
    ],
    "Plugins": [
      { "Name": "notes", "Url": "/App_Plugins/MySite/notes.js" }
    ]
  }
}
```

## Where the editor comes from

| Installed | `WebRoot` | `/draw/` serves |
|---|---|---|
| Base package | empty | Hosted diagrams.net |
| Selfhosted package | empty | The bundled distribution, which the build copies to `DrawioDistribution` in the site's output and publish folders |
| Either package | set | The folder `WebRoot` names |

A self-hosted site always uses its local editor. When the folder is missing, requests for
the editor's files answer 503, and the log names a missing `WebRoot` folder.

To serve a folder of your own with the Selfhosted package installed, set `WebRoot` and add
`<DrawioBundle>false</DrawioBundle>` to the site's project, which leaves the bundled copy
out of the build. The
[IIS guide](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/IIS-DEPLOYMENT.md)
serves the folder from an IIS virtual directory.

For a self-hosted editor, the site answers `/draw/js/PreConfig.js` and
`/draw/js/PostConfig.js` itself. The scripts turn off draw.io's export server, its hosted
viewer and its icon search, then apply the libraries and plugins from the settings. To add
script of your own, register an `IDrawioScriptContributor` as a service: its `PreConfig`
runs before draw.io starts and its `PostConfig` before the editor opens.

Hosted diagrams.net loads neither plugins nor contributed script. It receives the
configured libraries and fetches each from the website, so the website must allow
`https://embed.diagrams.net` to read them with CORS headers.

## Standalone editor

`/draw/editor?url=/media/example/plan.drawio` opens a file from the website in the editor,
without a backoffice sign-in. Leave out `url` to start an empty diagram. **Save** downloads a
`.drawio` file and leaves the file on the website as it is. `/draw/` opens the full draw.io
application.

| Source | Opens as |
|---|---|
| `.drawio` or `.xml` with a draw.io diagram | The diagram |
| A PNG or SVG that carries a diagram | The diagram |
| Any other PNG, SVG, JPEG, GIF or WebP | One picture on an empty page |

The file must be on the same website and at most 40 MiB. The editor reads `.drawio`,
`.xml`, `.svg` and `.png` files under `/media/` through `/draw/proxy`, which returns them
from Umbraco's media file system, so media in remote storage opens too. The proxy accepts
paths inside the media store only.

All paths include the site's path base under an IIS application, such as
`/intranet/draw/editor`.

## Backoffice diagrams

The migration creates these in a **Diagrams** folder. It runs once, so a site can rename,
change or delete them afterwards:

| Definition | Purpose |
|---|---|
| **Diagram** element type (`drawioDiagram`) | A block with a diagram, a caption, an alternative text, a click action and a link. |
| **Diagram (editable PNG)** data type | A picker for one image, with a **Create diagram** or **Edit diagram** button. |
| **Diagram click action** and **Diagram destination** data types | The block's click action and link. |

To use it:

1. Add the **Diagram** element type to a Block List or Block Grid data type.
2. In a page, add a Diagram block and select **Create diagram**.
3. Draw, then save in draw.io. The editor uploads the diagram as a PNG named
   `diagram-<key>.png` through Umbraco's management API, with the signed-in user's media
   permissions, and selects it in the block.

Each save creates a new media item, in the folder and with the name of the one before it.
The earlier item stays, so the published page keeps its image until the page is published
again.

The **Diagram (editable PNG)** data type also works on your own content types. The editor
reopens the original PNG file, so keep that file as it was saved: image optimisation that
strips its metadata removes the drawing.

## Viewer and click actions

`/draw/viewer?url=/media/example/plan.drawio` shows a file from the website read-only, with
zoom, page and layer controls. It opens the same sources as the standalone editor.

A published Diagram block shows the image and its caption. The block's click action decides
what selecting the image does:

| Click action | Published result |
|---|---|
| **Open diagram viewer** | Opens the viewer in a new tab. New blocks start with this action. |
| **Follow link below** | Follows the block's link to a content page, a media item or a URL, with the link's target. Without a link, the image is unlinked. |
| **No link** | Shows the image unlinked. |

A block without a click action shows the image unlinked. The block renders a link only to a
path on the website, an anchor, or an `http`, `https`, `mailto` or `tel` address.

The package compiles three views: `Views/Partials/Drawio/Diagram.cshtml`, and
`drawioDiagram.cshtml` under `Views/Partials/blocklist/Components` and
`Views/Partials/blockgrid/Components`. A file at the same path in the site replaces one.
The image's alternative text is the block's alternative text, then its caption, then the
`Drawio.OpenDiagram` dictionary item, which reads "Open diagram" until it is translated.

## Media retention

A **Retained diagram media** relation links a page to each editor-created diagram that its
draft, its published content or one of its kept versions uses. The package adds the
relations when a page is saved or published. They also cover diagrams inside nested blocks
and in other property editors that report their media references to Umbraco.

The package checks the relations again at two moments:

- **Umbraco deletes versions of a page.** The package removes the relations that no
  remaining version needs.
- **A page is deleted permanently.** The package checks the diagrams of that page and its
  descendants once the deletion has committed.

A diagram whose last relation of any kind is gone is removed as `SupersededImages` says:

| Value | Result |
|---|---|
| `Delete` | Deletes the media item and its file. |
| `RecycleBin` | Moves the media item to the media recycle bin. Its file stays in storage. |
| `Keep` | Leaves the media item in the library. |

Moving a page to the content recycle bin keeps its diagrams, and so does a version that
Umbraco keeps. The package removes only images named `diagram-<media key>.png`, the name
the editor gives them, and leaves other uploads alone. A diagram saved in the editor for a
page that is then never saved has no relation, so it stays in the media library. A cleanup
that fails or that a notification handler cancels is logged, and the content deletion
stands.

## Supported versions

| Umbraco | Supported releases | Runtime |
|---|---|---|
| 18 | <!-- umbraco-releases 18 -->18.0.0 to 18.3.2<!-- /umbraco-releases --> | .NET 10 |
| 17 | <!-- umbraco-releases 17 -->17.0.0 to 17.8.2<!-- /umbraco-releases --> | .NET 10 |

The package accepts every release in these ranges, and the tests run on the first and the
newest release of each major version.

## Documentation

| Page | Covers |
|---|---|
| [IIS virtual directory](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/IIS-DEPLOYMENT.md) | Serving the draw.io web app from an IIS virtual directory at `/draw`, and checking the deployment. |
| [Third-party notices](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/THIRD-PARTY-NOTICES.md) | The draw.io release the Selfhosted package carries, and its license. |
| [Security policy](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/SECURITY.md) | Reporting a vulnerability. |

## Developing

Building needs the .NET 10 SDK, which `global.json` selects. The Selfhosted project also
needs the `external/drawio` submodule, and the backoffice adapter tests need Node.js. They
use its built-in test runner and no npm packages.

```powershell
git submodule update --init --depth 1
dotnet build -c Release
# The backoffice media adapters
node --test tests/backoffice/media-api.test.mjs
# The tests on the newest supported Umbraco release
dotnet test --project tests/AurorisNet.Umbraco.Drawio.Tests -c Release
# The tests on another release
dotnet test --project tests/AurorisNet.Umbraco.Drawio.Tests -c Release -p:UmbracoVersion=17.0.0
# Both packages, numbered 0.0.0-dev, then a check of their contents and dependencies
dotnet pack src/AurorisNet.Umbraco.Drawio.csproj -c Release -o artifacts/packages
dotnet pack selfhosted/AurorisNet.Umbraco.Drawio.Selfhosted.csproj -c Release -o artifacts/packages
pwsh scripts/Test-Packages.ps1 -Version 0.0.0-dev
```

The full test matrix is the test command run once for each release in `UmbracoReleases` in
`Directory.Build.props`. It runs on a developer's machine. The workflows test one release.
The workflows also turn build warnings into errors, so keep the build free of warnings.

The calls that differ between Umbraco major versions sit behind adapters. On the server,
`src/Compatibility/IUmbracoApi.cs` has an implementation for each major version, and the
running version selects one. In the backoffice,
`src/wwwroot/App_Plugins/AurorisNet.Drawio/compatibility/umbraco-media-api.js` does the same
for media calls. A new major version
needs an adapter of its own in both places: the package refuses to run on a major version
it has none for.

The tests open no browser. After a change to the editor scripts, check by hand, with the
hosted and the self-hosted editor: create, save, reopen and publish a block, try each click
action, and open a PNG, an SVG and a `.drawio` file in the standalone editor and the viewer.

To move to a newer draw.io, update the `external/drawio` submodule and the release and
commit in `THIRD-PARTY-NOTICES.md`.

## Releases

The package follows Umbraco. Each day, `umbraco-releases.yml` looks on nuget.org for new
Umbraco releases and tests the package on each one. A release that passes is committed to
`UmbracoReleases` in `Directory.Build.props`, which also rewrites the ranges under
**Supported versions**, and `release.yml` then publishes a new version of both packages. A
release that fails gets an issue.

Versions are numbered MAJOR.MINOR.PATCH, and both packages share one version. A new Umbraco
release raises the patch number, or the minor number when it is a new Umbraco minor version.
A person starts the first release by hand, with a version, and starts a release after
updating the draw.io submodule. The
[release guide](https://github.com/auroris/AurorisNet.Build/blob/main/docs/releasing.md)
describes the workflows.

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/LICENSE). The Selfhosted
package also carries draw.io under the Apache License 2.0, as the
[third-party notices](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/THIRD-PARTY-NOTICES.md)
describe.
