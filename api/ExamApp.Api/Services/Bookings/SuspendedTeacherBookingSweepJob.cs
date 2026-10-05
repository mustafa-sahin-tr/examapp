using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Bookings;

/// <summary>issue #331: askıdaki öğretmende kalmış Pending talep süpürmesinin ayarları.</summary>
public sealed class SuspendedTeacherBookingSweepOptions
{
    public const string SectionName = "SuspendedTeacherBookingSweep";

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: 5 dakikada bir.</summary>
    [Required]
    public string Cron { get; set; } = "*/5 * * * *";

    /// <summary>Tek turda en fazla işlenecek talep (kalan bir sonraki tura kalır).</summary>
    [Range(1, MaxBatchSize)]
    public int BatchSize { get; set; } = 500;

    /// <summary>Security review D2: tek turun üst sınırı.</summary>
    public const int MaxBatchSize = 1_000;
}

/// <summary>
/// issue #331: askıdaki öğretmende kalmış Pending talepleri kapatan güvenlik ağı (Hangfire recurring job).
/// </summary>
public interface ISuspendedTeacherBookingSweepJob
{
    /// <summary>
    /// Gerçekten reddedilen (ve bildirim event'i yazılan) talep sayısını döner. Hangfire filtreleri arayüzde: iş
    /// <c>AddOrUpdate&lt;ISuspendedTeacherBookingSweepJob&gt;</c> ile kaydedildiği için attribute'lar buradan okunur.
    /// Retry yok: bir sonraki cron turu zaten yeniden dener.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> SweepAsync(CancellationToken ct = default);
}

/// <summary>
/// Asıl çözüm talep oluşturmada öğretmen satırı kilidiyle askının serileşmesidir (<c>BookingService.CreateBookingAsync</c>);
/// bu iş onun atlandığı durumlar (kilit dışı yazıcı, ileride eklenecek yeni bir talep yolu) için ucuz bir güvenlik ağıdır.
/// Askıdaki (<c>AccountSuspendedAt != null</c>) öğretmenin Pending talepleri, askıya alma anındakiyle AYNI yolla
/// (<see cref="TeacherUnavailableBookingRejection"/>: koşullu UPDATE + <c>BookingDecisionEvent</c> <c>TeacherUnavailable=true</c>)
/// kapatılır. İdempotent: koşullu UPDATE 0 satır dönen talep (zaten karara bağlanmış / askı kalkmış) için event yazılmaz;
/// aynı talebe ikinci bildirim gitmez. Bir talep bulunursa yarış yaşanmış demektir → uyarı loglanır.
/// </summary>
public sealed class SuspendedTeacherBookingSweepJob : ISuspendedTeacherBookingSweepJob
{
    public const string RecurringJobId = "suspended-teacher-pending-booking-sweep";

    /// <summary>Tek transaction'da en fazla bu kadar talep (security review D2).</summary>
    internal const int CommitChunkSize = 100;

    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<SuspendedTeacherBookingSweepOptions> _options;
    private readonly IAuthApiClient? _authApiClient;
    private readonly TimeProvider _clock;
    private readonly ILogger<SuspendedTeacherBookingSweepJob> _logger;

    public SuspendedTeacherBookingSweepJob(
        AppDbContext context,
        IOptionsMonitor<SuspendedTeacherBookingSweepOptions> options,
        IAuthApiClient? authApiClient = null,
        TimeProvider? clock = null,
        ILogger<SuspendedTeacherBookingSweepJob>? logger = null)
    {
        _context = context;
        _options = options;
        _authApiClient = authApiClient;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SuspendedTeacherBookingSweepJob>.Instance;
    }

    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var batchSize = _options.CurrentValue.BatchSize;

        // Sıralama/parti projeksiyondan ÖNCE (constructor projeksiyonu üzerinden OrderBy çevrilemez).
        var batch = _context.Bookings
            .Where(b => b.Status == BookingStatus.Pending && b.Teacher.AccountSuspendedAt != null)
            .OrderBy(b => b.Id)
            .Take(batchSize);
        var pending = await TeacherUnavailableBookingRejection
            .SelectPending(batch)
            .AsNoTracking()
            .ToListAsync(ct);

        if (pending.Count == 0)
            return 0;

        var now = _clock.GetUtcNow().UtcDateTime;
        var (teacherNames, studentSubs) = await TryLookupUsersAsync(pending, ct);

        // Okuma ile yazma arasında talep karara bağlanabilir ya da askı kalkabilir; koşullu UPDATE bunları atlar.
        // Security review D2: tek dev transaction yerine öğretmen bazında, en fazla CommitChunkSize'lık alt partiler halinde
        // commit (kısa kilit süresi; bir partinin hatası öncekileri geri almaz — kalan bir sonraki turda yine bulunur).
        var rejected = 0;
        var rejectedTeacherIds = new SortedSet<int>();
        var strategy = _context.Database.CreateExecutionStrategy();
        foreach (var teacherGroup in pending.GroupBy(p => p.TeacherId))
        {
            foreach (var chunk in teacherGroup.Chunk(CommitChunkSize))
            {
                var chunkRejected = 0;
                await strategy.ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();
                    await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);

                    chunkRejected = (await TeacherUnavailableBookingRejection.RejectAsync(
                        _context, chunk, now, updateUserId: null,
                        userId => teacherNames.TryGetValue(userId, out var name) ? name : string.Empty,
                        userId => studentSubs.TryGetValue(userId, out var sub) ? sub : string.Empty,
                        CancellationToken.None)).Count;

                    await _context.SaveChangesAsync(CancellationToken.None);
                    await tx.CommitAsync(CancellationToken.None);
                });

                rejected += chunkRejected;
                if (chunkRejected > 0)
                    rejectedTeacherIds.Add(teacherGroup.Key);
            }
        }

        if (rejected > 0)
        {
            // Review Ö2/D4: yalnız gerçekten reddedilen satırların öğretmenleri loglanır.
            _logger.LogWarning(
                "[SuspendedTeacherBookingSweep] Askıdaki öğretmende kalmış {Count} Pending talep otomatik reddedildi (Teacher#{TeacherIds}).",
                rejected, string.Join(",", rejectedTeacherIds));
        }

        return rejected;
    }

    /// <summary>
    /// Bildirim alanları (öğretmen adı, öğrenci Keycloak sub'ı) best-effort; auth-api erişilemezse boş geçer — consumer
    /// sub'ı kendi verisinden çözer, çözemezse retry → dead-letter (#298, bildirim kaybolmaz).
    /// </summary>
    private async Task<(IReadOnlyDictionary<int, string> TeacherNames, IReadOnlyDictionary<int, string> StudentSubs)> TryLookupUsersAsync(
        IReadOnlyList<PendingBookingRow> pending, CancellationToken ct)
    {
        var empty = new Dictionary<int, string>();
        if (_authApiClient is null)
            return (empty, empty);

        var teacherUserIds = pending.Select(p => p.TeacherUserId).ToHashSet();
        var studentUserIds = pending.Select(p => p.StudentUserId).ToHashSet();
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(teacherUserIds.Concat(studentUserIds).Distinct(), ct);
            var names = users
                .Where(u => teacherUserIds.Contains(u.Id))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName ?? string.Empty);
            var subs = users
                .Where(u => studentUserIds.Contains(u.Id) && !string.IsNullOrWhiteSpace(u.KeycloakId))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().KeycloakId);
            return (names, subs);
        }
        // Review Ö1: çağıranın iptali yutulmaz, yayılır; yalnız HttpClient zaman aşımı (iptal istenmemişken) best-effort sayılır.
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "[SuspendedTeacherBookingSweep] auth-api lookup başarısız; bildirim alanları boş geçilecek.");
            return (empty, empty);
        }
    }
}
