using System.Text;
using System.Text.Json;

namespace Drawio;

/// <summary>
/// Adds a site's own script to the editor's <c>PreConfig.js</c> or <c>PostConfig.js</c>. Register an implementation as a
/// service; each one's script follows the package's, in registration order.
/// </summary>
public interface IDrawioScriptContributor
{
    /// <summary>Script that runs before draw.io starts, such as a global a plugin reads. Return <see langword="null"/> for none.</summary>
    string? PreConfig(DrawioScriptContext context) => null;

    /// <summary>Script that runs once draw.io's code has loaded and before the editor opens. Return <see langword="null"/> for none.</summary>
    string? PostConfig(DrawioScriptContext context) => null;
}

/// <summary>What a script contributor needs to address the site.</summary>
/// <param name="SitePath">The site's path base, empty at the host root, such as <c>/intranet</c> under an IIS application.</param>
/// <param name="Options">The package settings.</param>
public sealed record DrawioScriptContext(string SitePath, DrawioOptions Options)
{
    /// <summary>The path of the package's own scripts on this site.</summary>
    public string PluginPath => SitePath + DrawioPaths.Plugin;

    /// <summary>A JavaScript string literal for a value, safe to place in a script.</summary>
    public static string Literal(string value) => JsonSerializer.Serialize(value);
}

/// <summary>Writes the editor's two configuration scripts, which draw.io's <c>index.html</c> loads from <c>js/</c>.</summary>
public static class DrawioScripts
{
    /// <summary>The libraries draw.io opens when none are configured, which the configured ones join.</summary>
    public const string StandardLibraries = "general;uml;er;bpmn;flowchart;basic;arrows2";

    private const string Banner = "// Served by AurorisNet.Umbraco.Drawio. See https://github.com/auroris/AurorisNet.Umbraco.Drawio/blob/main/docs/IIS-DEPLOYMENT.md\n";

    /// <summary>
    /// <c>PreConfig.js</c>: keeps the editor offline and on this site, then names the extra libraries. draw.io otherwise
    /// offers cloud storage, an export server and its own hosted viewer, none of which an intranet site can reach.
    /// </summary>
    public static string PreConfig(DrawioScriptContext context, IEnumerable<IDrawioScriptContributor> contributors)
    {
        var options = context.Options;
        var script = new StringBuilder(Banner);
        script.Append("window.DRAWIO_PUBLIC_BUILD=true;window.EXPORT_URL=null;window.DRAWIO_BASE_URL=null;window.DRAWIO_VIEWER_URL=null;")
            .Append("window.DRAWIO_LIGHTBOX_URL=null;window.DRAW_MATH_URL='math4/es5';urlParams.sync='manual';")
            .Append("window.DRAWIO_CONFIG=").Append(Configuration(context)).Append(";\n");
        if (options.Libraries.Count > 0)
            // draw.io fetches a library by absolute URL, so prefix this page's origin to each site path.
            script.Append("window.DRAWIO_CONFIG.libraries.forEach(function(g){g.entries.forEach(function(e){e.libs.forEach(function(l){l.url=location.origin+l.url;});});});\n");
        foreach (var contributor in contributors)
            if (contributor.PreConfig(context) is { Length: > 0 } extra) script.Append(extra).Append('\n');
        return script.ToString();
    }

    /// <summary>
    /// <c>PostConfig.js</c>: turns off the icon search services, which call draw.io's servers, then loads the configured
    /// plugins. draw.io's <c>plugins=0</c> and <c>offline=1</c> URL parameters still turn plugins off.
    /// </summary>
    public static string PostConfig(DrawioScriptContext context, IEnumerable<IDrawioScriptContributor> contributors)
    {
        var script = new StringBuilder(Banner);
        script.Append("window.ICONSEARCH_PATH=null;window.ICON_SERVICE_PATH=null;\n");
        var plugins = context.Options.Plugins;
        if (plugins.Count > 0)
        {
            script.Append("if(urlParams.plugins!=='0'&&urlParams.offline!=='1'){App.initPluginCallback();");
            foreach (var plugin in plugins)
                script.Append("App.pluginRegistry[").Append(DrawioScriptContext.Literal(plugin.Name)).Append("]=")
                    .Append(DrawioScriptContext.Literal(context.SitePath + plugin.Url)).Append(';');
            script.Append("App.loadPlugins(").Append(JsonSerializer.Serialize(plugins.Select(p => p.Name))).Append(");}\n");
        }
        foreach (var contributor in contributors)
            if (contributor.PostConfig(context) is { Length: > 0 } extra) script.Append(extra).Append('\n');
        return script.ToString();
    }

    /// <summary>The <c>DRAWIO_CONFIG</c> object, or <c>null</c>, which leaves draw.io's defaults.</summary>
    internal static string Configuration(DrawioScriptContext context)
    {
        var options = context.Options;
        if (options.Libraries.Count == 0)
            return string.IsNullOrWhiteSpace(options.DefaultLibraries) ? "null"
                : JsonSerializer.Serialize(new { defaultLibraries = options.DefaultLibraries.Trim() });
        var defaults = string.IsNullOrWhiteSpace(options.DefaultLibraries) ? StandardLibraries : options.DefaultLibraries.Trim();
        return JsonSerializer.Serialize(new
        {
            defaultLibraries = string.Join(';', new[] { defaults }.Concat(options.Libraries.Select(l => l.Id))),
            libraries = new[]
            {
                new
                {
                    title = new { main = options.LibraryGroup },
                    entries = options.Libraries.Select(library => new
                    {
                        id = library.Id,
                        title = new { main = library.Title },
                        desc = new { main = library.Description ?? "" },
                        libs = new[] { new { title = new { main = library.Title }, url = context.SitePath + library.Url } },
                    }),
                },
            },
        });
    }
}
