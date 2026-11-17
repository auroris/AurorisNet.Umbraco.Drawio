namespace Drawio;

/// <summary>The published diagram block's click action.</summary>
public enum DiagramClickAction
{
    /// <summary>Open the diagram in the read-only viewer.</summary>
    Viewer = 0,
    /// <summary>Follow the selected Umbraco link; an empty picker renders an unlinked image.</summary>
    Link = 1,
    /// <summary>Render an unlinked image.</summary>
    None = 2,
}
