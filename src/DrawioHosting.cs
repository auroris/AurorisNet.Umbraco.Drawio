using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Drawio.Compatibility;

namespace Drawio;

/// <summary>
/// Serves the editor at <c>/draw/</c> in Umbraco's pipeline. The configuration scripts are served before the editor's static files.
/// </summary>
internal static class DrawioHosting
{
    private const string Unavailable = "The local draw.io distribution is unavailable. Check the Selfhosted package's publish output or Drawio:WebRoot.";

    internal static void Use(IApplicationBuilder app)
    {
        var services = app.ApplicationServices;
        var options = services.GetRequiredService<IOptions<DrawioOptions>>().Value;
        var root = options.DirectoryPath(services.GetRequiredService<IHostEnvironment>());
        var files = options.IsSelfHosted && Directory.Exists(root) ? new PhysicalFileProvider(root) : null;
        if (files is not null)
            services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(files.Dispose);
        else if (!string.IsNullOrWhiteSpace(options.WebRoot))
            // An explicit folder that is missing is a deployment mistake.
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DrawioHosting).FullName!)
                .LogWarning("The draw.io folder {Folder} named by Drawio:WebRoot does not exist. /draw/ answers 503 until it does.", root);

        app.Use(async (context, next) =>
        {
            if (IsRead(context) && context.Request.Path.StartsWithSegments("/media", StringComparison.OrdinalIgnoreCase))
            {
                var format = context.Request.Query["drawio"].ToString();
                if (format is "raw" or "base64" && DrawioPaths.IsDiagramExtension(context.Request.Path.Value))
                {
                    await DrawioMediaResponse.Serve(context, next, format == "base64");
                    return;
                }
                if (options.RedirectMediaFiles && DrawioPaths.MediaRedirect(context.Request.PathBase.Value,
                    context.Request.Path.Value, context.Request.QueryString) is { } target)
                {
                    context.Response.Headers.AppendCommaSeparatedValues("Vary", "Accept");
                    // The editor requests XML explicitly. Navigating to the same URL still opens the editor page.
                    if (!string.Equals(context.Request.Headers.Accept, "application/xml", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.Redirect(target);
                        return;
                    }
                }
            }
            await next(context);
        });

        app.Map(DrawioPaths.Editor, draw =>
        {
            draw.Use(async (context, next) =>
            {
                context.Response.Headers["X-Drawio-Host"] = DrawioPaths.HostHeader;
                if (!IsRead(context))
                {
                    context.Response.Headers.Allow = "GET, HEAD";
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    return;
                }
                var path = context.Request.Path;
                if (!path.HasValue)
                {
                    // Relative URLs in index.html resolve against the folder, so /draw must become /draw/.
                    context.Response.Redirect(context.Request.PathBase + "/" + context.Request.QueryString);
                    return;
                }
                if (DrawioPaths.IsBlocked(path.Value))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                if (path.Equals("/js/PreConfig.js", StringComparison.OrdinalIgnoreCase) || path.Equals("/js/PostConfig.js", StringComparison.OrdinalIgnoreCase))
                {
                    await ConfigScript(context, pre: path.Value!.EndsWith("PreConfig.js", StringComparison.OrdinalIgnoreCase));
                    return;
                }
                if (path.Equals("/proxy", StringComparison.OrdinalIgnoreCase))
                {
                    RedirectToMedia(context);
                    return;
                }
                if (path.Equals("/settings", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Headers.CacheControl = "no-cache";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        editorUrl = options.IsSelfHosted ? context.Request.PathBase.ToUriComponent() + "/" : "https://embed.diagrams.net/",
                        umbracoMajor = context.RequestServices.GetRequiredService<IUmbracoApi>().MajorVersion,
                        configuration = JsonSerializer.Deserialize<JsonElement>(DrawioScripts.Configuration(new(SitePath(context), options)))
                    }, context.RequestAborted);
                    return;
                }
                if (path.Equals("/editor", StringComparison.OrdinalIgnoreCase) || path.Equals("/viewer", StringComparison.OrdinalIgnoreCase))
                {
                    await DiagramPage(context, path.Equals("/viewer", StringComparison.OrdinalIgnoreCase));
                    return;
                }
                if (!options.IsSelfHosted && path == "/")
                {
                    context.Response.Redirect("https://app.diagrams.net/" + context.Request.QueryString);
                    return;
                }
                await next(context);
            });
            if (files is null)
            {
                draw.Run(async context =>
                {
                    context.Response.StatusCode = options.IsSelfHosted ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync(Unavailable);
                });
                return;
            }
            draw.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            draw.UseStaticFiles(new StaticFileOptions { FileProvider = files });
            draw.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });
        });
    }

    private static bool IsRead(HttpContext context) => HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);

    // Inside Map("/draw"), PathBase includes the editor branch. Site assets live one level above it.
    private static string SitePath(HttpContext context) => context.Request.PathBase.ToUriComponent()[..^DrawioPaths.Editor.Length];

    private static async Task DiagramPage(HttpContext context, bool viewer)
    {
        var script = WebUtility.HtmlEncode(SitePath(context) + DrawioPaths.Plugin + "/standalone.js");
        var title = viewer ? "Diagram viewer" : "Diagram editor";
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (HttpMethods.IsHead(context.Request.Method)) return;
        await context.Response.WriteAsync($$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{title}}</title><style>html,body{height:100%;margin:0;font:16px system-ui}body{display:flex;flex-direction:column}#status{padding:.6rem 1rem}#status:empty{display:none}iframe{flex:1;border:0;width:100%}</style></head>
            <body data-viewer="{{viewer.ToString().ToLowerInvariant()}}"><div id="status" role="status">Loading diagram…</div><iframe title="{{title}}" referrerpolicy="no-referrer"></iframe><script type="module" src="{{script}}"></script></body></html>
            """, context.RequestAborted);
    }

    private static async Task ConfigScript(HttpContext context, bool pre)
    {
        var options = context.RequestServices.GetRequiredService<IOptionsSnapshot<DrawioOptions>>().Value;
        var contributors = context.RequestServices.GetServices<IDrawioScriptContributor>();
        var scriptContext = new DrawioScriptContext(SitePath(context), options);
        var script = pre ? DrawioScripts.PreConfig(scriptContext, contributors) : DrawioScripts.PostConfig(scriptContext, contributors);
        context.Response.ContentType = "text/javascript; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (HttpMethods.IsHead(context.Request.Method)) return;
        var cancel = context.RequestAborted;
        await context.Response.WriteAsync(script, cancel);

        var webRoot = context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        var fileName = pre ? "PreConfig.js" : "PostConfig.js";
        var siteScript = webRoot.GetFileInfo($"{DrawioPaths.Plugin}/{fileName}");
        if (siteScript.Exists && !siteScript.IsDirectory)
        {
            // A contributor may end in a line comment or omit its last semicolon.
            await context.Response.WriteAsync("\n;\n", cancel);
            await context.Response.SendFileAsync(siteScript, cancel);
        }
    }

    /// <summary>
    /// Directs draw.io's legacy proxy requests to the canonical media URL for authorization and delivery.
    /// </summary>
    private static void RedirectToMedia(HttpContext context)
    {
        if (!DrawioPaths.TryGetMediaPath(context.Request.Query["url"].ToString(), out var relative))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        var mediaPath = new PathString("/media/" + relative).ToUriComponent();
        var format = context.Request.Query["base64"] == "1" ? "base64" : "raw";
        context.Response.Redirect(SitePath(context) + mediaPath + QueryString.Create("drawio", format), false, true);
    }
}
