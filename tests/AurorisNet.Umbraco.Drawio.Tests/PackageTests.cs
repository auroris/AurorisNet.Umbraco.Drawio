using System.Text.Json;
using System.Xml.Linq;
using Drawio;
using Drawio.Migrations;

namespace AurorisNet.Umbraco.Drawio.Tests;

/// <summary>The files the package ships agree with each other and with the code.</summary>
public sealed class PackageTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "package");

    /// <summary>A site that references the package copies the bundled editor unless it opts out.</summary>
    [Fact]
    public void Sites_bundle_the_editor_by_default()
    {
        var targets = XDocument.Load(Path.Combine(Files, "AurorisNet.Umbraco.Drawio.Selfhosted.targets"));
        var bundle = targets.Descendants("DrawioBundle").Single();
        Assert.Equal("true", bundle.Value);
        Assert.Equal("'$(DrawioBundle)' == ''", bundle.Attribute("Condition")!.Value);
        Assert.Equal("'$(DrawioBundle)' == 'true'", targets.Descendants("ItemGroup").Single().Attribute("Condition")!.Value);
        var copy = targets.Descendants("None").Single();
        Assert.Equal(@"$(MSBuildThisFileDirectory)..\drawio\**", copy.Attribute("Include")!.Value);
        Assert.StartsWith(@"DrawioDistribution\", copy.Attribute("Link")!.Value);
    }
    /// <summary>The package.xml data type opens the backoffice editor the manifest registers, and the element type uses it.</summary>
    [Fact]
    public void The_element_type_uses_the_manifest_editor()
    {
        var package = XDocument.Load(Path.Combine(Files, "package.xml"));
        var dataType = package.Descendants("DataType").Single(t => t.Attribute("EditorUiAlias")!.Value == "AurorisNet.Drawio.Editor");
        Assert.Equal(ImportDiagramElementType.DataTypeKey, Guid.Parse(dataType.Attribute("Definition")!.Value));
        var editor = dataType.Attribute("EditorUiAlias")!.Value;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Files, "umbraco-package.json")));
        var extensions = manifest.RootElement.GetProperty("extensions").EnumerateArray().ToArray();
        Assert.Contains(extensions, e => e.GetProperty("type").GetString() == "propertyEditorUi" && e.GetProperty("alias").GetString() == editor);
        Assert.All(extensions.Where(e => e.GetProperty("type").GetString() == "blockEditorCustomView"),
            e => Assert.Equal(ImportDiagramElementType.ElementTypeAlias, e.GetProperty("forContentTypeAlias").GetString()));

        var element = package.Descendants("DocumentType").Single();
        Assert.Equal(ImportDiagramElementType.ElementTypeAlias, element.Element("Info")!.Element("Alias")!.Value);
        Assert.Equal(ImportDiagramElementType.ElementTypeKey, Guid.Parse(element.Element("Info")!.Element("Key")!.Value));
        Assert.Equal("True", element.Element("Info")!.Element("IsElement")!.Value);
        var image = element.Descendants("GenericProperty").Single(p => p.Element("Alias")!.Value == "image");
        Assert.Equal(dataType.Attribute("Definition")!.Value, image.Element("Definition")!.Value);
        Assert.Equal(["image", "caption", "altText", "clickAction", "link"], element.Descendants("GenericProperty").OrderBy(p => int.Parse(p.Element("SortOrder")!.Value)).Select(p => p.Element("Alias")!.Value));
        // Umbraco finds the embedded copy by the migration step's namespace.
        Assert.Contains($"{typeof(ImportDiagramElementType).Namespace}.package.xml", typeof(ImportDiagramElementType).Assembly.GetManifestResourceNames());
    }

    /// <summary>Every script the manifest names exists, under the folder the hosting code and the views address.</summary>
    [Fact]
    public void The_manifest_names_shipped_scripts()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Files, "umbraco-package.json")));
        Assert.Equal(DrawioMigrationPlan.Identifier, manifest.RootElement.GetProperty("id").GetString());
        foreach (var element in manifest.RootElement.GetProperty("extensions").EnumerateArray().Select(e => e.GetProperty("element").GetString()!))
        {
            Assert.StartsWith(DrawioPaths.Plugin + "/", element);
            Assert.True(File.Exists(Path.Combine(Files, Path.GetFileName(element))), element);
        }
        // The editor's draw.io URL is relative to its own folder, two levels below the site root.
        Assert.Equal(2, DrawioPaths.Plugin.Count(c => c == '/'));
        Assert.Contains("../../draw/settings", File.ReadAllText(Path.Combine(Files, "bridge.js")));
    }

    /// <summary>
    /// The editor names each PNG after its media key, which is the mark the cleanup looks for; a change to one side must
    /// change the other.
    /// </summary>
    [Fact]
    public void The_editor_names_files_as_the_cleanup_expects()
    {
        Assert.Contains("`diagram-${id.replaceAll('-', '')}.png`", File.ReadAllText(Path.Combine(Files, "editor.js")));
        var key = Guid.NewGuid();
        Assert.True(DiagramMediaRetention.IsDiagramFile($"/media/abc123/diagram-{key:N}.png", key));
        Assert.True(DiagramMediaRetention.IsDiagramFile($"/media/abc123/DIAGRAM-{key.ToString("N").ToUpperInvariant()}.PNG", key));
        Assert.False(DiagramMediaRetention.IsDiagramFile($"/media/abc123/diagram-{Guid.NewGuid():N}.png", key));
        Assert.False(DiagramMediaRetention.IsDiagramFile("/media/abc123/diagram.png", key));
        Assert.False(DiagramMediaRetention.IsDiagramFile($"/media/abc123/mydiagram-{key:N}.png", key));
        Assert.False(DiagramMediaRetention.IsDiagramFile(null, key));
    }

}
