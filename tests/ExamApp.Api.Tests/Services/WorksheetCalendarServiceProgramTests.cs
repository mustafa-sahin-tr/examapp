using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #110 — WorksheetCalendarService.GetMyCalendarAsync artık aktif çalışma programı
/// sayfa planlarını (UserProgramStudyPageSchedule) da "program-study-page" event'i olarak döner.
/// </summary>
public class WorksheetCalendarServiceProgramTests : IDisposable
{
    private const int StudentId = 1;
    private const string KeycloakUserId = "kc-student-1";
    private const string OtherKeycloakUserId = "kc-student-2";

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private WorksheetCalendarService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private async Task<(int programId, int scheduleId)> SeedScheduleAsync(
        string userId,
        DateTime startUtc,
        DateTime endUtc,
        bool programIsActive = true,
        bool isCompleted = false,
        string studyItemTitle = "Sayfa 1",
        string programName = "Planım")
    {
        await using var ctx = _db.NewContext();

        var page = new StudyItem { Title = studyItemTitle, Description = "d", CreatedByUserId = 1 };
        ctx.StudyItems.Add(page);
        await ctx.SaveChangesAsync();

        var program = new UserProgram
        {
            UserId = userId,
            ProgramName = programName,
            Description = "d",
            StudyType = "time",
            StudyDuration = "25-5",
            SubjectsPerDay = 1,
            RestDays = "",
            DifficultSubjects = "",
            IsActive = programIsActive,
        };
        ctx.UserPrograms.Add(program);
        await ctx.SaveChangesAsync();

        var schedule = new UserProgramStudyPageSchedule
        {
            UserProgramId = program.Id,
            StudyItemId = page.Id,
            StartDate = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc),
            IsCompleted = isCompleted,
        };
        ctx.UserProgramStudyPageSchedules.Add(schedule);
        await ctx.SaveChangesAsync();

        return (program.Id, schedule.Id);
    }

    [Fact]
    public async Task GetMyCalendarAsync_ScheduleFullyInsideRange_ReturnsProgramStudyItemEvent()
    {
        await SeedScheduleAsync(
            KeycloakUserId,
            startUtc: new DateTime(2026, 3, 3),
            endUtc: new DateTime(2026, 3, 3));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetMyCalendarAsync(
            StudentId, KeycloakUserId, null, null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        var ev = result.Events.ShouldHaveSingleItem();
        ev.Kind.ShouldBe("program-study-page");
        ev.ProgramName.ShouldBe("Planım");
        ev.StudyItemTitle.ShouldBe("Sayfa 1");
        ev.IsCompleted.ShouldBe(false);
    }

    [Fact]
    public async Task GetMyCalendarAsync_MultiDayScheduleCrossingRangeStart_IsIncluded()
    {
        // Starts before "from", ends inside the range -> should still intersect.
        await SeedScheduleAsync(
            KeycloakUserId,
            startUtc: new DateTime(2026, 2, 25),
            endUtc: new DateTime(2026, 3, 2));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetMyCalendarAsync(
            StudentId, KeycloakUserId, null, null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        result.Events.ShouldHaveSingleItem().Kind.ShouldBe("program-study-page");
    }

    [Fact]
    public async Task GetMyCalendarAsync_MultiDayScheduleCrossingRangeEnd_IsIncluded()
    {
        // Starts inside the range, ends after "to" -> should still intersect (StartDate < toUtc).
        await SeedScheduleAsync(
            KeycloakUserId,
            startUtc: new DateTime(2026, 3, 9),
            endUtc: new DateTime(2026, 3, 15));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetMyCalendarAsync(
            StudentId, KeycloakUserId, null, null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        result.Events.ShouldHaveSingleItem().Kind.ShouldBe("program-study-page");
    }

    [Fact]
    public async Task GetMyCalendarAsync_ScheduleEndsBeforeRangeStart_IsExcluded()
    {
        await SeedScheduleAsync(
            KeycloakUserId,
            startUtc: new DateTime(2026, 2, 1),
            endUtc: new DateTime(2026, 2, 20)); // ends well before "from"

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetMyCalendarAsync(
            StudentId, KeycloakUserId, null, null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetMyCalendarAsync_ScheduleStartsOnOrAfterRangeEnd_IsExcluded()
    {
        await SeedScheduleAsync(
            KeycloakUserId,
            startUtc: new DateTime(2026, 3, 10), // toUtc is exclusive
            endUtc: new DateTime(2026, 3, 12));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetMyCalendarAsync(
            StudentId, KeycloakUserId, null, null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetMyCalendarAsync_CompletedSchedule_ReturnsIsCompletedTrue()
    {
        await SeedScheduleAsync(
            KeycloakUserId,
            startUtc: new DateTime(2026, 3, 3),
            endUtc: new DateTime(2026, 3, 3),
            isCompleted: true);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetMyCalendarAsync(
            StudentId, KeycloakUserId, null, null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        result.Events.ShouldHaveSingleItem().IsCompleted.ShouldBe(true);
    }

    [Fact]
    public async Task GetMyCalendarAsync_InactiveProgram_ReturnsNoProgramStudyItemEvents()
    {
        await SeedScheduleAsync(
            KeycloakUserId,
            startUtc: new DateTime(2026, 3, 3),
            endUtc: new DateTime(2026, 3, 3),
            programIsActive: false);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetMyCalendarAsync(
            StudentId, KeycloakUserId, null, null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetMyCalendarAsync_AnotherStudentsProgram_DoesNotLeakIntoCallersCalendar()
    {
        await SeedScheduleAsync(
            OtherKeycloakUserId,
            startUtc: new DateTime(2026, 3, 3),
            endUtc: new DateTime(2026, 3, 3));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetMyCalendarAsync(
            StudentId, KeycloakUserId, null, null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetMyCalendarAsync_BlankKeycloakUserId_ReturnsNoProgramStudyItemEvents()
    {
        await SeedScheduleAsync(
            KeycloakUserId,
            startUtc: new DateTime(2026, 3, 3),
            endUtc: new DateTime(2026, 3, 3));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetMyCalendarAsync(
            StudentId, string.Empty, null, null,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        result.Events.ShouldBeEmpty();
    }

    public void Dispose() => _db.Dispose();
}
