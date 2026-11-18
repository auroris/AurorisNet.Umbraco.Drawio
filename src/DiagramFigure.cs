using System.Globalization;
using Drawio.Compatibility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Drawio;

/// <summary>
/// What <c>Views/Partials/Drawio/Diagram.cshtml</c> renders for one diagram element: the image, its text, and the link its
/// click action selects. A site's own copy of that view reads the same values, so it changes the markup alone.
/// </summary>
/// <param name="ImageUrl">The block's selected PNG or SVG, or <see langword="null"/> when the element has no image to show.</param>
/// <param name="AltText">
/// The image's alternative text: the element's own, then its caption, then the <c>Drawio.OpenDiagram</c> dictionary item.
/// </param>
/// <param name="Caption">The caption, or <see langword="null"/> when the element has none.</param>
/// <param name="Href">
/// Where selecting the image leads, or <see langword="null"/> for an unlinked image. It is the viewer's address, or the
/// element's link when that is a path on the website, an anchor, or an <c>http</c>, <c>https</c>, <c>mailto</c> or
/// <c>tel</c> address.
/// </param>
/// <param name="Target">The link's <c>target</c>: <c>_blank</c> for the viewer, otherwise the one the link picker holds.</param>
/// <param name="LinkLabel">The link's accessible name, or <see langword="null"/> to let the image's alternative text name it.</param>
public sealed record DiagramFigure(string? ImageUrl, string AltText, string? Caption, string? Href, string? Target, string? LinkLabel)
{
    /// <summary>Linked shapes in the exported image's coordinate system.</summary>
    public DiagramMap? Map { get; init; }

    /// <summary>Whether to display the exported SVG inline with the page.</summary>
    public bool InlineSvg { get; init; }

    /// <summary>Optional CSS classes supplied by this block for its figure.</summary>
    public string? CssClass { get; init; }

    /// <summary>Reads a diagram element for the current request.</summary>
    /// <param name="element">An element with an <c>image</c> property, and optionally a caption, alternative text and click action.</param>
    /// <param name="context">The request, which supplies the site's path base and services.</param>
    public static DiagramFigure From(IPublishedElement element, HttpContext context)
    {
        var diagram = context.RequestServices.GetRequiredService<IUmbracoApi>().ReadDiagram(element);
        var href = diagram.LinkUrl;
        if (diagram.OpenInViewer)
            href = string.IsNullOrEmpty(diagram.OriginalUrl) ? null
                : DrawioPaths.ViewerUrl(context.Request.PathBase, diagram.OriginalUrl, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        return new(diagram.ImageUrl,
            string.IsNullOrWhiteSpace(diagram.AltText) ? diagram.Caption ?? diagram.Label : diagram.AltText,
            string.IsNullOrWhiteSpace(diagram.Caption) ? null : diagram.Caption,
            IsSafeLink(href) ? href : null,
            diagram.OpenInViewer ? "_blank" : diagram.LinkTarget,
            diagram.OpenInViewer ? diagram.Label : diagram.LinkName)
        {
            Map = DiagramMap.Read(diagram.Map),
            InlineSvg = diagram.InlineSvg,
            CssClass = string.IsNullOrWhiteSpace(diagram.CssClass) ? null : diagram.CssClass.Trim()
        };
    }

    internal static bool IsSafeLink(string? href)
    {
        if (string.IsNullOrWhiteSpace(href) || href != href.Trim() || href.Any(char.IsControl)
            || href.Contains('\\') || href.StartsWith("//", StringComparison.Ordinal)) return false;
        if (href[0] is '/' or '#' or '?') return true;
        return Uri.TryCreate(href, UriKind.Absolute, out var absolute)
            ? absolute.Scheme is "http" or "https" or "mailto" or "tel"
            : Uri.IsWellFormedUriString(href, UriKind.Relative);
    }
}
