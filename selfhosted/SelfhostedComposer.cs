using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Drawio.Selfhosted;

/// <summary>Selects the bundled distribution when this package is installed.</summary>
public sealed class SelfhostedComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
        => builder.Services.Configure<DrawioOptions>(options => options.Bundled = true);
}
