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

    /// <summary>The largest media file the proxy returns as Base64, which draw.io holds in memory as a string.</summary>
    public const long MaxBase64Bytes = 40 * 1024 * 1024;

    private static readonly string[] ProxyExtensions = [".png", ".drawio", ".xml", ".svg"];

    /// <summary>
    /// Whether a path under <see cref="Editor"/> must not be served. The draw.io source tree has Java servlet folders,
    /// dotfiles and source maps that are no part of the web app, and a backslash can address a parent folder on Windows.
    /// </summary>
    public static bool IsBlocked(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.Contains('\\') || path.Any(char.IsControl) || path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var segment in path.Split('/'))
        {
            if (segment.StartsWith('.') || segment.Equals("WEB-INF", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("META-INF", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reads the proxy's <c>url</c> parameter as a path inside the media store. draw.io's <c>#U</c> links name a site file
    /// such as <c>media/abc/diagram.drawio</c>; anything else, including a remote URL, an encoded character or a step out of
    /// the media folder, is refused, so the proxy can never fetch from elsewhere.
    /// </summary>
    /// <param name="url">The parameter, as draw.io sends it.</param>
    /// <param name="relative">The path inside the media file system, without the <c>media/</c> prefix.</param>
    public static bool TryGetMediaPath(string? url, out string relative)
    {
        relative = "";
        if (url is null || !url.StartsWith("media/", StringComparison.Ordinal) || url.Length > 1024
            || url.IndexOfAny(['\\', ':', '%', '?', '#']) >= 0 || url.Any(char.IsControl))
            return false;
        foreach (var segment in url.Split('/'))
        {
            if (segment.Length == 0 || segment.StartsWith('.') || segment.EndsWith('.') || segment.EndsWith(' ')) return false;
        }
        if (!ProxyExtensions.Contains(Path.GetExtension(url), StringComparer.OrdinalIgnoreCase)) return false;
        relative = url["media/".Length..];
        return true;
    }

    /// <summary>
    /// The editor address that opens a <c>.drawio</c> media file, or <see langword="null"/> when the path names no such file.
    /// A browser that follows a link to the file then shows the diagram instead of downloading XML.
    /// </summary>
    /// <param name="pathBase">The site's path base, such as an IIS application's virtual path.</param>
    /// <param name="path">The request path below the path base.</param>
    public static string? MediaRedirect(string? pathBase, string? path)
    {
        if (path is null || !path.StartsWith("/media/", StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith(".drawio", StringComparison.OrdinalIgnoreCase) || IsBlocked(path))
            return null;
        return (pathBase ?? "") + Editor + "/editor?url=" + Uri.EscapeDataString((pathBase ?? "") + path);
    }

    /// <summary>
    /// Whether a configured URL is a path on this site, such as <c>/App_Plugins/MySite/plugin.js</c>. The editor loads these
    /// into its own page, so a scheme, another host or a backslash, which some browsers read as a slash, is refused.
    /// </summary>
    public static bool IsLocalPath(string? url) => url is { Length: > 1 } && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.Contains('\\') && !url.Any(char.IsControl) && !url.Any(char.IsWhiteSpace);
}
