using Drawio.Compatibility;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Packaging;

namespace Drawio;

internal sealed class DrawioMigrationPlan() : PackageMigrationPlan(DrawioInstallation.Identifier, DisplayName, DrawioInstallation.Identifier)
{
    internal const string DisplayName = "draw.io for Umbraco";
    public override bool IgnoreCurrentState => false;
    protected override void DefinePlan() => To<DrawioInstallation>("diagram-definitions");
}

internal sealed class DrawioInstallation(IPackagingService packages, IMediaService media, MediaFileManager files,
    MediaUrlGeneratorCollection urls, IShortStringHelper strings, IContentTypeBaseServiceProvider types,
    IMigrationContext context, IOptions<PackageMigrationSettings> settings, IMediaTypeService mediaTypes, IUmbracoApi cms)
    : AsyncPackageMigrationBase(packages, media, files, urls, strings, types, context, settings)
{
    internal const string Identifier = "AurorisNet.Umbraco.Drawio";
    internal static readonly Guid DataTypeKey = new("3ab7c43f-4ccd-4f95-bf81-adba6790f500");
    internal static readonly Guid ElementTypeKey = new("d8370e51-e3bb-449a-944f-cf4678bcbfb3");
    internal const string ElementTypeAlias = "drawioDiagram";
    internal const string MediaTypeAlias = "drawioDiagramMedia";
    internal static readonly Guid MediaTypeKey = new("0c4c6e74-02f5-43cb-92f5-36e3402a693b");

    private readonly bool runSchemaAndContentMigrations = settings.Value.RunSchemaAndContentMigrations;

    protected override async Task MigrateAsync()
    {
        if (!runSchemaAndContentMigrations) return;

        ImportPackage.FromEmbeddedResource<DrawioInstallation>().Do();
        var folder = await mediaTypes.GetAsync(new Guid("f38bd2d7-65d0-48e6-95dc-87ce06ec2d3d"))
            ?? throw new InvalidOperationException("Umbraco's Folder media type is missing.");
        var allowed = folder.AllowedContentTypes?.ToArray() ?? [];
        if (allowed.All(type => type.Key != MediaTypeKey))
        {
            folder.AllowedContentTypes = allowed.Append(new ContentTypeSort(MediaTypeKey,
                allowed.Select(type => type.SortOrder).DefaultIfEmpty(-1).Max() + 1, MediaTypeAlias)).ToArray();
            if (!(await mediaTypes.UpdateAsync(folder, Constants.Security.SuperUserKey)).Success)
                throw new InvalidOperationException("Umbraco could not allow diagrams in media folders.");
        }
        cms.EnsureRetentionRelationType();
    }
}
