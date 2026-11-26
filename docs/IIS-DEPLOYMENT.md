# Optional IIS virtual directory

For a standard IIS deployment, install the Selfhosted package and publish the Umbraco site
as usual. Umbraco serves the bundled editor and the site's optional configuration scripts.
See [Customizing the self-hosted editor](usage.md#customizing-the-self-hosted-editor).

Follow the steps below only if you want IIS to serve the static files from a separate
virtual directory at `/draw`. IIS
answers the app's static files, and the Umbraco application answers the configuration
scripts, the settings, the editor and viewer pages, the media proxy and `.drawio` media
links. [Install](../README.md#install) in the README
lists the other ways to serve the editor.

## Set up the virtual directory

1. Copy the draw.io web app into a folder of its own, such as `C:\inetpub\drawio`, with
   `index.html` and `js/app.min.js` at its top level. The `DrawioDistribution` folder in the
   output of a site that references the Selfhosted package holds the release that package
   carries.
2. Under the IIS site, add a **virtual directory** with the alias `Draw` that points to the
   folder. It shares the site's application and application pool. Give the pool's identity
   read access to the folder.
3. Add the `Draw` location below to the site's `web.config`, beside the `location` that holds
   the site's own `system.webServer` section.
4. Let Umbraco handle diagram downloads under the site’s configured media path, as described below.
5. Set `Drawio:WebRoot` to the folder from step 1. With the Selfhosted package installed,
   also set `DrawioBundle` to `false` in the site's project, which leaves the bundled copy
   out of the build.

The location sends six paths to the application ahead of IIS's static file handler, and
hides the servlet folders and source maps of the draw.io source:

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

The site's normal ASP.NET Core handler should receive diagram requests under Umbraco's
configured media path (by default, `/media/`) after the site's media access checks. This
includes `.drawio`, `.xml`, `.svg` and `.png`.
Umbraco redirects a navigated `.drawio` file to the editor and serves the original file
when the editor requests `application/xml`. Remove the earlier IIS rule named **Open draw.io
media files in the editor** on upgrade. It also redirects the editor's raw download.

## Upgrade

To upgrade draw.io, point the virtual directory to a folder with the new release. To change
libraries, plugins or other settings, deploy the site. The application writes `PreConfig.js`
and `PostConfig.js` for each request, so the copies in the virtual directory stay unused.

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
`X-Drawio-Host: AurorisNet.Umbraco.Drawio`. A configuration script that returns 200 without
that header came from the copy in the virtual directory. In that case, check the `Draw`
location in the deployed `web.config`, and check that IIS lists `/Draw` as a virtual
directory and not as an application.

Then check the editor in a browser:

1. Open a page with a diagram and select it. The viewer opens in a new tab.
2. Open the link to a `.drawio` media file. The editor loads it.
3. In the browser's network panel, check that the editor's request to the original media
   URL returns the diagram to a visitor who can read that file.
