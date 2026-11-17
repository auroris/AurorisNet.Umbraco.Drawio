# draw.io for Umbraco

Create, edit and view [draw.io](https://www.drawio.com) diagrams on an Umbraco 17 or 18 website.
Backoffice diagrams are PNGs containing their editable drawings. Published blocks display the
image and can open a viewer, follow an Umbraco link, or remain unlinked.

## Packages

| Package | Editor source |
|---|---|
| `AurorisNet.Umbraco.Drawio` | Hosted diagrams.net. `/draw/` opens `https://app.diagrams.net/`; embedded editors and viewers use `https://embed.diagrams.net/`. |
| `AurorisNet.Umbraco.Drawio.Selfhosted` | Depends on the matching base package, bundles draw.io for local hosting and automatically selects the local editor for all three modes. |

Both target .NET 10 and Umbraco `[17.0.0, 19.0.0)`. Use a currently patched Umbraco 17 or 18 release.
Umbraco 19 uses .NET 11 and is outside this package's supported range.
The base package contains no draw.io distribution. Selfhosted carries draw.io 31.4.5, including
its JavaScript, images, stencils, translations and notices, and copies it into
`DrawioDistribution` on build and publish.

Install one of these:

```xml
<PackageReference Include="AurorisNet.Umbraco.Drawio" Version="1.*" />
<!-- Or, to serve the editor locally: -->
<PackageReference Include="AurorisNet.Umbraco.Drawio.Selfhosted" Version="1.*" />
```

Umbraco discovers the composers automatically. A manually composed site calls
`builder.AddDrawio()` and includes the Selfhosted composer when using that package.
Hosted mode needs browser access to diagrams.net and a Content Security Policy allowing frames
from `https://embed.diagrams.net`. Diagram data is passed to that editor in the browser;
backoffice media is stored by the Umbraco website.

## Three modes

All paths include the site's path base when deployed under an IIS application.

### Standalone editor

Open `/draw/editor?url=/media/example/plan.drawio` to edit a website diagram without a backoffice
session. Omit `url` to start a blank diagram. Save downloads a `.drawio` file; it does not write
back to the website. `/draw/` opens the full draw.io application.

Sources can be `.drawio`, draw.io XML, editable PNG or editable SVG. Ordinary PNG, SVG, JPEG,
GIF and WebP images open as a single picture; their shapes cannot be recovered for editing.
Source URLs must be on this website and files at most 40 MiB. Site-relative Umbraco media is read
through the package's media proxy. External URLs, including separate CDN hosts, are not supported
as standalone/viewer sources.

### Backoffice integration

On first installation the package creates these definitions in the **Diagrams** folder:

- **Diagram (editable PNG)**: a single-image media picker with Create/Edit diagram buttons.
- **Diagram** (`drawioDiagram`): image, caption, alternative text, click action and destination link.
- **Diagram click action** and **Diagram destination** data types.

Add the Diagram element type to a Block List or Block Grid data type. Create a block, draw and
save. The editor uploads an editable PNG through Umbraco's authenticated media API and selects
it in the block. Each save creates a new media item beside the previous one, so changing a draft
does not replace the image on the published page. Standard content and media permissions apply.

The Diagram (editable PNG) data type can also be used on your own content types. Keep the
original PNG bytes intact: image optimisation that strips drawing metadata prevents reopening
it. The editor always reads the original file.

### Read-only viewer and block links

Open `/draw/viewer?url=/media/example/plan.drawio` to view a website diagram. It supports the
same source formats as the standalone editor, with zoom, pages, layers and diagram links.

New blocks explicitly select **Open diagram viewer**:

| Click action | Published result |
|---|---|
| Open diagram viewer | Opens the read-only viewer in a new tab. |
| Follow link below | Uses Umbraco's link picker for a content page, media or URL, including the link's target. Clearing the link produces an unlinked image. |
| No link | Shows just the image and optional caption. |

A missing or unknown click-action value produces an unlinked image.

## Media retention

A **Retained diagram media** relation connects a page to each package-created diagram needed
by its current draft, published content or retained history. Umbraco's reference readers handle
nested blocks and diagrams reused in other supported property editors. Relations use Umbraco's
existing table; there is no separate version ledger or cleanup schedule.

When Umbraco removes historical versions, the package reconciles the affected pages' relations
against the remaining versions. After permanent page deletion it checks that page's diagrams,
including those referenced by descendants. A diagram becomes eligible only after successful
deletion and when no retention or other incoming relation remains.

Moving a page to the content recycle bin preserves its diagrams. Protected historical versions
also retain them. By default, eligible media items and underlying files are permanently deleted
through Umbraco's media service.

Cleanup applies to editor-created PNGs named `diagram-<media key>.png`. Ordinary uploads are
left alone. A diagram uploaded during an edit abandoned before saving the page has no page
relation and is outside this lifecycle cleanup.

`Drawio:SupersededImages` controls removal: `Delete` (default), `RecycleBin` or `Keep`. All retain
references until the relevant history is removed. Recycling keeps media recoverable and does
not reclaim its file storage. Failed or cancelled cleanup is logged; it does not undo an already
committed content deletion.

## Settings and local extensions

The optional `Drawio` configuration section supports:

| Setting | Default | Purpose |
|---|---|---|
| `WebRoot` | empty | Local draw.io web-app folder, absolute or relative to the content root. Setting it selects self-hosting with either package. |
| `RedirectMediaFiles` | `true` | Redirect `.drawio` media links into the standalone editor. |
| `SupersededImages` | `Delete` | Delete, recycle or keep media once its final retained reference disappears. |
| `DefaultLibraries` | empty | Semicolon-separated shape libraries; empty keeps draw.io's defaults. |
| `LibraryGroup` | `Extra shapes` | Sidebar heading for configured libraries. |
| `Libraries` | empty | Objects with `Id`, `Title`, optional `Description`, and site-relative `Url`. |
| `Plugins` | empty | Local-editor plugins with `Name` and site-relative `Url`. |

Selfhosted selects `DrawioDistribution` when `WebRoot` is empty. A missing local distribution
returns 503 and never falls back to hosted diagrams.net. To use a separate folder without
copying the bundle, set `WebRoot` and `<DrawioBundle>false</DrawioBundle>` in the site's project.
[IIS deployment](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/IIS-DEPLOYMENT.md)
also covers serving the assets from a virtual directory.

Local `PreConfig.js` and `PostConfig.js` disable cloud storage, remote exports and icon search.
Configured plugins must be trusted site scripts. `IDrawioScriptContributor` can append script
to either configuration stage. Plugins and script contributors require self-hosting. Custom
libraries in hosted mode must be reachable by the hosted frame with appropriate CORS headers.

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

The package compiles `Views/Partials/Drawio/Diagram.cshtml` and the corresponding
`blocklist/Components/drawioDiagram.cshtml` and `blockgrid/Components/drawioDiagram.cshtml`
partials. Override them by placing files at the same paths in the site. The shared partial
accepts an `IPublishedElement` with the block's properties. Alternative text falls back to the
caption, then the `Drawio.OpenDiagram` dictionary item ("Open diagram" when untranslated).

## Developing and releasing

The base package builds without the draw.io submodule. Building Selfhosted needs the pinned
`external/drawio` submodule. Keep its original line endings when checking it out. The backoffice
adapter tests use Node.js 22 or later and its built-in test runner; no npm packages are needed.

```powershell
git submodule update --init --depth 1
dotnet build -c Release
node --test tests/backoffice/media-api.test.mjs
dotnet test --project tests/AurorisNet.Umbraco.Drawio.Tests -c Release -p:UmbracoVersion=17.0.0
dotnet test --project tests/AurorisNet.Umbraco.Drawio.Tests -c Release
dotnet pack src/AurorisNet.Umbraco.Drawio.csproj -c Release -o artifacts/packages
dotnet pack selfhosted/AurorisNet.Umbraco.Drawio.Selfhosted.csproj -c Release -o artifacts/packages
```

CI runs the full suite against Umbraco 17.0.0 and 18.3.1 on Windows and Linux, tests the backoffice
media adapters with Node.js, and packs both packages.
The Release workflow tests both versions, uses one package version
for both packages and publishes the base first. **dry-run** keeps both `.nupkg` files without
tagging or publishing. NuGet trusted publishing must authorise both package IDs for owner
`auroris`, repository `AurorisNet.Umbraco.Drawio`, workflow `release.yml`, under the `AurorisNet`
NuGet account.

`Compatibility/IUmbracoApi` isolates the CMS operations used by the package: content history,
relations, media removal and file access, and published block data. The running CMS major version
selects `Umbraco17Api` or `Umbraco18Api`. Backoffice editing and previews likewise use a selected
media adapter for reads, uploads, image creation and picker values. Shared calls stay in common
implementations; version-specific differences belong in the corresponding adapter. Notification,
migration and Razor entry points remain thin CMS integration points. A new CMS major requires an
explicit adapter and tests; it never silently uses the preceding version's implementation.

Before publishing, check hosted and local editing in a browser: create, save, reopen and publish
a block; exercise every destination; open editable PNG, SVG and `.drawio` sources in standalone
editing and viewing. Check a published site's output as well as project references. When updating
draw.io, update its submodule and
[third-party notices](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/THIRD-PARTY-NOTICES.md).

## License

The integration is [MIT](https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/LICENSE).
Selfhosted includes draw.io under Apache License 2.0, its `DRAWIO-LICENSE.txt` and upstream notices.
