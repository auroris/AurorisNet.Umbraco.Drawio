using System.Text.Json;
using Drawio;

namespace AurorisNet.Umbraco.Drawio.Tests;

/// <summary>The generated PreConfig.js and PostConfig.js.</summary>
public sealed class ScriptTests
{
    private const string Offline = "window.DRAWIO_PUBLIC_BUILD=true;window.EXPORT_URL=null;window.DRAWIO_BASE_URL=null;window.DRAWIO_VIEWER_URL=null;"
        + "window.DRAWIO_LIGHTBOX_URL=null;window.DRAW_MATH_URL='math4/es5';urlParams.sync='manual';";

    /// <summary>Without settings, draw.io keeps its own libraries, but every call to its servers is still off.</summary>
    [Fact]
    public void PreConfig_without_settings_keeps_the_editor_offline()
    {
        var script = DrawioScripts.PreConfig(new DrawioScriptContext("", new DrawioOptions()), []);
        Assert.StartsWith("// Served by AurorisNet.Umbraco.Drawio.", script);
        Assert.Contains(Offline + "window.DRAWIO_CONFIG=null;\n", script);
        Assert.DoesNotContain("location.origin", script);
    }

    /// <summary>
    /// A configured library joins draw.io's standard set in DRAWIO_CONFIG under the configured sidebar group, and the script
    /// turns its path into an absolute URL.
    /// </summary>
    [Fact]
    public void PreConfig_writes_libraries_into_DRAWIO_CONFIG()
    {
        var options = new DrawioOptions
        {
            LibraryGroup = "Extra icons",
            Libraries = [new() { Id = "site-icons", Title = "Site icons", Description = "CC BY 4.0 - Example", Url = "/App_Plugins/MySite/icons/site-icons.xml" }],
        };
        var script = DrawioScripts.PreConfig(new DrawioScriptContext("", options), []);
        const string expected = """{"defaultLibraries":"general;uml;er;bpmn;flowchart;basic;arrows2;site-icons","libraries":[{"title":{"main":"Extra icons"},"entries":[{"id":"site-icons","title":{"main":"Site icons"},"desc":{"main":"CC BY 4.0 - Example"},"libs":[{"title":{"main":"Site icons"},"url":"/App_Plugins/MySite/icons/site-icons.xml"}]}]}]}""";
        Assert.Contains(Offline + "window.DRAWIO_CONFIG=" + expected + ";\n", script);
        // draw.io loads libraries by absolute URL.
        Assert.Contains("l.url=location.origin+l.url", script);
    }

    /// <summary>Library paths follow the site's path base, and DefaultLibraries replaces draw.io's standard set.</summary>
    [Fact]
    public void Libraries_follow_the_path_base_and_the_default_list()
    {
        var options = new DrawioOptions
        {
            DefaultLibraries = "general;flowchart",
            Libraries = [new() { Id = "a", Title = "A", Url = "/shapes/a.xml" }, new() { Id = "b", Title = "B", Url = "/shapes/b.xml" }],
        };
        using var config = JsonDocument.Parse(DrawioScripts.Configuration(new DrawioScriptContext("/intranet", options)));
        Assert.Equal("general;flowchart;a;b", config.RootElement.GetProperty("defaultLibraries").GetString());
        var group = config.RootElement.GetProperty("libraries")[0];
        Assert.Equal("Extra shapes", group.GetProperty("title").GetProperty("main").GetString());
        Assert.Equal(["/intranet/shapes/a.xml", "/intranet/shapes/b.xml"],
            group.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("libs")[0].GetProperty("url").GetString()));
        Assert.Equal("""{"defaultLibraries":"general;flowchart"}""",
            DrawioScripts.Configuration(new DrawioScriptContext("", new DrawioOptions { DefaultLibraries = " general;flowchart " })));
    }

    /// <summary>A configured plugin is registered and loaded once the editor's code is ready, and plugins=0 or offline=1 still turn it off.</summary>
    [Fact]
    public void PostConfig_registers_and_loads_plugins()
    {
        var options = new DrawioOptions { Plugins = [new() { Name = "notes", Url = "/App_Plugins/MySite/notes/notes.js" }] };
        var script = DrawioScripts.PostConfig(new DrawioScriptContext("", options), []);
        Assert.Contains("window.ICONSEARCH_PATH=null;window.ICON_SERVICE_PATH=null;\n"
            + "if(urlParams.plugins!=='0'&&urlParams.offline!=='1'){App.initPluginCallback();App.pluginRegistry[\"notes\"]=\"/App_Plugins/MySite/notes/notes.js\";App.loadPlugins([\"notes\"]);}\n",
            script);
        var two = DrawioScripts.PostConfig(new DrawioScriptContext("/site", new DrawioOptions
        {
            Plugins = [new() { Name = "one", Url = "/p/one.js" }, new() { Name = "two", Url = "/p/two.js" }],
        }), []);
        Assert.Contains("App.pluginRegistry[\"one\"]=\"/site/p/one.js\";App.pluginRegistry[\"two\"]=\"/site/p/two.js\";App.loadPlugins([\"one\",\"two\"]);", two);
        Assert.DoesNotContain("App.loadPlugins", DrawioScripts.PostConfig(new DrawioScriptContext("", new DrawioOptions()), []));
    }

    /// <summary>Contributors add their script after the package's, in order, with the site's paths.</summary>
    [Fact]
    public void Contributors_follow_the_package_script()
    {
        var context = new DrawioScriptContext("/site", new DrawioOptions());
        IDrawioScriptContributor[] contributors = [new Contributor("first"), new Contributor(null), new Contributor("second")];
        var pre = DrawioScripts.PreConfig(context, contributors);
        Assert.EndsWith("window.DRAWIO_CONFIG=null;\npre first /site/App_Plugins/AurorisNet.Drawio\npre second /site/App_Plugins/AurorisNet.Drawio\n", pre);
        var post = DrawioScripts.PostConfig(context, contributors);
        Assert.EndsWith("ICON_SERVICE_PATH=null;\npost first\npost second\n", post);
        // A contributor that implements neither method adds nothing.
        Assert.Equal(DrawioScripts.PreConfig(context, []), DrawioScripts.PreConfig(context, [new Silent()]));
    }

    /// <summary>Configured text reaches the script as JSON string literals, which cannot close a string or a script.</summary>
    [Fact]
    public void Configured_text_is_escaped()
    {
        var options = new DrawioOptions
        {
            LibraryGroup = "</script><script>alert(1)</script>",
            Libraries = [new() { Id = "x", Title = "\"quoted\"\n", Url = "/a.xml" }],
        };
        var script = DrawioScripts.PreConfig(new DrawioScriptContext("", options), []);
        Assert.DoesNotContain("</script>", script);
        Assert.DoesNotContain("\"quoted\"", script);
        Assert.Equal("\"it\\u0027s\"", DrawioScriptContext.Literal("it's"));
    }

    private sealed class Contributor(string? name) : IDrawioScriptContributor
    {
        public string? PreConfig(DrawioScriptContext context) => name is null ? null : $"pre {name} {context.PluginPath}";
        public string? PostConfig(DrawioScriptContext context) => name is null ? "" : $"post {name}";
    }

    private sealed class Silent : IDrawioScriptContributor;
}
