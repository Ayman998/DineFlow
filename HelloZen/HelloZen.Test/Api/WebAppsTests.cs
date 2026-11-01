using System.Net;
using HelloZen.Api.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HelloZen.Test.Api;

/// <summary>The API serving both Angular apps itself, as on MonsterASP (WebApps:Enabled).</summary>
public class WebAppsTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("hellozen-webapps-").FullName;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        Write("guest/index.html", "guest index");
        Write("guest/main-ABCD2345.js", "guest main");
        Write("guest/favicon.svg", "<svg/>");
        Write("merchant/index.html", "merchant index");
        Write("merchant/main-WXYZ6789.js", "merchant main");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddWebApps();
        _app = builder.Build();
        _app.UseWebApps(_root);
        _app.MapGet("/v1/ping", () => "pong");
        await _app.StartAsync();
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("/", "guest index")]
    [InlineData("/q/Abc-123_xyz", "guest index")]
    [InlineData("/menu", "guest index")]
    [InlineData("/portal/", "merchant index")]
    [InlineData("/portal/orders", "merchant index")]
    [InlineData("/portal/menu", "merchant index")]
    public async Task Deep_links_get_their_apps_index_page_revalidated_every_time(string path, string page)
    {
        var response = await _http.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(page, await response.Content.ReadAsStringAsync());
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Theory]
    [InlineData("/main-ABCD2345.js", "guest main")]
    [InlineData("/portal/main-WXYZ6789.js", "merchant main")]
    public async Task Hashed_bundles_are_cached_for_good(string path, string content)
    {
        var response = await _http.GetAsync(path);

        Assert.Equal(content, await response.Content.ReadAsStringAsync());
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Other_files_are_revalidated()
    {
        var response = await _http.GetAsync("/favicon.svg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task The_bare_portal_path_redirects_to_its_folder()
    {
        var response = await _http.GetAsync("/portal?next=orders");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/portal/?next=orders", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/v1/missing")]
    [InlineData("/sandbox/pay/abc")]
    [InlineData("/hubs/guest")]
    [InlineData("/main-MISSING2.js")]
    public async Task Api_paths_and_missing_files_stay_404(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd("*/*");

        var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Api_endpoints_still_answer()
    {
        Assert.Equal("pong", await _http.GetStringAsync("/v1/ping"));
    }

    [Fact]
    public async Task A_page_navigation_with_a_dot_in_the_path_still_gets_the_app()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/l/some.token");
        request.Headers.Accept.ParseAdd("text/html");

        var response = await _http.SendAsync(request);

        Assert.Equal("guest index", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task App_files_are_compressed_but_api_responses_are_not()
    {
        var page = new HttpRequestMessage(HttpMethod.Get, "/");
        page.Headers.AcceptEncoding.ParseAdd("br");
        var api = new HttpRequestMessage(HttpMethod.Get, "/v1/ping");
        api.Headers.AcceptEncoding.ParseAdd("br");

        Assert.Contains("br", (await _http.SendAsync(page)).Content.Headers.ContentEncoding);
        Assert.Empty((await _http.SendAsync(api)).Content.Headers.ContentEncoding);
    }

    [Fact]
    public void Starting_without_a_built_app_fails_loudly()
    {
        var empty = Directory.CreateTempSubdirectory("hellozen-webapps-empty-").FullName;
        try
        {
            var app = WebApplication.CreateBuilder().Build();
            var error = Assert.Throws<InvalidOperationException>(() => app.UseWebApps(empty));
            Assert.Contains("index.html", error.Message);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
