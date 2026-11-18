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
            <object id="2" label="Editor round trip" link="https://example.org/floor"><mxCell parent="1" vertex="1" style="fillColor=#ffffff;gradientColor=#00aaff;">
              <mxGeometry as="geometry" x="40" y="40" width="180" height="60"/>
            </mxCell></object>
            <mxCell id="3" parent="1" vertex="1" style="html=1;whiteSpace=wrap;"
              value="Read the &lt;a href=&quot;#floor-plan&quot;&gt;floor plan&lt;/a&gt; here">
              <mxGeometry as="geometry" x="40" y="130" width="180" height="60"/>
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
        if (selfhosted)
        {
            var scripts = Directory.CreateDirectory(Path.Combine(webRoot, "App_Plugins", "AurorisNet.Drawio"));
            await File.WriteAllTextAsync(Path.Combine(scripts.FullName, "PreConfig.js"),
                "window.DRAWIO_CONFIG = { defaultGridSize: 37 };", cancel);
            await File.WriteAllTextAsync(Path.Combine(scripts.FullName, "PostConfig.js"),
                "document.documentElement.dataset.sitePostConfig = typeof App;", cancel);
        }
        await page.GotoAsync(EditorPage("saved.drawio"));
        await editor.GetByText("Saved from the editor", new() { Exact = true }).First.WaitForAsync();
        // Exercise the block editor's export contract against the installed draw.io build.
        var export = await page.EvaluateAsync<string[]>("""
            async ({plugin, editorOrigin}) => {
                const {extractDiagramMap} = await import(plugin + '/diagram-map.js');
                const {editableBytes} = await import(plugin + '/bridge.js');
                const frame = document.querySelector('iframe').contentWindow;
                const result = await new Promise((resolve, reject) => {
                    const timer = setTimeout(() => reject(Error('Timed out exporting linked drawing')), 30000);
                    const receive = event => {
                        if (event.source !== frame || event.origin !== editorOrigin || typeof event.data !== 'string') return;
                        const message = JSON.parse(event.data);
                        if (message.event !== 'export') return;
                        clearTimeout(timer);
                        window.removeEventListener('message', receive);
                        resolve(message);
                    };
                    window.addEventListener('message', receive);
                    frame.postMessage(JSON.stringify({action:'export', format:'xmlpng', withSvg:true, currentPage:true, border:0, scale:1}), editorOrigin);
                });
                const bytes = new DataView(editableBytes(result.data).buffer);
                const map = JSON.parse(extractDiagramMap(result.svg));
                if (map.viewBox !== `0 0 ${bytes.getUint32(16)} ${bytes.getUint32(20)}`) throw Error('PNG and SVG bounds differ');
                if (!result.xml.includes('<mxfile') || !map.links[0].shapes.length) throw Error('Missing drawing or outline');
                return [map.links[0].href, result.svg];
            }
            """, new { plugin = new Uri(site.Address, "App_Plugins/AurorisNet.Drawio").AbsoluteUri, editorOrigin = editorUrl.GetLeftPart(UriPartial.Authority) });
        Assert.Equal("https://example.org/floor", export[0]);
        if (selfhosted)
        {
            var frame = page.Frames.Single(f => f.ParentFrame == page.MainFrame);
            Assert.Equal(37, await frame.EvaluateAsync<int>("mxGraph.prototype.gridSize"));
            Assert.Equal("function", await frame.Locator("html").GetAttributeAsync("data-site-post-config"));
        }

        // Render the real export through the site's public module. The same file can appear twice on a page.
        await File.WriteAllTextAsync(Path.Combine(webRoot, "media", "fixture", "plan.svg"), export[1], cancel);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "inline.html"), """
            <!doctype html><style>.floor-plan svg rect { fill: rgb(10, 90, 160); }</style>
            <figure class="floor-plan"><drawio-diagram-image data-inline-svg="true" style="display:inline-block;position:relative">
              <a href="#whole-diagram"><img src="media/fixture/plan.svg" alt="Floor plan"></a>
            </drawio-diagram-image></figure>
            <drawio-diagram-image data-inline-svg="true"><img src="media/fixture/plan.svg" alt="Second plan"></drawio-diagram-image>
            <script type="module" src="App_Plugins/AurorisNet.Drawio/inline-svg.js"></script>
            """, cancel);
        await page.GotoAsync(new Uri(site.Address, "inline.html").AbsoluteUri);
        var drawing = page.Locator(".floor-plan drawio-diagram-image > svg");
        var words = drawing.GetByRole(AriaRole.Link, new() { Name = "floor plan", Exact = true });
        await words.ClickAsync();
        Assert.EndsWith("#floor-plan", page.Url);
        Assert.Equal("rgb(10, 90, 160)", await drawing.Locator("rect").First.EvaluateAsync<string>("el => getComputedStyle(el).fill"));
        await Assertions.Expect(page.Locator("drawio-diagram-image > svg")).ToHaveCountAsync(2);
        var gradients = await page.Locator("drawio-diagram-image > svg").EvaluateAllAsync<string[]>("""
            svgs => svgs.map(svg => {
                const fill = svg.querySelector('rect[fill^="url("]').getAttribute('fill');
                const gradient = svg.querySelector('linearGradient');
                if (fill !== 'url(#' + gradient.id + ')') throw Error('Gradient points outside its diagram');
                return gradient.id;
            })
            """);
        Assert.NotEqual(gradients[0], gradients[1]);
        await page.EvaluateAsync("history.replaceState(null, '', location.pathname)");
        await words.PressAsync("Enter");
        await page.WaitForURLAsync("**/inline.html#floor-plan");
        Assert.EndsWith("#floor-plan", page.Url);
    }
}
