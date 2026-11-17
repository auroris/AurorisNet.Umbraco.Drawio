using Microsoft.Extensions.Hosting;

namespace Drawio;

/// <summary>The <c>Drawio</c> settings section.</summary>
public sealed class DrawioOptions
{
    internal bool Bundled { get; set; }
    internal bool IsSelfHosted => Bundled || !string.IsNullOrWhiteSpace(WebRoot);

    /// <summary>The settings section the package binds.</summary>
    public const string Section = "Drawio";

    /// <summary>
    /// A local draw.io web-app folder, relative to the content root or absolute. Setting it selects self-hosting.
    /// When empty, the Selfhosted package serves its bundled distribution; the base package uses hosted diagrams.net.
    /// </summary>
    public string? WebRoot { get; set; }

    /// <summary>Sends a request for <c>/media/…/*.drawio</c> to the editor, which opens the file, instead of downloading it.</summary>
    public bool RedirectMediaFiles { get; set; } = true;

    /// <summary>How diagram media is removed after its last retained content reference disappears.</summary>
    public SupersededImageHandling SupersededImages { get; set; } = SupersededImageHandling.Delete;

    /// <summary>
    /// The shape libraries draw.io opens by default, separated by semicolons. Leave it empty for draw.io's own choice, or
    /// for its standard set plus <see cref="Libraries"/> when any are configured.
    /// </summary>
    public string? DefaultLibraries { get; set; }

    /// <summary>The title of the sidebar group that lists <see cref="Libraries"/>.</summary>
    public string LibraryGroup { get; set; } = "Extra shapes";

    /// <summary>Extra shape or icon libraries the editor offers and opens by default, each a draw.io library XML file the site serves.</summary>
    public List<DrawioLibrary> Libraries { get; set; } = [];

    /// <summary>Extra draw.io plugins the editor loads, each a script the site serves.</summary>
    public List<DrawioPlugin> Plugins { get; set; } = [];

    /// <summary>The draw.io web app folder this site serves, which may not exist.</summary>
    internal string DirectoryPath(IHostEnvironment environment) => string.IsNullOrWhiteSpace(WebRoot)
        ? Path.Combine(AppContext.BaseDirectory, "DrawioDistribution")
        : Path.GetFullPath(WebRoot, environment.ContentRootPath);
}

/// <summary>A draw.io library file the editor offers in its sidebar.</summary>
public sealed class DrawioLibrary
{
    /// <summary>The library's identifier in draw.io's default library list, such as <c>site-icons</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>The library's name in the sidebar.</summary>
    public string Title { get; set; } = "";

    /// <summary>A short note shown with the library, such as its licence.</summary>
    public string? Description { get; set; }

    /// <summary>The library XML file's path on this site, such as <c>/App_Plugins/MySite/shapes.xml</c>.</summary>
    public string Url { get; set; } = "";
}

/// <summary>A draw.io plugin script the editor loads after it starts.</summary>
public sealed class DrawioPlugin
{
    /// <summary>The plugin's name in draw.io's plugin registry.</summary>
    public string Name { get; set; } = "";

    /// <summary>The plugin script's path on this site, such as <c>/App_Plugins/MySite/plugin.js</c>.</summary>
    public string Url { get; set; } = "";
}

/// <summary>How unreferenced diagram media is removed after content history or a page is deleted.</summary>
public enum SupersededImageHandling
{
    /// <summary>Moves it to the media recycle bin, where an editor can restore it until the bin is emptied.</summary>
    RecycleBin,

    /// <summary>Deletes the media item and file after all retained content references are gone.</summary>
    Delete,

    /// <summary>Leaves it in the media library.</summary>
    Keep,
}
