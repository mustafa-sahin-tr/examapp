using ExamApp.Api.Data;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class WorksheetReminderServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();

    public WorksheetReminderServiceTests()
    {
        // Hangfire extension methods (Schedule<T>, ChangeState) bottom out at IBackgroundJobClient.Create.
        _jobs.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("job-123");
    }

    private WorksheetReminderService NewService(AppDbContext ctx) => new(ctx, _jobs);

    private sealed record World(int WorksheetId, int StudentId, int GradeId);

    /// <summary>Student + worksheet + a student-scoped assignment so UpsertAsync's access check passes.</summary>
    private async Task<World> SeedAsync(bool withAssignment = true)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = 55, StudentNumber = "n", SchoolName = "s", GradeId = grade.Id };
        var ws = new Worksheet { Name = "Planlanan", Description = "d", GradeId = grade.Id };
        ctx.AddRange(student, ws);
        await ctx.SaveChangesAsync();

        if (withAssignment)
        {
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = DateTime.UtcNow.AddDays(-1),
            });
            await ctx.SaveChangesAsync();
        }

        return new World(ws.Id, student.Id, grade.Id);
    }

    private static DateTime FutureUtc => DateTime.UtcNow.AddDays(2);

    // ---- validation ----

    [Fact]
    public async Task UpsertAsync_ScheduledForInThePast_ThrowsInvalidOperation()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).UpsertAsync(w.WorksheetId, w.StudentId, "kc", DateTime.UtcNow.AddHours(-1), 60, default));
        ex.Message.ShouldContain("Geçmiş");
    }

    [Fact]
    public async Task UpsertAsync_RemindBeforeMinutesNegative_ThrowsInvalidOperation()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).UpsertAsync(w.WorksheetId, w.StudentId, "kc", FutureUtc, -1, default));
    }

    [Fact]
    public async Task UpsertAsync_RemindBeforeMinutesAboveDayLimit_ThrowsInvalidOperation()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).UpsertAsync(w.WorksheetId, w.StudentId, "kc", FutureUtc, 1441, default));
    }

    [Fact]
    public async Task UpsertAsync_WorksheetDoesNotExist_ThrowsInvalidOperation()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).UpsertAsync(worksheetId: 987654, w.StudentId, "kc", FutureUtc, 60, default));
        ex.Message.ShouldContain("bulunamadı");
    }

    [Fact]
    public async Task UpsertAsync_WorksheetNotAssignedAndNoInstance_ThrowsInvalidOperation()
    {
        var w = await SeedAsync(withAssignment: false);
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).UpsertAsync(w.WorksheetId, w.StudentId, "kc", FutureUtc, 60, default));
        ex.Message.ShouldContain("atanmamış");
    }

    [Fact]
    public async Task UpsertAsync_NoAssignmentButStudentHasInstance_Succeeds()
    {
        var w = await SeedAsync(withAssignment: false);
        await using (var seed = _db.NewContext())
        {
            seed.TestInstances.Add(new WorksheetInstance
            {
                WorksheetId = w.WorksheetId, StudentId = w.StudentId,
                Status = WorksheetInstanceStatus.Started, StartTime = DateTime.UtcNow.AddDays(-1),
            });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpsertAsync(w.WorksheetId, w.StudentId, "kc", FutureUtc, 60, default);

        await using var check = _db.NewContext();
        (await check.WorksheetReminders.CountAsync()).ShouldBe(1);
    }

    // ---- new reminder ----

    [Fact]
    public async Task UpsertAsync_NewReminder_PersistsPendingStatusAndKeycloakId()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpsertAsync(w.WorksheetId, w.StudentId, "kc-subject-1", FutureUtc, 45, default);

        await using var check = _db.NewContext();
        var reminder = await check.WorksheetReminders.SingleAsync();
        reminder.Status.ShouldBe(WorksheetReminderStatus.Pending);
        reminder.StudentKeycloakId.ShouldBe("kc-subject-1");
        reminder.RemindBeforeMinutes.ShouldBe(45);
        reminder.HangfireJobId.ShouldBe("job-123");
    }

    // ---- upsert existing ----

    [Fact]
    public async Task UpsertAsync_CalledTwiceForSamePair_UpdatesRowInsteadOfInserting()
    {
        var w = await SeedAsync();
        var first = FutureUtc;
        var second = FutureUtc.AddDays(1);

        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpsertAsync(w.WorksheetId, w.StudentId, "kc", first, 30, default);
        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpsertAsync(w.WorksheetId, w.StudentId, "kc", second, 90, default);

        await using var check = _db.NewContext();
        var reminder = await check.WorksheetReminders.SingleAsync(); // still exactly one row
        reminder.RemindBeforeMinutes.ShouldBe(90);
        reminder.ScheduledFor.ShouldBe(second, tolerance: TimeSpan.FromSeconds(1));
    }

    // ---- delete ----

    [Fact]
    public async Task DeleteAsync_ExistingReminder_MarksCancelledAndClearsJobId()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.WorksheetReminders.Add(new WorksheetReminder
            {
                WorksheetId = w.WorksheetId,
                StudentId = w.StudentId,
                ScheduledFor = FutureUtc,
                RemindBeforeMinutes = 60,
                Status = WorksheetReminderStatus.Pending,
                HangfireJobId = "existing-job",
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            await NewService(ctx).DeleteAsync(w.WorksheetId, w.StudentId, default);

        await using var check = _db.NewContext();
        var reminder = await check.WorksheetReminders.IgnoreQueryFilters().SingleAsync();
        reminder.Status.ShouldBe(WorksheetReminderStatus.Cancelled);
        reminder.HangfireJobId.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_NoReminder_IsNoOp()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        await Should.NotThrowAsync(() => NewService(ctx).DeleteAsync(w.WorksheetId, w.StudentId, default));
        (await ctx.WorksheetReminders.CountAsync()).ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
