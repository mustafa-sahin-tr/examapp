using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// <item>Kapsam (#66): GET seti yine rastgele üretir; öğrenci "Başla"da ders/konu/alt konu seçerse ve setin oturumu henüz
/// açılmamışsa set, oturumu bağlayan aynı transaction'da kapsamdan (eksiği sınıf havuzundan) yeniden üretilir ve kapsam
/// <see cref="DailyQuestionSet.ScopeJson"/>'a yazılır. Oturum açıldıktan sonra kapsam değişmez.</item>
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
            Scope = await ResolveScopeAsync(set.ScopeJson, ct)
        };
    }

    public async Task<DailyStartResultDto?> StartTodayAsync(
        StudentProfileDto student,
        DailyStartRequestDto? scopeRequest = null,
        CancellationToken ct = default)
    {
        var today = _calendar.Today;
        var set = await GetOrCreateSetAsync(student, today, ct);
        if (set == null || (set.QuestionIds.Count == 0 && set.PracticeSessionId == null))
            return null;

        if (set.PracticeSessionId == null)
        {
            // Kapsam yalnız oturum açılmadan doğrulanır/uygulanır; açılmış sette istek kapsamı yok sayılır (gün içi
            // değişmezlik — sonradan silinen konu, devam eden seti 400'e düşürmez). Sınıf = setin sınıfı (havuzla aynı).
            var scope = await ValidateScopeAsync(scopeRequest, set.GradeId, ct);
            var selection = scope == null ? null : await SelectScopedAsync(set, scope, today, ct);
            await LinkNewSessionAsync(set, selection, ct);

            // Kazanan istek (bizimki ya da eşzamanlı rakip) seti yeniden üretmiş olabilir: canlı listeyi ve kapsamı tazele.
            set = await LoadSetAsync(student.Id, today, ct)
                ?? throw new InvalidOperationException("Daily question set disappeared after session link.");
        }

        var sessionId = set.PracticeSessionId
            ?? throw new InvalidOperationException("Daily question set session link lost.");

        var progress = await LoadProgressAsync(set, ct);
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

        return new DailyStartResultDto
        {
            SessionId = sessionId,
            Status = progress.Status,
            Total = set.QuestionIds.Count,
            Scope = await ResolveScopeAsync(set.ScopeJson, ct)
        };
    }

    // --- set üretimi ---

    /// <summary>Setin okumada gereken hali (izlenmeyen).</summary>
    /// <see cref="QuestionIds"/> = CANLI soru listesi (<see cref="DailySetQueries"/>); <see cref="TargetCount"/> set'te kaydedilen N (CR Ö6).
    private sealed record SetSnapshot(
        int Id, int StudentId, int GradeId, int TargetCount, int? PracticeSessionId, string? ScopeJson, IReadOnlyList<int> QuestionIds);

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
            return new SetSnapshot(set.Id, set.StudentId, set.GradeId, set.TargetCount, null, null, questionIds);
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
            .Select(d => new { d.Id, d.StudentId, d.GradeId, d.TargetCount, d.PracticeSessionId, d.ScopeJson })
            .FirstOrDefaultAsync(ct);

        if (row == null)
            return null;

        var live = await DailySetQueries.LoadLiveQuestionIdsAsync(_context, row.Id, ct);
        return new SetSnapshot(row.Id, row.StudentId, row.GradeId, row.TargetCount, row.PracticeSessionId, row.ScopeJson, live);
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
    /// #66 saf kapsamlı seçim (test edilebilir): önce <paramref name="scopedPoolIds"/>'ten <see cref="SelectQuestions"/> kuralıyla
    /// en çok <paramref name="count"/> soru; eksik kalırsa kalan, kapsamda olmayan <paramref name="generalPoolIds"/>
    /// sorularından aynı kuralla tamamlanır. Kapsam soruları önce gelir; tekrar yok.
    /// </summary>
    internal static (List<int> QuestionIds, int ScopedCount) SelectScopedQuestions(
        IReadOnlyList<int> scopedPoolIds,
        IReadOnlyList<int> generalPoolIds,
        IReadOnlySet<int> recentlyAnswered,
        int count,
        int seed)
    {
        var scoped = SelectQuestions(scopedPoolIds, recentlyAnswered, count, seed);
        var missing = Math.Max(0, count) - scoped.Count;
        if (missing <= 0)
            return (scoped, scoped.Count);

        // Eksik varsa kapsam havuzunun tamamı seçilmiştir; tamamlama yalnız kapsam dışı sorulardan yapılır.
        var taken = scopedPoolIds.ToHashSet();
        var rest = generalPoolIds.Where(id => !taken.Contains(id)).ToList();
        var filler = SelectQuestions(rest, recentlyAnswered, missing, seed);
        return (scoped.Concat(filler).ToList(), scoped.Count);
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
    /// oturumunu döner. Retry'lı execution strategy içinde; her deneme önceki denemenin entity'lerini bırakır.
    /// <para>
    /// #66: <paramref name="selection"/> doluysa bağlamayı kazanan istek aynı transaction'da setin kapsamını yazar ve
    /// sorularını kapsamlı seçimle değiştirir. Bağlama koşullu olduğu için set yalnız oturumsuzken (hiç cevap yokken) değişir.
    /// </para>
    /// </summary>
    private async Task<int> LinkNewSessionAsync(SetSnapshot set, ScopedSelection? selection, CancellationToken ct)
    {
        PracticeSession? attempt = null;
        var newItems = new List<DailyQuestionSetItem>();
        var strategy = _context.Database.CreateExecutionStrategy();

        var linked = await strategy.ExecuteAsync(async () =>
        {
            if (attempt != null)
                _context.Entry(attempt).State = EntityState.Detached;
            foreach (var item in newItems)
                _context.Entry(item).State = EntityState.Detached;
            newItems = new List<DailyQuestionSetItem>();

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

            var sessionId = attempt.Id;
            var scopeJson = selection?.ScopeJson;
            var unlinked = _context.DailyQuestionSets.Where(d => d.Id == set.Id && d.PracticeSessionId == null);
            var affected = selection == null
                ? await unlinked.ExecuteUpdateAsync(u => u.SetProperty(d => d.PracticeSessionId, (int?)sessionId), ct)
                : await unlinked.ExecuteUpdateAsync(u => u
                    .SetProperty(d => d.PracticeSessionId, (int?)sessionId)
                    .SetProperty(d => d.ScopeJson, scopeJson), ct);

            if (affected == 0)
            {
                await tx.RollbackAsync(ct);
                _context.Entry(attempt).State = EntityState.Detached;
                attempt = null;
                return (int?)null;
            }

            if (selection != null)
            {
                // Oturumsuz set henüz çözülmedi: rastgele sorular fiziksel olarak kapsamlı seçimle değiştirilir.
                await _context.DailyQuestionSetItems
                    .IgnoreQueryFilters()
                    .Where(i => i.DailyQuestionSetId == set.Id)
                    .ExecuteDeleteAsync(ct);

                newItems = selection.QuestionIds
                    .Select((id, i) => new DailyQuestionSetItem { DailyQuestionSetId = set.Id, QuestionId = id, Order = i })
                    .ToList();
                _context.DailyQuestionSetItems.AddRange(newItems);
                await _context.SaveChangesAsync(ct);
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

    // --- kapsam (#66) ---

    /// <summary><see cref="DailyQuestionSet.ScopeJson"/> şekli. Adlar saklanmaz (okumada çözülür).</summary>
    private sealed record StoredScope(int SubjectId, int? TopicId, int? SubTopicId, int ScopedCount, bool FilledFromGeneral);

    private sealed record ValidScope(int SubjectId, int? TopicId, int? SubTopicId);

    private sealed record ScopedSelection(IReadOnlyList<int> QuestionIds, string ScopeJson);

    private static readonly JsonSerializerOptions ScopeJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// İstek kapsamını doğrular: konu ders, alt konu konu gerektirir; ders setin sınıfında okutulmalı, konu o derse ve sınıfa,
    /// alt konu o konuya ait olmalı. Geçersizse <see cref="DailyScopeInvalidException"/>. Seçim yoksa null (rastgele, #99).
    /// </summary>
    private async Task<ValidScope?> ValidateScopeAsync(DailyStartRequestDto? request, int gradeId, CancellationToken ct)
    {
        if (request == null || (request.SubjectId == null && request.TopicId == null && request.SubTopicId == null))
            return null;

        if (request.SubjectId is not > 0
            || request.TopicId is <= 0
            || request.SubTopicId is <= 0
            || (request.SubTopicId != null && request.TopicId == null))
            throw new DailyScopeInvalidException();

        var subjectId = request.SubjectId.Value;
        if (!await _context.GradeSubjects.AsNoTracking().AnyAsync(gs => gs.GradeId == gradeId && gs.SubjectId == subjectId, ct))
            throw new DailyScopeInvalidException();

        if (request.TopicId is { } topicId)
        {
            var topicOk = await _context.Topics.AsNoTracking()
                .AnyAsync(t => t.Id == topicId && t.SubjectId == subjectId && t.GradeId == gradeId, ct);
            if (!topicOk)
                throw new DailyScopeInvalidException();

            if (request.SubTopicId is { } subTopicId
                && !await _context.SubTopics.AsNoTracking().AnyAsync(st => st.Id == subTopicId && st.TopicId == topicId, ct))
                throw new DailyScopeInvalidException();
        }

        return new ValidScope(subjectId, request.TopicId, request.SubTopicId);
    }

    /// <summary>
    /// Kapsamlı seçim: kapsam havuzu + (eksik için) setin sınıf havuzu; tohum ve son-cevap kuralı #99 ile aynı. Sınıf havuzu
    /// gün içinde boşaldıysa null: set olduğu gibi (rastgele) kalır.
    /// </summary>
    private async Task<ScopedSelection?> SelectScopedAsync(SetSnapshot set, ValidScope scope, DateOnly day, CancellationToken ct)
    {
        var scopedPool = await PracticeQuestionPool.Build(
                _context,
                set.GradeId,
                new[] { scope.SubjectId },
                scope.TopicId is { } topicId ? new[] { topicId } : Array.Empty<int>(),
                scope.SubTopicId is { } subTopicId ? new[] { subTopicId } : null)
            .Select(q => q.Id)
            .OrderBy(id => id)
            .ToListAsync(ct);

        var generalPool = await PracticeQuestionPool.Build(_context, set.GradeId, Array.Empty<int>(), Array.Empty<int>())
            .Select(q => q.Id)
            .OrderBy(id => id)
            .ToListAsync(ct);

        var recent = await LoadRecentlyAnsweredAsync(set.StudentId, day, ct);
        var (ids, scopedCount) = SelectScopedQuestions(scopedPool, generalPool, recent, set.TargetCount, SeedFor(set.StudentId, day));
        if (ids.Count == 0)
            return null;

        var stored = new StoredScope(scope.SubjectId, scope.TopicId, scope.SubTopicId, scopedCount, scopedCount < ids.Count);
        return new ScopedSelection(ids, JsonSerializer.Serialize(stored, ScopeJsonOptions));
    }

    private async Task<DailySetScopeDto?> ResolveScopeAsync(string? scopeJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scopeJson))
            return null;

        StoredScope? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredScope>(scopeJson, ScopeJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        if (stored == null)
            return null;

        var subjectName = await _context.Subjects.AsNoTracking()
            .Where(s => s.Id == stored.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var topicName = stored.TopicId is { } topicId
            ? await _context.Topics.AsNoTracking().Where(t => t.Id == topicId).Select(t => t.Name).FirstOrDefaultAsync(ct)
            : null;
        var subTopicName = stored.SubTopicId is { } subTopicId
            ? await _context.SubTopics.AsNoTracking().Where(st => st.Id == subTopicId).Select(st => st.Name).FirstOrDefaultAsync(ct)
            : null;

        return new DailySetScopeDto
        {
            SubjectId = stored.SubjectId,
            SubjectName = subjectName,
            TopicId = stored.TopicId,
            TopicName = topicName,
            SubTopicId = stored.SubTopicId,
            SubTopicName = subTopicName,
            ScopedCount = stored.ScopedCount,
            FilledFromGeneral = stored.FilledFromGeneral
        };
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

/// <summary>#66: günlük set kapsam isteği geçersiz (tutarsız ders/konu/alt konu ya da başka sınıfın konusu) — 400.</summary>
public sealed class DailyScopeInvalidException : Exception
{
    public DailyScopeInvalidException() : base("Invalid daily question scope.") { }
}
