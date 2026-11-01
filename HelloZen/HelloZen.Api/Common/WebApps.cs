using System.Text.RegularExpressions;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace HelloZen.Api.Common;

/// <summary>
/// Serves the built Angular apps from the API itself, for hosts with no reverse proxy in front (IIS on
/// MonsterASP, see deploy/monsterasp): the merchant portal under /portal/ from wwwroot/merchant, and the
/// guest app on every other path from wwwroot/guest. Behind Caddy (deploy/Caddyfile) this stays off.
/// </summary>
public static partial class WebApps
{
    public const string PortalPath = "/portal";

    /// <summary>The API's own paths. They never fall back to an app's index.html, so an unknown API URL still answers 404.</summary>
    private static readonly PathString[] ApiPaths = ["/v1", "/hubs", "/media", "/health", "/sandbox", "/swagger"];

    public static bool IsEnabled(IConfiguration config) => config.GetValue<bool>("WebApps:Enabled");

    public static IServiceCollection AddWebApps(this IServiceCollection services) =>
        services.AddResponseCompression(o =>
        {
            // Only the apps' files are compressed (see UseWebApps), never API responses that carry tokens.
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
        });

    /// <param name="webRoot">Folder holding guest/ and merchant/, each an Angular build with its index.html.</param>
    public static IApplicationBuilder UseWebApps(this IApplicationBuilder app, string webRoot)
    {
        var guest = OpenApp(webRoot, "guest");
        var merchant = OpenApp(webRoot, "merchant");

        app.UseWhen(context => !IsApi(context.Request.Path), web => web.UseResponseCompression());
        app.UseStaticFiles(new StaticFileOptions { FileProvider = merchant, RequestPath = PortalPath, OnPrepareResponse = SetCaching });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = guest, OnPrepareResponse = SetCaching });

        // Deep links (/menu, /q/{token}, /portal/orders) are the apps' own routes: answer them with the
        // app's index.html and let the Angular router take over. A missing script or stylesheet stays a 404
        // instead of turning into HTML the browser can't run.
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (context.GetEndpoint() is not null || IsApi(request.Path)
                || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                || (Path.HasExtension(request.Path.Value) && !AcceptsHtml(request)))
            {
                await next(context);
                return;
            }

            // The portal's files are relative to /portal/, so the bare path needs its slash.
            if (request.Path == PortalPath)
            {
                context.Response.Redirect(PortalPath + "/" + request.QueryString);
                return;
            }

            var index = (request.Path.StartsWithSegments(PortalPath) ? merchant : guest).GetFileInfo("index.html");
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.ContentLength = index.Length;
            if (!HttpMethods.IsHead(request.Method)) await context.Response.SendFileAsync(index);
        });
        return app;
    }

    private static PhysicalFileProvider OpenApp(string webRoot, string name)
    {
        var folder = Path.Combine(webRoot, name);
        if (!File.Exists(Path.Combine(folder, "index.html")))
            throw new InvalidOperationException($"WebApps:Enabled is on, but {folder} has no index.html. "
                + "Publish with the MonsterASP profile, which builds both apps into wwwroot (see deploy/monsterasp/README.md).");
        return new PhysicalFileProvider(folder);
    }

    private static bool IsApi(PathString path) => ApiPaths.Any(api => path.StartsWithSegments(api));

    private static bool AcceptsHtml(HttpRequest request) =>
        request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    // Angular puts a content hash in its js and css file names (main-4BFBHHNZ.js), so those can be cached
    // for good. Everything else (index.html, icons, the service worker's files) is revalidated, or phones
    // keep loading the previous release.
    private static void SetCaching(StaticFileResponseContext file) =>
        file.Context.Response.Headers.CacheControl = HashedName().IsMatch(file.File.Name)
            ? "public, max-age=31536000, immutable"
            : "no-cache";

    [GeneratedRegex(@"-[A-Z0-9]{8}\.(js|css)$")]
    private static partial Regex HashedName();
}
