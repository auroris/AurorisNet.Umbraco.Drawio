# Security

## Report a vulnerability

Report a vulnerability in private, with
[GitHub's vulnerability report form](https://github.com/auroris/AurorisNet.Umbraco.Drawio/security/advisories/new).
You also reach it from this repository's **Security** tab, under **Report a vulnerability**.
Please keep the details out of public issues until a fix is published.

A report is easier to act on when it includes:

- the package version and the site's Umbraco release;
- the steps that show the problem, and what someone gains by it.

## Versions that get fixes

A fix is published as a new package version. Earlier package versions stay as they are, so
install the newest version.

## What the package covers

The package serves `/draw/` and the media proxy `/draw/proxy`, redirects `.drawio` media links,
adds standalone editing and viewing, a backoffice property editor and block previews, and removes
unreferenced diagram media after content history or pages are permanently deleted.
A vulnerability in any of that belongs here, including a way to read a file outside the media
store through the proxy or to load a script from another site into the editor.

The bundled draw.io web app is JGraph's. For a vulnerability in draw.io itself, follow
[draw.io's security policy](https://github.com/jgraph/drawio/security/policy); the package
publishes a new version once a fixed draw.io release is available. For a vulnerability in
Umbraco, follow [Umbraco's security policy](https://github.com/umbraco/Umbraco-CMS/security/policy).
