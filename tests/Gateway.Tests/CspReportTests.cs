using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gateway.Tests;

// Issue #333 / #316: UI CSP ihlal raporu toplayıcısı (/csp-report) — anonim, sınırlı, sırları loglamaz, Ocelot'a gitmez.
public class CspReportTests
{
    private const string Legacy = """
        {"csp-report":{"document-uri":"http://localhost:5678/lesson/12?token=secret#frag","violated-directive":"script-src-elem",
        "effective-directive":"script-src-elem","original-policy":"default-src 'self'","blocked-uri":"https://evil.example/x.js?sig=abc",
        "source-file":"http://localhost:5678/main.js?v=1","line-number":42,"disposition":"enforce","status-code":200}}
        """;

    private const string ReportingApi = """
        [{"type":"csp-violation","age":1,"url":"http://localhost:5678/test/5","user_agent":"x",
          "body":{"documentURL":"http://localhost:5678/test/5?a=1","effectiveDirective":"img-src","blockedURL":"https://cdn.example/a.png?X-Amz-Signature=zzz",
          "sourceFile":"http://localhost:5678/chunk.js","lineNumber":7,"disposition":"enforce"}},
         {"type":"deprecation","body":{}},
         {"type":"csp-violation","body":{"documentURL":"http://localhost:5678/","effectiveDirective":"script-src-elem","blockedURL":"inline","disposition":"report"}}]
        """;

    private sealed class Capture : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (message.StartsWith("CSP violation", StringComparison.Ordinal)) Messages.Enqueue(message);
        }
    }

    private static async Task<(HttpClient Client, Capture Logs, Func<int> DownstreamHits)> CreateClientAsync(int? permit = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        var logs = new Capture();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        if (permit is not null)
            builder.Configuration["RateLimiting:CspReport:PermitLimit"] = permit.ToString();
        var app = builder.Build();
        app.UseCspReportCollector(app.Configuration);
        var hits = 0;
        app.Run(ctx =>
        {
            Interlocked.Increment(ref hits);
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await app.StartAsync();
        return (app.GetTestClient(), logs, () => Volatile.Read(ref hits));
    }

    private static HttpRequestMessage Post(string body, string contentType = "application/csp-report", string path = CspReportExtensions.Path)
        => new(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    [Fact]
    public async Task Legacy_report_is_logged_without_query_or_fragment_and_returns_204()
    {
        var (client, logs, hits) = await CreateClientAsync();

        var response = await client.SendAsync(Post(Legacy));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        hits().ShouldBe(0, "rapor Ocelot'a/downstream'e gitmemeli");
        var line = logs.Messages.ShouldHaveSingleItem();
        line.ShouldContain("directive=script-src-elem");
        line.ShouldContain("blocked=https://evil.example/x.js");
        line.ShouldContain("document=http://localhost:5678/lesson/12");
        line.ShouldContain("source=http://localhost:5678/main.js:42");
        line.ShouldContain("disposition=enforce");
        line.ShouldNotContain("secret");
        line.ShouldNotContain("sig=");
        line.ShouldNotContain("frag");
    }

    [Fact]
    public async Task Reporting_api_batch_logs_only_csp_violations()
    {
        var (client, logs, _) = await CreateClientAsync();

        var response = await client.SendAsync(Post(ReportingApi, "application/reports+json"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        logs.Messages.Count.ShouldBe(2);
        var first = logs.Messages.First();
        first.ShouldContain("directive=img-src");
        first.ShouldContain("blocked=https://cdn.example/a.png");
        first.ShouldNotContain("X-Amz-Signature");
        logs.Messages.Last().ShouldContain("blocked=inline");
        logs.Messages.Last().ShouldContain("disposition=report");
    }

    [Theory]
    [InlineData("/csp-report/")]
    [InlineData("/CSP-Report")]
    public async Task Path_match_is_case_and_trailing_slash_insensitive(string path)
    {
        var (client, _, hits) = await CreateClientAsync();

        (await client.SendAsync(Post(Legacy, path: path))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        hits().ShouldBe(0);
    }

    [Fact]
    public async Task Other_paths_pass_through()
    {
        var (client, logs, hits) = await CreateClientAsync();

        (await client.SendAsync(Post(Legacy, path: "/api/csp-report"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        hits().ShouldBe(1);
        logs.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_is_rejected_with_405()
    {
        var (client, _, hits) = await CreateClientAsync();

        var response = await client.GetAsync(CspReportExtensions.Path);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldContain("POST");
        hits().ShouldBe(0);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task Unknown_content_type_is_rejected_with_415(string contentType)
    {
        var (client, logs, _) = await CreateClientAsync();

        (await client.SendAsync(Post(Legacy, contentType))).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        logs.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Oversized_body_is_rejected_with_413()
    {
        var (client, logs, _) = await CreateClientAsync();
        var big = "{\"csp-report\":{\"blocked-uri\":\"" + new string('a', CspReportExtensions.MaxBodyBytes) + "\"}}";

        (await client.SendAsync(Post(big))).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        logs.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Oversized_chunked_body_without_content_length_is_rejected_with_413()
    {
        var (client, logs, _) = await CreateClientAsync();
        var bytes = Encoding.UTF8.GetBytes(new string(' ', CspReportExtensions.MaxBodyBytes + 10) + "{}");
        var request = new HttpRequestMessage(HttpMethod.Post, CspReportExtensions.Path)
        {
            Content = new StreamContent(new MemoryStream(bytes))
        };
        request.Content.Headers.ContentType = new("application/csp-report");
        request.Headers.TransferEncodingChunked = true;

        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        logs.Messages.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"csp-report\":\"x\"}")]
    [InlineData("[[[[[[[[[[[[[[[[[[[[]]]]]]]]]]]]]]]]]]]]")]
    public async Task Malformed_report_is_ignored_with_204(string body)
    {
        var (client, logs, _) = await CreateClientAsync();

        (await client.SendAsync(Post(body))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        logs.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Batch_is_capped_per_request()
    {
        var (client, logs, _) = await CreateClientAsync();
        var one = """{"type":"csp-violation","body":{"effectiveDirective":"img-src","blockedURL":"data"}}""";
        var batch = "[" + string.Join(',', Enumerable.Repeat(one, CspReportExtensions.MaxReportsPerRequest + 15)) + "]";

        (await client.SendAsync(Post(batch, "application/reports+json"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        logs.Messages.Count.ShouldBe(CspReportExtensions.MaxReportsPerRequest);
    }

    [Fact]
    public async Task Requests_over_the_ip_limit_get_429()
    {
        var (client, logs, _) = await CreateClientAsync(permit: 3);

        for (var i = 0; i < 3; i++)
            (await client.SendAsync(Post(Legacy))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.SendAsync(Post(Legacy))).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        logs.Messages.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Requests_over_the_global_limit_get_429()
    {
        // TestServer'da tüm istekler aynı (boş) IP'den gelir; IP limiti yüksek tutulup ortak tavan ayrı denenir.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Configuration["RateLimiting:CspReport:PermitLimit"] = "100";
        builder.Configuration["RateLimiting:CspReport:GlobalPermitLimit"] = "2";
        await using var app = builder.Build();
        app.UseCspReportCollector(app.Configuration);
        await app.StartAsync();
        var client = app.GetTestClient();

        for (var i = 0; i < 2; i++)
            (await client.SendAsync(Post(Legacy))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.SendAsync(Post(Legacy))).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("http://a.example/p?q=1#f", "http://a.example/p")]
    [InlineData("https://u:p@a.example/p?q", "https://a.example/p")]
    [InlineData("wss://a.example/hub?access_token=t", "wss://a.example/hub")]
    [InlineData("data:image/png;base64,AAAA", "data")]
    [InlineData("blob:http://a.example/uuid", "blob")]
    [InlineData("relative/path", "-")]
    [InlineData("", "-")]
    [InlineData(null, "-")]
    public void SanitizeUrl_keeps_origin_and_path_only(string? input, string expected)
        => CspReportExtensions.SanitizeUrl(input).ShouldBe(expected);

    [Theory]
    [InlineData("inline", "inline")]
    [InlineData("eval", "eval")]
    [InlineData("script-src-elem 'self' https://x", "script-src-elem")]
    [InlineData("img-src\r\nFAKE LOG LINE", "img-srcFAKE")]
    [InlineData("<script>", "script")]
    public void SanitizeToken_strips_everything_but_directive_characters(string input, string expected)
        => CspReportExtensions.SanitizeToken(input).ShouldBe(expected);

    [Fact]
    public void Long_urls_are_truncated_and_control_characters_removed()
    {
        var sanitized = CspReportExtensions.SanitizeUrl("http://a.example/" + new string('x', 500));
        sanitized.Length.ShouldBe(200);

        var bidi = CspReportExtensions.SanitizeBlocked("https://a.example/p\u202Eexe.js\u2028x");
        bidi.ShouldNotContain("\u202E");
        bidi.ShouldNotContain("\u2028");
    }
}
