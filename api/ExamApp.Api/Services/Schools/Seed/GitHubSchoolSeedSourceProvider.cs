using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary>
/// Okul listesini <c>dalgali/MEB-okul-listesi</c> deposundan, sabitlenmiş commit'ten indirir ve
/// git-ignore edilmiş <c>Data/SeedFixtures/</c> altına önbellekler (issue #216).
///
/// <para>MEB alan adlarına HİÇBİR istek atılmaz — tek adres <see cref="SourceUrl"/>
/// (raw.githubusercontent.com). Commit SHA sabit olduğu için dosya değişmez; indirilen içeriğin
/// SHA-256'sı <see cref="ExpectedSha256"/> ile karşılaştırılır, uyuşmazsa hata verilir (bozuk
/// indirme / vekil sunucu müdahalesi). Önbellekteki dosyanın hash'i uyuşmuyorsa yeniden indirilir.</para>
/// </summary>
public sealed class GitHubSchoolSeedSourceProvider : ISchoolSeedSourceProvider
{
    /// <summary>dalgali/MEB-okul-listesi — data/meb-okullar.csv, çekim tarihi 2026-08-31.</summary>
    public const string SourceCommitSha = "0f2b3e6af008f283c1f9b43bdcbd1556eab8da1f";

    public const string SourceUrl =
        "https://raw.githubusercontent.com/dalgali/MEB-okul-listesi/" + SourceCommitSha + "/data/meb-okullar.csv";

    /// <summary>Yukarıdaki commit'teki dosyanın SHA-256'sı (9.300.250 bayt). Değişirse kaynak değişmiş demektir.</summary>
    public const string ExpectedSha256 = "710fae0e71df551e58827e06d3a1be7c8ff1a1bf2ba88f8707bb1a7645e2c9a4";

    public const string CacheDirectoryName = "SeedFixtures";

    /// <summary>Adında commit'in ilk 12 karakteri var: farklı bir SHA'ya geçilirse eski önbellek karışmaz.</summary>
    public static readonly string CacheFileName = $"meb-okullar-{SourceCommitSha[..12]}.csv";

    /// <summary>İndirme üst sınırı (gerçek dosya ~9.3 MB). Aşılırsa indirme kesilir, dosya silinir.</summary>
    public const long MaxDownloadBytes = 32L * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubSchoolSeedSourceProvider> _logger;
    private readonly string _cacheDirectory;
    private readonly string _sourceUrl;
    private readonly string _expectedSha256;

    public GitHubSchoolSeedSourceProvider(
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<GitHubSchoolSeedSourceProvider> logger)
        : this(httpClientFactory, environment, logger, SourceUrl, ExpectedSha256)
    {
    }

    /// <summary>Test kancası: URL ve beklenen hash değiştirilebilir (sahte handler + fixture içerik).</summary>
    internal GitHubSchoolSeedSourceProvider(
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<GitHubSchoolSeedSourceProvider> logger,
        string sourceUrl,
        string expectedSha256)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheDirectory = Path.Combine(environment.ContentRootPath, "Data", CacheDirectoryName);
        _sourceUrl = sourceUrl;
        _expectedSha256 = expectedSha256;
    }

    public string CachePath => Path.Combine(_cacheDirectory, CacheFileName);

    public async Task<SchoolSeedSource> GetAsync(CancellationToken ct = default)
    {
        var path = CachePath;

        if (File.Exists(path))
        {
            var cachedHash = await ComputeSha256Async(path, ct);
            if (string.Equals(cachedHash, _expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Okul listesi önbellekten alındı: {Path} (sha256={Hash})", path, cachedHash);
                return new SchoolSeedSource(path, cachedHash, FromCache: true, () => File.OpenRead(path));
            }

            _logger.LogWarning(
                "Önbellekteki okul listesinin hash'i beklenenle uyuşmuyor (beklenen {Expected}, bulunan {Actual}); yeniden indirilecek.",
                _expectedSha256, cachedHash);
        }

        Directory.CreateDirectory(_cacheDirectory);
        var tempPath = path + ".download";

        try
        {
            _logger.LogInformation("Okul listesi indiriliyor: {Url}", _sourceUrl);
            var client = _httpClientFactory.CreateClient(nameof(GitHubSchoolSeedSourceProvider));
            client.Timeout = TimeSpan.FromMinutes(5);

            using var response = await client.GetAsync(_sourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new SchoolSeedSourceException(
                    $"Okul listesi indirilemedi: {_sourceUrl} → HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (response.Content.Headers.ContentLength is { } declared && declared > MaxDownloadBytes)
            {
                throw new SchoolSeedSourceException(
                    $"Okul listesi beklenenden büyük: Content-Length {declared} bayt > üst sınır {MaxDownloadBytes} bayt.");
            }

            await using (var body = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(tempPath))
            {
                await CopyWithLimitAsync(body, file, MaxDownloadBytes, ct);
            }
        }
        catch (HttpRequestException ex)
        {
            TryDelete(tempPath);
            throw new SchoolSeedSourceException($"Okul listesi indirilemedi: {_sourceUrl} → {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            TryDelete(tempPath);
            throw new SchoolSeedSourceException($"Okul listesi indirme zaman aşımı: {_sourceUrl}", ex);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        var hash = await ComputeSha256Async(tempPath, ct);
        if (!string.Equals(hash, _expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(tempPath);
            throw new SchoolSeedSourceException(
                $"İndirilen okul listesinin SHA-256'sı beklenenle uyuşmuyor. Beklenen {_expectedSha256}, bulunan {hash}. " +
                "Kaynak commit sabit olduğu için bu bozuk bir indirme ya da araya giren bir vekil demektir; dosya atıldı.");
        }

        File.Move(tempPath, path, overwrite: true);
        _logger.LogInformation("Okul listesi indirildi ve önbelleğe yazıldı: {Path} (sha256={Hash})", path, hash);
        return new SchoolSeedSource(_sourceUrl, hash, FromCache: false, () => File.OpenRead(path));
    }

    /// <summary>Sayaçlı kopya: <paramref name="maxBytes"/> aşılırsa kesilir (çağıran dosyayı siler).</summary>
    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new SchoolSeedSourceException(
                    $"Okul listesi indirme üst sınırı aşıldı ({maxBytes} bayt); indirme kesildi ve dosya atıldı.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // en iyi çaba — bir sonraki çalıştırmada üzerine yazılır
        }
    }
}

/// <summary>Kaynak dosyaya erişilemedi (ağ, HTTP durum, hash uyuşmazlığı). Mesaj kullanıcıya gösterilebilir.</summary>
public sealed class SchoolSeedSourceException : Exception
{
    public SchoolSeedSourceException(string message) : base(message) { }
    public SchoolSeedSourceException(string message, Exception inner) : base(message, inner) { }
}
