using Microsoft.Playwright;

namespace AurorisNet.Umbraco.Drawio.Tests;

/// <summary>Opens and saves a diagram through the site's public editor with the installed NuGet package.</summary>
public sealed class EditorTests
{
#if SELFHOSTED
    /// <summary>The bundled editor works when the browser may only contact the site's origin.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("/teams/Project #1")]
    public Task Selfhosted_package_opens_and_saves_a_diagram_without_external_requests(string pathBase) =>
        OpenAndSaveDiagram(pathBase, selfhosted: true);
#else
    /// <summary>The base installation uses the live diagrams.net embed.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("/teams/Project #1")]
    public Task Base_package_opens_and_saves_a_diagram_on_embed_diagrams_net(string pathBase) =>
        OpenAndSaveDiagram(pathBase, selfhosted: false);
#endif

    private static async Task OpenAndSaveDiagram(string pathBase, bool selfhosted)
    {
        var cancel = TestContext.Current.CancellationToken;
#if APPLICATION_VIRTUAL_PATH_FIX
        var settings = new Dictionary<string, string?> { ["Umbraco:CMS:Hosting:ApplicationVirtualPath"] = pathBase };
        await using var site = await TestSite.StartAsync(settings, cancel, new PathString(pathBase), hostSetsPathBase: false);
#else
        await using var site = await TestSite.StartAsync(new Dictionary<string, string?>(), cancel, new PathString(pathBase));
#endif
        string EditorPage(string file) => new Uri(site.Address, "draw/editor?lang=en&url=" +
            Uri.EscapeDataString(new Uri(site.Address, file).AbsolutePath)).AbsoluteUri;
        var webRoot = site.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        Directory.CreateDirectory(Path.Combine(webRoot, "media", "fixture"));
        const string source = "media/fixture/Floor plan + étage.drawio";
        await File.WriteAllTextAsync(Path.Combine(webRoot, source), """
            <mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/>
            <mxCell id="2" parent="1" vertex="1" value="Editor round trip">
              <mxGeometry as="geometry" x="40" y="40" width="180" height="60"/>
            </mxCell></root></mxGraphModel>
            """, cancel);

        using var playwright = await Playwright.CreateAsync();
        // The public embed service returns HTTP 403 to headless Chrome.
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = selfhosted });
        await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block });
        if (selfhosted)
        {
            await context.RouteAsync("**/*", route => site.Address.IsBaseOf(new Uri(route.Request.Url))
                ? route.ContinueAsync() : route.AbortAsync());
        }

        var page = await context.NewPageAsync();
        var response = await page.RunAndWaitForResponseAsync(() => page.GotoAsync(EditorPage(source)),
            r => r.Request.IsNavigationRequest && r.Frame.ParentFrame == page.MainFrame);
        Assert.True(response.Ok, $"Loading {response.Url} returned HTTP {response.Status}.");
        var editorUrl = new Uri(response.Url);
        Assert.Equal(selfhosted ? new Uri(site.Address, "draw/") : new Uri("https://embed.diagrams.net/"),
            new Uri(editorUrl.GetLeftPart(UriPartial.Path)));

        var editor = page.FrameLocator("iframe");
        var label = editor.GetByText("Editor round trip", new() { Exact = true });
        await label.First.WaitForAsync(new() { Timeout = 60_000 });

        await label.First.DblClickAsync();
        await editor.Locator("[contenteditable=true]:focus").PressAsync("ControlOrMeta+a");
        await page.Keyboard.InsertTextAsync("Saved from the editor");
        await page.Keyboard.PressAsync("ControlOrMeta+Enter");

        var download = await page.RunAndWaitForDownloadAsync(() => page.Keyboard.PressAsync("ControlOrMeta+s"));
        Assert.Equal("Floor plan + étage.drawio", download.SuggestedFilename);
        await download.SaveAsAsync(Path.Combine(webRoot, "saved.drawio"));
        await page.GotoAsync(EditorPage("saved.drawio"));
        await editor.GetByText("Saved from the editor", new() { Exact = true }).First.WaitForAsync();
    }
}
