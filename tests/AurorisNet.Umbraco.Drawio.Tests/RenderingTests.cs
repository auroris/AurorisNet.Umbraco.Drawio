using System.Net;
using System.Text.RegularExpressions;
using Drawio;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
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
    public async Task Replacing_the_media_file_refreshes_the_published_image_url()
    {
        var page = await DiagramPage("renderUpdatedImage");
        var image = DiagramImage();
        Pick(page, image);
        var content = Site.Services.GetRequiredService<IContentService>();
        Assert.True(content.Save(page).Success);
        Assert.True(content.Publish(page, ["*"]).Success);
        async Task<Uri> PublishedImage()
        {
            var html = await Http.GetStringAsync($"/test/diagram/{page.Id}", Cancel);
            var src = Regex.Match(html, "<img src=\"([^\"]+)\"").Groups[1].Value;
            return new Uri(Site.Address, WebUtility.HtmlDecode(src));
        }
        var before = await PublishedImage();
        var replacement = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYPj/HwADAgH/5ncLrgAAAABJRU5ErkJggg==");
        image.SetValue(Site.Services.GetRequiredService<MediaFileManager>(), Site.Services.GetRequiredService<MediaUrlGeneratorCollection>(),
            Site.Services.GetRequiredService<IShortStringHelper>(), Site.Services.GetRequiredService<IContentTypeBaseServiceProvider>(),
            Constants.Conventions.Media.File, $"diagram-{image.Key:N}.png", new MemoryStream(replacement));
        Assert.True(Site.Services.GetRequiredService<IMediaService>().Save(image).Success);
        var after = await PublishedImage();
        Assert.Equal(before.AbsolutePath, after.AbsolutePath);
        Assert.NotEqual(before.Query, after.Query);
        Assert.Equal(replacement, await Http.GetByteArrayAsync(after, Cancel));
    }

    [Fact]
    public async Task Blocks_share_the_source_and_clickable_regions_but_select_their_own_image_format()
    {
        var page = await DiagramPage("renderDiagramFormats");
        var map = """
            {"viewBox":"0 0 40 20","links":[
              {"href":"/room","label":"Room A","shapes":[{"d":"M 0 0 H 12 V 8 Z","transform":"rotate(12)"}]},
              {"href":"javascript:alert(1)","shapes":[{"d":"M 1 1 L 10 10"}]}]}
            """;
        var image = DiagramImage(map: map);
        var content = Site.Services.GetRequiredService<IContentService>();
        foreach (var format in new bool?[] { null, true, false })
        {
            Pick(page, image, openInViewer: true, svg: format);
            Assert.True(content.Save(page).Success);
            Assert.True(content.Publish(page, ["*"]).Success);
            var html = await Http.GetStringAsync($"/test/diagram/{page.Id}", Cancel);
            var src = WebUtility.HtmlDecode(Regex.Match(html, "<img src=\"([^\"]+)\"").Groups[1].Value);
            Assert.EndsWith(format == true ? ".svg" : ".png", new Uri(Site.Address, src).AbsolutePath);
            Assert.Equal(format == true, html.Contains("data-inline-svg=\"true\""));
            Assert.Equal(format == true, html.Contains("/App_Plugins/AurorisNet.Drawio/inline-svg.js"));
            using var file = await Http.GetAsync(src, Cancel);
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Contains(".drawio", html);
            Assert.Contains("viewBox=\"0 0 40 20\"", html);
            Assert.Contains("href=\"/room\" aria-label=\"Room A\"", html);
            Assert.Contains("transform=\"rotate(12)\"", html);
            Assert.DoesNotContain("javascript:", html);
        }
    }

    [Fact]
    public async Task The_figure_appends_the_blocks_CSS_classes_as_an_encoded_attribute()
    {
        var page = await DiagramPage("renderDiagramClasses");
        var image = DiagramImage();
        var content = Site.Services.GetRequiredService<IContentService>();
        foreach (var (value, expected) in new (string?, string)[]
        {
            (null, "drawio-diagram"),
            ("   ", "drawio-diagram"),
            ("  floor-plan wide  ", "drawio-diagram floor-plan wide"),
            ("map\" title=\"outside", "drawio-diagram map&quot; title=&quot;outside")
        })
        {
            Pick(page, image, cssClass: value);
            Assert.True(content.Save(page).Success);
            Assert.True(content.Publish(page, ["*"]).Success);
            var html = await Http.GetStringAsync($"/test/diagram/{page.Id}", Cancel);
            Assert.Contains($"<figure class=\"{expected}\">", html);
        }
    }

    [Fact]
    public async Task Published_destinations_render_viewer_custom_link_and_unlinked_images()
    {
        var page = await DiagramPage("renderDestinations");
        var image = DiagramImage();
        var content = Site.Services.GetRequiredService<IContentService>();
        async Task<string> Render(bool? openInViewer, object[]? links = null)
        {
            Pick(page, image, openInViewer, links); Assert.True(content.Save(page).Success); Assert.True(content.Publish(page, ["*"]).Success);
            using var response = await Http.GetAsync($"/test/diagram/{page.Id}", Cancel);
            var html = await response.Content.ReadAsStringAsync(Cancel);
            Assert.True(response.StatusCode == HttpStatusCode.OK, html);
            Assert.Contains("<img ", html);
            return html;
        }
        Assert.Contains("/draw/viewer?url=", await Render(true));
        object[] links = [new { name = "Example", url = "https://example.org/diagram", target = "_blank" }];
        var viewerWithLink = await Render(true, links);
        Assert.Contains("/draw/viewer?url=", viewerWithLink);
        Assert.DoesNotContain("https://example.org/diagram", viewerWithLink);
        Assert.Contains("href=\"https://example.org/diagram\"", await Render(null, links));
        var custom = await Render(false, links);
        Assert.Contains("href=\"https://example.org/diagram\"", custom);
        Assert.Contains("rel=\"noopener\"", custom);
        var contentLink = await Render(false,
            [new { name = "This page", udi = $"umb://document/{page.Key:N}", url = (string?)null, target = "", queryString = "#diagram" }]);
        using (var context = Site.Services.GetRequiredService<IUmbracoContextFactory>().EnsureUmbracoContext())
        {
            var destination = context.UmbracoContext.Content!.GetById(page.Id)!.Url();
            Assert.NotEqual("#", destination);
            Assert.Contains($"href=\"{WebUtility.HtmlEncode(destination)}#diagram\"", contentLink);
        }
        Assert.DoesNotContain("<a ", await Render(false));
        Assert.DoesNotContain("<a ", await Render(null));
        Assert.DoesNotContain("<a ", await Render(false, [new { name = "Bad", url = "javascript:alert(1)", target = "" }]));
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
