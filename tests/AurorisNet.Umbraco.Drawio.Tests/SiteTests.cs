using System.Net;
using System.Text;
using System.Text.Json;
using Drawio;
using Drawio.Migrations;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Manifest;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Core.Packaging;

namespace AurorisNet.Umbraco.Drawio.Tests;

/// <summary>One site for the class's tests, which xunit runs one at a time.</summary>
public sealed class SiteFixture : IAsyncLifetime
{
    internal TestSite Site { get; private set; } = null!;
    internal HttpClient Http { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Site = await TestSite.StartAsync(new Dictionary<string, string?>
        {
            ["Drawio:WebRoot"] = "draw",
            ["Drawio:Libraries:0:Id"] = "site-shapes",
            ["Drawio:Libraries:0:Title"] = "Site shapes",
            ["Drawio:Libraries:0:Url"] = "/App_Plugins/Site/shapes.xml",
            ["Drawio:Plugins:0:Name"] = "siteplugin",
            ["Drawio:Plugins:0:Url"] = "/App_Plugins/Site/plugin.js",
        }, TestContext.Current.CancellationToken);
        Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = Site.Address };
    }

    public async ValueTask DisposeAsync()
    {
        Http?.Dispose();
        if (Site is not null) await Site.DisposeAsync();
    }
}

/// <summary>Registers a script contributor through a composer, as a site's own layer would.</summary>
public sealed class TestContributorComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.Services.AddSingleton<IDrawioScriptContributor, TestContributor>();

    private sealed class TestContributor : IDrawioScriptContributor
    {
        public string? PreConfig(DrawioScriptContext context) => $"window.SITE_CONFIG={{dataUrl:{DrawioScriptContext.Literal(context.SitePath + "/data/")}}};";
    }
}

/// <summary>A composed site: the installed schema, the editor, the media proxy and redirect, and the cleanup.</summary>
public sealed partial class SiteTests(SiteFixture fixture) : IClassFixture<SiteFixture>
{
    private TestSite Site => fixture.Site;
    private HttpClient Http => fixture.Http;
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Umbraco's package migration installs the data type and element type once, through its package importer.</summary>
    [Fact]
    public async Task The_package_migration_installs_the_diagram_element_type()
    {
        var plans = Site.Services.GetRequiredService<PackageMigrationPlanCollection>();
        Assert.Single(plans.OfType<DrawioMigrationPlan>());
        var state = Site.Services.GetRequiredService<IKeyValueService>().FindByKeyPrefix(Constants.Conventions.Migrations.KeyValuePrefix);
        Assert.DoesNotContain(DrawioMigrationPlan.Identifier, Site.Services.GetRequiredService<PendingPackageMigrations>().GetPendingPackageMigrations(state));

        var folder = Assert.Single(Site.Services.GetRequiredService<IMediaService>().GetRootMedia(), item => item.Name == "drawio");
        Assert.Equal(Constants.Conventions.MediaTypes.Folder, folder.ContentType.Alias);

        var dataType = await Site.Services.GetRequiredService<IDataTypeService>().GetAsync(ImportDiagramElementType.DataTypeKey);
        Assert.NotNull(dataType);
        Assert.Equal(Constants.PropertyEditors.Aliases.MediaPicker3, dataType.EditorAlias);
        Assert.Equal("AurorisNet.Drawio.Editor", dataType.EditorUiAlias);
        Assert.Equal("cc07b313-0843-4aa8-bbda-871c8da728c8", dataType.ConfigurationData["filter"]?.ToString());

        var element = Site.Services.GetRequiredService<IContentTypeService>().Get(ImportDiagramElementType.ElementTypeKey);
        Assert.NotNull(element);
        Assert.True(element.IsElement);
        Assert.Equal(ImportDiagramElementType.ElementTypeAlias, element.Alias);
        Assert.Equal(dataType.Id, element.PropertyTypes.Single(p => p.Alias == "image").DataTypeId);
        Assert.Equal(Constants.PropertyEditors.Aliases.TextBox, element.PropertyTypes.Single(p => p.Alias == "caption").PropertyEditorAlias);
        Assert.Equal(Constants.PropertyEditors.Aliases.TextBox, element.PropertyTypes.Single(p => p.Alias == "altText").PropertyEditorAlias);
        var viewer = element.PropertyTypes.Single(p => p.Alias == "openInViewer");
        Assert.Equal("Umbraco.TrueFalse", viewer.PropertyEditorAlias);
        Assert.Equal("Umb.PropertyEditorUi.Toggle", (await Site.Services.GetRequiredService<IDataTypeService>().GetAsync(viewer.DataTypeKey))?.EditorUiAlias);
        Assert.Equal("Umbraco.MultiUrlPicker", element.PropertyTypes.Single(p => p.Alias == "link").PropertyEditorAlias);
        Assert.NotNull(Site.Services.GetRequiredService<IRelationService>().GetRelationTypeByAlias(DiagramMediaRetention.RelationAlias));
    }

    /// <summary>The backoffice finds the package's manifest, and the block views are compiled into the package.</summary>
    [Fact]
    public async Task The_manifest_and_views_are_registered()
    {
        var manifests = await Site.Services.GetRequiredService<IPackageManifestService>().GetAllPackageManifestsAsync();
        Assert.Contains(manifests, manifest => manifest.Id == DrawioMigrationPlan.Identifier);
        using var response = await Http.GetAsync(DrawioPaths.Plugin + "/editor.js", Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var views = new ViewsFeature();
        Site.Services.GetRequiredService<ApplicationPartManager>().PopulateFeature(views);
        var paths = views.ViewDescriptors.Select(view => view.RelativePath).ToArray();
        Assert.Contains("/Views/Partials/Drawio/Diagram.cshtml", paths);
        Assert.Contains("/Views/Partials/blockgrid/Components/drawioDiagram.cshtml", paths);
        Assert.Contains("/Views/Partials/blocklist/Components/drawioDiagram.cshtml", paths);
    }

    /// <summary>The editor's files come from Drawio:WebRoot, with the header that tells an IIS operator where they came from.</summary>
    [Fact]
    public async Task The_editor_is_served_from_the_web_root()
    {
        using (var folder = await Http.GetAsync("/draw?lang=fr", Cancel))
        {
            Assert.Equal(HttpStatusCode.Redirect, folder.StatusCode);
            Assert.Equal("/draw/?lang=fr", folder.Headers.Location!.OriginalString);
        }
        using (var index = await Http.GetAsync("/draw/", Cancel))
        {
            Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            Assert.Contains("<title>draw.io</title>", await index.Content.ReadAsStringAsync(Cancel));
            Assert.Equal(DrawioPaths.HostHeader, Assert.Single(index.Headers.GetValues("X-Drawio-Host")));
        }
        using (var app = await Http.GetAsync("/draw/js/app.min.js", Cancel))
            Assert.Equal("var App={};", await app.Content.ReadAsStringAsync(Cancel));

        foreach (var blocked in new[] { "/draw/js/app.min.js.map", "/draw/WEB-INF/web.xml", "/draw/web-inf/web.xml", "/draw/.gitignore", "/draw/missing.js" })
        {
            using var response = await Http.GetAsync(blocked, Cancel);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, blocked);
            Assert.Equal(DrawioPaths.HostHeader, Assert.Single(response.Headers.GetValues("X-Drawio-Host")));
        }
        using var post = await Http.PostAsync("/draw/", new StringContent(""), Cancel);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    /// <summary>The application answers for the configuration scripts, never the web app's stock copies.</summary>
    [Fact]
    public async Task The_configuration_scripts_carry_the_settings_and_contributions()
    {
        using var pre = await Http.GetAsync("/draw/js/PreConfig.js", Cancel);
        Assert.Equal(HttpStatusCode.OK, pre.StatusCode);
        Assert.Equal("text/javascript", pre.Content.Headers.ContentType!.MediaType);
        Assert.Equal(DrawioPaths.HostHeader, Assert.Single(pre.Headers.GetValues("X-Drawio-Host")));
        var script = await pre.Content.ReadAsStringAsync(Cancel);
        Assert.DoesNotContain("// stock", script);
        Assert.Contains("window.DRAWIO_PUBLIC_BUILD=true;", script);
        Assert.Contains("\"defaultLibraries\":\"general;uml;er;bpmn;flowchart;basic;arrows2;site-shapes\"", script);
        Assert.Contains("\"url\":\"/App_Plugins/Site/shapes.xml\"", script);
        Assert.Contains("window.SITE_CONFIG={dataUrl:\"/data/\"};", script);

        using var post = await Http.GetAsync("/draw/js/postconfig.js", Cancel);
        var postScript = await post.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("App.pluginRegistry[\"siteplugin\"]=\"/App_Plugins/Site/plugin.js\";App.loadPlugins([\"siteplugin\"]);", postScript);

        using var head = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/draw/js/PreConfig.js"), Cancel);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
    }

    /// <summary>The proxy returns media files as bytes or Base64, and nothing outside the media store.</summary>
    [Fact]
    public async Task The_proxy_reads_media_files()
    {
        var media = Site.Services.GetRequiredService<MediaFileManager>().FileSystem;
        var bytes = Encoding.UTF8.GetBytes("<mxfile><diagram>proxy</diagram></mxfile>");
        media.AddFile("proxytest/plan.drawio", new MemoryStream(bytes), overrideIfExists: true);

        using (var raw = await Http.GetAsync("/draw/proxy?url=media/proxytest/plan.drawio", Cancel))
        {
            Assert.Equal(HttpStatusCode.OK, raw.StatusCode);
            Assert.Equal(bytes, await raw.Content.ReadAsByteArrayAsync(Cancel));
            Assert.Equal("nosniff", Assert.Single(raw.Headers.GetValues("X-Content-Type-Options")));
        }
        using (var encoded = await Http.GetAsync("/draw/proxy?url=media/proxytest/plan.drawio&base64=1", Cancel))
        {
            var text = await encoded.Content.ReadAsStringAsync(Cancel);
            Assert.Equal(Convert.ToBase64String(bytes), text);
            Assert.Equal(text.Length, encoded.Content.Headers.ContentLength);
        }
        foreach (var (url, status) in new[]
        {
            ("media/proxytest/missing.drawio", HttpStatusCode.NotFound),
            ("media/../appsettings.json", HttpStatusCode.BadRequest),
            ("media/proxytest/../../umbraco.db.png", HttpStatusCode.BadRequest),
            ("https://example.com/media/a.png", HttpStatusCode.BadRequest),
            ("media/proxytest/plan.txt", HttpStatusCode.BadRequest),
        })
        {
            using var response = await Http.GetAsync("/draw/proxy?url=" + Uri.EscapeDataString(url), Cancel);
            Assert.True(response.StatusCode == status, $"{url}: {response.StatusCode}");
        }
    }

    /// <summary>A link to a .drawio media file opens it in the editor.</summary>
    [Fact]
    public async Task Drawio_media_links_open_the_editor()
    {
        using var response = await Http.GetAsync("/media/proxytest/plan.drawio", Cancel);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/draw/editor?url=%2Fmedia%2Fproxytest%2Fplan.drawio", response.Headers.Location!.OriginalString);
    }

    // A 1×1 PNG, so the image cropper's size detection has a real file to read.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
}
