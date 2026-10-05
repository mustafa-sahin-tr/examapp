using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #99 — "Günün soruları": tembel üretim, öğrenci+gün deterministik tohum, son X gün dışlaması, havuz &lt; N / boş havuz,
/// Europe/Istanbul gün sınırı, eşzamanlı ilk istekte tek set, idempotent start, Completed + sessionId, cevapların
/// AnswerSubmittedEvent outbox hattına gitmesi ve başka öğrencinin oturumuna erişimin reddi.
/// </summary>
public class DailyQuestionSetServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    // 2026-10-05 12:00 İstanbul (UTC+3).
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

    private DailyQuestionsOptions _options = new() { QuestionCount = 5, RecentAnswerExclusionDays = 14 };

    public void Dispose() => _db.Dispose();

    private DailyQuestionSetService NewDaily(AppDbContext ctx) =>
        new(ctx, new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, _clock), Options.Create(_options));

    private PracticeSessionService NewPractice(AppDbContext ctx) =>
        new(ctx, localizer: null, calendar: new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, _clock));

    private sealed record World(int GradeId, int SubjectId, int WorksheetId);

    private async Task<World> SeedWorldAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();

        var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = subject.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        return new World(grade.Id, subject.Id, ws.Id);
    }

    private async Task<StudentProfileDto> AddStudentAsync(World w, int userId, bool withGrade = true)
    {
        await using var ctx = _db.NewContext();
        var s = new Student { UserId = userId, StudentNumber = $"S{userId}", SchoolName = "S", GradeId = withGrade ? w.GradeId : null };
        ctx.Students.Add(s);
        await ctx.SaveChangesAsync();
        return new StudentProfileDto { Id = s.Id, GradeId = s.GradeId };
    }

    private sealed record SeededQuestion(int QuestionId, int CorrectAnswerId, int WrongAnswerId);

    private async Task<List<SeededQuestion>> AddQuestionsAsync(World w, int count)
    {
        var result = new List<SeededQuestion>();
        await using var ctx = _db.NewContext();
        for (var i = 0; i < count; i++)
        {
            var q = new Question { Text = $"Q{i}", SubjectId = w.SubjectId, Point = 10, DifficultyLevel = 2 };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();

            var correct = new Answer { QuestionId = q.Id, Text = "ok", Tag = "A", Order = 0 };
            var wrong = new Answer { QuestionId = q.Id, Text = "no", Tag = "B", Order = 1 };
            ctx.Answers.AddRange(correct, wrong);
            await ctx.SaveChangesAsync();

            q.CorrectAnswerId = correct.Id;
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = w.WorksheetId, QuestionId = q.Id, Order = i + 1 });
            await ctx.SaveChangesAsync();
            result.Add(new SeededQuestion(q.Id, correct.Id, wrong.Id));
        }
        return result;
    }

    private async Task<DailySetDto> GetTodayAsync(StudentProfileDto s)
    {
        await using var ctx = _db.NewContext();
        return await NewDaily(ctx).GetTodayAsync(s);
    }

    private async Task<DailyStartResultDto?> StartAsync(StudentProfileDto s)
    {
        await using var ctx = _db.NewContext();
        return await NewDaily(ctx).StartTodayAsync(s);
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

    /// <summary>Öğrencinin (serbest) pratikte bir soruyu cevaplamış olması.</summary>
    private async Task AddPracticeAnswerAsync(StudentProfileDto s, World w, int questionId, DateTime answeredAtUtc)
    {
        await using var ctx = _db.NewContext();
        var session = new PracticeSession { StudentId = s.Id, GradeId = w.GradeId, StartTime = answeredAtUtc, Status = PracticeSessionStatus.Ended };
        session.Questions.Add(new PracticeSessionQuestion { QuestionId = questionId, ShownAt = answeredAtUtc, AnsweredAt = answeredAtUtc, IsCorrect = true });
        ctx.PracticeSessions.Add(session);
        await ctx.SaveChangesAsync();
    }

    private static readonly DateOnly Today = new(2026, 10, 5);

    // ---- saf seçim kuralı ----

    [Fact]
    public void SeedFor_IsDeterministicPerStudentAndDay()
    {
        DailyQuestionSetService.SeedFor(7, Today).ShouldBe(DailyQuestionSetService.SeedFor(7, Today));
        DailyQuestionSetService.SeedFor(7, Today).ShouldNotBe(DailyQuestionSetService.SeedFor(8, Today));
        DailyQuestionSetService.SeedFor(7, Today).ShouldNotBe(DailyQuestionSetService.SeedFor(7, Today.AddDays(1)));
    }

    [Fact]
    public void SelectQuestions_SameSeed_SameOrder_NoDuplicates()
    {
        var pool = Enumerable.Range(1, 40).Concat(new[] { 3, 3 }).ToList();
        var a = DailyQuestionSetService.SelectQuestions(pool, new HashSet<int>(), 5, 1234);
        var b = DailyQuestionSetService.SelectQuestions(pool.AsEnumerable().Reverse().ToList(), new HashSet<int>(), 5, 1234);

        a.ShouldBe(b); // girdi sırasından bağımsız
        a.Count.ShouldBe(5);
        a.Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public void SelectQuestions_RecentlyAnsweredGoLast_AndFillWhenPoolIsShort()
    {
        var pool = new List<int> { 1, 2, 3, 4 };
        var recent = new HashSet<int> { 1, 2, 3 };

        var picked = DailyQuestionSetService.SelectQuestions(pool, recent, 3, 99);

        picked.Count.ShouldBe(3);
        picked[0].ShouldBe(4); // tek taze soru önce
        picked.Skip(1).ShouldAllBe(id => recent.Contains(id));
    }

    // ---- üretim ----

    [Fact]
    public async Task GetToday_FirstCall_GeneratesNQuestionSet_NotStarted()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 12);
        var s = await AddStudentAsync(w, 101);

        var dto = await GetTodayAsync(s);

        dto.Date.ShouldBe(Today);
        dto.Status.ShouldBe(DailySetStatus.NotStarted);
        dto.Total.ShouldBe(5);
        dto.TargetCount.ShouldBe(5);
        dto.Answered.ShouldBe(0);
        dto.SessionId.ShouldBeNull();
        dto.Scope.ShouldBeNull();

        var ids = await SetQuestionIdsAsync(s.Id, Today);
        ids.Count.ShouldBe(5);
        ids.Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public async Task GetToday_SameDay_ReturnsSameSet_NoSecondRow()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 12);
        var s = await AddStudentAsync(w, 101);

        await GetTodayAsync(s);
        var first = await SetQuestionIdsAsync(s.Id, Today);

        _clock.Now = _clock.Now.AddHours(8); // aynı İstanbul günü (20:00)
        await GetTodayAsync(s);
        var second = await SetQuestionIdsAsync(s.Id, Today);

        second.ShouldBe(first);
        await using var ctx = _db.NewContext();
        (await ctx.DailyQuestionSets.CountAsync(d => d.StudentId == s.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task GetToday_DifferentStudentsSameGrade_GetDifferentSets()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 30);
        var a = await AddStudentAsync(w, 101);
        var b = await AddStudentAsync(w, 102);

        await GetTodayAsync(a);
        await GetTodayAsync(b);

        (await SetQuestionIdsAsync(a.Id, Today)).ShouldNotBe(await SetQuestionIdsAsync(b.Id, Today));
    }

    [Fact]
    public async Task GetToday_PoolSmallerThanN_SetHasAvailableQuestions()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 3);
        var s = await AddStudentAsync(w, 101);

        var dto = await GetTodayAsync(s);

        dto.Status.ShouldBe(DailySetStatus.NotStarted);
        dto.Total.ShouldBe(3);
        dto.TargetCount.ShouldBe(5);
    }

    [Fact]
    public async Task GetToday_EmptyPool_ReturnsEmpty_AndPersistsNothing()
    {
        var w = await SeedWorldAsync();
        var s = await AddStudentAsync(w, 101);

        var dto = await GetTodayAsync(s);

        dto.Status.ShouldBe(DailySetStatus.Empty);
        dto.Total.ShouldBe(0);
        dto.TargetCount.ShouldBe(5);
        dto.SessionId.ShouldBeNull();
        await using var ctx = _db.NewContext();
        (await ctx.DailyQuestionSets.CountAsync()).ShouldBe(0);

        (await StartAsync(s)).ShouldBeNull(); // controller: 409
    }

    [Fact]
    public async Task GetToday_StudentWithoutGrade_ReturnsEmpty()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 5);
        var s = await AddStudentAsync(w, 101, withGrade: false);

        (await GetTodayAsync(s)).Status.ShouldBe(DailySetStatus.Empty);
    }

    [Fact]
    public async Task GetToday_ExcludesQuestionsAnsweredInLastXDays_ButNotOlderOnes()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 6);
        var s = await AddStudentAsync(w, 101);
        _options = new DailyQuestionsOptions { QuestionCount = 3, RecentAnswerExclusionDays = 14 };

        var now = _clock.GetUtcNow().UtcDateTime;
        await AddPracticeAnswerAsync(s, w, qs[0].QuestionId, now.AddDays(-1));
        await AddPracticeAnswerAsync(s, w, qs[1].QuestionId, now.AddDays(-13));
        await AddPracticeAnswerAsync(s, w, qs[2].QuestionId, now.AddHours(-1));
        // 20 gün önce cevaplanan soru pencerenin dışında → aday kalır.
        await AddPracticeAnswerAsync(s, w, qs[3].QuestionId, now.AddDays(-20));

        await GetTodayAsync(s);

        var ids = await SetQuestionIdsAsync(s.Id, Today);
        ids.OrderBy(x => x).ShouldBe(new[] { qs[3].QuestionId, qs[4].QuestionId, qs[5].QuestionId }.OrderBy(x => x));
    }

    [Fact]
    public async Task GetToday_PoolMostlyRecentlyAnswered_FallsBackToRecentQuestions()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 4);
        var s = await AddStudentAsync(w, 101);
        _options = new DailyQuestionsOptions { QuestionCount = 3, RecentAnswerExclusionDays = 14 };

        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var q in qs.Take(3))
            await AddPracticeAnswerAsync(s, w, q.QuestionId, now.AddDays(-1));

        var dto = await GetTodayAsync(s);

        dto.Total.ShouldBe(3);
        var ids = await SetQuestionIdsAsync(s.Id, Today);
        ids[0].ShouldBe(qs[3].QuestionId); // taze soru önce
    }

    [Fact]
    public async Task DayBoundary_IsIstanbulMidnight_NotUtc()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 20);
        var s = await AddStudentAsync(w, 101);

        // 2026-10-05 23:59:59 İstanbul = 20:59:59 UTC → hâlâ 5 Ekim.
        _clock.Now = new DateTimeOffset(2026, 10, 5, 20, 59, 59, TimeSpan.Zero);
        (await GetTodayAsync(s)).Date.ShouldBe(Today);

        // 2026-10-06 00:00 İstanbul = 21:00 UTC (UTC'de hâlâ 5 Ekim) → yeni gün, yeni set.
        _clock.Now = new DateTimeOffset(2026, 10, 5, 21, 0, 0, TimeSpan.Zero);
        var next = await GetTodayAsync(s);
        next.Date.ShouldBe(Today.AddDays(1));
        next.Status.ShouldBe(DailySetStatus.NotStarted);

        await using var ctx = _db.NewContext();
        (await ctx.DailyQuestionSets.Where(d => d.StudentId == s.Id).Select(d => d.Day).OrderBy(d => d).ToListAsync())
            .ShouldBe(new[] { Today, Today.AddDays(1) });
    }

    [Fact]
    public async Task ConcurrentFirstRequest_UniqueViolation_ReturnsWinnersSet_SingleRow()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 10);
        var s = await AddStudentAsync(w, 101);

        // Rakip istek, bizim SaveChanges'imizden hemen önce aynı (öğrenci, gün) setini (farklı sorularla) yazar.
        var winnerIds = new[] { qs[9].QuestionId, qs[8].QuestionId };
        var interceptor = new ConcurrentSetInsert(_db, s.Id, w.GradeId, Today, winnerIds);
        await using (var ctx = _db.NewContext(interceptor))
        {
            var dto = await NewDaily(ctx).GetTodayAsync(s);
            interceptor.Fired.ShouldBeTrue();
            dto.Total.ShouldBe(2);
            dto.Status.ShouldBe(DailySetStatus.NotStarted);
        }

        (await SetQuestionIdsAsync(s.Id, Today)).ShouldBe(winnerIds);
        await using var check = _db.NewContext();
        (await check.DailyQuestionSets.CountAsync(d => d.StudentId == s.Id)).ShouldBe(1);
    }

    // ---- start + çözme ----

    [Fact]
    public async Task Start_IsIdempotent_SingleSession()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 8);
        var s = await AddStudentAsync(w, 101);

        var first = await StartAsync(s);
        var second = await StartAsync(s);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        second.SessionId.ShouldBe(first.SessionId);
        first.Status.ShouldBe(DailySetStatus.NotStarted);

        await using var ctx = _db.NewContext();
        (await ctx.PracticeSessions.CountAsync(p => p.StudentId == s.Id)).ShouldBe(1);
        (await GetTodayAsync(s)).SessionId.ShouldBe(first.SessionId);
    }

    [Fact]
    public async Task Start_RetryingExecutionStrategy_DoesNotThrow()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 3);
        var s = await AddStudentAsync(w, 101);

        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        var result = await NewDaily(ctx).StartTodayAsync(s);
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task DailySession_ServesSetQuestionsInOrder_ThenCompletes_WithSessionId()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 10);
        var s = await AddStudentAsync(w, 101);
        _options = new DailyQuestionsOptions { QuestionCount = 3, RecentAnswerExclusionDays = 14 };

        var start = await StartAsync(s);
        start.ShouldNotBeNull();
        var setIds = await SetQuestionIdsAsync(s.Id, Today);
        var byId = qs.ToDictionary(q => q.QuestionId);

        for (var i = 0; i < setIds.Count; i++)
        {
            await using var ctx = _db.NewContext();
            var practice = NewPractice(ctx);
            var next = await practice.NextQuestionAsync(start.SessionId, s.Id);
            next.ShouldNotBeNull();
            next.Question.ShouldNotBeNull();
            next.Question.Id.ShouldBe(setIds[i]);

            // ilk soru doğru, ikinci yanlış, üçüncü pas
            var dto = i switch
            {
                0 => new PracticeAnswerSubmitDto { QuestionId = setIds[i], SelectedAnswerId = byId[setIds[i]].CorrectAnswerId, TimeTaken = 10 },
                1 => new PracticeAnswerSubmitDto { QuestionId = setIds[i], SelectedAnswerId = byId[setIds[i]].WrongAnswerId, TimeTaken = 10 },
                _ => new PracticeAnswerSubmitDto { QuestionId = setIds[i], Skipped = true, TimeTaken = 3 }
            };
            (await practice.SubmitAnswerAsync(start.SessionId, s.Id, dto, "kc-101")).ShouldNotBeNull();

            if (i == 0)
            {
                var mid = await GetTodayAsync(s);
                mid.Status.ShouldBe(DailySetStatus.InProgress);
                mid.Answered.ShouldBe(1);
            }
        }

        var done = await GetTodayAsync(s);
        done.Status.ShouldBe(DailySetStatus.Completed);
        done.SessionId.ShouldBe(start.SessionId);
        done.Total.ShouldBe(3);
        done.Answered.ShouldBe(3);
        done.Correct.ShouldBe(1);
        done.Wrong.ShouldBe(1);
        done.Skipped.ShouldBe(1);

        // Tamamlanınca oturum kapanır; start yeni oturum açmaz, mevcut id + Completed döner.
        var again = await StartAsync(s);
        again.ShouldNotBeNull();
        again.SessionId.ShouldBe(start.SessionId);
        again.Status.ShouldBe(DailySetStatus.Completed);

        await using var check = _db.NewContext();
        (await check.PracticeSessions.CountAsync(p => p.StudentId == s.Id)).ShouldBe(1);
        (await check.PracticeSessions.SingleAsync(p => p.Id == start.SessionId)).Status.ShouldBe(PracticeSessionStatus.Ended);
    }

    [Fact]
    public async Task DailySession_EndedBeforeCompletion_StartReopensSameSession()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 5);
        var s = await AddStudentAsync(w, 101);

        var start = await StartAsync(s);
        start.ShouldNotBeNull();
        await using (var ctx = _db.NewContext())
            await NewPractice(ctx).EndAsync(start.SessionId, s.Id);

        var resumed = await StartAsync(s);
        resumed.ShouldNotBeNull();
        resumed.SessionId.ShouldBe(start.SessionId);

        await using var check = _db.NewContext();
        (await check.PracticeSessions.SingleAsync(p => p.Id == start.SessionId)).Status.ShouldBe(PracticeSessionStatus.Active);
        (await NewPractice(check).NextQuestionAsync(start.SessionId, s.Id))!.Question.ShouldNotBeNull();
    }

    [Fact]
    public async Task DailyAnswers_WriteAnswerSubmittedEventsToOutbox()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 4);
        var s = await AddStudentAsync(w, 101);
        var start = (await StartAsync(s))!;

        int questionId;
        await using (var ctx = _db.NewContext())
        {
            var practice = NewPractice(ctx);
            questionId = (await practice.NextQuestionAsync(start.SessionId, s.Id))!.Question!.Id;
            var correct = qs.Single(q => q.QuestionId == questionId).CorrectAnswerId;
            await practice.SubmitAnswerAsync(start.SessionId, s.Id,
                new PracticeAnswerSubmitDto { QuestionId = questionId, SelectedAnswerId = correct, TimeTaken = 12 }, "kc-101");
        }

        await using var check = _db.NewContext();
        var outbox = await check.OutboxMessages.SingleAsync();
        outbox.Type.ShouldBe(OutboxEventRegistry.NameFor<AnswerSubmittedEvent>());
        var evt = JsonSerializer.Deserialize<AnswerSubmittedEvent>(outbox.Content)!;
        evt.EventId.ShouldBe(outbox.Id);
        evt.UserId.ShouldBe(101); // Student.UserId
        evt.ClientId.ShouldBe("kc-101");
        evt.QuestionId.ShouldBe(questionId);
        evt.IsCorrect.ShouldBeTrue();
        evt.QuestionPoint.ShouldBe(10);
        evt.DifficultyLevel.ShouldBe(2);
        evt.SubjectId.ShouldBe(w.SubjectId);
        evt.Subject.ShouldBe("Matematik");
        evt.TimeTakenInSeconds.ShouldBe(12);
        evt.TestInstanceId.ShouldBe(-start.SessionId);
        evt.Revision.ShouldBe(1);
    }

    [Fact]
    public async Task FreePracticeAnswers_StillDoNotWriteOutbox()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 2);
        var s = await AddStudentAsync(w, 101);

        await using (var ctx = _db.NewContext())
        {
            var practice = NewPractice(ctx);
            var session = await practice.StartAsync(s, new PracticeSessionStartDto());
            var q = (await practice.NextQuestionAsync(session.Id, s.Id))!.Question!.Id;
            await practice.SubmitAnswerAsync(session.Id, s.Id,
                new PracticeAnswerSubmitDto { QuestionId = q, SelectedAnswerId = qs.Single(x => x.QuestionId == q).CorrectAnswerId }, "kc-101");
        }

        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task OtherStudent_CannotUseDailySession_NextAndAnswerReturnNull()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 5);
        var owner = await AddStudentAsync(w, 101);
        var intruder = await AddStudentAsync(w, 102);
        var start = (await StartAsync(owner))!;
        var setIds = await SetQuestionIdsAsync(owner.Id, Today);

        await using var ctx = _db.NewContext();
        var practice = NewPractice(ctx);
        (await practice.NextQuestionAsync(start.SessionId, intruder.Id)).ShouldBeNull();
        (await practice.SubmitAnswerAsync(start.SessionId, intruder.Id,
            new PracticeAnswerSubmitDto { QuestionId = setIds[0], SelectedAnswerId = qs.Single(q => q.QuestionId == setIds[0]).CorrectAnswerId }, "kc-102"))
            .ShouldBeNull();
        (await practice.GetReviewAsync(start.SessionId, intruder.Id)).ShouldBeNull();

        // Öğrencinin kendi günlük durumu başkasının setini göstermez.
        var intruderDto = await NewDaily(ctx).GetTodayAsync(intruder);
        intruderDto.SessionId.ShouldBeNull();
        (await ctx.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    // ---- review düzeltmeleri (CR U1-U3, Ö6, security D2/D3/D5/O1) ----

    private async Task SoftDeleteQuestionAsync(int questionId)
    {
        await using var ctx = _db.NewContext();
        var q = await ctx.Questions.SingleAsync(x => x.Id == questionId);
        ctx.Questions.Remove(q); // ApplyAuditInfo → IsDeleted
        await ctx.SaveChangesAsync();
    }

    private async Task<int> OutboxCountAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.OutboxMessages.CountAsync();
    }

    [Fact]
    public async Task DeletedSetQuestion_IsDroppedFromLiveList_PendingSkipped_SetStillCompletes()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 10);
        var s = await AddStudentAsync(w, 101);
        _options = new DailyQuestionsOptions { QuestionCount = 3, RecentAnswerExclusionDays = 14 };
        var byId = qs.ToDictionary(q => q.QuestionId);

        var start = (await StartAsync(s))!;
        var setIds = await SetQuestionIdsAsync(s.Id, Today);

        // İlk soru gösterildi (bekliyor), sonra silindi → next onu atlar, sıradakini verir.
        await using (var ctx = _db.NewContext())
            (await NewPractice(ctx).NextQuestionAsync(start.SessionId, s.Id))!.Question!.Id.ShouldBe(setIds[0]);
        await SoftDeleteQuestionAsync(setIds[0]);

        (await GetTodayAsync(s)).Total.ShouldBe(2);

        foreach (var expected in setIds.Skip(1))
        {
            await using var ctx = _db.NewContext();
            var practice = NewPractice(ctx);
            var next = await practice.NextQuestionAsync(start.SessionId, s.Id);
            next!.Question!.Id.ShouldBe(expected);
            await practice.SubmitAnswerAsync(start.SessionId, s.Id,
                new PracticeAnswerSubmitDto { QuestionId = expected, SelectedAnswerId = byId[expected].CorrectAnswerId }, "kc-101");
        }

        var done = await GetTodayAsync(s);
        done.Status.ShouldBe(DailySetStatus.Completed);
        done.Total.ShouldBe(2);
        done.Answered.ShouldBe(2);
        await using var check = _db.NewContext();
        (await check.PracticeSessions.SingleAsync(p => p.Id == start.SessionId)).Status.ShouldBe(PracticeSessionStatus.Ended);
    }

    [Fact]
    public async Task AllSetQuestionsDeleted_WithoutSession_Empty_WithSession_Completed()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 2);
        var a = await AddStudentAsync(w, 101);
        var b = await AddStudentAsync(w, 102);

        await GetTodayAsync(a);
        var started = (await StartAsync(b))!;
        foreach (var id in await SetQuestionIdsAsync(a.Id, Today))
            await SoftDeleteQuestionAsync(id);

        var aDto = await GetTodayAsync(a);
        aDto.Status.ShouldBe(DailySetStatus.Empty);
        (await StartAsync(a)).ShouldBeNull();

        var bDto = await GetTodayAsync(b);
        bDto.Status.ShouldBe(DailySetStatus.Completed);
        bDto.SessionId.ShouldBe(started.SessionId);
        bDto.Total.ShouldBe(0);
    }

    [Fact]
    public async Task SkippedDailyAnswer_ProducesNoEvent()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 4);
        var s = await AddStudentAsync(w, 101);
        var start = (await StartAsync(s))!;

        await using (var ctx = _db.NewContext())
        {
            var practice = NewPractice(ctx);
            var q = (await practice.NextQuestionAsync(start.SessionId, s.Id))!.Question!.Id;
            (await practice.SubmitAnswerAsync(start.SessionId, s.Id,
                new PracticeAnswerSubmitDto { QuestionId = q, Skipped = true }, "kc-101"))!.Skipped.ShouldBeTrue();
        }

        (await OutboxCountAsync()).ShouldBe(0);
        (await GetTodayAsync(s)).Skipped.ShouldBe(1);
    }

    [Fact]
    public async Task DailyAnswer_SecondSubmitForSameQuestion_IsRejected_NoSecondEvent()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 4);
        var s = await AddStudentAsync(w, 101);
        var start = (await StartAsync(s))!;

        int q;
        await using (var ctx = _db.NewContext())
            q = (await NewPractice(ctx).NextQuestionAsync(start.SessionId, s.Id))!.Question!.Id;
        var dto = new PracticeAnswerSubmitDto { QuestionId = q, SelectedAnswerId = qs.Single(x => x.QuestionId == q).CorrectAnswerId };

        // Stale okuma: ikinci bağlam oturumu, birinci cevap yazılmadan ÖNCE yüklemiş gibi davranır — koşullu UPDATE 0 satır.
        var raced = new AnswerFirstInterceptor(_db, q);
        await using (var ctx = _db.NewContext(raced))
        {
            var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
                NewPractice(ctx).SubmitAnswerAsync(start.SessionId, s.Id, dto, "kc-101"));
            ex.Message.ShouldContain("cevaplandı");
        }

        raced.Fired.ShouldBeTrue();
        (await OutboxCountAsync()).ShouldBe(0); // yarışı kaybeden event yazmadı (kazanan doğrudan DB'ye yazdı)
    }

    [Fact]
    public async Task DailySessionAnsweredOnAnotherDay_RecordsAnswer_ButProducesNoEvent()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 4);
        var s = await AddStudentAsync(w, 101);
        var start = (await StartAsync(s))!;

        int q;
        await using (var ctx = _db.NewContext())
            q = (await NewPractice(ctx).NextQuestionAsync(start.SessionId, s.Id))!.Question!.Id;

        _clock.Now = _clock.Now.AddDays(1); // ertesi gün (İstanbul)
        await using (var ctx = _db.NewContext())
            (await NewPractice(ctx).SubmitAnswerAsync(start.SessionId, s.Id,
                new PracticeAnswerSubmitDto { QuestionId = q, SelectedAnswerId = qs.Single(x => x.QuestionId == q).CorrectAnswerId }, "kc-101"))
                .ShouldNotBeNull();

        (await OutboxCountAsync()).ShouldBe(0);
        await using var check = _db.NewContext();
        (await check.PracticeSessionQuestions.SingleAsync(p => p.PracticeSessionId == start.SessionId)).AnsweredAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task FreePractice_ExcludesTodaysUnansweredDailyQuestions()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 6);
        var s = await AddStudentAsync(w, 101);
        var daily = (await StartAsync(s))!; // 5 soru ayrıldı
        var setIds = await SetQuestionIdsAsync(s.Id, Today);
        var freeOnly = qs.Select(x => x.QuestionId).Except(setIds).Single();

        await using (var ctx = _db.NewContext())
        {
            var practice = NewPractice(ctx);
            var free = await practice.StartAsync(s, new PracticeSessionStartDto());
            (await practice.NextQuestionAsync(free.Id, s.Id))!.Question!.Id.ShouldBe(freeOnly);
            await practice.SubmitAnswerAsync(free.Id, s.Id, new PracticeAnswerSubmitDto { QuestionId = freeOnly, Skipped = true });
            (await practice.NextQuestionAsync(free.Id, s.Id))!.PoolExhausted.ShouldBeTrue();
        }

        // Günlük oturumda cevaplanan soru serbest pratiğe açılır.
        await using (var ctx = _db.NewContext())
        {
            var practice = NewPractice(ctx);
            var first = (await practice.NextQuestionAsync(daily.SessionId, s.Id))!.Question!.Id;
            await practice.SubmitAnswerAsync(daily.SessionId, s.Id, new PracticeAnswerSubmitDto { QuestionId = first, Skipped = true });

            var free2 = await practice.StartAsync(s, new PracticeSessionStartDto());
            var seen = new List<int>();
            while (true)
            {
                var n = await practice.NextQuestionAsync(free2.Id, s.Id);
                if (n!.PoolExhausted) break;
                seen.Add(n.Question!.Id);
                await practice.SubmitAnswerAsync(free2.Id, s.Id, new PracticeAnswerSubmitDto { QuestionId = n.Question.Id, Skipped = true });
            }
            seen.OrderBy(x => x).ShouldBe(new[] { first, freeOnly }.OrderBy(x => x));
        }
    }

    [Fact]
    public async Task TimeTaken_IsClampedTo3600_InRecordAndEvent()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 3);
        var s = await AddStudentAsync(w, 101);
        var start = (await StartAsync(s))!;

        await using (var ctx = _db.NewContext())
        {
            var practice = NewPractice(ctx);
            var q = (await practice.NextQuestionAsync(start.SessionId, s.Id))!.Question!.Id;
            await practice.SubmitAnswerAsync(start.SessionId, s.Id,
                new PracticeAnswerSubmitDto { QuestionId = q, SelectedAnswerId = qs.Single(x => x.QuestionId == q).CorrectAnswerId, TimeTaken = 999_999 }, "kc-101");
        }

        await using var check = _db.NewContext();
        (await check.PracticeSessionQuestions.SingleAsync()).TimeTaken.ShouldBe(3600);
        JsonSerializer.Deserialize<AnswerSubmittedEvent>((await check.OutboxMessages.SingleAsync()).Content)!
            .TimeTakenInSeconds.ShouldBe(3600);

        // DTO sözleşmesi: 3600 üstü model doğrulamasında 400.
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var tooLong = new PracticeAnswerSubmitDto { QuestionId = 1, TimeTaken = 3601 };
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            tooLong, new System.ComponentModel.DataAnnotations.ValidationContext(tooLong), results, true).ShouldBeFalse();
    }

    [Fact]
    public async Task TargetCount_ComesFromStoredSet_NotCurrentConfig()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 10);
        var s = await AddStudentAsync(w, 101);

        await GetTodayAsync(s);
        _options = new DailyQuestionsOptions { QuestionCount = 8, RecentAnswerExclusionDays = 14 };

        var dto = await GetTodayAsync(s);
        dto.TargetCount.ShouldBe(5);
        dto.Total.ShouldBe(5);
    }

    [Fact]
    public async Task Reopen_GoesThroughAudit_SetsUpdateTime()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 3);
        var s = await AddStudentAsync(w, 101);
        var start = (await StartAsync(s))!;
        await using (var ctx = _db.NewContext())
            await NewPractice(ctx).EndAsync(start.SessionId, s.Id);
        DateTime? endedUpdate;
        await using (var ctx = _db.NewContext())
            endedUpdate = (await ctx.PracticeSessions.SingleAsync(p => p.Id == start.SessionId)).UpdateTime;

        await Task.Delay(20);
        await StartAsync(s);

        await using var check = _db.NewContext();
        var row = await check.PracticeSessions.SingleAsync(p => p.Id == start.SessionId);
        row.Status.ShouldBe(PracticeSessionStatus.Active);
        row.UpdateTime.ShouldNotBeNull();
        row.UpdateTime!.Value.ShouldBeGreaterThan(endedUpdate!.Value);
    }

    [Fact]
    public async Task SessionHistory_ExcludesDailySessions()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 6);
        var s = await AddStudentAsync(w, 101);
        var daily = (await StartAsync(s))!;

        await using var ctx = _db.NewContext();
        var practice = NewPractice(ctx);
        var free = await practice.StartAsync(s, new PracticeSessionStartDto());

        var list = await practice.ListAsync(s.Id, 1, 20);
        list.TotalCount.ShouldBe(1);
        list.Items.Select(i => i.Id).ShouldBe(new[] { free.Id });
        list.Items.ShouldNotContain(i => i.Id == daily.SessionId);
    }

    [Fact]
    public async Task EndOnDailySession_IsIdempotent_NoErrorOnAlreadyEnded()
    {
        var w = await SeedWorldAsync();
        var qs = await AddQuestionsAsync(w, 1);
        var s = await AddStudentAsync(w, 101);
        var start = (await StartAsync(s))!;

        await using (var ctx = _db.NewContext())
        {
            var practice = NewPractice(ctx);
            var q = (await practice.NextQuestionAsync(start.SessionId, s.Id))!.Question!.Id;
            await practice.SubmitAnswerAsync(start.SessionId, s.Id,
                new PracticeAnswerSubmitDto { QuestionId = q, SelectedAnswerId = qs[0].CorrectAnswerId }, "kc-101");
        }

        // Set tamamlanınca oturum otomatik kapandı; end tekrar çağrılınca hata değil mevcut durum döner.
        await using var check = _db.NewContext();
        var first = await NewPractice(check).EndAsync(start.SessionId, s.Id);
        var second = await NewPractice(check).EndAsync(start.SessionId, s.Id);
        first!.Status.ShouldBe("Ended");
        second!.Status.ShouldBe("Ended");
        second.AnsweredCount.ShouldBe(1);
        (await GetTodayAsync(s)).Status.ShouldBe(DailySetStatus.Completed);
    }

    /// <summary>
    /// Servis koşullu UPDATE'i çalıştırmadan önce (oturum yüklendikten sonra, outbox/transaction öncesi okumada) rakip isteğin
    /// aynı soruyu cevapladığını simüle eder: ilk komut çalışmadan hemen önce ayrı bağlamla AnsweredAt yazılır.
    /// </summary>
    private sealed class AnswerFirstInterceptor(TestDb db, int questionId) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("UPDATE \"PracticeSessionQuestions\"", StringComparison.Ordinal))
            {
                Fired = true;
                // Aynı bağlantı/transaction üzerinde rakip yazımı: koşullu UPDATE'ten hemen önce satırı cevaplanmış yap.
                using var cmd = command.Connection!.CreateCommand();
                cmd.Transaction = command.Transaction;
                cmd.CommandText = $"UPDATE \"PracticeSessionQuestions\" SET \"AnsweredAt\" = '2026-10-05 09:00:00' WHERE \"QuestionId\" = {questionId}";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    /// <summary>İlk SaveChanges'ten hemen önce ayrı bağlamla aynı (öğrenci, gün) için set yazar (eşzamanlı ilk istek).</summary>
    private sealed class ConcurrentSetInsert(TestDb db, int studentId, int gradeId, DateOnly day, int[] questionIds) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var other = db.NewContext();
                other.DailyQuestionSets.Add(new DailyQuestionSet
                {
                    StudentId = studentId,
                    Day = day,
                    GradeId = gradeId,
                    TargetCount = 5,
                    Items = questionIds.Select((id, i) => new DailyQuestionSetItem { QuestionId = id, Order = i }).ToList()
                });
                await other.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }
}
