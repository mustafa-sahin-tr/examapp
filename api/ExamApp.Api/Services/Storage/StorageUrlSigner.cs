using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace ExamApp.Api.Services.Storage;

/// <summary>
/// issue #365 (S2): DB'de saklanan <c>/img/{bucket}/{key}</c> değerini tarayıcının gateway üzerinden açabileceği kısa
/// ömürlü imzalı göreli URL'ye çevirir (<c>/img/{bucket}/{key}?X-Amz-...</c>).
/// </summary>
public interface IStorageUrlSigner
{
    /// <summary>
    /// <paramref name="storedUrl"/> verilen alanlardan birinin allowlist'indeyse imzalı göreli URL; değilse (boş, tam URL,
    /// yabancı bucket/prefix, <c>question-transfer/</c>, gateway'den imzası bozulmadan geçemeyecek anahtar, imzalama
    /// yapılandırılmamış) değer DEĞİŞMEDEN döner. İmzasız değer hiçbir erişim vermez: S4'te bucket'lar özel olunca 403 alır.
    /// </summary>
    string? SignForBrowser(string? storedUrl, IReadOnlyList<StorageArea> areas);
}

/// <summary>
/// MinIO SDK ile çevrimdışı presign (ağ çağrısı yok: bölge sabit <see cref="Region"/>). İmzanın <c>host</c> başlığı
/// GATEWAY'İN MinIO'ya giderken kullandığı adrestir (<c>MinioConfig:PresignEndpoint</c>, yoksa <c>MinioConfig:Endpoint</c>):
/// Ocelot <c>/img/{everything}</c> isteğini Host'u downstream adrese çevirerek, yolu ve query'yi koruyarak iletir.
/// <para>
/// Önbellek kararlılığı: imza zamanı <see cref="SigningSlice"/> dilimine yuvarlanır ve dilim başına tek URL üretilir
/// (aynı görsel 15 dk boyunca aynı URL → tarayıcı önbelleği çalışır). Geçerlilik <see cref="Expiry"/> (4 saat);
/// dilim sonunda üretilen URL'nin en az 3 saat 45 dk ömrü kalır. Süre bilerek uzun: istemci URL'yi yeniden çekmediği
/// için uzun süren bir test/açık sayfa, süresi dolmuş imzada 403 alıp görseli kaybederdi (MinIO süresi dolmuş presign'ı
/// herkese açık prefix'te bile reddeder); S3 ayrıca /img hatasında UI tarafında yeniden çekme ekler.
/// <c>response-cache-control</c> tarayıcı önbelleğini 15 dk ile sınırlar (URL'nin kalan ömründen kısa).
/// </para>
/// </summary>
public sealed class MinioStorageUrlSigner : IStorageUrlSigner, IDisposable
{
    public const string Region = "us-east-1";
    public static readonly TimeSpan Expiry = TimeSpan.FromHours(4);
    public static readonly TimeSpan SigningSlice = TimeSpan.FromMinutes(15);
    public const string ResponseCacheControl = "private, max-age=900";
    private const int CacheEntryLimit = 50_000;

    private readonly StorageAreaPolicy _policy;
    private readonly TimeProvider _clock;
    private readonly ILogger<MinioStorageUrlSigner> _logger;
    private readonly IMinioClient? _client;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = CacheEntryLimit });
    private long _lastFailureLoggedSlice = long.MinValue;

    public MinioStorageUrlSigner(IConfiguration configuration, StorageAreaPolicy policy, TimeProvider clock,
        ILogger<MinioStorageUrlSigner> logger)
    {
        _policy = policy;
        _clock = clock;
        _logger = logger;

        var section = configuration.GetSection("MinioConfig");
        // Geçiş anahtarı (varsayılan açık): yanlış PresignEndpoint tüm görselleri 403'e düşürür; bucket'lar özel olana
        // (S4) kadar imzalamayı yeniden dağıtım olmadan kapatabilmek için. Kapalıyken değerler imzasız yazılır.
        if (!section.GetValue("PresignImageUrls", true))
        {
            _logger.LogWarning("[MinIO] Presigning disabled by MinioConfig:PresignImageUrls=false. Image URLs are emitted unsigned.");
            return;
        }

        var endpoint = FirstNonEmpty(section["PresignEndpoint"], section["Endpoint"]);
        // issue #402 (O1): imza YALNIZ ayrı presign hesabıyla atılır (s3:GetObject, izinli prefix'ler —
        // deploy/scripts/minio-presign-init.sh). Erişim anahtarı X-Amz-Credential'da her oturumlu kullanıcıya görünür;
        // root (MinioConfig:AccessKey) ile imzalamaya geri DÜŞÜLMEZ. Development dışında eksik/root değer açılışta
        // MinioPresignCredentialGuard ile patlar; burada (Development) imzasız yazılır.
        var accessKey = section[MinioPresignCredentialGuard.AccessKeyKey];
        var secretKey = section[MinioPresignCredentialGuard.SecretKeyKey];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            _logger.LogWarning(
                "[MinIO] Presigning disabled: MinioConfig endpoint or presign credentials (PresignAccessKey/PresignSecretKey) missing. Image URLs are emitted unsigned.");
            return;
        }
        if (MinioPresignCredentialGuard.IsRootKey(accessKey, section["AccessKey"]))
        {
            _logger.LogError(
                "[MinIO] Presigning disabled: MinioConfig:PresignAccessKey equals the root MinioConfig:AccessKey; refusing to expose the root key in image URLs.");
            return;
        }

        _client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithRegion(Region) // bölge sabit → presign GetBucketLocation ağ çağrısı yapmaz
            .Build();
        // Yanlış host'a bağlı imza bucket herkese açık olsa bile MinIO'da 403 alır (canlı doğrulandı) — ops için görünür olsun.
        _logger.LogInformation(
            "[MinIO] Presigned image URLs are bound to host {Host}; the gateway /img route must forward to exactly this host:port (MinioConfig:PresignEndpoint).",
            endpoint);
    }

    public string? SignForBrowser(string? storedUrl, IReadOnlyList<StorageArea> areas)
    {
        if (_client is null || string.IsNullOrEmpty(storedUrl) ||
            !MinioObjectUrl.TryParse(storedUrl, out var bucket, out var key) ||
            !_policy.IsAllowed(bucket, key, areas) ||
            !MinioObjectUrl.IsGatewaySafeKey(key)) // & / %XX Ocelot'tan geçerken imzayı bozar → imzasız bırak
            return storedUrl;

        var now = _clock.GetUtcNow().UtcDateTime;
        var slice = SliceStart(now);
        var cacheKey = new SignedUrlKey(bucket, key, slice.Ticks);
        if (_cache.TryGetValue(cacheKey, out object? cached))
        {
            if (cached is string hit)
                return hit;
            if (ReferenceEquals(cached, FailedMarker))
                return storedUrl; // bu dilimde zaten başarısız oldu → yeniden deneme yok (exception seli olmasın)
        }

        object entry;
        string? result;
        try
        {
            result = Presign(bucket, key, slice);
            entry = result;
        }
        catch (Exception ex)
        {
            // İmzasız değer erişim vermez; yanıtı bozmak yerine onu yaz. Aynı nesne bu dilimde yeniden denenmez
            // (başarısızlık işareti önbelleğe girer) ve log dilim başına en fazla bir kez yazılır.
            if (Interlocked.Exchange(ref _lastFailureLoggedSlice, slice.Ticks) != slice.Ticks)
                _logger.LogError(ex, "[MinIO] Presign failed for {Bucket}; emitting unsigned URLs for failing objects until the next signing slice.", bucket);
            result = storedUrl;
            entry = FailedMarker;
        }

        // MemoryCache kendi sistem saatini kullanır; dilim sonuna kalan süre TimeProvider'a göre hesaplanıp göreli verilir.
        var remaining = slice + SigningSlice - now;
        if (remaining > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, entry, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = remaining,
            });
        }
        return result;
    }

    /// <summary>Test dikişi: presign'ı değiştirir (hata yolu testleri). Üretimde null → MinIO SDK.</summary>
    internal Func<string, string, DateTime, string>? PresignOverride { get; init; }

    private static readonly object FailedMarker = new();

    /// <summary>Saat <see cref="SigningSlice"/> katına aşağı yuvarlanır (UTC).</summary>
    public static DateTime SliceStart(DateTime utcNow)
    {
        var utc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % SigningSlice.Ticks, DateTimeKind.Utc);
    }

    private string Presign(string bucket, string key, DateTime slice)
    {
        if (PresignOverride is { } overrideFn)
            return overrideFn(bucket, key, slice);

        var args = new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithExpiry((int)Expiry.TotalSeconds)
            .WithRequestDate(slice)
            .WithHeaders(new Dictionary<string, string> { ["response-cache-control"] = ResponseCacheControl });

        // Bölge sabitken SDK 6.0.4 presign'ı senkron tamamlar (yalnız HMAC); yine de güvenli bekle.
        var task = _client!.PresignedGetObjectAsync(args);
        var absolute = task.IsCompletedSuccessfully ? task.Result : task.GetAwaiter().GetResult();

        var uri = new Uri(absolute, UriKind.Absolute);
        if (!uri.AbsolutePath.StartsWith("/" + bucket + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected presigned path shape.");
        return "/img" + uri.PathAndQuery;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        return null;
    }

    public void Dispose() => _cache.Dispose();

    private readonly record struct SignedUrlKey(string Bucket, string Key, long SliceTicks);
}
