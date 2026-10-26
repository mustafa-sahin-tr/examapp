using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573: takvim olaylarında <c>worksheetKind</c> — yalnız worksheet tabanlı olaylarda (reminder, assignment-deadline)
/// dolu (0 = Test, 1 = Activity); program sayfası ve diğer olaylarda null. <c>Kind</c> (olay tipi string'i) ile karışmaz.
/// Sabit tarihli aralık kullanılır (zamana bağlı değil); hatırlatmalar <c>Pending</c> (Sent geriye dönük pencereye bağlıdır).
/// </summary>
public class WorksheetKindCalendarTests : IDisposable
{
    private const int StudentUserId = 4301;
    private const string KeycloakId = "kc-4301";

    private static readonly DateTime From = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedStudentAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var student = new Student { UserId = StudentUserId, StudentNumber = "S4301", SchoolName = "S", GradeId = grade.Id };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        return student.Id;
    }

    private async Task<int> SeedWorksheetAsync(string name, WorksheetKind kind)
    {
        await using var ctx = _db.NewContext();
        var grade = await ctx.Grades.FirstAsync();
        var ws = new Worksheet { Name = name, Description = "", GradeId = grade.Id, Kind = kind };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    private async Task AddReminderAsync(int studentId, int worksheetId, DateTime scheduledFor)
    {
        await using var ctx = _db.NewContext();
        ctx.WorksheetReminders.Add(new WorksheetReminder
        {
            WorksheetId = worksheetId,
            StudentId = studentId,
            ScheduledFor = scheduledFor,
            RemindBeforeMinutes = 60,
            Status = WorksheetReminderStatus.Pending,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task AddAssignmentAsync(int studentId, int worksheetId, DateTime endAt)
    {
        await using var ctx = _db.NewContext();
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheetId,
            StudentId = studentId,
            StartAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            EndAt = endAt,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<StudentCalendarResponseDto> CalendarAsync(int studentId)
    {
        await using var ctx = _db.NewContext();
        return await new WorksheetCalendarService(ctx, Substitute.For<IAuthApiClient>())
            .GetMyCalendarAsync(studentId, KeycloakId, null, null, From, To, CancellationToken.None);
    }

    [Fact]
    public async Task GetMyCalendarAsync_ReminderForActivity_CarriesWorksheetKindActivity()
    {
        var studentId = await SeedStudentAsync();
        var wsId = await SeedWorksheetAsync("Etkinlik", WorksheetKind.Activity);
        await AddReminderAsync(studentId, wsId, new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc));

        var ev = (await CalendarAsync(studentId)).Events.ShouldHaveSingleItem();

        ev.Kind.ShouldBe("reminder");
        ev.WorksheetKind.ShouldBe(WorksheetKind.Activity);
    }

    [Fact]
    public async Task GetMyCalendarAsync_ReminderForTest_CarriesWorksheetKindTest()
    {
        var studentId = await SeedStudentAsync();
        var wsId = await SeedWorksheetAsync("Test", WorksheetKind.Test);
        await AddReminderAsync(studentId, wsId, new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc));

        var ev = (await CalendarAsync(studentId)).Events.ShouldHaveSingleItem();

        ev.Kind.ShouldBe("reminder");
        ev.WorksheetKind.ShouldBe(WorksheetKind.Test);
    }

    [Fact]
    public async Task GetMyCalendarAsync_AssignmentDeadlineForActivity_CarriesWorksheetKindActivity()
    {
        var studentId = await SeedStudentAsync();
        var wsId = await SeedWorksheetAsync("Etkinlik", WorksheetKind.Activity);
        await AddAssignmentAsync(studentId, wsId, endAt: new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc));

        var ev = (await CalendarAsync(studentId)).Events.Single(e => e.Kind == "assignment-deadline");

        ev.WorksheetKind.ShouldBe(WorksheetKind.Activity);
        ev.IsCompleted.ShouldBe(false); // hiç oturum yok — tür alanı tamamlanma kuralını değiştirmez
    }

    [Fact]
    public async Task GetMyCalendarAsync_AssignmentDeadlineForTest_CarriesWorksheetKindTest()
    {
        var studentId = await SeedStudentAsync();
        var wsId = await SeedWorksheetAsync("Test", WorksheetKind.Test);
        await AddAssignmentAsync(studentId, wsId, endAt: new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc));

        var ev = (await CalendarAsync(studentId)).Events.Single(e => e.Kind == "assignment-deadline");

        ev.WorksheetKind.ShouldBe(WorksheetKind.Test);
    }

    [Fact]
    public async Task GetMyCalendarAsync_ReminderAndDeadlineOfSameActivity_BothCarryKindActivity()
    {
        var studentId = await SeedStudentAsync();
        var wsId = await SeedWorksheetAsync("Etkinlik", WorksheetKind.Activity);
        await AddReminderAsync(studentId, wsId, new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc));
        await AddAssignmentAsync(studentId, wsId, endAt: new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc));

        var events = (await CalendarAsync(studentId)).Events;

        events.Where(e => e.Kind is "reminder" or "assignment-deadline")
            .ShouldAllBe(e => e.WorksheetKind == WorksheetKind.Activity);
    }
}
