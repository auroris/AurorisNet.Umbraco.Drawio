using Drawio.Compatibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbraco.Extensions;

namespace Drawio;

/// <summary>Registers the package when Umbraco discovers it, so adding the package is the whole setup.</summary>
public sealed class DrawioComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder) => builder.AddDrawio();
}

/// <summary>Registers the package on an <see cref="IUmbracoBuilder"/>.</summary>
public static class DrawioBuilderExtensions
{
    /// <summary>
    /// Adds the editor and viewer under <c>/draw/</c>, the media proxy and redirect, diagram media retention and the
    /// diagram definitions and upload folder. Calling it again changes nothing.
    /// </summary>
    public static IUmbracoBuilder AddDrawio(this IUmbracoBuilder builder)
    {
        if (builder.Services.Any(service => service.ServiceType == typeof(DrawioMarker))) return builder;
        builder.Services.AddSingleton<DrawioMarker>();
        builder.Services.AddOptions<DrawioOptions>().Bind(builder.Config.GetSection(DrawioOptions.Section)).ValidateOnStart();
        builder.Services.PostConfigure<StaticFileOptions>(options =>
        {
            options.ContentTypeProvider ??= new FileExtensionContentTypeProvider();
            if (options.ContentTypeProvider is FileExtensionContentTypeProvider provider)
                provider.Mappings.TryAdd(".drawio", "application/xml");
        });
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DrawioOptions>, DrawioOptionsValidation>());
        builder.Services.Configure<UmbracoPipelineOptions>(options =>
            options.AddFilter(new UmbracoPipelineFilter("Drawio") { PrePipeline = DrawioHosting.Use }));
        builder.Services.AddTransient<DiagramMediaRetention>();
        UmbracoCompatibility.Register(builder);
        builder.PackageMigrationPlans().Add<DrawioMigrationPlan>();
        return builder;
    }

    private sealed class DrawioMarker;
}
