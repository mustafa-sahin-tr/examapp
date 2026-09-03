using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

public class WorksheetReminderDispatcherTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private static WorksheetReminderDispatcher NewService(AppDbContext ctx) =>
        new(ctx, NullLogger<WorksheetReminderDispatcher>.Instance);

    private static readonly string DueEventType = OutboxEventRegistry.NameFor<WorksheetReminderDueEvent>();

    private async Task<int> SeedReminderAsync(WorksheetReminderStatus status, int userId = 77)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "8" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = userId, StudentNumber = "n", SchoolName = "s", GradeId = grade.Id };
        var ws = new Worksheet { Name = "Sınav", Description = "d", GradeId = grade.Id };
        ctx.AddRange(student, ws);
        await ctx.SaveChangesAsync();

        var reminder = new WorksheetReminder
        {
            WorksheetId = ws.Id,
            StudentId = student.Id,
            ScheduledFor = DateTime.UtcNow.AddHours(3),
            RemindBeforeMinutes = 60,
            Status = status,
            StudentKeycloakId = "kc-sub",
        };
        ctx.WorksheetReminders.Add(reminder);
        await ctx.SaveChangesAsync();
        return reminder.Id;
    }

    [Fact]
    public async Task DispatchAsync_ReminderNotFound_IsNoOp()
    {
        await SeedReminderAsync(WorksheetReminderStatus.Pending);
        await using var ctx = _db.NewContext();

        await Should.NotThrowAsync(() => NewService(ctx).DispatchAsync(reminderId: 999999, null, default));
        (await ctx.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DispatchAsync_ReminderAlreadySent_DoesNotWriteOutbox()
    {
        var id = await SeedReminderAsync(WorksheetReminderStatus.Sent);
        await using (var ctx = _db.NewContext())
            await NewService(ctx).DispatchAsync(id, null, default);

        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DispatchAsync_ReminderCancelled_DoesNotWriteOutbox()
    {
        var id = await SeedReminderAsync(WorksheetReminderStatus.Cancelled);
        await using (var ctx = _db.NewContext())
            await NewService(ctx).DispatchAsync(id, null, default);

        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DispatchAsync_PendingReminder_WritesDueEventToOutboxAndMarksSent()
    {
        var id = await SeedReminderAsync(WorksheetReminderStatus.Pending, userId: 4242);

        await using (var ctx = _db.NewContext())
            await NewService(ctx).DispatchAsync(id, null, default);

        await using var check = _db.NewContext();

        var outbox = await check.OutboxMessages.SingleAsync();
        outbox.Type.ShouldBe(DueEventType);

        var payload = JsonSerializer.Deserialize<WorksheetReminderDueEvent>(outbox.Content)!;
        payload.ReminderId.ShouldBe(id);
        payload.UserId.ShouldBe(4242);
        payload.UserKeycloakId.ShouldBe("kc-sub");

        (await check.WorksheetReminders.SingleAsync(r => r.Id == id)).Status.ShouldBe(WorksheetReminderStatus.Sent);
    }

    public void Dispose() => _db.Dispose();
}
