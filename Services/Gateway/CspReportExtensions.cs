using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

/// <summary>
/// Issue #333 / #316: UI Content-Security-Policy ihlal raporlarının toplayıcısı. ui ve auth-ui politikası
/// <c>report-uri /csp-report</c> taşır; UI'lar gateway origin'inden servis edildiği için (yerelde 5678, prod'da Caddy → gateway)
/// rapor buraya gelir. Ocelot'tan önce yanıtlanır, downstream'e gitmez. Reporting API biçimi (<c>report-to</c>) de kabul edilir,
/// ancak politika şimdilik onu kullanmaz (bkz. deploy/nginx/angular-spa.conf.template).
///
/// Uç anonimdir (tarayıcı kimlik bilgisi göndermez) ve dışarıdan herkes çağırabilir; bu yüzden:
/// - yalnız POST + bilinen rapor içerik türleri (<c>application/csp-report</c>, <c>application/reports+json</c>, <c>application/json</c>);
/// - gövde en çok <see cref="MaxBodyBytes"/>, istek başına en çok <see cref="MaxReportsPerRequest"/> rapor;
/// - IP başına sabit pencere (<c>RateLimiting:CspReport:PermitLimit</c>/<c>WindowSeconds</c>, varsayılan 60/dk) ve tüm istemciler
///   için ortak bir tavan (<c>RateLimiting:CspReport:GlobalPermitLimit</c>, varsayılan 600/pencere): çok IP'den gelen log seli
///   sınırlı kalır (report-uri tarayıcısı istek başına tek rapor gönderir; toplu gövde yalnız elle üretilir);
/// - URL'ler loglanmadan önce origin + path'e indirgenir (sorgu ve fragment atılır: imzalı görsel adresi, aktivasyon jetonu
///   gibi sırlar log'a düşmez), kontrol karakterleri temizlenir ve uzunluk kısaltılır (log injection).
/// Rapor içeriği güvenilmez veridir; yalnız teşhis için Warning olarak loglanır, hiçbir karar buna dayanmaz.
/// </summary>
public static class CspReportExtensions
{
    public const string Path = "/csp-report";
    public const int MaxBodyBytes = 16 * 1024;
    public const int MaxReportsPerRequest = 5;
    public const int DefaultPermitLimit = 60;
    public const int DefaultWindowSeconds = 60;
    public const int DefaultGlobalPermitLimit = 600;
    private const int MaxFieldLength = 200;

    private static readonly string[] AllowedContentTypes = ["application/csp-report", "application/reports+json", "application/json"];

    public sealed record CspViolation(string Directive, string Blocked, string Document, string Source, string Disposition);

    public static IApplicationBuilder UseCspReportCollector(this IApplicationBuilder app, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:CspReport:PermitLimit", DefaultPermitLimit);
        var windowSeconds = configuration.GetValue("RateLimiting:CspReport:WindowSeconds", DefaultWindowSeconds);
        if (permitLimit < 1) throw new InvalidOperationException("RateLimiting:CspReport:PermitLimit must be >= 1.");
        var globalPermitLimit = configuration.GetValue("RateLimiting:CspReport:GlobalPermitLimit", DefaultGlobalPermitLimit);
        if (windowSeconds < 1) throw new InvalidOperationException("RateLimiting:CspReport:WindowSeconds must be >= 1.");
        if (globalPermitLimit < 1) throw new InvalidOperationException("RateLimiting:CspReport:GlobalPermitLimit must be >= 1.");

        FixedWindowRateLimiterOptions Window(int permit) => new()
        {
            PermitLimit = permit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        };
        // Önce IP kovası, sonra ortak kova: tek istemcinin reddedilen istekleri ortak tavanı tüketmez.
        var limiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<string, string>(ip => RateLimitPartition.GetFixedWindowLimiter(ip, _ => Window(permitLimit))),
            PartitionedRateLimiter.Create<string, string>(_ => RateLimitPartition.GetFixedWindowLimiter("global", _ => Window(globalPermitLimit))));

        app.ApplicationServices.GetService<IHostApplicationLifetime>()?.ApplicationStopping.Register(limiter.Dispose);
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("CspReport");

        return app.Use(async (context, next) =>
        {
            if (!string.Equals(context.Request.Path.Value?.TrimEnd('/'), Path, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            context.Response.StatusCode = await HandleAsync(context, limiter, logger);
        });
    }

    private static async Task<int> HandleAsync(HttpContext context, PartitionedRateLimiter<string> limiter, ILogger logger)
    {
        var request = context.Request;
        if (!HttpMethods.IsPost(request.Method))
        {
            context.Response.Headers.Allow = "POST";
            return StatusCodes.Status405MethodNotAllowed;
        }

        var mediaType = request.ContentType?.Split(';')[0].Trim();
        if (mediaType is null || !AllowedContentTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
            return StatusCodes.Status415UnsupportedMediaType;

        if (request.ContentLength > MaxBodyBytes)
            return StatusCodes.Status413PayloadTooLarge;

        using (var lease = limiter.AttemptAcquire(LoginRateLimitExtensions.ClientKey(context.Connection.RemoteIpAddress)))
        {
            if (!lease.IsAcquired)
                return StatusCodes.Status429TooManyRequests;
        }

        var body = await ReadBoundedAsync(request.Body, context.RequestAborted);
        if (body is null)
            return StatusCodes.Status413PayloadTooLarge;

        foreach (var v in Parse(body))
        {
            logger.LogWarning(
                "CSP violation: directive={Directive} blocked={Blocked} document={Document} source={Source} disposition={Disposition}",
                v.Directive, v.Blocked, v.Document, v.Source, v.Disposition);
        }

        return StatusCodes.Status204NoContent;
    }

    /// <summary>Gövdeyi en çok <see cref="MaxBodyBytes"/> bayt okur; aşarsa null (Content-Length yok/yalan olabilir).</summary>
    private static async Task<byte[]?> ReadBoundedAsync(Stream body, CancellationToken ct)
    {
        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(total), ct)) > 0)
            total += read;
        return total > MaxBodyBytes ? null : buffer[..total];
    }

    /// <summary>
    /// Eski biçim (<c>{"csp-report": {...}}</c>, report-uri) ve Reporting API biçimi (<c>[{"type":"csp-violation","body":{...}}]</c>,
    /// report-to). Bozuk JSON / tanınmayan yapı → boş liste (204 döner; tarayıcı yeniden denemez, saldırgana ipucu verilmez).
    /// </summary>
    public static IReadOnlyList<CspViolation> Parse(ReadOnlySpan<byte> body)
    {
        var result = new List<CspViolation>();
        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = 8 });
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("csp-report", out var legacy)
                && legacy.ValueKind == JsonValueKind.Object)
            {
                result.Add(new CspViolation(
                    SanitizeToken(Str(legacy, "effective-directive") ?? Str(legacy, "violated-directive")),
                    SanitizeBlocked(Str(legacy, "blocked-uri")),
                    SanitizeUrl(Str(legacy, "document-uri")),
                    SanitizeSource(Str(legacy, "source-file"), Num(legacy, "line-number")),
                    SanitizeToken(Str(legacy, "disposition"))));
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var report in root.EnumerateArray())
                {
                    if (result.Count >= MaxReportsPerRequest) break;
                    if (report.ValueKind != JsonValueKind.Object
                        || Str(report, "type") != "csp-violation"
                        || !report.TryGetProperty("body", out var b) || b.ValueKind != JsonValueKind.Object)
                        continue;

                    result.Add(new CspViolation(
                        SanitizeToken(Str(b, "effectiveDirective")),
                        SanitizeBlocked(Str(b, "blockedURL")),
                        SanitizeUrl(Str(b, "documentURL") ?? Str(report, "url")),
                        SanitizeSource(Str(b, "sourceFile"), Num(b, "lineNumber")),
                        SanitizeToken(Str(b, "disposition"))));
                }
            }
        }
        catch (JsonException)
        {
            // bozuk rapor: yok say
        }
        return result;
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static long? Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n) ? n : null;

    /// <summary>Mutlak http(s)/ws(s) URL → origin + path (sorgu/fragment atılır); başka her şey "-" ya da kısa anahtar sözcük.</summary>
    public static string SanitizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "-";
        if (uri.Scheme is not ("http" or "https" or "ws" or "wss"))
            return Clean(uri.Scheme);
        // userinfo (user:pw@) da atılır: tarayıcı göndermez, ama uydurma rapor log'a parola yazdırmasın.
        return Clean(uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped));
    }

    /// <summary>blocked-uri: URL ise <see cref="SanitizeUrl"/>; değilse "inline", "eval", "data", "blob" gibi anahtar sözcük.</summary>
    public static string SanitizeBlocked(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        return Uri.TryCreate(value, UriKind.Absolute, out _) ? SanitizeUrl(value) : SanitizeToken(value);
    }

    private static string SanitizeSource(string? file, long? line)
    {
        var source = SanitizeUrl(file);
        return line is > 0 && source != "-" ? $"{source}:{line}" : source;
    }

    /// <summary>Direktif adı / disposition / anahtar sözcük: yalnız harf, rakam, '-', '\'' (ör. 'unsafe-inline').</summary>
    public static string SanitizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        var sb = new StringBuilder(Math.Min(value.Length, 64));
        foreach (var c in value)
        {
            if (sb.Length >= 64) break;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '\'') sb.Append(c);
            else if (c == ' ') break; // "script-src-elem 'self' ..." → yalnız direktif adı
        }
        return sb.Length == 0 ? "-" : sb.ToString();
    }

    private static string Clean(string value)
    {
        var sb = new StringBuilder(Math.Min(value.Length, MaxFieldLength));
        foreach (var c in value)
        {
            if (sb.Length >= MaxFieldLength) break;
            if (char.IsControl(c)) continue;
            // U+2028/2029 satır ayracı ve biçim karakterleri (bidi U+202E vb.) metin log'unda satırı bölebilir/ters çevirebilir.
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
