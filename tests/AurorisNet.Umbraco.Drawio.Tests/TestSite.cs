using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace AurorisNet.Umbraco.Drawio.Tests;

/// <summary>A disposable, normally composed Umbraco site with SQLite and Kestrel, and a stand-in draw.io app.</summary>
internal sealed class TestSite(WebApplication app, string root, PathString pathBase) : IAsyncDisposable
{
    public IServiceProvider Services => app.Services;
    public Uri Address { get; } = new(new Uri(app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single()), pathBase.ToUriComponent() + "/");

    /// <summary>The folder Drawio:WebRoot names, which holds a few files shaped like the draw.io web app.</summary>
    public string WebRoot { get; } = Path.Combine(root, "draw");

    public static async Task<TestSite> StartAsync(IDictionary<string, string?> settings, CancellationToken cancellationToken,
        PathString pathBase = default, bool hostSetsPathBase = true, Action<IApplicationBuilder>? configurePipeline = null)
    {
        var root = Directory.CreateTempSubdirectory("drawio-test-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
        File.WriteAllText(Path.Combine(root, "wwwroot", "website.drawio"), "<mxfile />");
        var draw = Path.Combine(root, "draw");
        foreach (var (name, text) in new Dictionary<string, string>
        {
            ["index.html"] = "<!doctype html><title>draw.io</title>",
            ["js/app.min.js"] = "var App={};",
            ["js/app.min.js.map"] = "{}",
            ["js/PreConfig.js"] = "// stock",
            ["WEB-INF/web.xml"] = "<web-app/>",
            [".gitignore"] = "*",
        })
        {
            var path = Path.Combine(draw, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(TestSite).Assembly.GetName().Name,
            ContentRootPath = root,
            EnvironmentName = "Development",
        });
        // Serve the package's App_Plugins files from the project, as a site that references the package does.
        builder.WebHost.UseSetting(WebHostDefaults.StaticWebAssetsKey,
                Path.Combine(AppContext.BaseDirectory, typeof(TestSite).Assembly.GetName().Name + ".staticwebassets.runtime.json"))
            .UseStaticWebAssets();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:umbracoDbDSN"] = $"Data Source={Path.Combine(root, "umbraco.db")};Pooling=False",
            ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "Microsoft.Data.Sqlite",
            ["Umbraco:CMS:Global:Id"] = Guid.NewGuid().ToString(),
            ["Umbraco:CMS:Global:VersionCheckPeriod"] = "0",
            ["Umbraco:CMS:WebRouting:ApplicationUrlDetection"] = "FirstRequest",
            // Keep background jobs from writing to SQLite while the tests do.
            ["Umbraco:CMS:DistributedJobs:Delay"] = "01:00:00",
            ["Umbraco:CMS:Global:DatabaseServerMessenger:TimeBetweenSyncOperations"] = "01:00:00",
            ["Umbraco:CMS:Global:DatabaseServerRegistrar:WaitTimeBetweenCalls"] = "01:00:00",
            ["Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries:0"] = "xunit.",
            ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
            ["Umbraco:CMS:Unattended:UpgradeUnattended"] = "true",
            ["Umbraco:CMS:Unattended:UnattendedUserName"] = "Diagram test administrator",
            ["Umbraco:CMS:Unattended:UnattendedUserEmail"] = "drawio@example.test",
            ["Umbraco:CMS:Unattended:UnattendedUserPassword"] = "Drawio!Test5278",
            ["Umbraco:CMS:Unattended:UnattendedTelemetryLevel"] = "Minimal",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        // Use the real discovery path; never call the package's composer directly here.
        builder.CreateUmbracoBuilder().AddBackOffice().AddWebsite().AddComposers().Build();

        var app = builder.Build();
        try
        {
            await app.BootUmbracoAsync();
            if (pathBase.HasValue)
            {
                // A virtual application cannot serve a request that escaped to the host root.
                app.Use(async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments(pathBase)) await next();
                    else context.Response.StatusCode = StatusCodes.Status404NotFound;
                });
                if (hostSetsPathBase) app.UsePathBase(pathBase);
            }
            configurePipeline?.Invoke(app);
            app.UseUmbraco().WithMiddleware(u => { u.UseBackOffice(); u.UseWebsite(); })
                .WithEndpoints(u => { u.UseBackOfficeEndpoints(); u.UseWebsiteEndpoints(); });
            app.MapControllers();
            await app.StartAsync(cancellationToken);
            // Umbraco installs, then runs package migrations, in a hosted service after startup.
            var installer = app.Services.GetServices<IHostedService>().OfType<BackgroundService>()
                .SingleOrDefault(service => service.GetType().Name == "UnattendedUpgradeBackgroundService");
            if (installer?.ExecuteTask is { } installation)
                await installation.WaitAsync(TimeSpan.FromMinutes(3), cancellationToken);
            var runtime = app.Services.GetRequiredService<IRuntimeState>();
            Assert.True(runtime.Level == RuntimeLevel.Run, runtime.BootFailedException?.ToString() ?? runtime.Level.ToString());
            return new TestSite(app, root, pathBase);
        }
        catch
        {
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
            await Serilog.Log.CloseAndFlushAsync();
            DeleteRoot(root);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync(CancellationToken.None);
        await app.DisposeAsync();
        await Serilog.Log.CloseAndFlushAsync();
        DeleteRoot(root);
    }

    private static void DeleteRoot(string root)
    {
        // Delete only the uniquely named temporary site created above.
        var fullRoot = Path.GetFullPath(root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!fullRoot.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullRoot).StartsWith("drawio-test-", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected test directory: {fullRoot}");
        Directory.Delete(fullRoot, recursive: true);
    }
}
