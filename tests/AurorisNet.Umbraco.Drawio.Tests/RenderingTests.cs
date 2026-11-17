using System.Net;
using Drawio;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace AurorisNet.Umbraco.Drawio.Tests;

/// <summary>Renders the actual packaged partial against a published block.</summary>
public sealed class DiagramTestController(IUmbracoContextFactory contexts) : Controller
{
    [HttpGet("/test/diagram/{id:int}")]
    public IActionResult Diagram(int id)
    {
        using var context = contexts.EnsureUmbracoContext();
        var content = context.UmbracoContext.Content?.GetById(id);
        var block = content?.Value<BlockListModel>("diagrams")?.FirstOrDefault();
        return block is null ? NotFound() : View("~/Views/Partials/Drawio/Diagram.cshtml", block.Content);
    }
}

public sealed partial class SiteTests
{
    [Fact]
    public async Task Published_destinations_render_viewer_custom_link_and_unlinked_images()
    {
        var page = await DiagramPage("renderDestinations");
        var image = DiagramImage();
        var content = Site.Services.GetRequiredService<IContentService>();
        async Task<string> Render(int? action, object[]? links = null)
        {
            Pick(page, image, action, links); Assert.True(content.Save(page).Success); Assert.True(content.Publish(page, ["*"]).Success);
            using var response = await Http.GetAsync($"/test/diagram/{page.Id}", Cancel);
            var html = await response.Content.ReadAsStringAsync(Cancel);
            Assert.True(response.StatusCode == HttpStatusCode.OK, html);
            Assert.Contains("<img ", html);
            return html;
        }
        Assert.Contains("/draw/viewer?url=", await Render((int)DiagramClickAction.Viewer));
        var custom = await Render((int)DiagramClickAction.Link, [new { name = "Example", url = "https://example.org/diagram", target = "_blank" }]);
        Assert.Contains("href=\"https://example.org/diagram\"", custom);
        Assert.Contains("rel=\"noopener\"", custom);
        var contentLink = await Render((int)DiagramClickAction.Link,
            [new { name = "This page", udi = $"umb://document/{page.Key:N}", url = (string?)null, target = "", queryString = "#diagram" }]);
        using (var context = Site.Services.GetRequiredService<IUmbracoContextFactory>().EnsureUmbracoContext())
        {
            var destination = context.UmbracoContext.Content!.GetById(page.Id)!.Url();
            Assert.NotEqual("#", destination);
            Assert.Contains($"href=\"{WebUtility.HtmlEncode(destination)}#diagram\"", contentLink);
        }
        Assert.DoesNotContain("<a ", await Render((int)DiagramClickAction.Link));
        Assert.DoesNotContain("<a ", await Render((int)DiagramClickAction.None));
        Assert.DoesNotContain("<a ", await Render(null));
        Assert.DoesNotContain("<a ", await Render(99));
        Assert.DoesNotContain("<a ", await Render((int)DiagramClickAction.Link, [new { name = "Bad", url = "javascript:alert(1)", target = "" }]));
    }

    [Fact]
    public async Task Standalone_modes_and_settings_are_available_without_authentication()
    {
        using var source = await Http.GetAsync("/website.drawio", Cancel);
        Assert.Equal(HttpStatusCode.OK, source.StatusCode);
        Assert.Equal("application/xml", source.Content.Headers.ContentType!.MediaType);
        using var settings = await Http.GetAsync("/draw/settings", Cancel);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Contains("\"editorUrl\":\"/draw/\"", await settings.Content.ReadAsStringAsync(Cancel));
        foreach (var mode in new[] { "editor", "viewer" })
        {
            using var page = await Http.GetAsync($"/draw/{mode}?url=/media/a.drawio", Cancel);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync(Cancel);
            Assert.Contains($"data-viewer=\"{(mode == "viewer" ? "true" : "false")}\"", html);
            Assert.Contains("standalone.js", html);
        }
    }
}
