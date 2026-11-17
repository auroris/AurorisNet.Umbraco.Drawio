using Drawio.Compatibility;
using Umbraco.Cms.Core.Configuration;

namespace AurorisNet.Umbraco.Drawio.Tests;

public sealed class CompatibilityTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(19)]
    public void Unknown_versions_require_an_explicit_adapter(int major)
        => Assert.Throws<NotSupportedException>(() => UmbracoCompatibility.ImplementationType(major));
}

public sealed partial class SiteTests
{
    [Fact]
    public async Task The_running_CMS_selects_the_server_and_backoffice_adapters()
    {
        var version = Site.Services.GetRequiredService<IUmbracoVersion>().Version.Major;
        var api = Site.Services.GetRequiredService<IUmbracoApi>();
        Assert.Equal(version, api.MajorVersion);
        Assert.Equal(version == 17 ? typeof(Umbraco17Api) : typeof(Umbraco18Api), api.GetType());
        using var response = await Http.GetAsync("/draw/settings", Cancel);
        using var settings = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(version, settings.RootElement.GetProperty("umbracoMajor").GetInt32());
        foreach (var file in new[] { "media-api.js", "umbraco-media-api.js" })
        {
            using var script = await Http.GetAsync($"/App_Plugins/AurorisNet.Drawio/compatibility/{file}", Cancel);
            Assert.Equal(System.Net.HttpStatusCode.OK, script.StatusCode);
        }
    }
}
