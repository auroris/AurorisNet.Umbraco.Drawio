# IIS virtual directory

These steps let IIS serve the draw.io web app from a virtual directory at `/draw`, while the
Umbraco application answers the configuration scripts, settings, editor/viewer wrappers, media proxy and `.drawio` media
links. The [README](https://github.com/auroris/AurorisNet.Umbraco.Drawio#settings-and-local-extensions) compares
this setup with the bundled copy and a folder.

## Set up the virtual directory

1. Copy the draw.io web app into its own folder, such as `C:\inetpub\drawio`, with `index.html`
   and `js/app.min.js` at its top level. A site's `DrawioDistribution` output folder holds the
   release the package was tested with.
2. Under the IIS site, add a **virtual directory** with the alias `Draw` that points to that
   folder. It shares the site's application and application pool. Give the pool's identity read
   access to the folder.
3. Add the `Draw` location below to the site's `web.config`, beside the `location` that holds the
   site's own `system.webServer` section, and add the URL Rewrite rule to that section's `rules`.
4. Set `Drawio:WebRoot` to the folder from step 1. If the Selfhosted package is installed,
   set `DrawioBundle` to `false` in the site's project to omit its bundled copy.

The location sends the configuration scripts, settings, editor/viewer wrappers and proxy to the application ahead of IIS's
static file handler, and hides the servlet folders and source maps in the draw.io source:

```xml
<!-- /Draw is an IIS virtual directory holding the draw.io web app. Its files are static; these
     requests belong to the Umbraco application. -->
<location path="Draw" inheritInChildApplications="false">
  <system.webServer>
    <handlers>
      <add name="DrawioPreConfig" path="js/PreConfig.js" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      <add name="DrawioPostConfig" path="js/PostConfig.js" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      <add name="DrawioMediaProxy" path="proxy" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      <add name="DrawioSettings" path="settings" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      <add name="DrawioEditor" path="editor" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      <add name="DrawioViewer" path="viewer" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      <add name="DrawioStatic" path="*" verb="GET,HEAD" modules="StaticFileModule,DefaultDocumentModule" resourceType="Either" requireAccess="Read" />
    </handlers>
    <defaultDocument enabled="true">
      <files>
        <remove value="index.html" />
        <add value="index.html" />
      </files>
    </defaultDocument>
    <security>
      <requestFiltering>
        <hiddenSegments>
          <add segment="WEB-INF" />
          <add segment="META-INF" />
        </hiddenSegments>
        <fileExtensions>
          <remove fileExtension=".map" />
          <add fileExtension=".map" allowed="false" />
        </fileExtensions>
      </requestFiltering>
    </security>
  </system.webServer>
</location>
```

The application redirects `/media/…/*.drawio` into the editor. IIS answers some media requests
before they reach the application, so URL Rewrite needs the same redirect:

```xml
<rule name="Open draw.io media files in the editor" stopProcessing="true">
  <match url="^(media/.+\.drawio)$" />
  <action type="Redirect" url="/draw/editor?url=/{UrlEncode:{R:1}}" redirectType="Found" />
</rule>
```

Under an IIS application with its own virtual path, such as `/intranet`, prefix the redirect URL
with that path.

## Upgrade

To upgrade draw.io, point the virtual directory to a folder with the new release. To change
libraries, plugins or other settings, deploy the site. The application generates `PreConfig.js`
and `PostConfig.js` for each request, so the stock copies in the virtual directory stay unused.

## Check a deployment

Run these from a client that trusts the site's certificate:

```powershell
$site = 'https://intranet.example.org'
curl.exe -sSI "$site/draw/"
curl.exe -sSI "$site/draw/js/app.min.js"
curl.exe -sSI "$site/draw/js/PreConfig.js"
curl.exe -sSI "$site/draw/js/PostConfig.js"
```

All four return **200**, and the two configuration scripts carry
`X-Drawio-Host: AurorisNet.Umbraco.Drawio`. A configuration script that returns 200 without that
header came from the virtual directory's stock copy. Check the `Draw` location in the deployed
`web.config`, and check that IIS maps `/Draw` as a virtual directory rather than an application.

Then open a page with a diagram and select it, and the viewer opens in a new tab. Open the link to
a `.drawio` media file, and the editor loads it. In the browser's network panel, the editor's
request to `/draw/proxy?url=media/…` returns 200 with the same header.
