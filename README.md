# draw.io for Umbraco

Create, edit and view [draw.io](https://www.drawio.com) diagrams on an Umbraco website.

![MIT license](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/license-mit.svg)
![.NET 10](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/dotnet-10.svg)
![Umbraco 17 and 18](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/umbraco-17-18.svg)

The package adds three ways to work with diagrams:

- **A backoffice editor.** A **Diagram** block for Block List and Block Grid opens draw.io,
  and stores its draw.io source, editable PNG and SVG together in the media library.
- **A standalone editor** at `/draw/editor` for opening diagram files from the website or
  your computer. You can download your edits or use the full editor at `/draw/` to save
  diagrams in browser storage.
- **A read-only viewer** at `/draw/viewer`, which published Diagram blocks can link to.

The package also removes a diagram's media once no draft, published page or kept version
uses it.

## Where the editor comes from

| Installed package | Editor source by default |
|---|---|
| `AurorisNet.Umbraco.Drawio` | diagrams.net hosts the editor. `/draw/` opens `https://app.diagrams.net/`. The embedded editor and viewer load `https://embed.diagrams.net/`. |
| `AurorisNet.Umbraco.Drawio.Selfhosted` | Your website serves the bundled draw.io editor at `/draw/`. The embedded editor and viewer load from the same path. |

## Install

On a supported Umbraco site, install one package and deploy the site as usual.

```powershell
dotnet add package AurorisNet.Umbraco.Drawio
# Or, to serve the editor from the website:
dotnet add package AurorisNet.Umbraco.Drawio.Selfhosted
```

Umbraco discovers the package at startup. The Selfhosted package brings in the base package
and serves the bundled editor at `/draw/` through Umbraco, including on IIS. The default
install needs no Drawio settings, virtual directory or rewrite rules. A site that builds
its services by hand can call `builder.AddDrawio()`.

Umbraco's package migration installs the Diagram element type, media type, data types and
upload folder. Sites using attended package migrations can run it from **Packages**.
Sites that disable schema and content migrations can deploy the definitions through their
usual deployment process. Add the Diagram block to the Block List or Block Grid where
authors should use it.

With the base package, browsers need access to diagrams.net, and a Content Security Policy
on the site must allow frames from `https://embed.diagrams.net`. The diagram is sent to that
frame in the browser. Media stays in the website's media library.

## Settings

The `Drawio` section is optional. The site checks it at startup and stops with a message
that names each invalid value.

| Setting | Default | What it does |
|---|---|---|
| `WebRoot` | empty | To serve your own copy of draw.io at `/draw/`, set this to its web app folder. Use an absolute path or one relative to the site's content root. |
| `RedirectMediaFiles` | `true` | Opens links to `.drawio` files under Umbraco's configured media path in the editor. The default media path is `/media/`. |
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

### Customize the self-hosted editor

The Selfhosted package copies draw.io to `DrawioDistribution` in the site's build and
publish folders. To supply your own copy, set `WebRoot` to its folder and add
`<DrawioBundle>false</DrawioBundle>` to the site's project to skip the bundled copy.
If the editor folder is missing, requests for its files return HTTP 503. When `WebRoot`
is set, the log names the missing folder.

Umbraco serves `/draw/js/PreConfig.js` and `/draw/js/PostConfig.js` with the package
defaults. To customize either script, add the corresponding JavaScript file to your site
and deploy it with the rest of your web assets.

| Optional site file | When draw.io runs it |
|---|---|
| `wwwroot/App_Plugins/AurorisNet.Drawio/PreConfig.js` | Before loading the editor. Use it to change `window.DRAWIO_CONFIG`. |
| `wwwroot/App_Plugins/AurorisNet.Drawio/PostConfig.js` | After loading the editor code. Use it to customize draw.io's JavaScript APIs. |

For example, this `PreConfig.js` sets the grid spacing and preserves any libraries configured
in the site's Drawio settings.

```javascript
window.DRAWIO_CONFIG = { ...window.DRAWIO_CONFIG, defaultGridSize: 20 };
```

For configuration generated in C#, register an `IDrawioScriptContributor` as a service.
Each response contains the package defaults, then the registered contributors in order,
then the site's file if present.

Hosted diagrams.net loads neither plugins nor contributed script. It receives the
configured libraries and fetches each from the website, so the website must allow
`https://embed.diagrams.net` to read them with CORS headers.

## Standalone editor

Open `/draw/editor` to start a blank diagram. To load a file from your computer, choose
**File → Import From → Device**. To open a file from the website, pass its path in the
`url` parameter, for example `/draw/editor?url=/media/example/plan.drawio`.

**Save** downloads the edited diagram to your computer as a `.drawio` file. To keep a
diagram in browser storage, open the full draw.io application at `/draw/?browser=1` and choose
**Browser** as the storage location.

| File opened with `url` | Opens as |
|---|---|
| `.drawio` or `.xml` with a draw.io diagram | The diagram |
| A PNG or SVG that carries a diagram | The diagram |
| Any other PNG, SVG, JPEG, GIF or WebP | One picture on an empty page |

Files opened with `url` must be on the same website and at most 40 MiB. The editor reads
the original media URL through the website's usual media handler, including from remote
storage exposed through that handler. For a `.drawio` file it sends `Accept: application/xml`. Opening
that file's link in a browser launches the editor. The legacy `/draw/proxy` endpoint
redirects clients to the media URL for a bounded download. Its `base64=1` option selects a
Base64 response. The redirect uses Umbraco's configured media path under the site's application path.

A storage provider can keep files in S3 or another remote store and serve them through the
site's media URL. For a direct CDN URL, the published image can display and the backoffice can
read the source if the CDN permits the browser's cross-origin request. The standalone editor
and viewer require a same-origin source URL, including when a block uses **Open in Viewer**.

The examples use Umbraco's default installation at the site's URL root.

## Backoffice diagrams

The package installs the following definitions under **Settings**.
After installation you can rename, change or delete the definitions.

| Definition | Purpose |
|---|---|
| **Diagram** element type (`drawioDiagram`) | A block with a diagram, display format, caption, alternative text and an optional click action. |
| **Diagram** media type (`drawioDiagramMedia`) | Holds the original drawing (`drawioSource`), editable PNG (`umbracoFile`), SVG (`drawioSvg`) and clickable regions (`drawioMap`). |
| **Diagram picker** data type | Selects one Diagram media item and provides the **Create diagram** and **Edit diagram** buttons. |
| **Open in Viewer** | Uses Umbraco’s built-in **True/false** data type. The separate **Link** field uses the **Diagram destination** link picker. |

To use it:

1. Add the **Diagram** element type to a Block List or Block Grid data type.
2. In a page, add a Diagram block and select **Create diagram**.
3. Draw, then save in draw.io. The editor saves all three files on one media item through
   Umbraco's management API using your media permissions, then selects the item in the block.
4. Leave **Format** set to **PNG** or select **SVG**. Each block can display the same drawing
   in a different format. For a multi-page drawing, both images show the page open when you save.
   Choose **SVG** if individual words in a label need to be clickable or if the site's CSS
   needs to style the drawing itself.
5. To style this block, enter one or more space-separated names in **CSS class**.
   For example, `floor-plan wide` renders `<figure class="drawio-diagram floor-plan wide">`.

A new diagram gets a media item named after the page, or `diagram` if the page name is
unavailable. On a fresh database the upload folder starts at **Media → diagrams**.
You can rename it or move it anywhere in Media. The editor finds it by its GUID.
If the folder is missing, recycled or outside the editor's media access, the editor
tries the media root with the signed-in user's permissions.

**Edit diagram** opens the original `.drawio` file. Saving updates the source, both images
and their linked regions on the same media item. Every block that uses it picks up the edit.
You can also use the **Diagram picker** on your own content types.

For a slide deck or a local copy, save the PNG from the page or the **Editable PNG** field in
Media. The exported PNG carries the drawing and can be reopened in draw.io. An optimizer
that strips PNG metadata can remove editability from that downloaded copy. The backoffice
and viewer read the separate `.drawio` source. To keep downloaded PNGs editable, configure
your optimizer to preserve the original PNG.

### Protecting and restoring the upload folder

Umbraco blocks recycling or permanently deleting the diagram upload folder. To delete
a folder that contains it, move the diagram upload folder elsewhere first. You can
still delete individual diagrams.

If the upload folder is already in **Media → Recycle Bin**, restore it to the desired
location. If it has been permanently removed, an administrator can recreate it through
Umbraco's authenticated Management API. Send a `POST` to `/umbraco/management/api/v1/media` with this JSON body.
The `id` is the GUID the editor looks up.

```json
{
  "id": "3ff1e030-bba1-4f59-bde9-e5f9131be6f1",
  "parent": null,
  "mediaType": { "id": "f38bd2d7-65d0-48e6-95dc-87ce06ec2d3d" },
  "variants": [{ "name": "diagrams", "culture": null, "segment": null }],
  "values": []
}
```

## Viewer and click actions

`/draw/viewer?url=/media/example/plan.drawio` shows a file from the website read-only, with
zoom, page and layer controls. It supports the same website file formats as the
standalone editor.

**PNG** supports links on whole shapes, including standalone text objects. Select the
object in draw.io and use **Edit Link**. The block overlays the linked outlines on the
PNG and scales them with the image. A link on just a few words inside a label has no
separate clickable region in PNG.

**SVG** displays the exported drawing inline. Readers can follow a link on individual
words within a label. The site's stylesheet can also target elements inside the drawing.
For example, give the block the **CSS class** `floor-plan` and use
`.floor-plan svg rect { stroke: steelblue; }` to style its rectangles.

The browser loads and sanitizes the SVG from its media URL. Put custom styles and fonts
in the site stylesheet. Embedded stylesheets are omitted from the inline drawing. While it loads, or if
JavaScript is unavailable, the block shows the image with whole-shape links. Sites
serving SVGs from a separate media origin must allow the page to fetch them using CORS.

**Open in Viewer** and **Link** control what happens when someone selects the rest of
the diagram. Open the viewer to use draw.io actions such as switching pages or showing a layer.

| Open in Viewer | Link | Published result |
|---|---|---|
| Off | Empty | Only the drawing's own links are clickable. |
| Off | Set | Opens the selected content page, media item or URL using the link's target. |
| On | Any | Opens the diagram viewer in a new tab. |

The block renders a link only to a path on the website, an anchor, or an `http`, `https`,
`mailto` or `tel` address.

The package compiles three views: `Views/Partials/Drawio/Diagram.cshtml`, and
`drawioDiagram.cshtml` under `Views/Partials/blocklist/Components` and
`Views/Partials/blockgrid/Components`. A file at the same path in the site replaces one.
A copy of `Diagram.cshtml` reads the block with `DiagramFigure.From(Model, Context)`, which
also chooses the link, so the copy changes the markup alone.
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
| `Delete` | Deletes the media item and its files. |
| `RecycleBin` | Moves the media item to the media recycle bin. Its files stay in storage. |
| `Keep` | Leaves the media item in the library. |

Moving a page to the content recycle bin keeps its diagrams, and so does a version that
Umbraco keeps. Cleanup identifies an editor-created Diagram by its media type and its
source filename, `diagram-<media key>.drawio`. A diagram saved in the editor for a
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
| [Optional IIS virtual directory](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/IIS-DEPLOYMENT.md) | For sites that choose to have IIS serve the editor's static files directly. |
| [Third-party notices](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/THIRD-PARTY-NOTICES.md) | The draw.io release the Selfhosted package carries, and its license. |

## Developing

Building needs the .NET 10 SDK, which `global.json` selects. The Selfhosted project also
needs the `external/drawio` submodule, and the backoffice adapter tests need Node.js. They
use its built-in test runner and no npm packages.

```powershell
git submodule update --init --depth 1
dotnet build -c Release
# The backoffice media adapters
node --test "tests/backoffice/*.test.mjs"
# The tests on the newest supported Umbraco release
dotnet test --project tests/AurorisNet.Umbraco.Drawio.Tests -c Release
# The tests on another release
dotnet test --project tests/AurorisNet.Umbraco.Drawio.Tests -c Release -p:UmbracoVersion=17.0.0
# Both packages, numbered 0.0.0-dev, then a check of their contents and dependencies
dotnet pack src/AurorisNet.Umbraco.Drawio.csproj -c Release -o artifacts/packages
dotnet pack selfhosted/AurorisNet.Umbraco.Drawio.Selfhosted.csproj -c Release -o artifacts/packages
pwsh scripts/Test-Packages.ps1 -Version 0.0.0-dev
# Open and save a diagram with each installation in Google Chrome
pwsh scripts/Test-Editors.ps1 -Version 0.0.0-dev
```

The full test matrix is the test command run once for each release in `UmbracoReleases` in
`Directory.Build.props`. It runs on a developer's machine. The workflows test one release.
The workflows also turn build warnings into errors, so keep the build free of warnings.

The server selects the Umbraco 17 or 18 implementation of `src/Compatibility/IUmbracoApi.cs`
for content-history access. Both versions use the same backoffice media adapter. The server
and backoffice each check the supported major versions.

The editor tests install the packed NuGet files in two separate Umbraco sites and require
Google Chrome. The base-package test uses the live editor at `https://embed.diagrams.net/`
and needs internet access and a display (CI uses Xvfb). The self-hosted test allows the
browser to contact only the local site. Each test runs at the host root and under a nested
application path, edits a label, saves the diagram and reopens it to check the edit.

For an optional compatibility test, add `-WithApplicationVirtualPathFix` to the
`Test-Editors.ps1` command. The test installs `AurorisNet.Umbraco.ApplicationVirtualPathFix`
in the disposable test site. You can also select this option in a manual CI run.

After a change to the editor scripts, also check both editors by hand. Create, save, reopen
and publish a block, try each click action, and open a PNG, an SVG and a `.drawio` file in
the standalone editor and the viewer.

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
