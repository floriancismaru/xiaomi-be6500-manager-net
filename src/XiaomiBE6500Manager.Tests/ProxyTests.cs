using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using XiaomiBE6500Manager.Core;

namespace XiaomiBE6500Manager.Tests;

public sealed class ProxyTests : IAsyncLifetime
{
    WebApplication _router = null!;
    RouterProxy _proxy = null!;
    HttpClient _http = null!;
    string _missingFile = Path.Combine(Path.GetTempPath(), $"xt-{Guid.NewGuid():N}.json");
    string? _lastBody, _lastContentLength, _lastTransferEncoding, _lastHost, _lastOrigin, _lastCookie;

    public async Task InitializeAsync()
    {
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        _router = b.Build();
        _router.Run(async ctx =>
        {
            _lastHost = ctx.Request.Headers.Host;
            _lastOrigin = ctx.Request.Headers.Origin;
            _lastCookie = ctx.Request.Headers.Cookie;
            switch (ctx.Request.Path.Value)
            {
                case "/page":
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.Headers.Append("Set-Cookie", "sid=abc; Path=/");
                    await ctx.Response.WriteAsync("<html><head><title>t</title></head><body>你好</body></html>");
                    break;
                case "/json":   // the router serves JSON as text/html
                    ctx.Response.ContentType = "text/html";
                    await ctx.Response.WriteAsync("{\"code\":0,\"token\":\"x\"}");
                    break;
                case "/redir":
                    ctx.Response.StatusCode = 302;
                    ctx.Response.Headers.Location = $"http://{ctx.Request.Host}/page";
                    break;
                case "/echo":
                    _lastContentLength = ctx.Request.Headers.ContentLength?.ToString();
                    _lastTransferEncoding = ctx.Request.Headers.TransferEncoding;
                    using (var r = new StreamReader(ctx.Request.Body)) _lastBody = await r.ReadToEndAsync();
                    ctx.Response.ContentType = "text/html";
                    await ctx.Response.WriteAsync("{\"ok\":1}");
                    break;
                default: ctx.Response.StatusCode = 404; break;
            }
        });
        await _router.StartAsync();
        var routerHost = new Uri(_router.Urls.First()).Authority;
        _proxy = new RouterProxy(routerHost, "en", TranslationAssets.Dictionary, TranslationAssets.Translator, _missingFile);
        await _proxy.StartAsync();
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = new Uri($"http://127.0.0.1:{_proxy.Port}") };
    }

    public async Task DisposeAsync()
    {
        await _proxy.DisposeAsync();
        await _router.DisposeAsync();
        File.Delete(_missingFile);
    }

    [Fact]
    public async Task Full_html_page_gets_translator_after_head()
    {
        var html = await _http.GetStringAsync("/page");
        Assert.Contains("<head><script>window.__XT_CFG={\"lang\":\"en\",\"dict\":", html);
        Assert.Contains("<title>t</title>", html);
        Assert.Contains("你好", html);
    }

    [Fact]
    public async Task Json_served_as_html_is_byte_identical()
    {
        Assert.Equal("{\"code\":0,\"token\":\"x\"}", await _http.GetStringAsync("/json"));
    }

    [Fact]
    public async Task Post_body_has_content_length_and_no_chunking()
    {
        var res = await _http.PostAsync("/echo", new StringContent("username=admin&password=p%40ss", Encoding.UTF8, "application/x-www-form-urlencoded"));
        Assert.Equal("{\"ok\":1}", await res.Content.ReadAsStringAsync());
        Assert.Equal("username=admin&password=p%40ss", _lastBody);
        Assert.Equal("30", _lastContentLength);
        Assert.True(string.IsNullOrEmpty(_lastTransferEncoding));
    }

    [Fact]
    public async Task Origin_and_host_point_at_the_router_not_the_proxy()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/echo") { Content = new StringContent("a=1", Encoding.UTF8, "application/x-www-form-urlencoded") };
        req.Headers.TryAddWithoutValidation("Origin", $"http://127.0.0.1:{_proxy.Port}");
        await _http.SendAsync(req);
        Assert.Equal(new Uri(_router.Urls.First()).Authority, _lastHost);
        Assert.Equal(_router.Urls.First().TrimEnd('/'), _lastOrigin);
    }

    [Fact]
    public async Task Redirect_Location_stays_inside_the_proxy()
    {
        var res = await _http.GetAsync("/redir");
        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        Assert.Equal($"http://127.0.0.1:{_proxy.Port}/page", res.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Router_cookies_stay_in_the_proxy_not_the_browser()
    {
        var res = await _http.GetAsync("/page");
        Assert.False(res.Headers.Contains("Set-Cookie"));
        await _http.GetAsync("/json");
        Assert.Contains("sid=abc", _lastCookie);
    }

    [Fact]
    public async Task Missing_strings_are_recorded()
    {
        var res = await _http.PostAsync("/__xt/missing", new StringContent("{\"page\":\"/p\",\"strings\":[\"未知\"]}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(1, _proxy.MissingCount);
        Assert.Contains("未知", File.ReadAllText(_missingFile));
    }

    [Theory]
    [InlineData("192.168.31.1", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("192.168.31", false)]
    [InlineData("abc", false)]
    public void Only_private_ipv4_is_accepted(string ip, bool ok) => Assert.Equal(ok, IpValidation.IsPrivateIpv4(ip));
}
