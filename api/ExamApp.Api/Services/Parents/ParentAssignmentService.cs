using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// <inheritdoc cref="IParentAssignmentService"/>
/// <para>
/// Kapsam ve kova kuralı V2 özetiyle ortak (<see cref="ParentAssignmentScope"/>). Sonuç sayıları öğrencinin kendi sonuç
/// ekranıyla aynı kuraldan (<see cref="WorksheetScoring"/>) hesaplanır. Doğru şık yalnızca sunucuda karşılaştırma için okunur; yanıta hiçbir şık/soru içeriği girmez.
/// </para>
/// </summary>
public sealed class ParentAssignmentService : IParentAssignmentService
{
    /// <summary>Sayfa boyutu (sabit; istemci değiştiremez).</summary>
    internal const int PageSize = 20;

    private readonly AppDbContext _context;
    private readonly IParentChildAccess _access;
    private readonly IParentAccessAuditLog _audit;
    private readonly IAuthApiClient _authApiClient;
    private readonly IStringLocalizer<Messages> _localizer;
    private readonly TimeProvider _time;
    private readonly ILogger<ParentAssignmentService>? _logger;
    private readonly IParentTestResultProbeMonitor? _probeMonitor;

    public ParentAssignmentService(
        AppDbContext context,
        IParentChildAccess access,
        IParentAccessAuditLog audit,
        IAuthApiClient authApiClient,
        IStringLocalizer<Messages>? localizer = null,
        TimeProvider? time = null,
        ILogger<ParentAssignmentService>? logger = null,
        IParentTestResultProbeMonitor? probeMonitor = null)
    {
        _probeMonitor = probeMonitor;
        _context = context;
        _access = access;
        _audit = audit;
        _authApiClient = authApiClient;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>Öğretmen adı zenginleştirmesinin süre tavanı (auth-api yavaşsa liste yine döner, ad null).</summary>
    internal TimeSpan NameLookupTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary><c>?status=</c> değerini kovaya çevirir. Boş = filtre yok; tanınmayan değer false (400).</summary>
    public static bool TryParseStatus(string? value, out ParentAssignmentBucket? bucket)
    {
        bucket = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case ParentAssignmentStatuses.Completed: bucket = ParentAssignmentBucket.Completed; return true;
            case ParentAssignmentStatuses.Overdue: bucket = ParentAssignmentBucket.Overdue; return true;
            case ParentAssignmentStatuses.Pending: bucket = ParentAssignmentBucket.Pending; return true;
            default: return false;
        }
    }

    internal static string ToStatus(ParentAssignmentBucket bucket) => bucket switch
    {
        ParentAssignmentBucket.Completed => ParentAssignmentStatuses.Completed,
        ParentAssignmentBucket.Overdue => ParentAssignmentStatuses.Overdue,
        _ => ParentAssignmentStatuses.Pending
    };

    public async Task<ParentChildAssignmentListDto?> GetAssignmentsAsync(
        int parentUserId, int studentId, ParentAssignmentBucket? status, int page, CancellationToken ct = default)
    {
        // 1) Kapı: Active bağlantı değilse hiçbir veri sorgusu yok, audit yok.
        var grant = await _access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant == null)
            return null;

        // 2) Audit (veri okunmadan önce).
        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildAssignments, ct: ct);

        // 3) Veri.
        page = Math.Max(1, page);
        var now = _time.GetUtcNow().UtcDateTime;
        var scope = await ParentAssignmentScope.LoadAsync(_context, grant, now, ct);
        if (scope.Capped)
        {
            _logger?.LogWarning(
                "[ParentAssignments] Atama satırı tavanına ulaşıldı ({Cap}); liste kesilmiş olabilir: studentId={StudentId}",
                ParentAssignmentScope.MaxRows, grant.StudentId);
        }

        var filtered = scope.Items
            .Where(c => status == null || c.Bucket == status.Value)
            .ToList();
        var pageItems = Order(filtered)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        var result = new ParentChildAssignmentListDto
        {
            StudentId = grant.StudentId,
            Status = status.HasValue ? ToStatus(status.Value) : null,
            Page = page,
            PageSize = PageSize,
            TotalCount = filtered.Count,
            Counts = ParentAssignmentScope.Count(scope.Items)
        };
        if (pageItems.Count == 0)
            return result;

        // Yalnız sayfadaki satırlar zenginleştirilir (her biri TEK toplu sorgu; N+1 yok).
        var worksheetIds = pageItems.Select(c => c.Assignment.WorksheetId).Distinct().ToList();
        var worksheets = await _context.Worksheets.AsNoTracking()
            .Where(w => worksheetIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Name, Subject = w.Subject != null ? w.Subject.Name : null })
            .ToDictionaryAsync(w => w.Id, ct);

        var resultInstances = pageItems
            .Where(c => c.Instance is { HasResult: true })
            .Select(c => c.Instance!)
            .ToList();
        var scores = await ScoreInstancesAsync(resultInstances, ct);

        var teacherNames = await LookupNamesAsync(
            pageItems.Select(c => c.Assignment.CreateUserId ?? 0), ct);

        foreach (var item in pageItems)
        {
            worksheets.TryGetValue(item.Assignment.WorksheetId, out var worksheet);
            var instance = item.Instance is { HasResult: true } finished ? finished : null;
            result.Items.Add(new ParentChildAssignmentItemDto
            {
                WorksheetId = item.Assignment.WorksheetId,
                Title = worksheet?.Name ?? string.Empty,
                Subject = worksheet?.Subject,
                TeacherName = item.Assignment.CreateUserId is { } teacherId && teacherNames.TryGetValue(teacherId, out var name)
                    ? name
                    : null,
                StartAt = AsUtc(item.Assignment.StartAt),
                Deadline = item.Assignment.EndAt.HasValue ? AsUtc(item.Assignment.EndAt.Value) : null,
                Status = ToStatus(item.Bucket),
                TestInstanceId = instance?.InstanceId,
                Result = instance != null && scores.TryGetValue(instance.InstanceId, out var score) ? score : null
            });
        }

        return result;
    }

    /// <summary>
    /// Sıra: en yeni teslim tarihi önce — teslim tarihsiz (açık uçlu) atamalar en üstte; eşitlikte en yeni başlangıç, sonra
    /// worksheet id (kararlı sayfalama).
    /// </summary>
    internal static IEnumerable<ParentAssignmentScope.Classified> Order(IEnumerable<ParentAssignmentScope.Classified> items)
        => items
            .OrderByDescending(c => c.Assignment.EndAt ?? DateTime.MaxValue)
            .ThenByDescending(c => c.Assignment.StartAt)
            .ThenByDescending(c => c.Assignment.WorksheetId);

    public async Task<ParentTestResultLookup> GetTestResultAsync(
        int parentUserId, int studentId, int testInstanceId, CancellationToken ct = default)
    {
        // 1) Kapı.
        var grant = await _access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant == null)
            return ParentTestResultLookup.NoAccess;

        // 2) Kapsam çözümü (yalnızca id/durum/zaman — içerik yok). Review (#421): yalnızca listenin döndürdüğü kapsamdaki
        // (ParentAssignmentScope: görünür atama, 30 gün penceresi, worksheet başına temsilci atama) ve SONUCU OLAN oturumlar
        // açılır. Başka öğrencinin oturumu, olmayan id, silinmiş oturum, emekliye ayrılmış worksheet, hiç atanmamış (kendi
        // başına çözülen) test ya da pencere dışındaki eski test → 404; hangisi olduğu sızdırılmaz. Atamadan ÖNCE çözülmüş test,
        // atama var olduğunda açılır (#367 — liste ve öğretmen ekranıyla tutarlı; AssignmentInstanceWindow).
        // Audit bu çözümden SONRA, sonuç içeriği (cevaplar/konu sayıları) okunmadan ÖNCE yazılır: bulunan oturumda kaynak
        // id'si, 404'te kaynak null — taranan her olmayan id ayrı satır açmaz, 10 dk kovada tek satıra iner (tarama görünürlüğü
        // ayrıca IParentTestResultProbeMonitor'da).
        var row = testInstanceId <= 0
            ? null
            : (await ParentAssignmentScope.LoadAsync(_context, grant, _time.GetUtcNow().UtcDateTime, ct)).Items
                .Select(c => c.Instance)
                .FirstOrDefault(i => i is { HasResult: true } && i.InstanceId == testInstanceId);

        var worksheet = row == null
            ? null
            : await _context.Worksheets.AsNoTracking()
                .Where(w => w.Id == row.WorksheetId)
                .Select(w => new { w.Name, Subject = w.Subject != null ? w.Subject.Name : null })
                .FirstOrDefaultAsync(ct);

        var found = row != null && worksheet != null;

        // 3) Audit.
        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildTestResult,
            found ? row!.InstanceId : null, ct);

        if (!found)
        {
            _probeMonitor?.RecordNotFound(grant.ParentId, grant.StudentId);
            return new ParentTestResultLookup(true, null);
        }

        // 4) Sonuç içeriği.

        var answers = await LoadAnswersAsync(new[] { row!.InstanceId }, ct);
        var meta = await LoadQuestionMetaAsync(answers, ct);
        var unclassified = _localizer["worksheets.detail.unclassifiedTopic"].Value;

        return new ParentTestResultLookup(true, new ParentChildTestResultDto
        {
            StudentId = grant.StudentId,
            TestInstanceId = row.InstanceId,
            WorksheetId = row.WorksheetId,
            Title = worksheet!.Name,
            Subject = worksheet.Subject,
            Outcome = row.Status == WorksheetInstanceStatus.Expired ? ParentTestOutcomes.TimedOut : ParentTestOutcomes.Completed,
            // Review (#421): başlangıç/bitiş saate KESİLİR (V2 son aktivite kuralı; dakika hassasiyetinde hareket izi yok);
            // süre (DurationSeconds) kesin değerden hesaplanır.
            StartedAt = ParentDashboardService.TruncateToHour(row.StartTime),
            FinishedAt = row.EndTime.HasValue ? ParentDashboardService.TruncateToHour(row.EndTime.Value) : null,
            Score = Score(answers, meta, row.StartTime, row.EndTime),
            Topics = answers
                .GroupBy(a => meta.TryGetValue(a.WorksheetQuestionId, out var m)
                    ? WorksheetScoring.TopicKey(true, m.TopicId, m.TopicName, unclassified)
                    : WorksheetScoring.TopicKey(false, null, null, unclassified))
                .Select(g =>
                {
                    var (correct, wrong, blank) = Tally(g, meta);
                    return new ParentTestTopicResultDto
                    {
                        TopicId = g.Key.TopicId,
                        Name = g.Key.Name,
                        CorrectCount = correct,
                        WrongCount = wrong,
                        BlankCount = blank,
                        TotalCount = g.Count()
                    };
                })
                .OrderByDescending(t => t.TotalCount)
                .ThenBy(t => t.Name, StringComparer.Ordinal)
                .ToList()
        });
    }

    internal sealed record AnswerRow(int InstanceId, int WorksheetQuestionId, int? SelectedAnswerId);

    internal sealed record QuestionMeta(int? CorrectAnswerId, int? TopicId, string? TopicName);

    private async Task<Dictionary<int, ParentTestScoreDto>> ScoreInstancesAsync(
        IReadOnlyList<ParentAssignmentScope.InstanceRow> instances, CancellationToken ct)
    {
        if (instances.Count == 0)
            return new Dictionary<int, ParentTestScoreDto>();

        var answers = await LoadAnswersAsync(instances.Select(i => i.InstanceId).ToList(), ct);
        var meta = await LoadQuestionMetaAsync(answers, ct);
        var byInstance = answers.ToLookup(a => a.InstanceId);
        return instances.ToDictionary(
            i => i.InstanceId,
            i => Score(byInstance[i.InstanceId].ToList(), meta, i.StartTime, i.EndTime));
    }

    private Task<List<AnswerRow>> LoadAnswersAsync(IReadOnlyCollection<int> instanceIds, CancellationToken ct)
        => _context.TestInstanceQuestions.AsNoTracking()
            .Where(q => instanceIds.Contains(q.WorksheetInstanceId))
            .Select(q => new AnswerRow(q.WorksheetInstanceId, q.WorksheetQuestionId, q.SelectedAnswerId))
            .ToListAsync(ct);

    /// <summary>
    /// Doğru şık + konu, yalnızca puanlama için (yanıta girmez). Silinmiş soru/worksheet sorusu sözlükte yer almaz → o satır
    /// cevaplandıysa yanlış, cevaplanmadıysa boş sayılır (öğrenci ekranındaki <c>correctMap</c> ile aynı).
    /// </summary>
    private async Task<Dictionary<int, QuestionMeta>> LoadQuestionMetaAsync(IReadOnlyCollection<AnswerRow> answers, CancellationToken ct)
    {
        var ids = answers.Select(a => a.WorksheetQuestionId).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, QuestionMeta>();

        return await _context.TestQuestions.AsNoTracking()
            .Where(wq => ids.Contains(wq.Id))
            .Select(wq => new
            {
                wq.Id,
                wq.Question.CorrectAnswerId,
                wq.Question.TopicId,
                TopicName = wq.Question.Topic != null ? wq.Question.Topic.Name : null
            })
            .ToDictionaryAsync(x => x.Id, x => new QuestionMeta(x.CorrectAnswerId, x.TopicId, x.TopicName), ct);
    }

    internal static ParentTestScoreDto Score(
        IReadOnlyCollection<AnswerRow> answers, IReadOnlyDictionary<int, QuestionMeta> meta, DateTime startTime, DateTime? endTime)
    {
        var (correct, wrong, blank) = Tally(answers, meta);
        var total = answers.Count;
        return new ParentTestScoreDto
        {
            ScorePercent = WorksheetScoring.ScorePercent(correct, total),
            CorrectCount = correct,
            WrongCount = wrong,
            BlankCount = blank,
            TotalCount = total,
            // Review (#421): dakikaya AŞAĞI yuvarlanır — saate kesilmiş başlangıç/bitişle birlikte dakika geri hesaplanamasın.
            DurationSeconds = WorksheetScoring.DurationSeconds(startTime, endTime) / 60 * 60
        };
    }

    private static (int Correct, int Wrong, int Blank) Tally(IEnumerable<AnswerRow> answers, IReadOnlyDictionary<int, QuestionMeta> meta)
        => WorksheetScoring.Tally(answers, a => a.SelectedAnswerId,
            a => meta.TryGetValue(a.WorksheetQuestionId, out var m) ? m.CorrectAnswerId : null);

    /// <summary>Öğretmen adları: auth-api'ye TEK toplu çağrı; erişilemezse/süre aşılırsa boş (ad null döner).</summary>
    private async Task<IReadOnlyDictionary<int, string>> LookupNamesAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, string>();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(NameLookupTimeout);
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(ids, timeout.Token);
            return users
                .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _logger?.LogWarning(ex, "[ParentAssignments] Öğretmen adları çözülemedi ({Count} kullanıcı).", ids.Count);
            return new Dictionary<int, string>();
        }
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
