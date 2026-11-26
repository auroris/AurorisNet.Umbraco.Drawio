# Usage and customization

## Settings example

An `Id` or `Name` allows up to 64 letters, digits, `-`, or `_` and must be unique. Every `Url` must be a same-site path; remote hosts are rejected.

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

### Customizing the self-hosted editor

`AurorisNet.Umbraco.Drawio.Selfhosted` copies draw.io into `DrawioDistribution` on build/publish. Set `Drawio:WebRoot` and `<DrawioBundle>false</DrawioBundle>` in your `.csproj` to serve your own copy instead. Missing editor folders return HTTP 503.

Customize `/draw/js/PreConfig.js` or `/draw/js/PostConfig.js` by adding:
- `wwwroot/App_Plugins/AurorisNet.Drawio/PreConfig.js` (runs before editor load; modify `window.DRAWIO_CONFIG`)
- `wwwroot/App_Plugins/AurorisNet.Drawio/PostConfig.js` (runs after editor load)

```javascript
window.DRAWIO_CONFIG = { ...window.DRAWIO_CONFIG, defaultGridSize: 20 };
```

For C#-generated script config, register `IDrawioScriptContributor`. Output order is package defaults → contributors → site file.

Hosted `diagrams.net` does not load plugins or script contributors, and requires CORS headers allowing `https://embed.diagrams.net` to fetch custom `Libraries`.

## Standalone editor (`/draw/editor`)

- Open `/draw/editor` for a blank diagram, or pass a same-origin file (up to 40 MiB) via `?url=/media/example/plan.drawio` (supports `.drawio`, `.xml`, editable PNG/SVG, and raster images).
- **Save** downloads a `.drawio` file. For browser storage, open `/draw/?browser=1`.
- Legacy `/draw/proxy` redirects to the configured media path (`base64=1` returns Base64).
- Direct CDN URLs work for published images and backoffice editing (with CORS), while the standalone editor and viewer require a same-origin URL.

## Backoffice diagrams

Installed under **Settings** (editable or removable after install; manual sites can call `builder.AddDrawio()`):

| Definition | Purpose |
|---|---|
| **Diagram** element type (`drawioDiagram`) | Block with diagram picker, format (`PNG`/`SVG`), caption, alt text, CSS class, **Open in Viewer**, and **Link**. |
| **Diagram** media type (`drawioDiagramMedia`) | Stores `.drawio` source (`drawioSource`), editable PNG (`umbracoFile`), SVG (`drawioSvg`), and clickable regions (`drawioMap`). |
| **Diagram picker** data type | Selects a Diagram media item and adds **Create diagram** / **Edit diagram** buttons. |
| **Diagram destination** link picker | Target link when **Open in Viewer** is off. |

- New diagrams save into **Media → diagrams** (located by GUID `3ff1e030-bba1-4f59-bde9-e5f9131be6f1`, falling back to media root if inaccessible).
- **PNG** embeds editable draw.io data (preserve PNG metadata if using an image optimizer) and overlays whole-shape links. **SVG** renders inline (sanitized, without embedded stylesheets) so links on individual words and site CSS (e.g. `.floor-plan svg rect`) work.
- Partial views: `Views/Partials/Drawio/Diagram.cshtml` plus `drawioDiagram.cshtml` under `Views/Partials/blocklist/Components` and `blockgrid/Components`. Override any view at the same path; `DiagramFigure.From(Model, Context)` builds the view model. Fallback alt text uses dictionary item `Drawio.OpenDiagram`.

### Restoring a permanently deleted upload folder

If **Media → diagrams** was permanently deleted (not just recycled), recreate it with its expected GUID via `POST /umbraco/management/api/v1/media`:

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

`/draw/viewer?url=/media/example/plan.drawio` renders a same-origin diagram read-only with zoom, page, and layer controls.

| Open in Viewer | Link | Published click result |
|---|---|---|
| Off | Empty | Only links drawn inside the diagram are clickable. |
| Off | Set | Opens the selected page, media item, or URL (`http`, `https`, `mailto`, `tel`, anchor, or site path). |
| On | Any | Opens `/draw/viewer` in a new tab. |

## Media retention

A **Retained diagram media** relation tracks editor-created diagrams (`diagram-<media key>.drawio`) across drafts, published content, and kept versions (including nested blocks). When Umbraco deletes old versions or permanently deletes a page, diagrams with no remaining relations are handled according to `Drawio:SupersededImages` (`Delete`, `RecycleBin`, or `Keep`). Recycling a page preserves its diagrams.
