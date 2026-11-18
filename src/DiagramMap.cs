using System.Globalization;
using System.Text.Json;

namespace Drawio;

/// <summary>Clickable regions for a draw.io export, scaled to the displayed image by its SVG viewBox.</summary>
public sealed record DiagramMap(string ViewBox, DiagramRegion[] Links)
{
    internal static DiagramMap? Read(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var map = JsonSerializer.Deserialize<DiagramMap>(json, JsonSerializerOptions.Web);
            if (map?.Links is null || !ValidViewBox(map.ViewBox)) return null;
            var links = map.Links.Where(link => link is not null && DiagramFigure.IsSafeLink(link.Href))
                .Select(link => link with { Shapes = (link.Shapes ?? []).Where(shape => !string.IsNullOrWhiteSpace(shape?.D)).ToArray() })
                .Where(link => link.Shapes.Length > 0).ToArray();
            return links.Length == 0 ? null : map with { Links = links };
        }
        catch (JsonException) { return null; }
    }

    private static bool ValidViewBox(string? value)
    {
        var bounds = value?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (bounds is not { Length: 4 }) return false;
        for (var i = 0; i < bounds.Length; i++)
        {
            if (!double.TryParse(bounds[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                || !double.IsFinite(n) || (i >= 2 && n <= 0)) return false;
        }
        return true;
    }
}

/// <summary>A linked shape or group of shapes in drawing order.</summary>
public sealed record DiagramRegion(string Href, string? Label, DiagramOutline[] Shapes, bool NewTab);

/// <summary>An SVG path and its transform. A stroke width identifies a linked line or connector.</summary>
public sealed record DiagramOutline(string D, string? Transform, double? StrokeWidth);
