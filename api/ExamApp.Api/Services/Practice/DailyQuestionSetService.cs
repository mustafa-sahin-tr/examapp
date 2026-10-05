using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// "Günün soruları" (issue #99).
/// <list type="bullet">
/// <item>Gün: <see cref="ILocalDayCalendar.Today"/> (Europe/Istanbul; dashboard'larla aynı gün sınırı, #265/#294).</item>
/// <item>Havuz: serbest pratikle aynı kural (<see cref="PracticeQuestionPool"/>) — öğrencinin sınıfı, Normal görünürlük.</item>
/// <item>Seçim: öğrenci+gün tohumlu deterministik karıştırma (<see cref="SeedFor"/>); son
/// <see cref="DailyQuestionsOptions.RecentAnswerExclusionDays"/> günde cevaplananlar sona itilir (havuz yetmezse kullanılır).</item>
/// <item>Yarış: (StudentId, Day) unique index; kaybeden unique ihlalini yakalayıp kazananın setini okur.</item>
/// <item>Havuz boşsa set KAYDEDİLMEZ (Empty döner) — gün içinde soru eklenirse sonraki istekte set üretilebilir.</item>
/// </list>
/// </summary>
public sealed class DailyQuestionSetService : IDailyQuestionSetService
{
    private readonly AppDbContext _context;
    private readonly ILocalDayCalendar _calendar;
    private readonly DailyQuestionsOptions _options;

    public DailyQuestionSetService(AppDbContext context, ILocalDayCalendar calendar, IOptions<DailyQuestionsOptions> options)
    {
        _context = context;
        _calendar = calendar;
        _options = options.Value;
    }

    public async Task<DailySetDto> GetTodayAsync(StudentProfileDto student, CancellationToken ct = default)
    {
        var today = _calendar.Today;
        var set = await GetOrCreateSetAsync(student, today, ct);
        if (set == null)
            return EmptyDto(today, _options.QuestionCount);

        // Canlı liste boş (setin tüm soruları gün içinde silindi): oturum açılmamışsa Empty, açılmışsa Completed (CR U1).
        if (set.QuestionIds.Count == 0 && set.PracticeSessionId == null)
            return EmptyDto(today, set.TargetCount);

        var progress = await LoadProgressAsync(set, ct);
        return new DailySetDto
        {
            Date = today,
            Status = progress.Status,
            Total = set.QuestionIds.Count,
            TargetCount = set.TargetCount,
            Answered = progress.Answered,
            Correct = progress.Correct,
            Wrong = progress.Answered - progress.Correct - progress.Skipped,
            Skipped = progress.Skipped,
            SessionId = set.PracticeSessionId,
            Scope = null
        };
    }

    public async Task<DailyStartResultDto?> StartTodayAsync(StudentProfileDto student, CancellationToken ct = default)
    {
        var today = _calendar.Today;
        var set = await GetOrCreateSetAsync(student, today, ct);
        if (set == null || (set.QuestionIds.Count == 0 && set.PracticeSessionId == null))
            return null;

        var sessionId = set.PracticeSessionId ?? await LinkNewSessionAsync(set, ct);

        var progress = await LoadProgressAsync(set with { PracticeSessionId = sessionId }, ct);
        if (progress.Status != DailySetStatus.Completed)
        {
            // Öğrenci oturumu set bitmeden "Bitir" ile kapattıysa: aynı oturum yeniden açılır (yeni oturum/ikinci set yok).
            // İzlenen güncelleme: audit (UpdateTime/UpdateUserId) ApplyAuditInfo'dan gelir (CR Ö9).
            var ended = await _context.PracticeSessions
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.Status == PracticeSessionStatus.Ended, ct);
            if (ended != null)
            {
                ended.Status = PracticeSessionStatus.Active;
                ended.EndTime = null;
                await _context.SaveChangesAsync(ct);
            }
        }

        return new DailyStartResultDto { SessionId = sessionId, Status = progress.Status };
    }

    // --- set üretimi ---

    /// <summary>Setin okumada gereken hali (izlenmeyen).</summary>
    /// <see cref="QuestionIds"/> = CANLI soru listesi (<see cref="DailySetQueries"/>); <see cref="TargetCount"/> set'te kaydedilen N (CR Ö6).
    private sealed record SetSnapshot(int Id, int StudentId, int GradeId, int TargetCount, int? PracticeSessionId, IReadOnlyList<int> QuestionIds);

    private async Task<SetSnapshot?> GetOrCreateSetAsync(StudentProfileDto student, DateOnly day, CancellationToken ct)
    {
        var existing = await LoadSetAsync(student.Id, day, ct);
        if (existing != null)
            return existing;

        if (!student.GradeId.HasValue)
            return null;

        var questionIds = await PickQuestionIdsAsync(student.Id, student.GradeId.Value, day, ct);
        if (questionIds.Count == 0)
            return null;

        var set = new DailyQuestionSet
        {
            StudentId = student.Id,
            Day = day,
            GradeId = student.GradeId.Value,
            TargetCount = _options.QuestionCount,
            ScopeJson = null,
            Items = questionIds.Select((id, i) => new DailyQuestionSetItem { QuestionId = id, Order = i }).ToList()
        };

        _context.DailyQuestionSets.Add(set);
        try
        {
            await _context.SaveChangesAsync(ct);
            return new SetSnapshot(set.Id, set.StudentId, set.GradeId, set.TargetCount, null, questionIds);
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // Eşzamanlı ilk istek aynı (StudentId, Day) setini önce yazdı: bizimkini bırak, kazananınkini oku.
            foreach (var item in set.Items.ToList()) // Detach, navigasyon düzeltmesiyle koleksiyonu değiştirir.
                _context.Entry(item).State = EntityState.Detached;
            _context.Entry(set).State = EntityState.Detached;

            return await LoadSetAsync(student.Id, day, ct)
                ?? throw new InvalidOperationException("Daily question set unique violation but no existing set found.");
        }
    }

    private async Task<SetSnapshot?> LoadSetAsync(int studentId, DateOnly day, CancellationToken ct)
    {
        var row = await _context.DailyQuestionSets
            .AsNoTracking()
            .Where(d => d.StudentId == studentId && d.Day == day)
            .Select(d => new { d.Id, d.StudentId, d.GradeId, d.TargetCount, d.PracticeSessionId })
            .FirstOrDefaultAsync(ct);

        if (row == null)
            return null;

        var live = await DailySetQueries.LoadLiveQuestionIdsAsync(_context, row.Id, ct);
        return new SetSnapshot(row.Id, row.StudentId, row.GradeId, row.TargetCount, row.PracticeSessionId, live);
    }

    private async Task<List<int>> PickQuestionIdsAsync(int studentId, int gradeId, DateOnly day, CancellationToken ct)
    {
        // Yalnız id'ler çekilir (deterministik karıştırma bellekte; sıralı okuma tohumu sabit tutar).
        var poolIds = await PracticeQuestionPool.Build(_context, gradeId, Array.Empty<int>(), Array.Empty<int>())
            .Select(q => q.Id)
            .OrderBy(id => id)
            .ToListAsync(ct);

        if (poolIds.Count == 0)
            return poolIds;

        var recent = await LoadRecentlyAnsweredAsync(studentId, day, ct);
        return SelectQuestions(poolIds, recent, _options.QuestionCount, SeedFor(studentId, day));
    }

    /// <summary>
    /// Öğrencinin son <see cref="DailyQuestionsOptions.RecentAnswerExclusionDays"/> yerel günde (bugün dahil) cevapladığı
    /// sorular: pratik log'u (pas hariç) + worksheet cevapları.
    /// </summary>
    private async Task<HashSet<int>> LoadRecentlyAnsweredAsync(int studentId, DateOnly day, CancellationToken ct)
    {
        var days = _options.RecentAnswerExclusionDays;
        if (days <= 0)
            return new HashSet<int>();

        var cutoffUtc = _calendar.StartOfDayUtc(day.AddDays(-(days - 1)));

        var practice = await _context.PracticeSessionQuestions
            .AsNoTracking()
            .Where(p => p.PracticeSession.StudentId == studentId
                && p.AnsweredAt != null && p.AnsweredAt >= cutoffUtc && !p.IsSkipped)
            .Select(p => p.QuestionId)
            .Distinct()
            .ToListAsync(ct);

        // IX_TestInstanceQuestions_UpdateTime_Answered yüklemiyle aynı şekil (cevaplı + UpdateTime aralığı).
        var worksheet = await _context.TestInstanceQuestions
            .AsNoTracking()
            .Where(q => q.WorksheetInstance.StudentId == studentId
                && (q.SelectedAnswerId != null || q.AnswerPayload != null)
                && q.UpdateTime != null && q.UpdateTime >= cutoffUtc)
            .Select(q => q.WorksheetQuestion.QuestionId)
            .Distinct()
            .ToListAsync(ct);

        var result = practice.ToHashSet();
        result.UnionWith(worksheet);
        return result;
    }

    /// <summary>
    /// Saf seçim kuralı (test edilebilir): <paramref name="poolIds"/> tohumla karıştırılır; son dönemde cevaplanmamış
    /// sorular önce, cevaplananlar (havuz yetmezse) sonra; ilk <paramref name="count"/> alınır. Tekrar yok.
    /// </summary>
    internal static List<int> SelectQuestions(IReadOnlyList<int> poolIds, IReadOnlySet<int> recentlyAnswered, int count, int seed)
    {
        var ordered = poolIds.Distinct().OrderBy(id => id).ToList();
        var fresh = ordered.Where(id => !recentlyAnswered.Contains(id)).ToList();
        var recent = ordered.Where(recentlyAnswered.Contains).ToList();

        var rng = new Random(seed);
        Shuffle(fresh, rng);
        Shuffle(recent, rng);

        return fresh.Concat(recent).Take(Math.Max(0, count)).ToList();
    }

    /// <summary>
    /// Öğrenci + gün tohumu. <see cref="string.GetHashCode()"/> süreç başına rastgele olduğu için kullanılmaz; SHA-256'nın ilk
    /// 4 baytı süreçler/sunucular arasında aynıdır (yarışan iki istek de aynı seti üretir).
    /// </summary>
    internal static int SeedFor(int studentId, DateOnly day)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"daily-questions:{studentId}:{day:yyyy-MM-dd}"));
        return BitConverter.ToInt32(hash, 0);
    }

    private static void Shuffle(List<int> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // --- oturum ---

    /// <summary>
    /// Sete yeni pratik oturumu açıp bağlar. Eşzamanlı iki "Başla"da tek oturum kalır: bağlama koşullu UPDATE
    /// (<c>PracticeSessionId IS NULL</c>) ile yapılır, kaybeden kendi oturumunu aynı transaction'da geri alır ve kazananın
    /// oturumunu döner. Retry'lı execution strategy içinde; her deneme önceki denemenin entity'sini bırakır.
    /// </summary>
    private async Task<int> LinkNewSessionAsync(SetSnapshot set, CancellationToken ct)
    {
        PracticeSession? attempt = null;
        var strategy = _context.Database.CreateExecutionStrategy();

        var linked = await strategy.ExecuteAsync(async () =>
        {
            if (attempt != null)
                _context.Entry(attempt).State = EntityState.Detached;

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            attempt = new PracticeSession
            {
                StudentId = set.StudentId,
                GradeId = set.GradeId,
                StartTime = DateTime.UtcNow,
                Status = PracticeSessionStatus.Active
            };
            _context.PracticeSessions.Add(attempt);
            await _context.SaveChangesAsync(ct);

            var affected = await _context.DailyQuestionSets
                .Where(d => d.Id == set.Id && d.PracticeSessionId == null)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.PracticeSessionId, (int?)attempt.Id), ct);

            if (affected == 0)
            {
                await tx.RollbackAsync(ct);
                _context.Entry(attempt).State = EntityState.Detached;
                attempt = null;
                return (int?)null;
            }

            await tx.CommitAsync(ct);
            return (int?)attempt.Id;
        });

        if (linked.HasValue)
            return linked.Value;

        return await _context.DailyQuestionSets.AsNoTracking()
            .Where(d => d.Id == set.Id)
            .Select(d => d.PracticeSessionId)
            .FirstAsync(ct)
            ?? throw new InvalidOperationException("Daily question set session link lost.");
    }

    // --- ilerleme ---

    private sealed record Progress(string Status, int Answered, int Correct, int Skipped);

    private async Task<Progress> LoadProgressAsync(SetSnapshot set, CancellationToken ct)
    {
        if (set.PracticeSessionId == null)
            return new Progress(DailySetStatus.NotStarted, 0, 0, 0);

        var ids = set.QuestionIds;
        var rows = await _context.PracticeSessionQuestions
            .AsNoTracking()
            .Where(p => p.PracticeSessionId == set.PracticeSessionId && p.AnsweredAt != null && ids.Contains(p.QuestionId))
            .Select(p => new { p.IsCorrect, p.IsSkipped })
            .ToListAsync(ct);

        var answered = rows.Count;
        var status = answered >= ids.Count
            ? DailySetStatus.Completed
            : answered > 0 ? DailySetStatus.InProgress : DailySetStatus.NotStarted;

        return new Progress(status, answered, rows.Count(r => r.IsCorrect), rows.Count(r => r.IsSkipped));
    }

    private static DailySetDto EmptyDto(DateOnly day, int targetCount) => new()
    {
        Date = day,
        Status = DailySetStatus.Empty,
        Total = 0,
        TargetCount = targetCount,
        SessionId = null,
        Scope = null
    };
}
