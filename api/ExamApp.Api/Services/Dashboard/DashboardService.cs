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

    public DashboardService(AppDbContext context)
    {
        _context = context;
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
        // Audit interceptor (AppDbContext.ApplyAuditInfo) tüm zaman damgalarını UtcNow ile yazar;
        // cutoff da Kind=Utc olmalı, aksi halde Npgsql timestamptz karşılaştırmasında hata verir.
        var today = DateTime.UtcNow.Date;
        var cutoff = today.AddDays(-(days - 1)); // bugün dahil son `days` gün

        // Dört sorgu da tarih aralığına göre filtrelenip SQL tarafında group-by yapılır; yalnızca
        // veri olan günler döner (sparse). 0-dolgu bellek içinde yapılır, tam tablo taraması yok.
        // AppDbContext thread-safe olmadığı için art arda await edilir.
        var created = await _context.Questions.AsNoTracking()
            .Where(q => q.CreateTime >= cutoff)
            .GroupBy(q => q.CreateTime.Date)
            .Select(g => new DayCount(g.Key, g.Count()))
            .ToListAsync(ct);

        // Pratik oturumu: "çözüldü" anı = AnsweredAt.
        var practiceSolved = await _context.PracticeSessionQuestions.AsNoTracking()
            .Where(p => p.AnsweredAt != null && p.AnsweredAt >= cutoff)
            .GroupBy(p => p.AnsweredAt!.Value.Date)
            .Select(g => new DayCount(g.Key, g.Count()))
            .ToListAsync(ct);

        // Test/worksheet instance: satır test başlarken boş açılır (CreateTime = başlama anı);
        // cevap verildiğinde SelectedAnswerId/AnswerPayload set edilir ve UpdateTime cevap anını taşır.
        var instanceSolved = await _context.TestInstanceQuestions.AsNoTracking()
            .Where(w => (w.SelectedAnswerId != null || w.AnswerPayload != null)
                        && w.UpdateTime != null && w.UpdateTime >= cutoff)
            .GroupBy(w => w.UpdateTime!.Value.Date)
            .Select(g => new DayCount(g.Key, g.Count()))
            .ToListAsync(ct);

        // Öğrenci login'i (issue #89): yalnızca başarılı denemeler ve Student rolü sayılır. Role
        // alanının yazımı (Student / student) garanti değil; ToLower() Npgsql'de LOWER() olarak
        // SQL tarafına çevrilir, bellek içi filtre yapılmaz.
        var studentLogins = await _context.LoginEvents.AsNoTracking()
            .Where(l => l.Success && l.OccurredAtUtc >= cutoff && l.Role.ToLower() == "student")
            .GroupBy(l => l.OccurredAtUtc.Date)
            .Select(g => new DayCount(g.Key, g.Count()))
            .ToListAsync(ct);

        return new DashboardTrendsDto
        {
            QuestionCreated = BuildSeries(cutoff, days, created),
            QuestionSolved = BuildSeries(cutoff, days, practiceSolved.Concat(instanceSolved)),
            StudentLogin = BuildSeries(cutoff, days, studentLogins)
        };
    }

    private sealed record DayCount(DateTime Day, int Count);

    /// <summary>
    /// Sparse (yalnızca veri olan günler) sonuçları, cutoff'tan başlayan <paramref name="days"/> günlük
    /// boşluksuz seriye dönüştürür. Aynı güne düşen birden fazla kaynak (practice + instance) toplanır.
    /// </summary>
    private static List<DailyPointDto> BuildSeries(DateTime cutoff, int days, IEnumerable<DayCount> sparse)
    {
        var byDay = new Dictionary<DateOnly, int>();
        foreach (var item in sparse)
        {
            var day = DateOnly.FromDateTime(item.Day);
            byDay[day] = byDay.TryGetValue(day, out var existing) ? existing + item.Count : item.Count;
        }

        var series = new List<DailyPointDto>(days);
        var start = DateOnly.FromDateTime(cutoff);
        for (var i = 0; i < days; i++)
        {
            var day = start.AddDays(i);
            series.Add(new DailyPointDto { Date = day, Count = byDay.GetValueOrDefault(day) });
        }

        return series;
    }
}
