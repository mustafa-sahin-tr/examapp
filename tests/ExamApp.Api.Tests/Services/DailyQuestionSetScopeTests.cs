using System.Data.Common;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #66 — "Günün soruları" kapsamı: başlatırken ders/konu/alt konu seçimi, kapsam filtresi, kapsam yetmezse sınıf
/// havuzundan tamamlama (+ bilgi bayrağı), tekrarsızlık, gün içi değişmezlik, ertesi gün yeni seçim, geçersiz kapsam reddi.
/// </summary>
public class DailyQuestionSetScopeTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    // 2026-10-05 12:00 İstanbul (UTC+3).
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

    private readonly DailyQuestionsOptions _options = new() { QuestionCount = 5, RecentAnswerExclusionDays = 14 };

    private static readonly DateOnly Today = new(2026, 10, 5);

    public void Dispose() => _db.Dispose();

    private DailyQuestionSetService NewDaily(AppDbContext ctx) =>
        new(ctx, new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, _clock), Options.Create(_options));

    private PracticeSessionService NewPractice(AppDbContext ctx) =>
        new(ctx, localizer: null, calendar: new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, _clock));

    /// <summary>
    /// 7. sınıf: Matematik (Kesirler konusu → "Sadeleştirme" alt konusu) ve Türkçe; ayrıca 8. sınıfın bir Matematik konusu.
    /// </summary>
    private sealed record World(
        int GradeId, int MathId, int TurkishId, int FractionsTopicId, int SimplifySubTopicId, int OtherGradeTopicId, int WorksheetId);

    private async Task<World> SeedWorldAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var otherGrade = new Grade { Name = "8" };
        var math = new Subject { Name = "Matematik" };
        var turkish = new Subject { Name = "Türkçe" };
        ctx.AddRange(grade, otherGrade, math, turkish);
        await ctx.SaveChangesAsync();

        var fractions = new Topic { Name = "Kesirler", SubjectId = math.Id, GradeId = grade.Id };
        var otherGradeTopic = new Topic { Name = "Üslü sayılar", SubjectId = math.Id, GradeId = otherGrade.Id };
        ctx.Topics.AddRange(fractions, otherGradeTopic);
        await ctx.SaveChangesAsync();

        var simplify = new SubTopic { Name = "Sadeleştirme", TopicId = fractions.Id };
        ctx.SubTopics.Add(simplify);

        // 7. sınıfta Matematik + Türkçe okutulur; "Fen" yalnız 8. sınıfta.
        var science = new Subject { Name = "Fen" };
        ctx.Subjects.Add(science);
        await ctx.SaveChangesAsync();
        ctx.GradeSubjects.AddRange(
            new GradeSubject { GradeId = grade.Id, SubjectId = math.Id },
            new GradeSubject { GradeId = grade.Id, SubjectId = turkish.Id },
            new GradeSubject { GradeId = otherGrade.Id, SubjectId = science.Id });

        var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = math.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();

        return new World(grade.Id, math.Id, turkish.Id, fractions.Id, simplify.Id, otherGradeTopic.Id, ws.Id);
    }

    private async Task<StudentProfileDto> AddStudentAsync(World w, int userId)
    {
        await using var ctx = _db.NewContext();
        var s = new Student { UserId = userId, StudentNumber = $"S{userId}", SchoolName = "S", GradeId = w.GradeId };
        ctx.Students.Add(s);
        await ctx.SaveChangesAsync();
        return new StudentProfileDto { Id = s.Id, GradeId = s.GradeId };
    }

    private int _order;

    private async Task<List<int>> AddQuestionsAsync(World w, int count, int subjectId, int? topicId = null, int? subTopicId = null)
    {
        var ids = new List<int>();
        await using var ctx = _db.NewContext();
        for (var i = 0; i < count; i++)
        {
            var q = new Question { Text = $"Q{_order}", SubjectId = subjectId, TopicId = topicId, Point = 10, DifficultyLevel = 2 };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();

            var correct = new Answer { QuestionId = q.Id, Text = "ok", Tag = "A", Order = 0 };
            ctx.Answers.Add(correct);
            await ctx.SaveChangesAsync();

            q.CorrectAnswerId = correct.Id;
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = w.WorksheetId, QuestionId = q.Id, Order = ++_order });
            if (subTopicId is { } st)
                ctx.Add(new QuestionSubTopic { QuestionId = q.Id, SubTopicId = st });
            await ctx.SaveChangesAsync();
            ids.Add(q.Id);
        }
        return ids;
    }

    private async Task<DailySetDto> GetTodayAsync(StudentProfileDto s)
    {
        await using var ctx = _db.NewContext();
        return await NewDaily(ctx).GetTodayAsync(s);
    }

    private async Task<DailyStartResultDto?> StartAsync(StudentProfileDto s, DailyStartRequestDto? scope)
    {
        await using var ctx = _db.NewContext();
        return await NewDaily(ctx).StartTodayAsync(s, scope);
    }

    private async Task<List<int>> SetQuestionIdsAsync(int studentId, DateOnly day)
    {
        await using var ctx = _db.NewContext();
        return await ctx.DailyQuestionSetItems.AsNoTracking()
            .Where(i => i.DailyQuestionSet.StudentId == studentId && i.DailyQuestionSet.Day == day)
            .OrderBy(i => i.Order)
            .Select(i => i.QuestionId)
            .ToListAsync();
    }

    // ---- saf seçim kuralı ----

    [Fact]
    public void SelectScopedQuestions_EnoughInScope_AllFromScope()
    {
        var scoped = Enumerable.Range(1, 10).ToList();
        var general = Enumerable.Range(1, 40).ToList();

        var (ids, scopedCount) = DailyQuestionSetService.SelectScopedQuestions(scoped, general, new HashSet<int>(), 5, 42);

        ids.Count.ShouldBe(5);
        ids.ShouldAllBe(id => scoped.Contains(id));
        ids.Distinct().Count().ShouldBe(5);
        scopedCount.ShouldBe(5);
    }

    [Fact]
    public void SelectScopedQuestions_ShortScope_ScopedFirst_ThenFilledFromGeneral_NoDuplicates()
    {
        var scoped = new List<int> { 3, 7 };
        var general = Enumerable.Range(1, 20).ToList(); // kapsam ⊂ genel

        var (ids, scopedCount) = DailyQuestionSetService.SelectScopedQuestions(scoped, general, new HashSet<int>(), 5, 42);

        scopedCount.ShouldBe(2);
        ids.Count.ShouldBe(5);
        ids.Distinct().Count().ShouldBe(5);
        ids.Take(2).OrderBy(x => x).ShouldBe(scoped);
        ids.Skip(2).ShouldAllBe(id => !scoped.Contains(id));
    }

    [Fact]
    public void SelectScopedQuestions_IsDeterministic_AndRespectsRecentRule()
    {
        var scoped = new List<int> { 1, 2, 3 };
        var general = Enumerable.Range(1, 30).ToList();
        var recent = new HashSet<int> { 1, 2, 3, 4, 5 };

        var a = DailyQuestionSetService.SelectScopedQuestions(scoped, general, recent, 5, 7);
        var b = DailyQuestionSetService.SelectScopedQuestions(scoped, general.AsEnumerable().Reverse().ToList(), recent, 5, 7);

        a.QuestionIds.ShouldBe(b.QuestionIds);
        a.ScopedCount.ShouldBe(3); // son cevaplanmış olsalar da kapsam soruları önce (kapsamda yalnız onlar var)
        a.QuestionIds.Skip(3).ShouldAllBe(id => !recent.Contains(id)); // tamamlama taze sorulardan
    }

    [Fact]
    public void SelectScopedQuestions_EmptyScope_AllFromGeneral()
    {
        var (ids, scopedCount) = DailyQuestionSetService.SelectScopedQuestions(
            Array.Empty<int>(), Enumerable.Range(1, 10).ToList(), new HashSet<int>(), 5, 1);

        scopedCount.ShouldBe(0);
        ids.Count.ShouldBe(5);
    }

    // ---- kapsam filtresi ----

    [Fact]
    public async Task Start_WithSubject_SetComesFromThatSubject_ScopeSaved()
    {
        var w = await SeedWorldAsync();
        var math = await AddQuestionsAsync(w, 8, w.MathId);
        await AddQuestionsAsync(w, 8, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        var result = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId });

        result.ShouldNotBeNull();
        result.Total.ShouldBe(5);
        result.Scope.ShouldNotBeNull();
        result.Scope.SubjectId.ShouldBe(w.MathId);
        result.Scope.SubjectName.ShouldBe("Matematik");
        result.Scope.TopicId.ShouldBeNull();
        result.Scope.ScopedCount.ShouldBe(5);
        result.Scope.FilledFromGeneral.ShouldBeFalse();

        var ids = await SetQuestionIdsAsync(s.Id, Today);
        ids.Count.ShouldBe(5);
        ids.ShouldAllBe(id => math.Contains(id));
        ids.Distinct().Count().ShouldBe(5);

        var dto = await GetTodayAsync(s);
        dto.Scope.ShouldNotBeNull();
        dto.Scope.SubjectId.ShouldBe(w.MathId);
        dto.SessionId.ShouldBe(result.SessionId);
    }

    [Fact]
    public async Task Start_AfterRandomGet_ReplacesRandomSetWithScopedOne()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 10, w.MathId);
        var turkish = await AddQuestionsAsync(w, 6, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        (await GetTodayAsync(s)).Scope.ShouldBeNull(); // dashboard kartı: rastgele set üretildi

        await StartAsync(s, new DailyStartRequestDto { SubjectId = w.TurkishId });

        (await SetQuestionIdsAsync(s.Id, Today)).ShouldAllBe(id => turkish.Contains(id));
    }

    [Fact]
    public async Task Start_WithTopic_SetComesFromThatTopic()
    {
        var w = await SeedWorldAsync();
        var fractions = await AddQuestionsAsync(w, 6, w.MathId, w.FractionsTopicId);
        await AddQuestionsAsync(w, 10, w.MathId); // konusuz matematik
        var s = await AddStudentAsync(w, 101);

        var result = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId, TopicId = w.FractionsTopicId });

        result!.Scope!.TopicName.ShouldBe("Kesirler");
        result.Scope.FilledFromGeneral.ShouldBeFalse();
        (await SetQuestionIdsAsync(s.Id, Today)).ShouldAllBe(id => fractions.Contains(id));
    }

    [Fact]
    public async Task Start_WithSubTopic_SetComesFromThatSubTopic()
    {
        var w = await SeedWorldAsync();
        var simplify = await AddQuestionsAsync(w, 5, w.MathId, w.FractionsTopicId, w.SimplifySubTopicId);
        await AddQuestionsAsync(w, 6, w.MathId, w.FractionsTopicId); // alt konusuz kesir soruları
        var s = await AddStudentAsync(w, 101);

        var result = await StartAsync(s, new DailyStartRequestDto
        {
            SubjectId = w.MathId, TopicId = w.FractionsTopicId, SubTopicId = w.SimplifySubTopicId
        });

        result!.Scope!.SubTopicName.ShouldBe("Sadeleştirme");
        (await SetQuestionIdsAsync(s.Id, Today)).OrderBy(x => x).ShouldBe(simplify.OrderBy(x => x));
    }

    // ---- fallback tamamlama ----

    [Fact]
    public async Task Start_ScopeHasFewerThanN_FilledFromGradePool_WithInfoFlag()
    {
        var w = await SeedWorldAsync();
        var fractions = await AddQuestionsAsync(w, 2, w.MathId, w.FractionsTopicId);
        var others = await AddQuestionsAsync(w, 10, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        var result = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId, TopicId = w.FractionsTopicId });

        result!.Total.ShouldBe(5);
        result.Scope!.ScopedCount.ShouldBe(2);
        result.Scope.FilledFromGeneral.ShouldBeTrue();

        var ids = await SetQuestionIdsAsync(s.Id, Today);
        ids.Count.ShouldBe(5);
        ids.Distinct().Count().ShouldBe(5); // tekrarsız
        ids.Take(2).OrderBy(x => x).ShouldBe(fractions.OrderBy(x => x)); // kapsam soruları önce
        ids.Skip(2).ShouldAllBe(id => others.Contains(id));

        (await GetTodayAsync(s)).Scope!.FilledFromGeneral.ShouldBeTrue();
    }

    [Fact]
    public async Task Start_ScopeWithNoQuestions_WholeSetFromGradePool()
    {
        var w = await SeedWorldAsync();
        var turkish = await AddQuestionsAsync(w, 7, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        var result = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId });

        result!.Total.ShouldBe(5);
        result.Scope!.ScopedCount.ShouldBe(0);
        result.Scope.FilledFromGeneral.ShouldBeTrue();
        (await SetQuestionIdsAsync(s.Id, Today)).ShouldAllBe(id => turkish.Contains(id));
    }

    [Fact]
    public async Task Start_WholeGradePoolInScope_ButBelowN_NotMarkedAsFilled()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 3, w.MathId);
        var s = await AddStudentAsync(w, 101);

        var result = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId });

        result!.Total.ShouldBe(3); // havuz < N: #99'daki gibi eldeki kadar
        result.Scope!.ScopedCount.ShouldBe(3);
        result.Scope.FilledFromGeneral.ShouldBeFalse();
    }

    // ---- seçim yok = #99 ----

    [Fact]
    public async Task Start_WithoutScope_KeepsRandomSet_NoScope()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 6, w.MathId);
        await AddQuestionsAsync(w, 6, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        await GetTodayAsync(s);
        var before = await SetQuestionIdsAsync(s.Id, Today);

        var result = await StartAsync(s, new DailyStartRequestDto());

        result!.Scope.ShouldBeNull();
        result.Total.ShouldBe(5);
        (await SetQuestionIdsAsync(s.Id, Today)).ShouldBe(before);
    }

    // ---- gün içi değişmezlik / ertesi gün ----

    [Fact]
    public async Task Start_SecondCallWithDifferentScope_SameDay_IsIgnored()
    {
        var w = await SeedWorldAsync();
        var math = await AddQuestionsAsync(w, 6, w.MathId);
        await AddQuestionsAsync(w, 6, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        var first = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId });
        var ids = await SetQuestionIdsAsync(s.Id, Today);

        _clock.Now = _clock.Now.AddHours(8); // aynı İstanbul günü
        var second = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.TurkishId });

        second!.SessionId.ShouldBe(first!.SessionId);
        second.Scope!.SubjectId.ShouldBe(w.MathId);
        (await SetQuestionIdsAsync(s.Id, Today)).ShouldBe(ids);
        ids.ShouldAllBe(id => math.Contains(id));
    }

    [Fact]
    public async Task Start_RandomFirst_ThenScopeSameDay_IsIgnored()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 6, w.MathId);
        await AddQuestionsAsync(w, 6, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        await StartAsync(s, null);
        var ids = await SetQuestionIdsAsync(s.Id, Today);

        var again = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.TurkishId });

        again!.Scope.ShouldBeNull();
        (await SetQuestionIdsAsync(s.Id, Today)).ShouldBe(ids);
    }

    [Fact]
    public async Task Start_ScopedSet_AfterAnswering_DoesNotChange()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 6, w.MathId);
        await AddQuestionsAsync(w, 6, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        var start = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId });
        var ids = await SetQuestionIdsAsync(s.Id, Today);

        await using (var ctx = _db.NewContext())
        {
            var practice = NewPractice(ctx);
            var next = await practice.NextQuestionAsync(start!.SessionId, s.Id);
            next.ShouldNotBeNull();
            next.Question!.Id.ShouldBe(ids[0]); // pratik akışı kapsamlı seti sırasıyla verir
            await practice.SubmitAnswerAsync(start.SessionId, s.Id,
                new PracticeAnswerSubmitDto { QuestionId = ids[0], Skipped = true, TimeTaken = 3 }, "kc-101");
        }

        var again = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.TurkishId });
        again!.Status.ShouldBe(DailySetStatus.InProgress);
        (await SetQuestionIdsAsync(s.Id, Today)).ShouldBe(ids);
    }

    [Fact]
    public async Task NextDay_NewScopeChoice_NoPersistentPreference()
    {
        var w = await SeedWorldAsync();
        var math = await AddQuestionsAsync(w, 6, w.MathId);
        var turkish = await AddQuestionsAsync(w, 6, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId });

        _clock.Now = _clock.Now.AddDays(1);
        var tomorrow = Today.AddDays(1);

        (await GetTodayAsync(s)).Scope.ShouldBeNull(); // kalıcı tercih yok: yeni gün rastgele başlar

        var next = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.TurkishId });
        next!.Scope!.SubjectId.ShouldBe(w.TurkishId);
        (await SetQuestionIdsAsync(s.Id, tomorrow)).ShouldAllBe(id => turkish.Contains(id));
        (await SetQuestionIdsAsync(s.Id, Today)).ShouldAllBe(id => math.Contains(id));
    }

    // ---- eşzamanlılık ----

    [Fact]
    public async Task ConcurrentStart_RivalLinksFirst_OurScopeIsNotApplied()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 6, w.MathId);
        await AddQuestionsAsync(w, 6, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        await GetTodayAsync(s);
        var randomIds = await SetQuestionIdsAsync(s.Id, Today);

        int rivalSessionId;
        await using (var ctx = _db.NewContext())
        {
            var rival = new PracticeSession { StudentId = s.Id, GradeId = w.GradeId, StartTime = DateTime.UtcNow, Status = PracticeSessionStatus.Active };
            ctx.PracticeSessions.Add(rival);
            await ctx.SaveChangesAsync();
            rivalSessionId = rival.Id;
        }

        var interceptor = new RivalLinksSessionFirst(rivalSessionId);
        DailyStartResultDto? result;
        await using (var ctx = _db.NewContext(interceptor))
            result = await NewDaily(ctx).StartTodayAsync(s, new DailyStartRequestDto { SubjectId = w.TurkishId });

        interceptor.Fired.ShouldBeTrue();
        result!.SessionId.ShouldBe(rivalSessionId);
        result.Scope.ShouldBeNull();
        (await SetQuestionIdsAsync(s.Id, Today)).ShouldBe(randomIds);
    }

    [Fact]
    public async Task Start_WithScope_RetryingExecutionStrategy_DoesNotThrow()
    {
        var w = await SeedWorldAsync();
        var math = await AddQuestionsAsync(w, 6, w.MathId);
        await AddQuestionsAsync(w, 6, w.TurkishId);
        var s = await AddStudentAsync(w, 101);

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            var result = await NewDaily(ctx).StartTodayAsync(s, new DailyStartRequestDto { SubjectId = w.MathId });
            result.ShouldNotBeNull();
        }

        (await SetQuestionIdsAsync(s.Id, Today)).ShouldAllBe(id => math.Contains(id));
    }

    // ---- geçersiz kapsam ----

    public static TheoryData<string> InvalidScopes => new()
    {
        "topicWithoutSubject", "subTopicWithoutTopic", "unknownSubject", "subjectOfOtherGrade", "nonPositiveIds",
        "topicOfOtherSubject", "topicOfOtherGrade", "subTopicOfOtherTopic"
    };

    [Theory]
    [MemberData(nameof(InvalidScopes))]
    public async Task Start_InvalidScope_Throws_AndNothingStarts(string kind)
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 6, w.MathId, w.FractionsTopicId);
        var s = await AddStudentAsync(w, 101);

        int otherTopicId;
        await using (var ctx = _db.NewContext())
        {
            var other = new Topic { Name = "Fiiller", SubjectId = w.TurkishId, GradeId = w.GradeId };
            ctx.Topics.Add(other);
            await ctx.SaveChangesAsync();
            otherTopicId = other.Id;
        }

        var request = kind switch
        {
            "topicWithoutSubject" => new DailyStartRequestDto { TopicId = w.FractionsTopicId },
            "subTopicWithoutTopic" => new DailyStartRequestDto { SubjectId = w.MathId, SubTopicId = w.SimplifySubTopicId },
            "unknownSubject" => new DailyStartRequestDto { SubjectId = 999_999 },
            "subjectOfOtherGrade" => new DailyStartRequestDto { SubjectId = await ScienceIdAsync() },
            "nonPositiveIds" => new DailyStartRequestDto { SubjectId = w.MathId, TopicId = 0 },
            "topicOfOtherSubject" => new DailyStartRequestDto { SubjectId = w.MathId, TopicId = otherTopicId },
            "topicOfOtherGrade" => new DailyStartRequestDto { SubjectId = w.MathId, TopicId = w.OtherGradeTopicId },
            "subTopicOfOtherTopic" => new DailyStartRequestDto { SubjectId = w.TurkishId, TopicId = otherTopicId, SubTopicId = w.SimplifySubTopicId },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        await Should.ThrowAsync<DailyScopeInvalidException>(() => StartAsync(s, request));

        await using var check = _db.NewContext();
        (await check.PracticeSessions.CountAsync(p => p.StudentId == s.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Start_StartedSet_ScopeNoLongerValid_IsIgnored_NotRejected()
    {
        var w = await SeedWorldAsync();
        var fractions = await AddQuestionsAsync(w, 6, w.MathId, w.FractionsTopicId);
        var s = await AddStudentAsync(w, 101);

        var first = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId, TopicId = w.FractionsTopicId });

        // Konu gün içinde başka sınıfa taşındı; öğrenci aynı URL ile geri dönüyor.
        await using (var ctx = _db.NewContext())
        {
            var topic = await ctx.Topics.AsTracking().SingleAsync(t => t.Id == w.FractionsTopicId);
            topic.GradeId = (await ctx.Grades.SingleAsync(g => g.Name == "8")).Id;
            await ctx.SaveChangesAsync();
        }

        var again = await StartAsync(s, new DailyStartRequestDto { SubjectId = w.MathId, TopicId = w.FractionsTopicId });

        again!.SessionId.ShouldBe(first!.SessionId);
        (await SetQuestionIdsAsync(s.Id, Today)).ShouldAllBe(id => fractions.Contains(id));
    }

    private async Task<int> ScienceIdAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.Subjects.Where(x => x.Name == "Fen").Select(x => x.Id).SingleAsync();
    }

    /// <summary>
    /// Bağlama transaction'ımız başlamadan hemen önce rakip isteğin seti kendi oturumuna bağlayıp commit etmesi (bizim okuduğumuz
    /// snapshot'ta set hâlâ oturumsuz; koşullu UPDATE 0 satır döner).
    /// </summary>
    private sealed class RivalLinksSessionFirst(int rivalSessionId) : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"UPDATE \"DailyQuestionSets\" SET \"PracticeSessionId\" = {rivalSessionId}";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }
}
