using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Drawio.Compatibility;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Packaging;

namespace Drawio.Migrations;

/// <summary>
/// The package's migrations, which Umbraco runs once per site and lists under Packages. Each step runs once, so a site can
/// rename, change or delete what a step installed without the package putting it back.
/// </summary>
public sealed class DrawioMigrationPlan() : PackageMigrationPlan(Identifier, DisplayName, Identifier)
{
    /// <summary>The package's identifier in Umbraco's package list and migration state.</summary>
    public const string Identifier = "AurorisNet.Umbraco.Drawio";

    /// <summary>The package's name in the backoffice.</summary>
    public const string DisplayName = "draw.io for Umbraco";

    /// <inheritdoc />
    protected override void DefinePlan() => To<ImportDiagramElementType>("drawio-diagram-element-type");
}

/// <summary>
/// Imports the diagram data type and element type from the embedded <c>package.xml</c> through Umbraco's package importer,
/// the same path a package installed from the backoffice takes.
/// </summary>
public sealed class ImportDiagramElementType(
    IPackagingService packaging,
    IMediaService media,
    MediaFileManager mediaFiles,
    MediaUrlGeneratorCollection urlGenerators,
    IShortStringHelper shortStrings,
    IContentTypeBaseServiceProvider contentTypes,
    IServiceProvider services,
    IMigrationContext context,
    IOptions<PackageMigrationSettings> settings)
    : AsyncPackageMigrationBase(packaging, media, mediaFiles, urlGenerators, shortStrings, contentTypes, context, settings)
{
    /// <summary>The data type that pairs a single-image media picker with the diagram editor.</summary>
    public static readonly Guid DataTypeKey = new("3ab7c43f-4ccd-4f95-bf81-adba6790f500");

    /// <summary>The <c>drawioDiagram</c> element type.</summary>
    public static readonly Guid ElementTypeKey = new("d8370e51-e3bb-449a-944f-cf4678bcbfb3");

    /// <summary>The element type's alias, which also names its block views.</summary>
    public const string ElementTypeAlias = "drawioDiagram";

    /// <inheritdoc />
    protected override Task MigrateAsync()
    {
        // Umbraco finds package.xml in this types namespace.
        ImportPackage.FromEmbeddedResource<ImportDiagramElementType>().Do();
        services.GetRequiredService<IUmbracoApi>().EnsureRetentionRelationType();
        return Task.CompletedTask;
    }
}
