using Umbraco.Cms.Core.Models;

namespace Drawio.Compatibility;

internal sealed class Umbraco17Api(IServiceProvider services) : UmbracoApi(services)
{
    public override int MajorVersion => 17;
    protected override IEnumerable<IContent> GetVersions(int contentId) => Content.GetVersions(contentId);
}
