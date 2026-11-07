using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace XiaomiBE6500Manager.Core;

/// <summary>
/// Local reverse proxy from 127.0.0.1 (random port) to the router. It keeps the router session
/// cookies, keeps redirects inside the proxy and injects the translator into full HTML pages.
/// </summary>
public sealed partial class RouterProxy : IAsyncDisposable
{
    static readonly HashSet<string> SkipRequest =
        new(StringComparer.OrdinalIgnoreCase) { "host", "accept-encoding", "content-length", "transfer-encoding", "cookie", "connection" };
    static readonly HashSet<string> ContentRequest =
        new(StringComparer.OrdinalIgnoreCase) { "content-type", "content-language", "content-disposition" };
    static readonly HashSet<string> SkipResponse =
        new(StringComparer.OrdinalIgnoreCase) { "set-cookie", "content-length", "content-encoding", "transfer-encoding", "connection", "keep-alive" };

    readonly string _routerIp;
    readonly string _dictionaryJson;
    readonly string _translatorJs;
    readonly string _configTail;
    readonly object _missingLock = new();
    readonly SortedDictionary<string, SortedSet<string>> _missing = new(StringComparer.Ordinal);
    // Router cookies live here, not in the browser, so stopping the proxy ends the session.
    readonly ConcurrentDictionary<string, string> _jar = new();
    readonly HttpClient _client;
    WebApplication? _app;

    /// <summary>'en', 'ro', or 'zh' (original, no translation).</summary>
    public volatile string Lang;

    /// <summary>Where strings the dictionary doesn't cover are recorded.</summary>
    public string MissingFile { get; }
    public int MissingCount { get { lock (_missingLock) return _missing.Count; } }
    public event Action? MissingChanged;

    public int Port { get; private set; }
    public string EntryUrl => $"http://127.0.0.1:{Port}/cgi-bin/luci/web";

    public RouterProxy(string routerIp, string lang, string dictionaryJson, string translatorJs, string missingFile)
    {
        _routerIp = routerIp;
        Lang = lang;
        _dictionaryJson = dictionaryJson;
        _translatorJs = translatorJs;
        _configTail = dictionaryJson[dictionaryJson.IndexOf("\"dict\"", StringComparison.Ordinal)..];
        MissingFile = missingFile;
        _client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,   // Location must be rewritten, not followed
            UseCookies = false,          // the jar above is the only cookie store
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        }) { Timeout = TimeSpan.FromSeconds(60) };
        try
        {
            var j = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(missingFile));
            if (j != null) foreach (var (k, v) in j) _missing[k] = new SortedSet<string>(v, StringComparer.Ordinal);
        }
        catch { /* no previous capture */ }
    }

    public async Task StartAsync()
    {
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        var app = b.Build();
        app.Run(HandleAsync);
        await app.StartAsync();
        Port = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
        _app = app;
    }

    /// <summary>Stops the server and forgets the router session.</summary>
    public async ValueTask DisposeAsync()
    {
        _jar.Clear();
        if (_app != null)
        {
            try { using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)); await _app.StopAsync(cts.Token); await _app.DisposeAsync(); } catch { }
            _app = null;
        }
        _client.Dispose();
    }

    async Task HandleAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        if (req.Path == "/__xt/missing" && HttpMethods.IsPost(req.Method))
        {
            await RecordMissingAsync(ctx);
            return;
        }
        try
        {
            var self = $"http://127.0.0.1:{Port}";
            var routerBase = $"http://{_routerIp}";
            using var up = new HttpRequestMessage(new HttpMethod(req.Method), routerBase + req.GetEncodedPathAndQuery());

            // Send a real Content-Length: many embedded web servers reject chunked request bodies.
            byte[] body;
            using (var ms = new MemoryStream())
            {
                await req.Body.CopyToAsync(ms, ctx.RequestAborted);
                body = ms.ToArray();
            }
            if (body.Length > 0 || HttpMethods.IsPost(req.Method) || HttpMethods.IsPut(req.Method))
                up.Content = new ByteArrayContent(body);

            foreach (var (name, values) in req.Headers)
            {
                if (SkipRequest.Contains(name)) continue;
                // The router must see its own address, not the proxy's.
                var v = string.Join(", ", values.Select(x => x?.Replace(self, routerBase)));
                if (ContentRequest.Contains(name)) up.Content?.Headers.TryAddWithoutValidation(name, v);
                else up.Headers.TryAddWithoutValidation(name, v);
            }
            up.Headers.Host = _routerIp;

            var cookies = new Dictionary<string, string>();
            foreach (var h in req.Headers.Cookie)
                foreach (var c in (h ?? "").Split(';'))
                {
                    var i = c.IndexOf('=');
                    if (i > 0) cookies[c[..i].Trim()] = c[(i + 1)..].Trim();
                }
            foreach (var (k, v) in _jar) cookies[k] = v;
            if (cookies.Count > 0) up.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(e => $"{e.Key}={e.Value}")));

            using var res = await _client.SendAsync(up, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
            ctx.Response.StatusCode = (int)res.StatusCode;

            if (res.Headers.TryGetValues("Set-Cookie", out var setCookies))
                foreach (var c in setCookies)
                {
                    var kv = c.Split(';')[0];
                    var i = kv.IndexOf('=');
                    if (i > 0) _jar[kv[..i].Trim()] = kv[(i + 1)..].Trim();
                }
            foreach (var (name, values) in res.Headers.Concat(res.Content.Headers))
            {
                if (SkipResponse.Contains(name)) continue;
                var vs = values.Select(x => name.Equals("Location", StringComparison.OrdinalIgnoreCase)
                    ? x.Replace(routerBase, self) : x).ToArray();   // keep redirects inside the proxy
                ctx.Response.Headers.Append(name, new StringValues(vs));
            }

            if (res.Content.Headers.ContentType?.MediaType == "text/html")
            {
                var html = Encoding.UTF8.GetString(await res.Content.ReadAsByteArrayAsync(ctx.RequestAborted));
                await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(Inject(html)), ctx.RequestAborted);
            }
            else
            {
                await res.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
            }
        }
        catch (Exception e) when (!ctx.Response.HasStarted)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = 502;
            await ctx.Response.WriteAsync($"Proxy error: {e.Message}");
        }
        catch { /* client went away mid-response */ }
    }

    [GeneratedRegex(@"<head[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadTag();

    string Inject(string html)
    {
        var cfg = $"{{\"lang\":{JsonSerializer.Serialize(Lang)},{_configTail}";
        var tag = $"<script>window.__XT_CFG={cfg};</script><script>{_translatorJs}</script>";
        // The router serves JSON and HTML fragments as text/html too; only full pages get the script.
        var m = HeadTag().Match(html);
        return m.Success ? html.Insert(m.Index + m.Length, tag) : html;
    }

    async Task RecordMissingAsync(HttpContext ctx)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var page = doc.RootElement.GetProperty("page").GetString() ?? "";
            lock (_missingLock)
            {
                foreach (var s in doc.RootElement.GetProperty("strings").EnumerateArray())
                {
                    var str = s.GetString();
                    if (str == null) continue;
                    if (!_missing.TryGetValue(str, out var pages)) _missing[str] = pages = new(StringComparer.Ordinal);
                    pages.Add(page);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(MissingFile)!);
                File.WriteAllText(MissingFile, JsonSerializer.Serialize(
                    _missing.ToDictionary(e => e.Key, e => e.Value.ToList()),
                    new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            }
            MissingChanged?.Invoke();
        }
        catch { /* best-effort diagnostics */ }
        ctx.Response.StatusCode = 204;
    }
}
