# Third-party notices

Both packages' own code is under the [MIT license](LICENSE).

## DOMPurify

The base package uses DOMPurify 3.4.18 by Cure53 and contributors to sanitize inline diagrams.
The unmodified ES module and its Apache 2.0 license ship in
`App_Plugins/AurorisNet.Drawio/vendor/dompurify/`. [DOMPurify release](https://github.com/cure53/DOMPurify/tree/3.4.18)

## draw.io

- **What:** the draw.io web app, the `src/main/webapp` folder of
  [jgraph/drawio](https://github.com/jgraph/drawio), without its `WEB-INF` and `META-INF` folders
  and source maps. The `external/drawio` submodule pins the release; this package version carries
  draw.io 31.4.5, commit `f3abfe0f082c18f7b4fee8a34c2d07b1987687fd`.
- **Where:** `drawio/` in the Selfhosted NuGet package, and `DrawioDistribution/` in a site's output.
- **Copyright:** © JGraph Ltd and draw.io contributors.
- **Licence:** [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0). The full text ships
  beside the web app as `DRAWIO-LICENSE.txt`.
- **Changes:** none. The package serves the files as they are, and answers for `js/PreConfig.js` and
  `js/PostConfig.js` itself instead of the stock copies, which the web app provides for that purpose.

The draw.io web app in turn includes third-party libraries, such as mxGraph, MathJax, Mermaid and
JSZip, each under its own licence. Those notices stay in the files where draw.io keeps them, and
draw.io's repository lists them.

## Libraries and plugins a site adds

A site that adds icon libraries or draw.io plugins through `Drawio:Libraries` or `Drawio:Plugins`
serves those files itself and keeps their notices, such as an icon set's licence.
