using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Bookings;

/// <summary>issue #439: veli onayı bekleyen taleplerin süre aşımı süpürmesinin ayarları.</summary>
public sealed class AwaitingParentBookingExpiryOptions
{
    public const string SectionName = "AwaitingParentBookingExpiry";

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: 15 dakikada bir.</summary>
    [Required]
    public string Cron { get; set; } = "*/15 * * * *";

    /// <summary>Tek turda en fazla işlenecek talep (kalan bir sonraki tura kalır).</summary>
    [Range(1, MaxBatchSize)]
    public int BatchSize { get; set; } = 500;

    public const int MaxBatchSize = 1_000;
}

/// <summary>issue #439: süresi dolan AwaitingParent talepleri Expired'a çeken Hangfire recurring job.</summary>
public interface IAwaitingParentBookingExpiryJob
{
    /// <summary>
    /// Gerçekten Expired'a çekilen (ve bildirim event'i yazılan) talep sayısını döner. Retry yok: bir sonraki cron turu zaten
    /// yeniden dener. Hangfire filtreleri arayüzde (iş <c>AddOrUpdate&lt;IAwaitingParentBookingExpiryJob&gt;</c> ile kayıtlı).
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> ExpireAsync(CancellationToken ct = default);
}

/// <summary>
/// issue #439: veli onayı bekleyen talep oluşturmadan 7 gün sonra YA DA ders saati geldiğinde (hangisi önceyse,
/// <see cref="BookingParentApproval.ExpiredAt"/>) Expired olur ve slot serbest kalır. Her satır koşullu UPDATE
/// (<c>Status = AwaitingParent</c>) ile güncellenir: arada veli karar verdiyse 0 satır → event YAZILMAZ (idempotent; aynı talebe
/// ikinci bildirim gitmez). Gerçekten kapanan her talep için öğrenci + Active velilere <see cref="BookingParentDecisionEvent"/>
/// (<c>Decision=Expired</c>) aynı transaction'da yazılır. <see cref="SuspendedTeacherBookingSweepJob"/> deseni: en fazla
/// <see cref="CommitChunkSize"/> talep / transaction.
/// </summary>
public sealed class AwaitingParentBookingExpiryJob : IAwaitingParentBookingExpiryJob
{
    public const string RecurringJobId = "awaiting-parent-booking-expiry";

    internal const int CommitChunkSize = 100;

    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<AwaitingParentBookingExpiryOptions> _options;
    private readonly IAuthApiClient? _authApiClient;
    private readonly TimeProvider _clock;
    private readonly ILogger<AwaitingParentBookingExpiryJob> _logger;

    public AwaitingParentBookingExpiryJob(
        AppDbContext context,
        IOptionsMonitor<AwaitingParentBookingExpiryOptions> options,
        IAuthApiClient? authApiClient = null,
        TimeProvider? clock = null,
        ILogger<AwaitingParentBookingExpiryJob>? logger = null)
    {
        _context = context;
        _options = options;
        _authApiClient = authApiClient;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AwaitingParentBookingExpiryJob>.Instance;
    }

    private sealed record ExpiringRow(
        int Id, int TeacherId, int TeacherUserId, int StudentId, int StudentUserId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);

    public async Task<int> ExpireAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var batchSize = _options.CurrentValue.BatchSize;

        // Slotu soft-delete edilmiş (eski veri) talep de kapansın; INNER JOIN'de düşmesin (#376 deseni).
        var rows = await _context.Bookings
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.AwaitingParent)
            .Where(BookingParentApproval.ExpiredAt(now))
            .OrderBy(b => b.Id)
            .Take(batchSize)
            .Select(b => new ExpiringRow(
                b.Id, b.TeacherId, b.Teacher.UserId, b.StudentId, b.Student.UserId,
                b.AvailabilitySlot.Date, b.AvailabilitySlot.StartTime, b.AvailabilitySlot.EndTime))
            .ToListAsync(ct);

        if (rows.Count == 0)
            return 0;

        var parentsByStudent = await BookingParentApproval.ActiveParentUserIdsByStudentAsync(
            _context, rows.Select(r => r.StudentId), ct);
        // Ad/sub çözümü ortak veli bildirimi yardımcısıyla (zaman aşımı + kısa görünen ad). Ön okuma yalnız çözüm için; alıcı
        // listesi transaction içinde kilitli okumadan gelir.
        var users = await ParentNotificationSupport.LookupAsync(
            _authApiClient,
            rows.SelectMany(r => new[] { r.StudentUserId, r.TeacherUserId }).Concat(parentsByStudent.Values.SelectMany(v => v)),
            _logger, ct);

        var expired = 0;
        var strategy = _context.Database.CreateExecutionStrategy();
        foreach (var chunk in rows.Chunk(CommitChunkSize))
        {
            var chunkExpired = 0;
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                chunkExpired = 0;
                await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);

                // issue #439 review: alıcılar transaction içinde FOR SHARE ile yeniden okunur (koparılan veliye gitmez).
                var lockedParents = await BookingParentApproval.ActiveParentUserIdsLockedAsync(
                    _context, chunk.Select(c => c.StudentId), CancellationToken.None);

                foreach (var row in chunk)
                {
                    var id = row.Id;
                    var updated = await _context.Bookings
                        .Where(b => b.Id == id && b.Status == BookingStatus.AwaitingParent)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(b => b.Status, BookingStatus.Expired)
                            .SetProperty(b => b.DecisionAt, now)
                            .SetProperty(b => b.UpdateTime, now)
                            .SetProperty(b => b.UpdateUserId, (int?)null), CancellationToken.None);
                    if (updated == 0)
                        continue;

                    chunkExpired++;
                    var @event = new BookingParentDecisionEvent
                    {
                        EventId = Guid.NewGuid(),
                        BookingId = row.Id,
                        Decision = BookingParentDecisions.Expired,
                        StudentId = row.StudentId,
                        StudentUserId = row.StudentUserId,
                        StudentKeycloakId = SubOf(users, row.StudentUserId),
                        StudentName = NameOf(users, row.StudentUserId),
                        Parents = lockedParents.TryGetValue(row.StudentId, out var parentIds)
                            ? BookingParentApproval.Recipients(parentIds, uid => SubOf(users, uid))
                            : new List<BookingParentRecipient>(),
                        DecidedByParentUserId = null,
                        DecidedByParentName = null,
                        TeacherId = row.TeacherId,
                        TeacherName = NameOf(users, row.TeacherUserId),
                        Date = row.Date,
                        StartTime = row.StartTime,
                        EndTime = row.EndTime,
                        Note = null,
                        DecidedAt = now
                    };
                    _context.OutboxMessages.Add(new OutboxMessage
                    {
                        Type = OutboxEventRegistry.NameFor<BookingParentDecisionEvent>(),
                        Content = JsonSerializer.Serialize(@event),
                        CreatedAt = now
                    });
                }

                await _context.SaveChangesAsync(CancellationToken.None);
                await tx.CommitAsync(CancellationToken.None);
            });
            expired += chunkExpired;
        }

        if (expired > 0)
            _logger.LogInformation("[AwaitingParentBookingExpiry] {Count} veli onayı bekleyen talep süre aşımıyla kapatıldı.", expired);

        return expired;
    }

    private static string SubOf(IReadOnlyDictionary<int, NotificationUser> users, int userId)
        => ParentNotificationSupport.Of(users, userId).KeycloakId;

    private static string NameOf(IReadOnlyDictionary<int, NotificationUser> users, int userId)
        => ParentNotificationSupport.Of(users, userId).DisplayName;
}
