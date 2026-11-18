using Drawio;

namespace AurorisNet.Umbraco.Drawio.Tests;

/// <summary>Path hardening, proxy URL validation and the media redirect rule.</summary>
public sealed class PathTests
{
    [Theory]
    [InlineData("/index.html")]
    [InlineData("/js/app.min.js")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("/resources/dia_fr.txt")]
    [InlineData("/stencils/a.b/c.xml")]
    public void Editor_files_are_served(string path) => Assert.False(DrawioPaths.IsBlocked(path));

    [Theory]
    [InlineData("/WEB-INF/web.xml")]
    [InlineData("/web-inf/appengine-web.xml")]
    [InlineData("/META-INF/MANIFEST.MF")]
    [InlineData("/js/app.min.js.map")]
    [InlineData("/js/APP.MAP")]
    [InlineData("/.gitignore")]
    [InlineData("/js/.hidden/a.js")]
    [InlineData("/..")]
    [InlineData("/js\\..\\web.config")]
    [InlineData("/js/a\u0000.js")]
    // Windows opens WEB-INF for "WEB-INF." and for "WEB-INF ".
    [InlineData("/WEB-INF./web.xml")]
    [InlineData("/web-inf /web.xml")]
    [InlineData("/META-INF./MANIFEST.MF")]
    [InlineData("/js/app.min.js.map.")]
    [InlineData("/js/app.min.js.map ")]
    public void Servlet_folders_dotfiles_source_maps_and_backslashes_are_refused(string path) => Assert.True(DrawioPaths.IsBlocked(path));

    [Theory]
    [InlineData("media/abc/diagram.drawio", "abc/diagram.drawio")]
    [InlineData("media/abc/diagram.PNG", "abc/diagram.PNG")]
    [InlineData("media/a/b/c.xml", "a/b/c.xml")]
    [InlineData("media/x/y.svg", "x/y.svg")]
    [InlineData("MEDIA/a/b.png", "a/b.png")]
    [InlineData("media/a/%C3%A9tage%20A+B.drawio", "a/étage A+B.drawio")]
    public void The_proxy_reads_diagram_files_in_the_media_store(string url, string expected)
    {
        Assert.True(DrawioPaths.TryGetMediaPath(url, out var relative));
        Assert.Equal(expected, relative);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/media/a.png")]
    [InlineData("//example.com/media/a.png")]
    [InlineData("/media/a/b.png")]
    [InlineData("media/../appsettings.json")]
    [InlineData("media/a/../../web.png")]
    [InlineData("media/./a.png")]
    [InlineData("media/a//b.png")]
    [InlineData("media/.hidden/a.png")]
    [InlineData("media/a./b.png")]
    [InlineData("media/a /b.png")]
    [InlineData("media/a\\b.png")]
    [InlineData("media/a%2e%2e/b.png")]
    [InlineData("media/a/b.png?x=1")]
    [InlineData("media/a/b.png#x")]
    [InlineData("media/c:/b.png")]
    [InlineData("media/a/b.png\n")]
    [InlineData("media/a/b.exe")]
    [InlineData("media/a/b.config")]
    [InlineData("media/a/b")]
    public void The_proxy_refuses_anything_else(string? url)
    {
        Assert.False(DrawioPaths.TryGetMediaPath(url, out var relative));
        Assert.Equal("", relative);
    }

    [Fact]
    public void The_proxy_refuses_very_long_paths() => Assert.False(DrawioPaths.TryGetMediaPath("media/" + new string('a', 1100) + ".png", out _));

    [Theory]
    [InlineData("", "/media/abc/plan.drawio", "/draw/editor?url=%2Fmedia%2Fabc%2Fplan.drawio")]
    [InlineData("/intranet", "/media/abc/Floor plan.DRAWIO", "/intranet/draw/editor?url=%2Fintranet%2Fmedia%2Fabc%2FFloor%2520plan.DRAWIO")]
    [InlineData(null, "/MEDIA/a/b.drawio", "/draw/editor?url=%2FMEDIA%2Fa%2Fb.drawio")]
    public void A_drawio_media_file_opens_in_the_editor(string? pathBase, string path, string expected)
        => Assert.Equal(expected, DrawioPaths.MediaRedirect(pathBase, path));

    [Fact]
    public void Viewer_encodes_the_application_path_and_preserves_the_source_query()
    {
        const string source = "/teams/Project%20%231/media/plan%20A.png?rnd=123";
        var viewer = new Uri(new Uri("https://site.example"), DrawioPaths.ViewerUrl(new("/teams/Project #1"), source, "en"));
        Assert.Equal("/teams/Project%20%231/draw/viewer", viewer.AbsolutePath);
        Assert.Equal("", viewer.Fragment);
        Assert.Equal(source, Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(viewer.Query)["url"]);
    }

    [Theory]
    [InlineData("/media/abc/plan.png")]
    [InlineData("/media/abc/plan.drawio.png")]
    [InlineData("/other/plan.drawio")]
    [InlineData("/mediafiles/plan.drawio")]
    [InlineData("/media/.hidden/plan.drawio")]
    [InlineData(null)]
    public void Other_requests_are_not_redirected(string? path) => Assert.Null(DrawioPaths.MediaRedirect("", path));

    [Theory]
    [InlineData("/App_Plugins/Site/plugin.js", true)]
    [InlineData("/a", true)]
    [InlineData("/", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("plugin.js", false)]
    [InlineData("//cdn.example.com/plugin.js", false)]
    [InlineData("https://cdn.example.com/plugin.js", false)]
    [InlineData("/\\cdn.example.com/plugin.js", false)]
    [InlineData("/a b.js", false)]
    [InlineData("/a\tb.js", false)]
    public void Configured_urls_must_be_paths_on_the_site(string? url, bool expected) => Assert.Equal(expected, DrawioPaths.IsLocalPath(url));
}
