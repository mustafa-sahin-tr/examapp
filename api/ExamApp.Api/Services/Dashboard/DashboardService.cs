using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    // issue #265: gün kovaları yerel takvim günüdür (Dashboard:TimeZone, varsayılan Europe/Istanbul). DI'siz kurulumda varsayılan.
    private readonly ILocalDayCalendar _dayCalendar;

    public DashboardService(AppDbContext context, ILocalDayCalendar? dayCalendar = null)
    {
        _context = context;
        _dayCalendar = dayCalendar ?? LocalDayCalendar.Default;
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

        return new DashboardSummaryDto
        {
            TeacherCount = teacherCount,
            StudentCount = studentCount,
            WorksheetCount = worksheetCount,
            QuestionCount = questionCount,
            AiClassifiedQuestionCount = aiClassifiedCount,
            AiClassifiedRatio = questionCount == 0 ? 0 : (double)aiClassifiedCount / questionCount
        };
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
