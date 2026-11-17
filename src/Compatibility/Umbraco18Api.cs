using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;

namespace Drawio.Compatibility;

internal sealed class Umbraco18Api(IServiceProvider services, IDocumentRepository documents) : UmbracoApi(services)
{
    public override int MajorVersion => 18;

    // Umbraco 18 moved the service method to an interface absent in 17. Its underlying repository API is shared.
    // The retention caller holds the same content-tree lock as Umbraco's service implementation.
    protected override IEnumerable<IContent> GetVersions(int contentId) => documents.GetAllVersions(contentId);
}
