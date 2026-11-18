# Security

## Report a vulnerability

Report a vulnerability in private, with
[GitHub's vulnerability report form](https://github.com/auroris/AurorisNet.Umbraco.Drawio/security/advisories/new).
You also reach it from this repository's **Security** tab, under **Report a vulnerability**.
Please keep the details out of public issues until a fix is published.

A report is easier to act on when it includes:

- the package version and the site's Umbraco release;
- whether the site uses hosted diagrams.net or a self-hosted editor;
- the steps that show the problem, and what someone gains by it.

## Versions that get fixes

A fix is published as a new package version. Earlier versions stay as they are, so install
the newest version.

## What the package covers

The package serves the editor and viewer under `/draw/`, redirects `.drawio` media links to
the editor, adds a backoffice property editor and block previews, and removes diagram media
that no content refers to after versions or pages are deleted. A vulnerability in any of
that belongs here. Examples are a way to read a file that the website keeps from the
visitor, or to load a script from another site into the editor.

Some access is by design:

- `/draw/editor`, `/draw/viewer` and `/draw/settings` answer without a sign-in. The editor
  and viewer read a file from its own address on the website, so they show a visitor only
  what that address already gives them, and `/draw/proxy` only redirects to that address.
  The settings name the editor's address, the configured libraries and the Umbraco major
  version.
- The scripts in `Drawio:Plugins`, and script from an `IDrawioScriptContributor`, run in the
  self-hosted editor's page with the signed-in editor's access to the site. Anyone who can
  write the site's configuration or code chooses them.

The Selfhosted package carries JGraph's draw.io web app. For a vulnerability in draw.io
itself, follow [draw.io's security policy](https://github.com/jgraph/drawio/security/policy);
this package publishes a new version once a fixed draw.io release is available. For a
vulnerability in Umbraco, follow
[Umbraco's security policy](https://github.com/umbraco/Umbraco-CMS/security/policy).
