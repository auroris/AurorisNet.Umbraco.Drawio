using System.Text.Json;
using System.Text.RegularExpressions;
using Drawio;
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

    /// <summary>
    /// Every name the package's scripts import exists in the module it comes from: this CMS release's backoffice, or
    /// another script of the package. A browser refuses a whole script over one missing name, and no other test loads
    /// the backoffice scripts in a browser.
    /// </summary>
    [Fact]
    public async Task The_scripts_import_only_names_their_modules_export()
    {
        using var manifest = JsonDocument.Parse(await Http.GetStringAsync(DrawioPaths.Plugin + "/umbraco-package.json", Cancel));
        using var backoffice = JsonDocument.Parse(await Http.GetStringAsync("/umbraco/backoffice/umbraco-package.json", Cancel));
        var importMap = backoffice.RootElement.GetProperty("importmap").GetProperty("imports");
        // Start from the scripts the backoffice and the standalone page load, then follow the package's own imports.
        var pending = new Queue<string>(manifest.RootElement.GetProperty("extensions").EnumerateArray()
            .Select(extension => extension.GetProperty("element").GetString()!).Append(DrawioPaths.Plugin + "/standalone.js").Distinct());
        var scripts = pending.ToHashSet();
        var exports = new Dictionary<string, HashSet<string>>();
        while (pending.TryDequeue(out var script))
        {
            foreach (var (names, specifier) in ScriptImports(await Http.GetStringAsync(script, Cancel)))
            {
                var own = specifier.StartsWith('.');
                Assert.True(own || importMap.TryGetProperty(specifier, out _), $"{script} imports {specifier}, which this backoffice does not provide.");
                var module = own ? new Uri(new Uri(Site.Address, script), specifier).AbsolutePath : importMap.GetProperty(specifier).GetString()!;
                if (!exports.TryGetValue(module, out var exported)) exports[module] = exported = await ScriptExports(module);
                Assert.All(names, name => Assert.True(exported.Contains(name), $"{script} imports {name} from {specifier}, which does not export it."));
                if (own && scripts.Add(module)) pending.Enqueue(module);
            }
        }
        Assert.Contains(DrawioPaths.Plugin + "/bridge.js", scripts);
        Assert.Contains(exports, module => module.Key.StartsWith("/umbraco/backoffice/", StringComparison.Ordinal));
    }

    // A module's static imports: the names it asks for, and the module it asks for them.
    private static IEnumerable<(string[] Names, string Specifier)> ScriptImports(string source)
    {
        foreach (Match import in Regex.Matches(source, """^\s*import\s+(?:(?<clause>[^'"]+?)\s*from\s*)?['"](?<module>[^'"]+)['"]""", RegexOptions.Multiline))
        {
            var clause = import.Groups["clause"].Value;
            var named = Regex.Match(clause, @"\{(?<names>[^}]*)\}");
            var names = named.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Split(' ')[0]).ToList();
            // What is left besides the braces is a default import, unless it is a namespace import.
            var rest = (named.Success ? clause.Remove(named.Index, named.Length) : clause).Trim(' ', ',');
            if (rest.Length > 0 && !rest.StartsWith('*')) names.Add("default");
            yield return (names.ToArray(), import.Groups["module"].Value);
        }
    }

    private async Task<HashSet<string>> ScriptExports(string module)
    {
        var source = await Http.GetStringAsync(module, Cancel);
        var names = new HashSet<string>();
        foreach (Match list in Regex.Matches(source, @"\bexport\s*\{(?<names>[^}]*)\}"))
            names.UnionWith(list.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Split(' ')[^1]));
        foreach (Match declaration in Regex.Matches(source, @"\bexport\s+(?<default>default\s+)?(?:async\s+)?(?:function\s*\*?|class|const|let|var)\s+(?<name>[\w$]+)"))
            names.Add(declaration.Groups["default"].Success ? "default" : declaration.Groups["name"].Value);
        if (Regex.IsMatch(source, @"\bexport\s+default\b")) names.Add("default");
        foreach (Match all in Regex.Matches(source, """\bexport\s*\*\s*from\s*['"](?<module>[^'"]+)['"]"""))
            names.UnionWith(await ScriptExports(new Uri(new Uri(Site.Address, module), all.Groups["module"].Value).AbsolutePath));
        return names;
    }
}
