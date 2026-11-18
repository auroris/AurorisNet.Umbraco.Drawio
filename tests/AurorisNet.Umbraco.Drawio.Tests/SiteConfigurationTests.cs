using System.Net;
using System.Text;
using System.Text.Json;
using Drawio;
using Microsoft.AspNetCore.WebUtilities;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Install;

namespace AurorisNet.Umbraco.Drawio.Tests;

// Umbraco also keeps process-wide state. Give each of these configurations its own site lifetime.
[CollectionDefinition(DisableParallelization = true)]
public sealed class IsolatedSites;

[Collection<IsolatedSites>]
public sealed class SiteConfigurationTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task The_site_controls_package_migrations(bool unattended, bool installSchema)
    {
        await using var site = await TestSite.StartAsync(new Dictionary<string, string?>
        {
            ["Umbraco:CMS:Unattended:PackageMigrationsUnattended"] = unattended.ToString(),
            ["Umbraco:CMS:PackageMigration:RunSchemaAndContentMigrations"] = installSchema.ToString(),
        }, Cancel);
        var types = site.Services.GetRequiredService<IMediaTypeService>();
        var media = site.Services.GetRequiredService<IMediaService>();
        var relations = site.Services.GetRequiredService<IRelationService>();
        Assert.Null(await types.GetAsync(DrawioInstallation.MediaTypeKey));
        Assert.Null(media.GetById(DiagramMediaFolder.Key));
        Assert.Null(relations.GetRelationTypeByAlias(DiagramMediaRetention.RelationAlias));
        if (unattended) return;

        var runner = site.Services.GetRequiredService<PackageMigrationRunner>();
        var plan = Assert.Single(await runner.RunPackageMigrationsIfPendingAsync(DrawioMigrationPlan.DisplayName));
        Assert.True(plan.Successful);
        Assert.NotNull(await types.GetAsync(DrawioInstallation.MediaTypeKey));
        Assert.NotNull(media.GetById(DiagramMediaFolder.Key));
        Assert.NotNull(relations.GetRelationTypeByAlias(DiagramMediaRetention.RelationAlias));
    }

    [Fact]
    public async Task Media_redirects_and_proxy_downloads_use_the_configured_media_path()
    {
        const string folder = "assets/site media";
        const string file = "floor/plan + 1.drawio";
        await using var site = await TestSite.StartAsync(new Dictionary<string, string?>
        {
            ["Umbraco:CMS:Global:UmbracoMediaPath"] = "~/" + folder,
        }, Cancel, new PathString("/intranet"));
        var files = site.Services.GetRequiredService<MediaFileManager>().FileSystem;
        var bytes = Encoding.UTF8.GetBytes("<mxfile/>");
        files.AddFile(file, new MemoryStream(bytes), true);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = site.Address };
        var source = new Uri(site.Address, new PathString("/" + folder + "/" + file).ToUriComponent().TrimStart('/'));
        using var navigation = await client.GetAsync(source, Cancel);
        Assert.Equal(HttpStatusCode.Redirect, navigation.StatusCode);
        var editor = new Uri(site.Address, navigation.Headers.Location!);
        Assert.Equal("/intranet/draw/editor", editor.AbsolutePath);
        Assert.Equal(source.AbsolutePath, QueryHelpers.ParseQuery(editor.Query)["url"]);

        using var xml = new HttpRequestMessage(HttpMethod.Get, source);
        xml.Headers.Accept.ParseAdd("application/xml");
        using var original = await client.SendAsync(xml, Cancel);
        Assert.Equal(bytes, await original.Content.ReadAsByteArrayAsync(Cancel));
        foreach (var prefix in new[] { "assets/site%20media/", "media/" })
        {
            var proxy = QueryHelpers.AddQueryString("draw/proxy?base64=1", "url", prefix + file);
            using var response = await client.GetAsync(proxy, Cancel);
            Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
            var target = new Uri(site.Address, response.Headers.Location!);
            Assert.Equal(source.AbsolutePath, target.AbsolutePath);
            Assert.Equal(Convert.ToBase64String(bytes), await client.GetStringAsync(target, Cancel));
        }
        using var obsoletePath = await client.GetAsync("media/" + file, Cancel);
        Assert.Equal(HttpStatusCode.NotFound, obsoletePath.StatusCode);
    }

    [Fact]
    public async Task Hosted_settings_and_selfhosted_script_reflect_the_same_library_reload()
    {
        await using var site = await TestSite.StartAsync(new Dictionary<string, string?> { ["Drawio:DefaultLibraries"] = "general" }, Cancel);
        using var http = new HttpClient { BaseAddress = site.Address };
        var configuration = (IConfigurationRoot)site.Services.GetRequiredService<IConfiguration>();
        foreach (var libraries in new[] { "general", "general;uml" })
        {
            configuration["Drawio:DefaultLibraries"] = libraries;
            configuration.Reload();
            using var settings = JsonDocument.Parse(await http.GetStringAsync("draw/settings", Cancel));
            var editorConfig = settings.RootElement.GetProperty("configuration");
            Assert.Equal(libraries, editorConfig.GetProperty("defaultLibraries").GetString());
            Assert.Contains("window.DRAWIO_CONFIG=" + editorConfig.GetRawText() + ";", await http.GetStringAsync("draw/js/PreConfig.js", Cancel));
        }
    }
}
