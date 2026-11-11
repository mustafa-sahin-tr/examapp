using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Parents;

/// <summary>Issue #1112: haftalık veli raporu job'ının ayarları.</summary>
public sealed class ParentWeeklyReportOptions
{
    public const string SectionName = "ParentWeeklyReport";

    /// <summary>
    /// Hangfire recurring job cron'u (UTC). Varsayılan Pazartesi 04–09 UTC (07–12 İstanbul) saat başı: ilk tur raporları gönderir,
    /// sonrakiler yalnız aksayanları (hafta işareti tek gönderimi garanti eder).
    /// </summary>
    [Required]
    public string Cron { get; set; } = "0 4-9 * * 1";

    /// <summary>Tek sorguda okunacak en fazla bağlantı; tur bütün bağlantıları sayfa sayfa gezer.</summary>
    [Range(1, 2_000)]
    public int BatchSize { get; set; } = 200;
}

/// <summary>Issue #1112: son biten haftanın raporunu her Active veliye gönderir.</summary>
public interface IParentWeeklyReportJob
{
    /// <summary>Gönderilen rapor (bağlantı) sayısını döner. Retry yok: sonraki tur zaten yeniden dener.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #1112: son biten haftanın (<see cref="IParentWeeklyReportService.LastCompletedWeek"/>) raporu henüz gönderilmemiş her Active
/// bağlantı için çocuğun planında <see cref="EntitlementFeatures.WeeklyReport"/> varsa raporu hesaplar (çocuk başına bir kez,
/// panelin ucuyla aynı servis) ve veliye <see cref="ParentWeeklyReportEvent"/> yazar. Bağlantının
/// <see cref="ParentStudentLink.WeeklyReportSentFor"/> işareti koşullu güncellemeyle (bağlantı hâlâ Active ve o hafta işaretsiz)
/// event'le AYNI transaction'da yazılır: eşzamanlı ikinci tur ve yeniden koşma ikinci rapor üretmez. Planında hak olmayan çocuğun
/// bağlantısı işaretlenmez (hafta içinde plan alınırsa sonraki turda gönderilir). E-postayı kapatan veliye (<see
/// cref="Parent.WeeklyReportEmailOptOut"/>) e-posta adresi boş yazılır: yalnız in-app.
/// </summary>
public sealed class ParentWeeklyReportJob : IParentWeeklyReportJob
{
    public const string RecurringJobId = "parent-weekly-report";

    private readonly AppDbContext _context;
    private readonly IParentWeeklyReportService _reports;
    private readonly IEntitlementService _entitlements;
    private readonly IAuthApiClient? _authApi;
    private readonly IOptionsMonitor<ParentWeeklyReportOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ParentWeeklyReportJob> _logger;

    public ParentWeeklyReportJob(
        AppDbContext context,
        IParentWeeklyReportService reports,
        IEntitlementService entitlements,
        IAuthApiClient? authApi,
        IOptionsMonitor<ParentWeeklyReportOptions> options,
        TimeProvider? clock = null,
        ILogger<ParentWeeklyReportJob>? logger = null)
    {
        _context = context;
        _reports = reports;
        _entitlements = entitlements;
        _authApi = authApi;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ParentWeeklyReportJob>.Instance;
    }

    private sealed record Candidate(ParentChildAccessGrant Grant, int ParentUserId, bool EmailOptOut);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var week = _reports.LastCompletedWeek;
        var batchSize = _options.CurrentValue.BatchSize;
        var sent = 0;
        var lastId = 0;
        // Çocuk başına bir hesap: aynı çocuğun iki velisi aynı raporu alır; hak kontrolü de çocuk başına.
        var entitled = new Dictionary<int, bool>();
        var built = new Dictionary<int, ParentWeeklyReportDto>();

        while (true)
        {
            var candidates = await _context.ParentStudentLinks.Where(ParentChildAccess.GrantsAccess).AsNoTracking()
                .Where(l => l.Id > lastId && (l.WeeklyReportSentFor == null || l.WeeklyReportSentFor < week))
                .OrderBy(l => l.Id)
                .Take(batchSize)
                .Select(l => new Candidate(
                    new ParentChildAccessGrant(l.Id, l.ParentId, l.StudentId, l.Student.UserId, l.Student.GradeId,
                        l.Student.SchoolVerifiedAt != null ? l.Student.SchoolId : null),
                    l.Parent.UserId,
                    l.Parent.WeeklyReportEmailOptOut))
                .ToListAsync(ct);
            if (candidates.Count == 0)
                break;
            lastId = candidates[^1].Grant.LinkId;

            var due = new List<Candidate>();
            foreach (var c in candidates)
            {
                var studentUserId = c.Grant.StudentUserId;
                if (!entitled.TryGetValue(studentUserId, out var has))
                {
                    var check = await _entitlements.CheckAsync(EntitlementSubject.Create(studentUserId), EntitlementFeatures.WeeklyReport, ct);
                    entitled[studentUserId] = has = check.Limit != 0;
                }
                if (has)
                    due.Add(c);
            }
            if (due.Count > 0)
                sent += await SendBatchAsync(due, week, built, ct);

            if (candidates.Count < batchSize)
                break;
        }

        if (sent > 0)
            _logger.LogInformation("[ParentWeeklyReport] {Week} haftasının raporu {Sent} veli bağlantısına gönderildi.", week, sent);
        return sent;
    }

    private async Task<int> SendBatchAsync(List<Candidate> due, DateOnly week, Dictionary<int, ParentWeeklyReportDto> built,
        CancellationToken ct)
    {
        var users = await LookupAsync(due.Select(c => c.ParentUserId).Concat(due.Select(c => c.Grant.StudentUserId)), ct);
        UserLookupResultDto? UserOf(int id) => users.TryGetValue(id, out var u) ? u : null;
        var now = _clock.GetUtcNow().UtcDateTime;
        var sent = 0;

        foreach (var c in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!built.TryGetValue(c.Grant.StudentId, out var report))
                {
                    report = await _reports.BuildAsync(c.Grant, week, ct);
                    built[c.Grant.StudentId] = report;
                }
                _context.ChangeTracker.Clear();

                var done = false;
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();
                    done = false;
                    await using var tx = await _context.Database.BeginTransactionAsync(ct);
                    // Koşullu işaret: koparılmış bağlantı ve eşzamanlı ikinci tur 0 satır günceller, event yazılmaz.
                    var marked = await _context.ParentStudentLinks
                        .Where(l => l.Id == c.Grant.LinkId && l.Status == ParentStudentLinkStatus.Active && l.RevokedAt == null
                            && (l.WeeklyReportSentFor == null || l.WeeklyReportSentFor < week))
                        .ExecuteUpdateAsync(u => u.SetProperty(l => l.WeeklyReportSentFor, week), ct);
                    if (marked == 0)
                    {
                        await tx.RollbackAsync(ct);
                        return;
                    }

                    var parent = UserOf(c.ParentUserId);
                    _context.OutboxMessages.Add(new OutboxMessage
                    {
                        Type = OutboxEventRegistry.NameFor<ParentWeeklyReportEvent>(),
                        Content = JsonSerializer.Serialize(ToEvent(report, c, parent,
                            WorksheetCommentService.FormatStudentDisplayName(UserOf(c.Grant.StudentUserId)?.FullName))),
                        CreatedAt = now
                    });
                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    done = true;
                });
                if (done)
                    sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Tek bağlantının hatası turun geri kalanını engellemez; işaret yazılmadığından sonraki turda yeniden denenir.
                _logger.LogError(ex, "[ParentWeeklyReport] LinkId={LinkId} işlenemedi; sonraki tura kalır.", c.Grant.LinkId);
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }
        return sent;
    }

    internal static ParentWeeklyReportEvent ToEvent(ParentWeeklyReportDto report, ParentChildAccessGrant grant, int parentUserId,
        bool emailOptOut, UserLookupResultDto? parent, string? studentName) =>
        new()
        {
            EventId = Guid.NewGuid(),
            StudentId = grant.StudentId,
            StudentDisplayName = studentName ?? string.Empty,
            WeekStart = report.WeekStart.ToDateTime(TimeOnly.MinValue),
            QuestionsSolved = report.QuestionsSolved,
            Correct = report.Correct,
            ActiveDays = report.ActiveDays,
            StudyMinutes = report.StudyMinutes,
            Points = report.Points,
            AssignmentsCompleted = report.AssignmentsCompleted,
            AssignmentsOverdue = report.AssignmentsOverdue,
            Subjects = report.Subjects
                .Select(s => new ParentWeeklyReportSubject { Name = s.SubjectName, Answered = s.Answered, AccuracyPercent = s.AccuracyPercent })
                .ToList(),
            DroppedTopics = report.DroppedTopics
                .Select(t => new ParentWeeklyReportTopicDrop
                {
                    Name = t.TopicName, AccuracyPercent = t.AccuracyPercent, PreviousAccuracyPercent = t.PreviousAccuracyPercent,
                })
                .ToList(),
            MockExams = report.MockExams
                .Select(m => new ParentWeeklyReportMockExam { Name = m.Name, Net = m.Net, Rank = m.Rank, Takers = m.Takers })
                .ToList(),
            Badges = report.Badges.Select(b => b.Name).ToList(),
            Suggestion = report.Suggestion,
            SuggestionTopic = report.SuggestionTopic,
            RecipientUserId = parentUserId,
            RecipientKeycloakId = parent?.KeycloakId ?? string.Empty,
            RecipientEmail = emailOptOut ? string.Empty : parent?.Email ?? string.Empty,
        };

    private ParentWeeklyReportEvent ToEvent(ParentWeeklyReportDto report, Candidate c, UserLookupResultDto? parent, string? studentName) =>
        ToEvent(report, c.Grant, c.ParentUserId, c.EmailOptOut, parent, studentName);

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
            _logger.LogWarning(ex, "[ParentWeeklyReport] Kullanıcılar çözülemedi ({Count}); event'ler boş ad / e-postayla yazılacak.", ids.Count);
            return new Dictionary<int, UserLookupResultDto>();
        }
    }
}
