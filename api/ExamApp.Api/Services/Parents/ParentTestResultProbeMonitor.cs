using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// issue #421 review: velinin test sonucu ucunda art arda 404 alması (bağlı çocuğun id'siyle başka/olmayan testInstanceId
/// denemesi — id taraması) için süreç içi sayaç. Veli başına sabit <see cref="Window"/>'luk pencerede <see cref="Threshold"/>'u
/// AŞAN ilk 404'te bir kez Warning yazar. Engellemez (rate limit ayrı); yalnızca görünürlük. Süreç içidir: birden çok replikada
/// sayaçlar ayrıdır, yeniden başlatmada sıfırlanır (bilinçli — kalıcı iz audit tablosunda).
/// </summary>
public interface IParentTestResultProbeMonitor
{
    /// <returns>Bu çağrıda eşik aşıldıysa (uyarı yazıldıysa) true.</returns>
    bool RecordNotFound(int parentId, int studentId);
}

/// <inheritdoc cref="IParentTestResultProbeMonitor"/>
public sealed class ParentTestResultProbeMonitor : IParentTestResultProbeMonitor
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    public const int Threshold = 20;

    /// <summary>Sözlük bu boyutu aşınca süresi dolmuş pencereler temizlenir (bellek tavanı).</summary>
    internal const int PruneAbove = 10_000;

    private readonly ConcurrentDictionary<int, Counter> _counters = new();
    private readonly TimeProvider _time;
    private readonly ILogger<ParentTestResultProbeMonitor> _logger;

    public ParentTestResultProbeMonitor(ILogger<ParentTestResultProbeMonitor> logger, TimeProvider? time = null)
    {
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    private sealed class Counter
    {
        public DateTime WindowStart;
        public int Count;
    }

    public bool RecordNotFound(int parentId, int studentId)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        if (_counters.Count > PruneAbove)
            Prune(now);

        var counter = _counters.GetOrAdd(parentId, _ => new Counter { WindowStart = now });
        int count;
        lock (counter)
        {
            if (now - counter.WindowStart >= Window)
            {
                counter.WindowStart = now;
                counter.Count = 0;
            }

            count = ++counter.Count;
        }

        if (count != Threshold + 1)
            return false;

        _logger.LogWarning(
            "[ParentAssignments] Veli kısa sürede çok sayıda bulunamayan test sonucu istedi (olası id taraması): parentId={ParentId} studentId={StudentId} count={Count} window={WindowMinutes}dk",
            parentId, studentId, count, Window.TotalMinutes);
        return true;
    }

    private void Prune(DateTime now)
    {
        foreach (var (key, counter) in _counters)
        {
            if (now - counter.WindowStart >= Window)
                _counters.TryRemove(key, out _);
        }
    }
}
