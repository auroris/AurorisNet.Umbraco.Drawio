using Drawio;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;

namespace AurorisNet.Umbraco.Drawio.Tests;

/// <summary>
/// The README says a view file at the same path in the site replaces the packaged one. This site builds its own copies
/// of the package's views, as the project file describes.
/// </summary>
public sealed class SiteViewTests
{
    /// <summary>MVC renders the site's copy of each view, with the packaged one still present.</summary>
    [Fact]
    public async Task A_site_copy_of_each_view_replaces_the_packaged_one()
    {
        await using var site = await TestSite.StartAsync(new Dictionary<string, string?>(), TestContext.Current.CancellationToken);
        var views = new ViewsFeature();
        site.Services.GetRequiredService<ApplicationPartManager>().PopulateFeature(views);
        var compiler = site.Services.GetRequiredService<IViewCompilerProvider>().GetCompiler();
        foreach (var path in new[]
        {
            "/Views/Partials/Drawio/Diagram.cshtml",
            "/Views/Partials/blockgrid/Components/drawioDiagram.cshtml",
            "/Views/Partials/blocklist/Components/drawioDiagram.cshtml",
        })
        {
            var compiled = views.ViewDescriptors.Where(view => view.RelativePath == path).Select(view => view.Type?.Assembly).ToArray();
            Assert.Contains(typeof(DrawioPaths).Assembly, compiled);
            Assert.Contains(typeof(SiteViewTests).Assembly, compiled);
            Assert.Equal(typeof(SiteViewTests).Assembly, (await compiler.CompileAsync(path)).Type?.Assembly);
        }
    }
}
