using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Drawio;

/// <summary>The request paths the package serves, and the checks that keep them inside the draw.io app and the media store.</summary>
public static class DrawioPaths
{
    /// <summary>The path the editor is served at, under the site's path base.</summary>
    public const string Editor = "/draw";

    /// <summary>The package's backoffice and site scripts.</summary>
    public const string Plugin = "/App_Plugins/AurorisNet.Drawio";

    /// <summary>The value of the <c>X-Drawio-Host</c> header on every response the application gives under <see cref="Editor"/>.</summary>
    public const string HostHeader = "AurorisNet.Umbraco.Drawio";

    /// <summary>The largest diagram the editor reads, in bytes before any Base64 encoding.</summary>
    public const long MaxBase64Bytes = 40 * 1024 * 1024;

    private static readonly string[] ProxyExtensions = [".png", ".drawio", ".xml", ".svg"];

    internal static bool IsDiagramExtension(string? path) =>
        ProxyExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a path under <see cref="Editor"/> must not be served. The draw.io source tree has Java servlet folders,
    /// dotfiles and source maps that are no part of the web app, and a backslash can address a parent folder on Windows.
    /// Windows also drops a dot or space from the end of a name, so <c>WEB-INF.</c> would open <c>WEB-INF</c>.
    /// </summary>
    public static bool IsBlocked(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.Contains('\\') || path.Any(char.IsControl) || path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var segment in path.Split('/'))
        {
            if (segment.StartsWith('.') || segment.EndsWith('.') || segment.EndsWith(' ')
                || segment.Equals("WEB-INF", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("META-INF", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reads the proxy's <c>url</c> parameter as a path inside the media store. draw.io's <c>#U</c> links name a site file
    /// such as <c>media/abc/diagram.drawio</c>. Decode each filename segment independently before checking it.
    /// </summary>
    /// <param name="url">The parameter, as draw.io sends it.</param>
    /// <param name="relative">The path inside the media file system, without the <c>media/</c> prefix.</param>
    public static bool TryGetMediaPath(string? url, out string relative)
        => TryGetMediaPath(url, "/media/", out relative);

    internal static bool TryGetMediaPath(string? url, string mediaPrefix, out string relative)
    {
        relative = "";
        if (string.IsNullOrEmpty(url) || url.Length > 1024) return false;
        var segments = url.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = Uri.UnescapeDataString(segments[i]);
            if (segment.Length == 0 || segment.StartsWith('.') || segment.EndsWith('.') || segment.EndsWith(' ')
                || segment.IndexOfAny(['/', '\\', ':', '%', '?', '#']) >= 0 || segment.Any(char.IsControl)) return false;
            segments[i] = segment;
        }
        if (!IsDiagramExtension(segments[^1])) return false;
        var path = "/" + string.Join('/', segments);
        // Older draw.io links use media/ as the logical root even on sites with a custom media path.
        var prefix = path.StartsWith(mediaPrefix, StringComparison.OrdinalIgnoreCase) ? mediaPrefix : "/media/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        relative = path[prefix.Length..];
        return true;
    }

    /// <summary>
    /// The editor address that opens a <c>.drawio</c> media file, or <see langword="null"/> when the path names no such file.
    /// A browser that follows a link to the file then shows the diagram instead of downloading XML.
    /// </summary>
    /// <param name="pathBase">The site's path base, such as an IIS application's virtual path.</param>
    /// <param name="path">The request path below the path base.</param>
    public static string? MediaRedirect(string? pathBase, string? path) =>
        path?.StartsWith("/media/", StringComparison.OrdinalIgnoreCase) == true
            ? MediaFileRedirect(new PathString(pathBase), new PathString(path), QueryString.Empty) : null;

    internal static string? MediaFileRedirect(PathString site, PathString path, QueryString query)
    {
        if (path.Value?.EndsWith(".drawio", StringComparison.OrdinalIgnoreCase) != true || IsBlocked(path.Value)) return null;
        var source = site.Add(path).ToUriComponent() + query.ToUriComponent();
        return QueryHelpers.AddQueryString(site.Add(Editor + "/editor").ToUriComponent(), "url", source);
    }

    internal static string ViewerUrl(PathString site, string source, string language) =>
        QueryHelpers.AddQueryString(site.Add(Editor + "/viewer").ToUriComponent(),
            new Dictionary<string, string?> { ["url"] = source, ["lang"] = language });

    /// <summary>
    /// Whether a configured URL is a path on this site, such as <c>/App_Plugins/MySite/plugin.js</c>. The editor loads these
    /// into its own page, so a scheme, another host or a backslash, which some browsers read as a slash, is refused.
    /// </summary>
    public static bool IsLocalPath(string? url) => url is { Length: > 1 } && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.Contains('\\') && !url.Any(char.IsControl) && !url.Any(char.IsWhiteSpace);
}
