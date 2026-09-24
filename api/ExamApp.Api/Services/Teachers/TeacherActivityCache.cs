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
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
        where T : class;
}

public sealed class TeacherActivityCache : ITeacherActivityCache, IDisposable
{
    // Öğretmen başına birkaç anahtar (days/kapsam); üst sınır bellek büyümesine karşı emniyet.
    private const int MaxEntries = 10_000;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });
    private readonly object _gate = new();
    private readonly TimeSpan _ttl;

    public TeacherActivityCache(IOptions<DashboardOptions> options)
        : this(TimeSpan.FromSeconds(options.Value.TeacherActivityCacheSeconds))
    {
    }

    public TeacherActivityCache(TimeSpan ttl) => _ttl = ttl < TimeSpan.Zero ? TimeSpan.Zero : ttl;

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
        where T : class
    {
        // En fazla bir yeniden deneme: beklediğimiz hesabın sahibi isteği iptal edildiyse (tarayıcı sekmeyi kapattı vb.)
        // hesabı bu istek üstlenir.
        for (var attempt = 0; ; attempt++)
        {
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
                    // Süren hesap da girdi olarak durur (paylaşım için); TTL 0 ise tamamlanınca silinir.
                    _cache.Set(key, shared, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _ttl > TimeSpan.Zero ? _ttl : TimeSpan.FromMinutes(5),
                        Size = 1
                    });
                }
            }

            if (owner is not null)
            {
                // Hesap sahibin kendi akışında (kendi DbContext'iyle) çalışır — context'e eşzamanlı erişim yok.
                try
                {
                    var value = await factory(ct);
                    owner.SetResult(value);
                    if (_ttl <= TimeSpan.Zero)
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
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt == 0)
            {
                // Sahibin isteği iptal edildi, bizimki değil → yeniden dene (bu kez büyük olasılıkla sahip biziz).
            }
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
