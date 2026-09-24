using System;
using System.Linq;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #87: admin dashboard trend serileri (GET /api/admin/dashboard/trends).
/// </summary>
public class DashboardServiceTrendsTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    /// <summary>
    /// issue #265: gün kovaları yerel (Europe/Istanbul, UTC+3) gündür. "Şimdi" bugünün UTC 12:00'ına sabitlenir: bu anda
    /// TR tarihi UTC tarihiyle aynıdır ve UTC gece yarısı (TR 03:00) aynı TR gününe düşer — aşağıdaki UTC-gün tabanlı
    /// senaryolar koşunun saatinden bağımsız (eskiden 21:00-24:00 UTC arasında kayabilirdi) aynı sonucu verir.
    /// </summary>
    private DashboardService NewService(AppDbContext ctx) => new(ctx, IstanbulAt(Today.AddHours(12)));

    private static LocalDayCalendar IstanbulAt(DateTime utcNow)
        => new(LocalDayCalendar.DefaultTimeZoneId, new FixedTimeProvider(new DateTimeOffset(utcNow, TimeSpan.Zero)));

    /// <summary>Bugünün UTC tarihi, testler boyunca sabit referans olarak kullanılır.</summary>
    private static DateTime Today => DateTime.UtcNow.Date;

    // ---- Helpers ----

    private async Task<int> AddGradeAsync(AppDbContext ctx, string name = "G")
    {
        var g = new Grade { Name = name };
        ctx.Grades.Add(g);
        await ctx.SaveChangesAsync();
        return g.Id;
    }

    private async Task<int> AddQuestionAsync(AppDbContext ctx)
    {
        var q = new Question { Text = "q", Point = 1 };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    /// <summary>
    /// Question.CreateTime, BaseEntity denetim (audit) interceptor'ı yüzünden Add sırasında her
    /// zaman DateTime.UtcNow ile ezilir; istenen güne taşımak için ekten sonra ayrı bir
    /// ExecuteUpdate ile (interceptor'a uğramadan) günceller.
    /// </summary>
    private static async Task SetQuestionCreateTimeAsync(AppDbContext ctx, int questionId, DateTime day)
    {
        await ctx.Questions.Where(q => q.Id == questionId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.CreateTime, day));
    }

    private async Task<(int studentId, int gradeId)> AddStudentAsync(AppDbContext ctx, int userId)
    {
        var gradeId = await AddGradeAsync(ctx, $"G{userId}");
        var student = new Student { UserId = userId, StudentNumber = $"S{userId}", SchoolName = "School" };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        return (student.Id, gradeId);
    }

    /// <summary>
    /// Cevaplanmış (veya isteğe bağlı cevaplanmamış) bir PracticeSessionQuestion satırı ekler.
    /// AnsweredAt, BaseEntity'nin denetim alanı DEĞİL; doğrudan set edilen değer aynen kalır.
    /// </summary>
    private async Task AddPracticeSolvedAsync(AppDbContext ctx, DateTime? answeredAt)
    {
        var (studentId, gradeId) = await AddStudentAsync(ctx, Random.Shared.Next(1, 1_000_000));
        var session = new PracticeSession { StudentId = studentId, GradeId = gradeId, StartTime = Today };
        ctx.PracticeSessions.Add(session);
        var questionId = await AddQuestionAsync(ctx);
        await ctx.SaveChangesAsync();

        ctx.PracticeSessionQuestions.Add(new PracticeSessionQuestion
        {
            PracticeSessionId = session.Id,
            QuestionId = questionId,
            ShownAt = Today,
            AnsweredAt = answeredAt,
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Cevaplanmış (veya isteğe bağlı cevaplanmamış) bir WorksheetInstanceQuestion satırı ekler.
    /// UpdateTime, BaseEntity'nin denetim alanı olsa da yalnızca EntityState.Modified geçişinde
    /// ezilir; Added durumunda (ilk ekleme) dokunulmaz, bu yüzden burada verilen değer kalıcıdır.
    /// </summary>
    private async Task AddWorksheetSolvedAsync(AppDbContext ctx, DateTime? updateTime, int? selectedAnswerId = -1, string? answerPayload = null)
    {
        var (studentId, gradeId) = await AddStudentAsync(ctx, Random.Shared.Next(1, 1_000_000));
        var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
        ctx.Worksheets.Add(worksheet);
        var questionId = await AddQuestionAsync(ctx);
        await ctx.SaveChangesAsync();

        var answerId = selectedAnswerId;
        if (answerId == -1)
        {
            var answer = new Answer { QuestionId = questionId, Text = "A", Tag = "A" };
            ctx.Answers.Add(answer);
            await ctx.SaveChangesAsync();
            answerId = answer.Id;
        }

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 };
        ctx.TestQuestions.Add(wq);
        var instance = new WorksheetInstance
        {
            StudentId = studentId,
            WorksheetId = worksheet.Id,
            StartTime = Today,
            Status = WorksheetInstanceStatus.Started,
        };
        ctx.Add(instance);
        await ctx.SaveChangesAsync();

        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
        {
            WorksheetInstanceId = instance.Id,
            WorksheetQuestionId = wq.Id,
            SelectedAnswerId = answerId,
            AnswerPayload = answerPayload,
            UpdateTime = updateTime,
        });
        await ctx.SaveChangesAsync();
    }

    // ---- days param → fixed-length series with zero-fill ----

    [Fact]
    public async Task GetTrendsAsync_EmptyDatabase_ReturnsExactlyDaysEntriesAllZero()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetTrendsAsync(30);

        result.QuestionCreated.Count.ShouldBe(30);
        result.QuestionSolved.Count.ShouldBe(30);
        result.QuestionCreated.ShouldAllBe(p => p.Count == 0);
        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_SparseData_MissingDaysAreZeroFilled()
    {
        await using (var ctx = _db.NewContext())
        {
            var q1 = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, q1, Today);

            var q2 = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, q2, Today.AddDays(-5));
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(7);

        result.QuestionCreated.Count.ShouldBe(7);
        result.QuestionCreated.Last().Date.ShouldBe(DateOnly.FromDateTime(Today));
        result.QuestionCreated.Last().Count.ShouldBe(1);
        result.QuestionCreated.First().Date.ShouldBe(DateOnly.FromDateTime(Today.AddDays(-6)));

        var dayMinus5 = result.QuestionCreated.Single(p => p.Date == DateOnly.FromDateTime(Today.AddDays(-5)));
        dayMinus5.Count.ShouldBe(1);

        // Between the two seeded days, everything else must be zero.
        result.QuestionCreated.Where(p => p.Date != DateOnly.FromDateTime(Today) && p.Date != DateOnly.FromDateTime(Today.AddDays(-5)))
            .ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_SeriesIsOrderedAscendingByDateStartingFromCutoff()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetTrendsAsync(5);

        var expectedDates = Enumerable.Range(0, 5)
            .Select(i => DateOnly.FromDateTime(Today.AddDays(-4 + i)))
            .ToList();

        result.QuestionCreated.Select(p => p.Date).ShouldBe(expectedDates);
    }

    // ---- merging two sources into a single QuestionSolved series ----

    [Fact]
    public async Task GetTrendsAsync_PracticeAndWorksheetSolvedOnSameDay_SumsIntoOneSeries()
    {
        await using (var ctx = _db.NewContext())
        {
            await AddPracticeSolvedAsync(ctx, Today);
            await AddWorksheetSolvedAsync(ctx, Today);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.Last().Count.ShouldBe(2);
    }

    // ---- exclusion rules ----

    [Fact]
    public async Task GetTrendsAsync_UnansweredPracticeQuestion_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            await AddPracticeSolvedAsync(ctx, answeredAt: null);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_UnansweredWorksheetQuestion_WithNoSelectedAnswerAndNoPayload_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            // Row opened when the test started (CreateTime = today) but never answered:
            // both SelectedAnswerId and AnswerPayload are null, UpdateTime is null too.
            await AddWorksheetSolvedAsync(ctx, updateTime: null, selectedAnswerId: null, answerPayload: null);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_WorksheetQuestionAnsweredViaPayloadOnly_IsCounted()
    {
        // Non-MCQ (e.g. drag-drop) answers are stored in AnswerPayload with no SelectedAnswerId.
        await using (var ctx = _db.NewContext())
        {
            await AddWorksheetSolvedAsync(ctx, updateTime: Today, selectedAnswerId: null, answerPayload: "{\"order\":[1,2]}");
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.Last().Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetTrendsAsync_WorksheetQuestionAnsweredButUpdateTimeNull_IsNotCounted()
    {
        // Edge case guarded explicitly by the service's `w.UpdateTime != null` check.
        await using (var ctx = _db.NewContext())
        {
            var (studentId, gradeId) = await AddStudentAsync(ctx, 999001);
            var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            ctx.Worksheets.Add(worksheet);
            var questionId = await AddQuestionAsync(ctx);
            await ctx.SaveChangesAsync();

            var answer = new Answer { QuestionId = questionId, Text = "A", Tag = "A" };
            ctx.Answers.Add(answer);
            var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 };
            ctx.TestQuestions.Add(wq);
            var instance = new WorksheetInstance { StudentId = studentId, WorksheetId = worksheet.Id, StartTime = Today, Status = WorksheetInstanceStatus.Started };
            ctx.Add(instance);
            await ctx.SaveChangesAsync();

            ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
            {
                WorksheetInstanceId = instance.Id,
                WorksheetQuestionId = wq.Id,
                SelectedAnswerId = answer.Id,
                UpdateTime = null, // answered flag present, but UpdateTime somehow missing
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_QuestionCreatedOutsideDateWindow_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            var qOld = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, qOld, Today.AddDays(-40)); // outside a 30-day window
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionCreated.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_PracticeAndWorksheetSolvedOutsideDateWindow_AreNotCounted()
    {
        // Note: the helpers below also insert a supporting Question (CreateTime = today), which
        // is irrelevant to QuestionCreated and is intentionally not asserted on here.
        await using (var ctx = _db.NewContext())
        {
            await AddPracticeSolvedAsync(ctx, Today.AddDays(-40));
            await AddWorksheetSolvedAsync(ctx, Today.AddDays(-40));
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    // ---- StudentLogin series (issue #89) ----

    private static async Task AddLoginEventAsync(AppDbContext ctx, DateTime occurredAtUtc, string role, bool success)
    {
        ctx.LoginEvents.Add(new LoginEvent
        {
            KeycloakUserId = Guid.NewGuid().ToString(),
            Role = role,
            OccurredAtUtc = occurredAtUtc,
            Success = success,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task GetTrendsAsync_SuccessfulStudentLogin_IsGroupedIntoCorrectDay()
    {
        await using (var ctx = _db.NewContext())
        {
            await AddLoginEventAsync(ctx, Today, "Student", success: true);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.StudentLogin.Last().Date.ShouldBe(DateOnly.FromDateTime(Today));
        result.StudentLogin.Last().Count.ShouldBe(1);
        result.StudentLogin.Where(p => p.Date != DateOnly.FromDateTime(Today)).ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_FailedStudentLogin_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            await AddLoginEventAsync(ctx, Today, "Student", success: false);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.StudentLogin.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_TeacherOrAdminLogin_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            await AddLoginEventAsync(ctx, Today, "Teacher", success: true);
            await AddLoginEventAsync(ctx, Today, "Admin", success: true);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.StudentLogin.ShouldAllBe(p => p.Count == 0);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("STUDENT")]
    [InlineData("Student")]
    public async Task GetTrendsAsync_StudentRoleIsCaseInsensitive_IsCounted(string roleCasing)
    {
        await using (var ctx = _db.NewContext())
        {
            await AddLoginEventAsync(ctx, Today, roleCasing, success: true);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.StudentLogin.Last().Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetTrendsAsync_StudentLoginSeries_LengthMatchesDaysParameter()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetTrendsAsync(14);

        result.StudentLogin.Count.ShouldBe(14);
    }

    [Fact]
    public async Task GetTrendsAsync_StudentLoginOutsideDateWindow_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            // days=30 window's cutoff is today-29; a login 31 days ago falls outside it.
            await AddLoginEventAsync(ctx, Today.AddDays(-31), "Student", success: true);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.StudentLogin.ShouldAllBe(p => p.Count == 0);
    }

    // ---- pencere sınırları (issue #265 review: yerel gece yarısı = önceki gün 21:00 UTC) ----

    /// <summary><see cref="NewService"/> ile aynı sabit saatteki 7 günlük pencere: [StartUtc, EndUtc).</summary>
    private static LocalDayWindow SevenDayWindow => IstanbulAt(Today.AddHours(12)).LastDays(7);

    [Fact]
    public async Task GetTrendsAsync_RowAtWindowStart_IsCountedOnFirstDay_AndOneTickEarlierIsExcluded()
    {
        // StartUtc = ilk günün TR 00:00'ı (inclusive, >=). Bir tick öncesi önceki TR günüdür → pencere dışı.
        var window = SevenDayWindow;
        window.StartUtc.ShouldBe(Today.AddDays(-7).AddHours(21));
        await using (var ctx = _db.NewContext())
        {
            var inside = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, inside, window.StartUtc);
            var outside = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, outside, window.StartUtc.AddTicks(-1));
            await AddLoginEventAsync(ctx, window.StartUtc, "Student", success: true);
            await AddLoginEventAsync(ctx, window.StartUtc.AddTicks(-1), "Student", success: true);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(7);

        result.QuestionCreated.First().Date.ShouldBe(window.FirstDay);
        result.QuestionCreated.First().Count.ShouldBe(1);
        result.QuestionCreated.Sum(p => p.Count).ShouldBe(1);
        result.StudentLogin.First().Count.ShouldBe(1);
        result.StudentLogin.Sum(p => p.Count).ShouldBe(1);
    }

    [Fact]
    public async Task GetTrendsAsync_RowOneTickBeforeWindowEnd_IsCountedOnLastDay_AndAtEndIsExcluded()
    {
        // EndUtc = yarının TR 00:00'ı (exclusive, <). Bir tick öncesi bugünün son anı.
        var window = SevenDayWindow;
        window.EndUtc.ShouldBe(Today.AddHours(21));
        await using (var ctx = _db.NewContext())
        {
            var inside = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, inside, window.EndUtc.AddTicks(-1));
            var outside = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, outside, window.EndUtc);
            await AddLoginEventAsync(ctx, window.EndUtc.AddTicks(-1), "Student", success: true);
            await AddLoginEventAsync(ctx, window.EndUtc, "Student", success: true);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(7);

        result.QuestionCreated.Last().Date.ShouldBe(window.LastDay);
        result.QuestionCreated.Last().Count.ShouldBe(1);
        result.QuestionCreated.Sum(p => p.Count).ShouldBe(1);
        result.StudentLogin.Last().Count.ShouldBe(1);
        result.StudentLogin.Sum(p => p.Count).ShouldBe(1);
    }

    // ---- issue #265: yerel (Europe/Istanbul) gün kovaları ----

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetTrendsAsync_BucketsByIstanbulDay_NotUtcDay()
    {
        await using (var ctx = _db.NewContext())
        {
            await AddLoginEventAsync(ctx, Utc(2026, 9, 24, 22, 30), "Student", success: true); // 25 Eylül 01:30 TR
            await AddLoginEventAsync(ctx, Utc(2026, 9, 24, 20, 59), "Student", success: true); // 24 Eylül 23:59 TR
            await AddWorksheetSolvedAsync(ctx, Utc(2026, 9, 24, 21, 0));                       // 25 Eylül 00:00 TR
            var q = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, q, Utc(2026, 9, 18, 20, 59));                // 18 Eylül 23:59 TR → pencere dışı
        }

        await using var check = _db.NewContext();
        var result = await new DashboardService(check, IstanbulAt(Utc(2026, 9, 25, 10, 0))).GetTrendsAsync(7);

        result.StudentLogin.Select(p => p.Date).ShouldBe(
            Enumerable.Range(19, 7).Select(d => new DateOnly(2026, 9, d)));
        result.StudentLogin.Single(p => p.Date == new DateOnly(2026, 9, 25)).Count.ShouldBe(1);
        result.StudentLogin.Single(p => p.Date == new DateOnly(2026, 9, 24)).Count.ShouldBe(1);
        result.QuestionSolved.Single(p => p.Date == new DateOnly(2026, 9, 25)).Count.ShouldBe(1);
        result.QuestionSolved.Single(p => p.Date == new DateOnly(2026, 9, 24)).Count.ShouldBe(0);
        result.QuestionCreated.Where(p => p.Date == new DateOnly(2026, 9, 19)).ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_DstZone_TransitionDayIsCountedAsOneLocalDay()
    {
        // Yapılandırılabilir bölge: Europe/Berlin 25 Ekim 2026 25 saatlik gün (03:00 CEST → 02:00 CET).
        await using (var ctx = _db.NewContext())
        {
            await AddLoginEventAsync(ctx, Utc(2026, 10, 24, 22, 30), "Student", success: true); // 25 Ekim 00:30 CEST
            await AddLoginEventAsync(ctx, Utc(2026, 10, 25, 22, 30), "Student", success: true); // 25 Ekim 23:30 CET
            await AddLoginEventAsync(ctx, Utc(2026, 10, 25, 23, 30), "Student", success: true); // 26 Ekim 00:30 CET
        }

        var berlin = new LocalDayCalendar("Europe/Berlin", new FixedTimeProvider(new DateTimeOffset(Utc(2026, 10, 27, 12, 0))));
        await using var check = _db.NewContext();
        var result = await new DashboardService(check, berlin).GetTrendsAsync(5);

        result.StudentLogin.Single(p => p.Date == new DateOnly(2026, 10, 25)).Count.ShouldBe(2);
        result.StudentLogin.Single(p => p.Date == new DateOnly(2026, 10, 26)).Count.ShouldBe(1);
        result.StudentLogin.Sum(p => p.Count).ShouldBe(3);
    }

    public void Dispose() => _db.Dispose();
}
