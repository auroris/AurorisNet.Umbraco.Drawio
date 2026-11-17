using System.Security.Cryptography;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.IO;

namespace Drawio;

/// <summary>
/// Serves the editor at <c>/draw/</c> ahead of Umbraco's routing. Under IIS, <c>/draw</c> can instead be a virtual directory
/// holding the stock draw.io app; IIS then passes the configuration scripts, settings, editor/viewer wrappers and media
/// proxy to the application, which answer the same way here.
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

        if (options.RedirectMediaFiles)
        {
            app.Use(async (context, next) =>
            {
                if (IsRead(context) && DrawioPaths.MediaRedirect(context.Request.PathBase, context.Request.Path.Value) is { } target)
                {
                    context.Response.Redirect(target);
                    return;
                }
                await next(context);
            });
        }

        // ASP.NET Core's default static-file types omit .drawio. Make website files available to the standalone reader.
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = new FileExtensionContentTypeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".drawio"] = "application/xml"
            })
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
                    await Proxy(context);
                    return;
                }
                if (path.Equals("/settings", StringComparison.OrdinalIgnoreCase))
                {
                    var site = context.Request.PathBase.Value![..^DrawioPaths.Editor.Length];
                    context.Response.Headers.CacheControl = "no-cache";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        editorUrl = options.IsSelfHosted ? site + DrawioPaths.Editor + "/" : "https://embed.diagrams.net/",
                        configuration = JsonSerializer.Deserialize<JsonElement>(DrawioScripts.Configuration(new(site, options)))
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

    private static async Task DiagramPage(HttpContext context, bool viewer)
    {
        var site = context.Request.PathBase.Value![..^DrawioPaths.Editor.Length];
        var script = WebUtility.HtmlEncode(site + DrawioPaths.Plugin + "/standalone.js");
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
        // PathBase here includes the /draw branch; the site's own paths start above it.
        var pathBase = context.Request.PathBase.Value ?? "";
        var site = pathBase[..^DrawioPaths.Editor.Length];
        var options = context.RequestServices.GetRequiredService<IOptionsSnapshot<DrawioOptions>>().Value;
        var contributors = context.RequestServices.GetServices<IDrawioScriptContributor>();
        var scriptContext = new DrawioScriptContext(site, options);
        var script = pre ? DrawioScripts.PreConfig(scriptContext, contributors) : DrawioScripts.PostConfig(scriptContext, contributors);
        context.Response.ContentType = "text/javascript; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.WriteAsync(script, context.RequestAborted);
    }

    /// <summary>
    /// Returns a media file to draw.io, which opens <c>#U</c> links through this proxy. <c>base64=1</c> asks for the bytes as
    /// Base64 text, which draw.io uses for PNGs that carry a diagram.
    /// </summary>
    private static async Task Proxy(HttpContext context)
    {
        if (!DrawioPaths.TryGetMediaPath(context.Request.Query["url"].ToString(), out var relative))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        var media = context.RequestServices.GetRequiredService<MediaFileManager>().FileSystem;
        if (!media.FileExists(relative))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var encoded = context.Request.Query["base64"] == "1";
        var size = media.GetSize(relative);
        if (encoded && size > DrawioPaths.MaxBase64Bytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        context.Response.ContentType = encoded ? "text/plain" : "application/octet-stream";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.ContentLength = encoded ? 4 * ((size + 2L) / 3L) : size;
        if (HttpMethods.IsHead(context.Request.Method)) return;
        await using var input = media.OpenFile(relative);
        if (!encoded)
        {
            await input.CopyToAsync(context.Response.Body, context.RequestAborted);
            return;
        }
        using var transform = new ToBase64Transform();
        await using var output = new CryptoStream(context.Response.Body, transform, CryptoStreamMode.Write, leaveOpen: true);
        await input.CopyToAsync(output, context.RequestAborted);
        await output.FlushFinalBlockAsync(context.RequestAborted);
    }
}
