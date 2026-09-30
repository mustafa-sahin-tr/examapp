using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Whiteboard;

/// <summary>
/// issue #98: açık tahtaları periyodik (varsayılan dakikada bir) tarar ve kapatır — katılım penceresi dolan tahtalar
/// (<see cref="WhiteboardCloseReasons.WindowClosed"/>), randevusu artık Approved olmayan / silinen
/// (<see cref="WhiteboardCloseReasons.BookingCancelled"/>) ve öğretmeni askıya alınan
/// (<see cref="WhiteboardCloseReasons.TeacherNotApproved"/>) tahtalar. Kapanışta <c>BoardClosed</c> yayınlanır, üyeler
/// gruptan çıkarılır, durum silinir. Hiç tahta açık değilse DB'ye gitmez.
/// </summary>
public sealed class WhiteboardCleanupService : BackgroundService
{
    private static readonly TimeSpan RateBucketIdle = TimeSpan.FromMinutes(5);

    private readonly IWhiteboardStore _store;
    private readonly IWhiteboardSessionCloser _closer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<WhiteboardOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<WhiteboardCleanupService> _logger;

    public WhiteboardCleanupService(IWhiteboardStore store, IWhiteboardSessionCloser closer,
        IServiceScopeFactory scopeFactory, IOptionsMonitor<WhiteboardOptions> options, TimeProvider clock,
        ILogger<WhiteboardCleanupService> logger)
    {
        _store = store;
        _closer = closer;
        _scopeFactory = scopeFactory;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CurrentValue.SweepIntervalSeconds), _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Tek bir başarısız tur (DB kesintisi) servisi öldürmesin; sonraki turda tekrar denenir.
                    _logger.LogError(ex, "Whiteboard cleanup sweep failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // kapanış
        }
    }

    /// <summary>Tek tarama turu. Kapatılan tahta sayısını döner (testler doğrudan çağırır).</summary>
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        _store.PruneRateBuckets(now, RateBucketIdle);

        var boards = _store.ListBoards();
        if (boards.Count == 0)
            return 0;

        var closed = 0;
        foreach (var board in boards.Where(b => now > b.WindowClosesAtUtc))
        {
            if (await _closer.CloseAsync(board.BookingId, WhiteboardCloseReasons.WindowClosed, ct))
                closed++;
        }

        var stillOpen = boards.Where(b => now <= b.WindowClosesAtUtc).Select(b => b.BookingId).ToList();
        if (stillOpen.Count == 0)
            return closed;

        using var scope = _scopeFactory.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IWhiteboardAccessService>();
        var invalid = await access.FindInvalidBoardsAsync(stillOpen, ct);
        foreach (var (bookingId, reason) in invalid)
        {
            if (await _closer.CloseAsync(bookingId, reason, ct))
                closed++;
        }

        await RevalidateIdleConnectionsAsync(access, now, ct);
        return closed;
    }

    /// <summary>
    /// Hub'daki çağrı başı yeniden doğrulama yalnızca çağrı yapan bağlantıları kapsar; yalnızca dinleyen (hiç çağrı
    /// yapmayan) bir bağlantı bu tur olmadan yetkisi düşse de yayın almaya devam ederdi. <see
    /// cref="WhiteboardOptions.ListenerRevalidateSeconds"/>'tır doğrulanmamış her bağlantı yeniden kontrol edilir;
    /// başarısızsa yalnızca o bağlantı düşürülür (<c>BoardClosed("AccessRevoked")</c>). Geçici hata (profil sağlayıcı
    /// kesintisi) bağlantıyı düşürmez, loglanır ve sonraki turda tekrar denenir.
    /// </summary>
    private async Task RevalidateIdleConnectionsAsync(IWhiteboardAccessService access, DateTime now, CancellationToken ct)
    {
        var threshold = TimeSpan.FromSeconds(_options.CurrentValue.ListenerRevalidateSeconds);
        var stale = _store.ListMembers()
            .Where(m => now - m.LastAuthorizedAtUtc >= threshold && _store.IsOpen(m.BookingId))
            .ToList();

        foreach (var member in stale)
        {
            WhiteboardAccessResult result;
            try
            {
                result = await access.AuthorizeAsync(member.User, member.BookingId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Whiteboard connection revalidation failed. BookingId={BookingId}, UserId={UserId}",
                    member.BookingId, member.UserId);
                continue;
            }

            if (result.Allowed)
                _store.MarkAuthorized(member.ConnectionId, now);
            else
                await _closer.RevokeConnectionAsync(member, ct);
        }
    }
}
