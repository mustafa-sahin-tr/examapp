using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;

namespace ExamApp.Api.Services.Dashboard;

public class DashboardService : IDashboardService
{
    /// <summary>Issue #1315: tek "AI ile sınıflandır" çağrısının kuyruğa aldığı en çok soru (sınıflandırıcı sırayla işler).</summary>
    public const int ClassificationRetryBatchSize = 200;

    private readonly AppDbContext _context;

    // issue #265: gün kovaları yerel takvim günüdür (Dashboard:TimeZone, varsayılan Europe/Istanbul). DI'siz kurulumda varsayılan.
    private readonly ILocalDayCalendar _dayCalendar;

    // issue #441: hoşgörü süresi (admin "velisiz öğrenci" sayıları). DI'siz kurulumda varsayılan (30 gün).
    private readonly IOptionsMonitor<ParentRequirementOptions>? _parentRequirement;
    private readonly TimeProvider _time;

    public DashboardService(AppDbContext context, ILocalDayCalendar? dayCalendar = null,
        IOptionsMonitor<ParentRequirementOptions>? parentRequirement = null, TimeProvider? time = null)
    {
        _context = context;
        _dayCalendar = dayCalendar ?? LocalDayCalendar.Default;
        _parentRequirement = parentRequirement;
        _time = time ?? TimeProvider.System;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        // Beş basit COUNT sorgusu. AppDbContext thread-safe olmadığı için art arda çalıştırılır
        // (Task.WhenAll aynı context üzerinde kullanılamaz). Include/navigation yok, N+1 yok.
        var teacherCount = await _context.Teachers.AsNoTracking().CountAsync(ct);
        var studentCount = await _context.Students.AsNoTracking().CountAsync(ct);
        var worksheetCount = await _context.Worksheets.AsNoTracking().CountAsync(ct);
        var questionCount = await _context.Questions.AsNoTracking().CountAsync(ct);
        var aiClassifiedCount = await _context.Questions.AsNoTracking()
            .CountAsync(q => q.ClassificationSource == ClassificationSource.AI, ct);

        // issue #441: kayıp riskini izlemek için velisiz öğrenci kırılımı — review R2: yaptırımla AYNI kural (StudentParentStatusService
        // .QueryByMode = Evaluate'in SQL karşılığı). Hedef küme = Active velisi olmayan ve kısıtlı (aktivasyon bekleyen) modda olmayanlar
        // (= Grace + Expired; sayacı başlamamışlar Grace'te). Rol bilgisi DB'de olmadığından öğrenci satırı olan her hesap sayılır.
        var now = _time.GetUtcNow().UtcDateTime;
        var modes = Parents.StudentParentStatusService.QueryByMode(_context, now, _parentRequirement?.CurrentValue ?? new ParentRequirementOptions());
        var awaitingActivation = await modes.AwaitingActivation.CountAsync(ct);
        var inGrace = await modes.Grace.CountAsync(ct);
        var expired = await modes.Expired.CountAsync(ct);
        var studentsWithoutParent = inGrace + expired;

        // issue #887: sistem durumu satırı — sınıflandırıcının son çalışması ve yayımlanamayan olay sayısı.
        var lastAiClassificationAt = await _context.Questions.AsNoTracking()
            .Where(q => q.ClassificationSource == ClassificationSource.AI)
            .MaxAsync(q => (DateTime?)(q.UpdateTime ?? q.CreateTime), ct); // boş kümede null
        var failedOutbox = await _context.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.ProcessedAt == null && m.Error != null, ct);

        var classification = await GetClassificationBreakdownAsync(questionCount, ct);

        return new DashboardSummaryDto
        {
            StudentsWithoutParent = studentsWithoutParent,
            LastAiClassificationAt = lastAiClassificationAt,
            FailedOutboxCount = failedOutbox,
            StudentsInParentGrace = inGrace,
            StudentsParentGraceExpired = expired,
            StudentsAwaitingParentActivation = awaitingActivation,
            TeacherCount = teacherCount,
            StudentCount = studentCount,
            WorksheetCount = worksheetCount,
            QuestionCount = questionCount,
            AiClassifiedQuestionCount = aiClassifiedCount,
            AiClassifiedRatio = questionCount == 0 ? 0 : (double)aiClassifiedCount / questionCount,
            Classification = classification
        };
    }

    /// <summary>
    /// Issue #1315: soru bankasında görünen sorular (#690: hak sahibi talebiyle kaldırılmamış, lisansı sürüyor). Kırılım ve yeniden
    /// sınıflandırma yalnız bunlara bakar; kaldırılmış sorular bankada açılmaz ve sınıflandırılmalarına AI harcanmaz.
    /// </summary>
    private IQueryable<Question> PublishedQuestions()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return _context.Questions.AsNoTracking()
            .Where(q => !q.IsUnpublished && (q.LicenseEndsAt == null || q.LicenseEndsAt > now));
    }

    /// <summary>Issue #1315: kaynak × (ders + konu var mı) kırılımı; tek group-by sorgusu.</summary>
    private async Task<ClassificationBreakdownDto> GetClassificationBreakdownAsync(int questionCount, CancellationToken ct)
    {
        var rows = await PublishedQuestions()
            .GroupBy(q => new { q.ClassificationSource, Classified = q.SubjectId != null && q.TopicId != null })
            .Select(g => new { g.Key.ClassificationSource, g.Key.Classified, Count = g.Count() })
            .ToListAsync(ct);

        var result = new ClassificationBreakdownDto();
        foreach (var row in rows)
        {
            if (!row.Classified)
            {
                if (row.ClassificationSource == ClassificationSource.AI) result.AiNoTopic += row.Count;
                else result.NotClassified += row.Count;
            }
            else if (row.ClassificationSource == ClassificationSource.AI) result.Ai += row.Count;
            else if (row.ClassificationSource == ClassificationSource.Book) result.Book += row.Count;
            else result.Teacher += row.Count; // Human ya da eski, kaynağı boş sorular
        }

        var classified = result.Ai + result.Book + result.Teacher;
        var published = classified + result.AiNoTopic + result.NotClassified;
        result.Unpublished = questionCount - published;
        result.ClassifiedRatio = published == 0 ? 0 : (double)classified / published;
        return result;
    }

    public async Task<ClassificationRetryResultDto> QueueClassificationRetryAsync(ClassificationRetryGroup group, CancellationToken ct = default)
    {
        // Konusuz sorular; alt konusu olan (kitap içindekilerinden) sorular yeniden sınıflandırılmaz. Öğretmen kilidi (#590) yalnız
        // konuyla birlikte konur, bu yüzden kilitli sorular zaten bu kümede değildir.
        var candidates = PublishedQuestions()
            .Where(q => (q.SubjectId == null || q.TopicId == null) && !q.QuestionSubTopics.Any());
        candidates = group == ClassificationRetryGroup.AiNoTopic
            ? candidates.Where(q => q.ClassificationSource == ClassificationSource.AI)
            : candidates.Where(q => q.ClassificationSource == null || q.ClassificationSource != ClassificationSource.AI);

        var total = await candidates.CountAsync(ct);
        var batch = await candidates
            .OrderBy(q => q.Id)
            .Take(ClassificationRetryBatchSize)
            .Select(q => new
            {
                q.Id,
                q.SubjectId,
                q.TopicId,
                q.Text,
                // #565: taksonomi sorunun bağlı olduğu testin sınıfına daralır; testsiz soruda bütün taksonomi.
                GradeId = _context.TestQuestions
                    .Where(tq => tq.QuestionId == q.Id && tq.Worksheet.GradeId > 0)
                    .OrderBy(tq => tq.Id)
                    .Select(tq => (int?)tq.Worksheet.GradeId)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var queuedAt = _time.GetUtcNow().UtcDateTime;
        foreach (var q in batch)
        {
            // Kaynak boş gönderilir: tüketici "AI" kaynaklı olayı atlar; sonuç yine ClassificationSource=AI olarak yazılır.
            var @event = new QuestionCreatedEvent
            {
                QuestionId = q.Id,
                SubjectId = q.SubjectId,
                TopicId = q.TopicId,
                GradeId = q.GradeId,
                ClassificationSource = null,
                SubTopicIds = new List<int>(),
                TaxonomyLocked = false,
                CreatedAt = queuedAt,
                Text = q.Text
            };
            _context.OutboxMessages.Add(new OutboxMessage
            {
                Type = OutboxEventRegistry.NameFor<QuestionCreatedEvent>(),
                Content = JsonSerializer.Serialize(@event),
                CreatedAt = queuedAt
            });
        }

        await _context.SaveChangesAsync(ct);
        return new ClassificationRetryResultDto { Queued = batch.Count, Remaining = total - batch.Count };
    }

    public async Task<DashboardTrendsDto> GetTrendsAsync(int days, CancellationToken ct = default)
    {
        // issue #265: pencere ve gün kovaları YEREL takvim günü (öğretmen aktivite uçlarıyla aynı ILocalDayCalendar).
        // Sınırlar C#'ta UTC anı olarak hesaplanır (Kind=Utc — Npgsql timestamptz karşılaştırması için şart; audit
        // interceptor zaman damgalarını UtcNow ile yazar). SQL'de gün = (ts + sabit kaydırma).Date: parça boyunca ofset
        // sabit olduğundan sağlayıcıdan bağımsız (Npgsql + SQLite) çevrilir, AT TIME ZONE gerekmez. Europe/Istanbul'da
        // pencere tek parça → kaynak başına tek sorgu (önceki gibi); DST'li bir bölgede geçiş günü ayrı parça olur.
        var window = _dayCalendar.LastDays(days);

        // Dört kaynak da tarih aralığına göre filtrelenip SQL tarafında group-by yapılır; yalnızca
        // veri olan günler döner (sparse). 0-dolgu bellek içinde yapılır, tam tablo taraması yok.
        // AppDbContext thread-safe olmadığı için art arda await edilir.
        var created = await CountByLocalDayAsync(window, (from, to, shift) => _context.Questions.AsNoTracking()
            .Where(q => q.CreateTime >= from && q.CreateTime < to)
            .GroupBy(q => q.CreateTime.AddMinutes(shift).Date)
            .Select(g => new DayCount(g.Key, g.Count())), ct);

        // Pratik oturumu: "çözüldü" anı = AnsweredAt.
        var practiceSolved = await CountByLocalDayAsync(window, (from, to, shift) => _context.PracticeSessionQuestions.AsNoTracking()
            .Where(p => p.AnsweredAt != null && p.AnsweredAt >= from && p.AnsweredAt < to)
            .GroupBy(p => p.AnsweredAt!.Value.AddMinutes(shift).Date)
            .Select(g => new DayCount(g.Key, g.Count())), ct);

        // Test/worksheet instance: satır test başlarken boş açılır (CreateTime = başlama anı);
        // cevap verildiğinde SelectedAnswerId/AnswerPayload set edilir ve UpdateTime cevap anını taşır.
        // issue #265: IX_TestInstanceQuestions_UpdateTime_Answered (kısmi, kapsayan) bu filtreye göre kurulu.
        var instanceSolved = await CountByLocalDayAsync(window, (from, to, shift) => _context.TestInstanceQuestions.AsNoTracking()
            .Where(w => (w.SelectedAnswerId != null || w.AnswerPayload != null)
                        && w.UpdateTime != null && w.UpdateTime >= from && w.UpdateTime < to)
            .GroupBy(w => w.UpdateTime!.Value.AddMinutes(shift).Date)
            .Select(g => new DayCount(g.Key, g.Count())), ct);

        // Öğrenci login'i (issue #89): yalnızca başarılı denemeler ve Student rolü sayılır. Role
        // alanının yazımı (Student / student) garanti değil; ToLower() Npgsql'de LOWER() olarak
        // SQL tarafına çevrilir, bellek içi filtre yapılmaz.
        var studentLogins = await CountByLocalDayAsync(window, (from, to, shift) => _context.LoginEvents.AsNoTracking()
            .Where(l => l.Success && l.OccurredAtUtc >= from && l.OccurredAtUtc < to && l.Role.ToLower() == "student")
            .GroupBy(l => l.OccurredAtUtc.AddMinutes(shift).Date)
            .Select(g => new DayCount(g.Key, g.Count())), ct);

        return new DashboardTrendsDto
        {
            QuestionCreated = BuildSeries(window, created),
            QuestionSolved = BuildSeries(window, practiceSolved.Concat(instanceSolved)),
            StudentLogin = BuildSeries(window, studentLogins)
        };
    }

    /// <summary>
    /// Pencerenin her sabit-ofsetli parçası için <paramref name="query"/>'yi (from, to, kaydırma dakikası) çalıştırır ve
    /// sonuçları yerel güne (<see cref="DateOnly"/>) çevirir. Tek günlük DST geçiş parçasında ofset gün içinde değiştiği
    /// için SQL'in verdiği tarih yok sayılır; parçanın tüm satırları o güne sayılır.
    /// </summary>
    private static async Task<List<LocalDayCount>> CountByLocalDayAsync(
        LocalDayWindow window, Func<DateTime, DateTime, double, IQueryable<DayCount>> query, CancellationToken ct)
    {
        var result = new List<LocalDayCount>();
        foreach (var segment in window.Segments)
        {
            var rows = await query(segment.StartUtc, segment.EndUtc, segment.Offset.TotalMinutes).ToListAsync(ct);
            foreach (var row in rows)
                result.Add(new LocalDayCount(segment.SingleDay ?? DateOnly.FromDateTime(row.Day), row.Count));
        }

        return result;
    }

    private sealed record DayCount(DateTime Day, int Count);

    private sealed record LocalDayCount(DateOnly Day, int Count);

    /// <summary>
    /// Sparse (yalnızca veri olan günler) sonuçları, pencerenin ilk yerel gününden başlayan boşluksuz seriye
    /// dönüştürür. Aynı güne düşen birden fazla kaynak (practice + instance) toplanır.
    /// </summary>
    private static List<DailyPointDto> BuildSeries(LocalDayWindow window, IEnumerable<LocalDayCount> sparse)
    {
        var byDay = new Dictionary<DateOnly, int>();
        foreach (var item in sparse)
            byDay[item.Day] = byDay.TryGetValue(item.Day, out var existing) ? existing + item.Count : item.Count;

        var series = new List<DailyPointDto>(window.Days);
        for (var day = window.FirstDay; day <= window.LastDay; day = day.AddDays(1))
            series.Add(new DailyPointDto { Date = day, Count = byDay.GetValueOrDefault(day) });

        return series;
    }
}
