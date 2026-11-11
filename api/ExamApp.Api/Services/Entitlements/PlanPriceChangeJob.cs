using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.TargetGoals;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>Issue #1116: fiyat değişikliği bildirimi ve yürürlüğe girişi job'ının ayarları.</summary>
public sealed class PlanPriceChangeOptions
{
    public const string SectionName = "PlanPriceChange";

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: saat başı 40. dakika.</summary>
    [Required]
    public string Cron { get; set; } = "40 * * * *";

    /// <summary>Tek sorguda okunacak en fazla satın alma; tur bütün süren satın almaları sayfa sayfa gezer.</summary>
    [Range(1, 2_000)]
    public int BatchSize { get; set; } = 200;
}

/// <summary>Issue #1116: planlanmış fiyat artışını süren satın almaların ödeyenine bildirir, vadesi geleni fiyata geçirir.</summary>
public interface IPlanPriceChangeJob
{
    /// <summary>Bildirilen satın alma sayısını döner. Retry yok: sonraki tur zaten yeniden dener.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #1116: "fiyat değişikliği yalnız sonraki dönemde, en az 30 gün önceden bildirilerek". Admin artışı
/// (<see cref="PurchaseTermsPolicy.Apply"/>) bekleyen fiyat olarak yazılır; bu job:
/// <list type="number">
/// <item>Süren her satın almanın (ödenmiş, iptal edilmemiş, bitmemiş, ardından başlayan atama yok, yaz planı değil) yenileme fiyatını
/// bulur: sınıfı olan öğrencide fiyat tablosunun satırı (bugünkü sınıfın bandı, sınıfa özel satır önce, kapsam, süre), öğretmende ve
/// sınıfı olmayan öğrencide aylık plan fiyatı. Satırda bekleyen artış varsa ve atama o yürürlük tarihi için bildirilmediyse ödeyene
/// <see cref="PlanPriceChangeNoticeEvent"/> yazar: öğrenci planında Active veliler (yoksa öğrencinin kendisi), öğretmen planında
/// öğretmen. İşaret (<see cref="UserSubscriptionPlan.PriceChangeNoticeForUtc"/>) event'lerle AYNI transaction'da; Active veli
/// listesi transaction içinde <c>FOR SHARE</c> ile yeniden okunur (#423).</item>
/// <item>Sonra yürürlük tarihi gelen bekleyen fiyatları fiyata geçirir (o ana dek satın alma eski fiyatla).</item>
/// </list>
/// Ödenmiş dönemin fiyatı hiçbir zaman değişmez; yenileme yeni satın almadır.
/// </summary>
public sealed class PlanPriceChangeJob : IPlanPriceChangeJob
{
    public const string RecurringJobId = "plan-price-change";

    private readonly AppDbContext _context;
    private readonly IAuthApiClient? _authApi;
    private readonly IOptionsMonitor<PlanPriceChangeOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<PlanPriceChangeJob> _logger;

    public PlanPriceChangeJob(
        AppDbContext context,
        IAuthApiClient? authApi,
        IOptionsMonitor<PlanPriceChangeOptions> options,
        TimeProvider? clock = null,
        ILogger<PlanPriceChangeJob>? logger = null)
    {
        _context = context;
        _authApi = authApi;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<PlanPriceChangeJob>.Instance;
    }

    private sealed record Candidate(
        int AssignmentId, int UserId, int PlanId, string PlanName, string Currency, SubscriptionPlanAudience Audience,
        PlanDuration Duration, int? ScopeSubjectId, DateTime EndsAt, DateTime? NoticeFor,
        decimal MonthlyPrice, decimal? PendingMonthlyPrice, DateTime? PendingMonthlyPriceFrom);

    private sealed record PriceChange(decimal Current, decimal Next, DateTime From);

    private sealed record Recipient(int UserId, int? StudentId);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var notified = await NotifyAsync(now, ct);
        await ApplyDueAsync(now, ct);
        return notified;
    }

    private async Task<int> NotifyAsync(DateTime now, CancellationToken ct)
    {
        // Bekleyen artışı olan planlar; yoksa tur boş geçer.
        var planIds = await _context.SubscriptionPlans.AsNoTracking()
            .Where(p => p.PendingMonthlyPrice != null || p.Prices.Any(r => r.PendingPrice != null))
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (planIds.Count == 0)
            return 0;

        var priceRows = await _context.SubscriptionPlanPrices.AsNoTracking()
            .Where(r => planIds.Contains(r.PlanId))
            .ToListAsync(ct);
        var rowsByPlan = priceRows.ToLookup(r => r.PlanId);
        var levels = (await _context.Grades.AsNoTracking().Select(g => new { g.Id, g.Code, g.Name }).ToListAsync(ct))
            .ToDictionary(g => g.Id, g => TargetGoalService.GradeLevelOf(g.Code) ?? TargetGoalService.GradeLevelOf(g.Name));

        var batchSize = _options.CurrentValue.BatchSize;
        var processed = 0;
        var lastId = 0;
        while (true)
        {
            var candidates = await _context.UserSubscriptionPlans.AsNoTracking()
                .Where(a => a.Id > lastId && planIds.Contains(a.PlanId) && a.PricePaid != null && a.CancelledAtUtc == null
                    && a.ValidToUtc != null && a.ValidToUtc > now && a.Duration != PlanDuration.Summer
                    && !_context.UserSubscriptionPlans.Any(next => next.UserId == a.UserId && next.Id != a.Id && next.ValidFromUtc >= a.ValidToUtc))
                .OrderBy(a => a.Id)
                .Take(batchSize)
                .Select(a => new Candidate(a.Id, a.UserId, a.PlanId, a.Plan.Name, a.Plan.Currency, a.Plan.Audience, a.Duration,
                    a.ScopeSubjectId, a.ValidToUtc!.Value, a.PriceChangeNoticeForUtc,
                    a.Plan.MonthlyPrice, a.Plan.PendingMonthlyPrice, a.Plan.PendingMonthlyPriceFromUtc))
                .ToListAsync(ct);
            if (candidates.Count == 0)
                break;
            lastId = candidates[^1].AssignmentId;
            processed += await NotifyBatchAsync(candidates, rowsByPlan, levels, now, ct);
            if (candidates.Count < batchSize)
                break;
        }

        if (processed > 0)
            _logger.LogInformation("[PlanPriceChange] {Processed} satın almanın ödeyenine fiyat değişikliği bildirildi.", processed);
        return processed;
    }

    private async Task<int> NotifyBatchAsync(List<Candidate> candidates, ILookup<int, SubscriptionPlanPrice> rowsByPlan,
        Dictionary<int, int?> levels, DateTime now, CancellationToken ct)
    {
        var userIds = candidates.Select(c => c.UserId).Distinct().ToList();
        var students = await _context.Students.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId) && !s.IsDeleted)
            .Select(s => new { s.Id, s.UserId, s.GradeId })
            .ToDictionaryAsync(s => s.UserId, ct);

        PriceChange? ChangeOf(Candidate c)
        {
            var isStudentPlan = c.Audience is SubscriptionPlanAudience.Student or SubscriptionPlanAudience.Any;
            int? level = isStudentPlan && students.TryGetValue(c.UserId, out var s) && s.GradeId is { } g && levels.TryGetValue(g, out var l)
                ? l : null;
            if (isStudentPlan && StudentPlanPricing.BandOf(level) is { } band)
            {
                var scope = c.ScopeSubjectId is null ? PlanScope.AllSubjects : PlanScope.SingleSubject;
                var row = rowsByPlan[c.PlanId]
                    .Where(r => !r.IsDeleted && r.Band == band && (r.GradeLevel == null || r.GradeLevel == level)
                        && r.Scope == scope && r.Duration == c.Duration)
                    .OrderByDescending(r => r.GradeLevel != null)
                    .FirstOrDefault();
                return row is { PendingPrice: { } next, PendingFromUtc: { } from } ? new PriceChange(row.Price, next, from) : null;
            }
            return c.Duration == PlanDuration.Monthly && c.ScopeSubjectId is null
                && c is { PendingMonthlyPrice: { } monthly, PendingMonthlyPriceFrom: { } monthlyFrom }
                ? new PriceChange(c.MonthlyPrice, monthly, monthlyFrom)
                : null;
        }

        var due = candidates
            .Select(c => (Candidate: c, Change: ChangeOf(c)))
            .Where(x => x.Change is not null && x.Candidate.NoticeFor != x.Change.From)
            .Select(x => (x.Candidate, Change: x.Change!))
            .ToList();
        if (due.Count == 0)
            return 0;

        var studentIds = due.Where(x => students.ContainsKey(x.Candidate.UserId)).Select(x => students[x.Candidate.UserId].Id).ToList();
        var links = await _context.ParentStudentLinks.Where(ParentChildAccess.GrantsAccess).AsNoTracking()
            .Where(l => studentIds.Contains(l.StudentId))
            .Select(l => new { l.StudentId, l.ParentId, ParentUserId = l.Parent.UserId })
            .ToListAsync(ct);
        var parentsByStudent = links.ToLookup(l => l.StudentId);
        var users = await LookupAsync(links.Select(l => l.ParentUserId).Concat(due.Select(x => x.Candidate.UserId)), ct);
        UserLookupResultDto? UserOf(int id) => users.TryGetValue(id, out var u) ? u : null;

        var processed = 0;
        foreach (var (c, change) in due)
        {
            ct.ThrowIfCancellationRequested();
            var student = students.TryGetValue(c.UserId, out var s) ? s : null;
            try
            {
                var done = false;
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();
                    done = false;
                    await using var tx = await _context.Database.BeginTransactionAsync(ct);

                    // Öğrenci planını veli öder; Active velisi kalmadıysa öğrencinin kendisi (kendi satın alması).
                    List<Recipient> recipients = [new Recipient(c.UserId, null)];
                    if (student is not null)
                    {
                        var stillActive = await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, student.Id, ct);
                        var parents = parentsByStudent[student.Id].Where(p => stillActive.Contains(p.ParentId))
                            .Select(p => new Recipient(p.ParentUserId, student.Id)).ToList();
                        if (parents.Count > 0)
                            recipients = parents;
                    }

                    // Koşullu işaret: eşzamanlı ikinci örnek 0 satır günceller ve event yazmaz.
                    var marked = await _context.UserSubscriptionPlans
                        .Where(a => a.Id == c.AssignmentId && (a.PriceChangeNoticeForUtc == null || a.PriceChangeNoticeForUtc != change.From))
                        .ExecuteUpdateAsync(u => u.SetProperty(a => a.PriceChangeNoticeForUtc, change.From), ct);
                    if (marked == 0)
                    {
                        await tx.RollbackAsync(ct);
                        return;
                    }

                    var studentName = WorksheetCommentService.FormatStudentDisplayName(UserOf(c.UserId)?.FullName) ?? string.Empty;
                    foreach (var r in recipients)
                    {
                        var user = UserOf(r.UserId);
                        _context.OutboxMessages.Add(new OutboxMessage
                        {
                            Type = OutboxEventRegistry.NameFor<PlanPriceChangeNoticeEvent>(),
                            Content = JsonSerializer.Serialize(new PlanPriceChangeNoticeEvent
                            {
                                EventId = Guid.NewGuid(),
                                AssignmentId = c.AssignmentId,
                                PlanName = c.PlanName,
                                Duration = c.Duration.ToString(),
                                CurrentPrice = change.Current,
                                NewPrice = change.Next,
                                Currency = c.Currency,
                                EffectiveFromUtc = DateTime.SpecifyKind(change.From, DateTimeKind.Utc),
                                RenewsAtUtc = DateTime.SpecifyKind(c.EndsAt, DateTimeKind.Utc),
                                StudentId = r.StudentId,
                                StudentDisplayName = r.StudentId is null ? string.Empty : studentName,
                                RecipientUserId = r.UserId,
                                RecipientKeycloakId = user?.KeycloakId ?? string.Empty,
                                RecipientEmail = user?.Email ?? string.Empty,
                            }),
                            CreatedAt = now
                        });
                    }
                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    done = true;
                });
                if (done)
                    processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Tek satın almanın hatası turun geri kalanını engellemez; işaret yazılmadığından sonraki turda yeniden denenir.
                _logger.LogError(ex, "[PlanPriceChange] AssignmentId={AssignmentId} işlenemedi; sonraki tura kalır.", c.AssignmentId);
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }
        return processed;
    }

    /// <summary>Vadesi gelen bekleyen fiyatlar fiyata geçer (bildirimden sonra: son turda satın alan da bildirimini almıştır).</summary>
    private async Task ApplyDueAsync(DateTime now, CancellationToken ct)
    {
        var rows = await _context.SubscriptionPlanPrices
            .Where(r => r.PendingPrice != null && r.PendingFromUtc <= now)
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Price, r => r.PendingPrice!.Value)
                .SetProperty(r => r.PendingPrice, (decimal?)null)
                .SetProperty(r => r.PendingFromUtc, (DateTime?)null), ct);
        var plans = await _context.SubscriptionPlans
            .Where(p => p.PendingMonthlyPrice != null && p.PendingMonthlyPriceFromUtc <= now)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.MonthlyPrice, p => p.PendingMonthlyPrice!.Value)
                .SetProperty(p => p.PendingMonthlyPrice, (decimal?)null)
                .SetProperty(p => p.PendingMonthlyPriceFromUtc, (DateTime?)null), ct);
        if (rows + plans > 0)
            _logger.LogInformation("[PlanPriceChange] {Rows} fiyat satırı ve {Plans} aylık plan fiyatı yürürlüğe girdi.", rows, plans);
    }

    /// <summary>auth-api'den ad / sub / e-posta (best-effort, kısa üst süre); erişilemezse boş — event boş değerlerle yazılır.</summary>
    private async Task<IReadOnlyDictionary<int, UserLookupResultDto>> LookupAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (_authApi == null || ids.Count == 0)
            return new Dictionary<int, UserLookupResultDto>();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ParentNotificationSupport.LookupTimeout);
        try
        {
            return (await _authApi.GetUsersByIdsAsync(ids, timeout.Token)).DistinctBy(u => u.Id).ToDictionary(u => u.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "[PlanPriceChange] Kullanıcılar çözülemedi ({Count}); event'ler boş ad / e-postayla yazılacak.", ids.Count);
            return new Dictionary<int, UserLookupResultDto>();
        }
    }
}
