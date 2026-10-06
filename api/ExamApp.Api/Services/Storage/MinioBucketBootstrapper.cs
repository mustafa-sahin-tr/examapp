using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Storage;

/// <summary>
/// issue #365 (S1): açılışta bilinen MinIO bucket'larını oluşturur (yoksa) ve anonim okuma politikalarını
/// <see cref="MinioBucketPolicies"/>'deki prefix bazlı GEÇİCİ tanıma eşitler. Mevcut bucket'lar da düzeltilir
/// (eski bucket geneli <c>s3:GetObject *</c> politikası SetPolicy ile değiştirilir).
/// <para>
/// MinIO'ya ulaşılamazsa API çökmez: başarısız bucket'lar loglanır ve artan aralıkla (üst sınır
/// <see cref="MaxRetryDelay"/>) tamamı başarılı olana kadar yeniden denenir — düzeltilmemiş bir bucket eski açık
/// politikasıyla kalabileceği için denemeye son verilmez.
/// </para>
/// </summary>
public sealed class MinioBucketBootstrapper : BackgroundService
{
    internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    private readonly IMinIoService _minio;
    private readonly IReadOnlyList<MinioBucketSpec> _buckets;
    private readonly TimeProvider _clock;
    private readonly ILogger<MinioBucketBootstrapper> _logger;

    public MinioBucketBootstrapper(IMinIoService minio, IConfiguration configuration, TimeProvider clock,
        ILogger<MinioBucketBootstrapper> logger)
    {
        _minio = minio;
        _buckets = MinioBucketPolicies.KnownBuckets(configuration.GetSection("MinioConfig")["BucketName"]);
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<MinioBucketSpec> Buckets => _buckets;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // host açılışını bekletme

        IReadOnlyList<MinioBucketSpec> pending = _buckets;
        var delay = InitialRetryDelay;
        var attempt = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                attempt++;
                pending = await EnsureBucketsAsync(pending, stoppingToken);
                if (pending.Count == 0)
                {
                    _logger.LogInformation("[MinIO] Bucket policies ensured for {Count} bucket(s).", _buckets.Count);
                    await WarnAboutUnknownBucketPoliciesAsync(stoppingToken);
                    return;
                }

                _logger.Log(attempt >= 5 ? LogLevel.Error : LogLevel.Warning,
                    "[MinIO] Bucket policy bootstrap incomplete (attempt {Attempt}); retrying in {Delay}. Pending: {Buckets}",
                    attempt, delay, string.Join(", ", pending.Select(b => b.Name)));
                await Task.Delay(delay, _clock, stoppingToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // kapanış
        }
    }

    /// <summary>
    /// issue #365 (security L1): <see cref="MinioBucketPolicies.KnownBuckets"/> dışında politikası olan bucket'ları
    /// UYARI olarak loglar (ör. elle açılmış/eski bir bucket anonim okunabilir kalmış olabilir). Bilinmeyen bucket'a
    /// dokunulmaz — kime ait olduğu bilinmeden politikası değiştirilmez. Uyarı verilen bucket adlarını döner (test için);
    /// listeleme hatası loglanır, bootstrap sonucunu etkilemez.
    /// </summary>
    public async Task<IReadOnlyList<string>> WarnAboutUnknownBucketPoliciesAsync(CancellationToken ct = default)
    {
        var flagged = new List<string>();
        try
        {
            var known = _buckets.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var name in await _minio.ListBucketNamesAsync(ct))
            {
                if (known.Contains(name))
                    continue;
                var policy = await _minio.GetBucketPolicyAsync(name, ct);
                if (policy is null)
                    continue;
                flagged.Add(name);
                _logger.LogWarning(
                    "[MinIO] Bucket {Bucket} is not managed by the exam API but has a bucket policy (possibly anonymous read). Review it manually: {Policy}",
                    name, policy);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[MinIO] Could not audit policies of unknown buckets.");
        }
        return flagged;
    }

    /// <summary>Tek tur: her bucket'ı ayrı dener, başarısız olanları döner (testler doğrudan çağırır).</summary>
    public async Task<IReadOnlyList<MinioBucketSpec>> EnsureBucketsAsync(IEnumerable<MinioBucketSpec> buckets,
        CancellationToken ct = default)
    {
        var failed = new List<MinioBucketSpec>();
        foreach (var spec in buckets)
        {
            try
            {
                await _minio.EnsureBucketAsync(spec.Name, MinioBucketPolicies.BuildAnonymousReadPolicy(spec), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[MinIO] Could not ensure bucket {Bucket}.", spec.Name);
                failed.Add(spec);
            }
        }
        return failed;
    }
}
