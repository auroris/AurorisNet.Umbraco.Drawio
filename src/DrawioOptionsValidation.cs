using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Drawio;

/// <summary>
/// Checks the <c>Drawio</c> settings when the site starts. Libraries and plugins load into the editor's page with the
/// signed-in editor's access to the site, so each must be a script or file this site serves.
/// </summary>
internal sealed partial class DrawioOptionsValidation : IValidateOptions<DrawioOptions>
{
    public ValidateOptionsResult Validate(string? name, DrawioOptions options)
    {
        var errors = new List<string>();
        if (options.WebRoot?.Any(char.IsControl) == true)
            errors.Add("Drawio:WebRoot must be a folder path.");
        // An empty value keeps draw.io's own set, as a missing one does.
        if (!string.IsNullOrWhiteSpace(options.DefaultLibraries) && !LibraryList().IsMatch(options.DefaultLibraries.Trim()))
            errors.Add("Drawio:DefaultLibraries must be library names separated by semicolons, such as general;uml;flowchart.");
        if (string.IsNullOrWhiteSpace(options.LibraryGroup))
            errors.Add("Drawio:LibraryGroup must not be empty.");
        if (!Enum.IsDefined(options.SupersededImages))
            errors.Add("Drawio:SupersededImages must be RecycleBin, Delete or Keep.");

        for (var i = 0; i < options.Libraries.Count; i++)
        {
            var library = options.Libraries[i];
            if (library is null) { errors.Add($"Drawio:Libraries:{i} is empty."); continue; }
            if (!Identifier().IsMatch(library.Id ?? ""))
                errors.Add($"Drawio:Libraries:{i}:Id must be letters, digits, '-' or '_'.");
            if (string.IsNullOrWhiteSpace(library.Title))
                errors.Add($"Drawio:Libraries:{i}:Title must not be empty.");
            if (!DrawioPaths.IsLocalPath(library.Url))
                errors.Add($"Drawio:Libraries:{i}:Url must be a path on this site, such as /App_Plugins/MySite/shapes.xml.");
        }
        foreach (var id in options.Libraries.Where(l => l is not null).GroupBy(l => l.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"Drawio:Libraries names '{id.Key}' more than once.");

        for (var i = 0; i < options.Plugins.Count; i++)
        {
            var plugin = options.Plugins[i];
            if (plugin is null) { errors.Add($"Drawio:Plugins:{i} is empty."); continue; }
            if (!Identifier().IsMatch(plugin.Name ?? ""))
                errors.Add($"Drawio:Plugins:{i}:Name must be letters, digits, '-' or '_'.");
            if (!DrawioPaths.IsLocalPath(plugin.Url))
                errors.Add($"Drawio:Plugins:{i}:Url must be a path on this site, such as /App_Plugins/MySite/plugin.js.");
        }
        foreach (var plugin in options.Plugins.Where(p => p is not null).GroupBy(p => p.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"Drawio:Plugins names '{plugin.Key}' more than once.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex Identifier();

    [GeneratedRegex("^[A-Za-z0-9_.-]+(;[A-Za-z0-9_.-]+)*$")]
    private static partial Regex LibraryList();
}
