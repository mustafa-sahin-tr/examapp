using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Services.Dashboard;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Teachers;

/// <summary>
/// issue #265: öğretmen dashboard'unun <c>own-activity-summary</c> ve <c>students-activity-summary</c> uçları aynı ağır
/// öğrenci aktivitesi toplamasını (TestInstanceQuestions × WorksheetInstances, 90 güne kadar) kullanır. UI iki ucu sayfa
/// açılışında PARALEL çağırdığı için yalnızca "sonucu önbellekle" yetmez — ikinci istek birinci bitmeden gelir. Bu yüzden:
/// <list type="bullet">
/// <item>aynı anahtar için süren hesap paylaşılır (single-flight: ikinci istek birincinin Task'ını bekler),</item>
/// <item>başarılı sonuç kısa süre (<see cref="DashboardOptions.TeacherActivityCacheSeconds"/>, varsayılan 60 sn) tutulur.</item>
/// </list>
/// Süreç içi (replica başına) — Redis'e serileştirme maliyetine değmez; N replica'da en fazla N hesap. Hata/iptal
/// önbelleğe alınmaz. Anahtar çağıranın sorumluluğundadır (öğretmen + kapsam + pencere başlangıcı).
/// </summary>
public interface ITeacherActivityCache
{
    /// <param name="itemCount">
    /// Sonucun eleman sayısı — önbellek girdisinin boyutu buna orantılıdır (<c>1 + count/100</c>), böylece
    /// <c>SizeLimit</c> girdi SAYISINI değil toplam belleği sınırlar (security review LOW-2).
    /// </param>
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, Func<T, int> itemCount,
        CancellationToken ct = default)
        where T : class;
}

public sealed class TeacherActivityCache : ITeacherActivityCache, IDisposable
{
    /// <summary>
    /// Toplam boyut birimi üst sınırı. 1 birim ≈ 100 öğrenci satırı (+1 girdi başı) → en kötü ~1M küçük kayıt.
    /// Dolunca yeni sonuç önbelleğe alınmaz / eskiler sıkıştırılır; istek yine hesaplanıp döner.
    /// </summary>
    internal const long SizeLimit = 10_000;

    /// <summary>Girdi boyutu: öğrenci satırı sayısıyla orantılı (security review LOW-2).</summary>
    internal static long EntrySize(int itemCount) => 1 + Math.Max(0, itemCount) / 100;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = SizeLimit });
    private readonly object _gate = new();
    private readonly TimeSpan _ttl;

    public TeacherActivityCache(IOptions<DashboardOptions> options)
        : this(TimeSpan.FromSeconds(options.Value.TeacherActivityCacheSeconds))
    {
    }

    public TeacherActivityCache(TimeSpan ttl) => _ttl = ttl < TimeSpan.Zero ? TimeSpan.Zero : ttl;

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, Func<T, int> itemCount,
        CancellationToken ct = default)
        where T : class
    {
        // Beklediğimiz hesabın sahibi isteği iptal edilirse (tarayıcı sekmeyi kapattı vb.) hesabı üstlenmeyi deneriz;
        // kendi isteğimiz iptal edilene dek tekrarlanır (sahip biz olursak döngü factory sonucuyla biter).
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            TaskCompletionSource<T>? owner = null;
            Task<T> shared;
            lock (_gate)
            {
                if (_cache.TryGetValue(key, out Task<T>? existing) && existing is not null)
                {
                    shared = existing;
                }
                else
                {
                    owner = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                    shared = owner.Task;
                    // Süren hesap da girdi olarak durur (paylaşım için; boyutu henüz bilinmiyor → 1). Tamamlanınca gerçek
                    // boyutla yeniden yazılır; TTL 0 ise silinir.
                    _cache.Set(key, shared, EntryOptions(1, _ttl > TimeSpan.Zero ? _ttl : TimeSpan.FromMinutes(5)));
                }
            }

            if (owner is not null)
            {
                // Hesap sahibin kendi akışında (kendi DbContext'iyle) çalışır — context'e eşzamanlı erişim yok.
                try
                {
                    var value = await factory(ct);
                    owner.SetResult(value);
                    if (_ttl > TimeSpan.Zero)
                        Replace(key, shared, EntrySize(itemCount(value)));
                    else
                        Remove(key, shared);
                    return value;
                }
                catch (OperationCanceledException)
                {
                    Remove(key, shared);
                    owner.SetCanceled(CancellationToken.None);
                    throw;
                }
                catch (Exception ex)
                {
                    Remove(key, shared);
                    owner.SetException(ex);
                    _ = owner.Task.Exception; // bekleyen yoksa UnobservedTaskException gürültüsü olmasın
                    throw;
                }
            }

            try
            {
                return await shared.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Sahibin isteği iptal edildi, bizimki değil → yeniden dene (sahip girdiyi zaten sildi).
            }
        }
    }

    private MemoryCacheEntryOptions EntryOptions(long size, TimeSpan ttl)
        => new() { AbsoluteExpirationRelativeToNow = ttl, Size = size };

    private void Replace<T>(string key, Task<T> expected, long size)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out Task<T>? current) && ReferenceEquals(current, expected))
                _cache.Set(key, expected, EntryOptions(size, _ttl));
        }
    }

    private void Remove<T>(string key, Task<T> expected)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out Task<T>? current) && ReferenceEquals(current, expected))
                _cache.Remove(key);
        }
    }

    public void Dispose() => _cache.Dispose();
}
