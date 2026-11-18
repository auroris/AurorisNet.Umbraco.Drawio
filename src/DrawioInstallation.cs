using System.Xml.Linq;
using Drawio.Compatibility;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;

namespace Drawio;

internal sealed class DrawioInstallation(ICoreScopeProvider scopes, IKeyValueService values, IPackagingService packages,
    IUmbracoApi cms, IMediaTypeService mediaTypes) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    internal const string Identifier = "AurorisNet.Umbraco.Drawio";
    internal static readonly Guid DataTypeKey = new("3ab7c43f-4ccd-4f95-bf81-adba6790f500");
    internal static readonly Guid ElementTypeKey = new("d8370e51-e3bb-449a-944f-cf4678bcbfb3");
    internal const string ElementTypeAlias = "drawioDiagram";
    internal const string MediaTypeAlias = "drawioDiagramMedia";
    internal static readonly Guid MediaTypeKey = new("0c4c6e74-02f5-43cb-92f5-36e3402a693b");
    private const string Installed = Identifier + ".Installed";

    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        if (notification.RuntimeLevel != RuntimeLevel.Run || values.GetValue(Installed) is not null) return;

        using var scope = scopes.CreateCoreScope();
        scope.WriteLock(Constants.Locks.KeyValues);
        // Two servers can start against the same new database.
        if (values.GetValue(Installed) is null)
        {
            using var xml = typeof(DrawioInstallation).Assembly.GetManifestResourceStream(typeof(DrawioInstallation), "package.xml")
                ?? throw new InvalidOperationException("The draw.io package is missing its installation data.");
            var installed = packages.InstallCompiledPackageData(XDocument.Load(xml));
            if (installed.DocumentTypesInstalled.All(type => type.Key != ElementTypeKey))
                throw new InvalidOperationException("Umbraco could not install the Diagram element type.");
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
            values.SetValue(Installed, "true");
        }
        scope.Complete();
    }
}
