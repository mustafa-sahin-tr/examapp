using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573: etkinlik sayfasının atama akışı — öğretmen atar, öğrencinin aktif atamaları (<c>kind</c> filtresi: yok = hepsi),
/// öğretmenin atama özeti (<c>worksheetKind</c>, ilk kontrol "7 doğru · 3 yanlış" sayaçları; testte / kontrolsüzde null) ve
/// takvim (<c>worksheetKind</c>, yalnız worksheet tabanlı olaylarda). Uçtan uca: atama → aktif liste → takvim.
/// </summary>
public class WorksheetKindAssignmentFlowTests : IDisposable
{
    private const int Owner = 4201;
    private const int StudentUserId = 4202;
    private const string StudentKeycloakId = "kc-4202";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static WorksheetAssignmentService NewAssignments(AppDbContext ctx) => new(ctx, new SchoolAccessPolicy(ctx));

    private sealed record World(int GradeId, int SchoolId, int StudentId, int TestWorksheetId, int ActivityWorksheetId);

    /// <summary>Okullu öğrenci + öğretmen; bir Test ve bir Activity sayfası (ikisi de öğretmenin).</summary>
    private async Task<World> SeedWorldAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(Owner);
        var grade = new Grade { Name = "6" };
        var school = new School { Name = "Okul" };
        ctx.AddRange(grade, school);
        await ctx.SaveChangesAsync();

        var student = new Student
        {
            UserId = StudentUserId, StudentNumber = "n", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id,
        };
        var test = new Worksheet { Name = "Test", Description = "", GradeId = grade.Id, Kind = WorksheetKind.Test, CommentsEnabled = true };
        var activity = new Worksheet { Name = "Etkinlik", Description = "", GradeId = grade.Id, Kind = WorksheetKind.Activity, CommentsEnabled = true };
        ctx.AddRange(student, test, activity, new Teacher { UserId = Owner, SchoolId = school.Id });
        await ctx.SaveChangesAsync();

        return new World(grade.Id, school.Id, student.Id, test.Id, activity.Id);
    }

    private async Task AssignAsync(int worksheetId, int studentId)
    {
        await using var ctx = _db.NewContext();
        var r = await NewAssignments(ctx).AssignAsUserAsync(ctx, new WorksheetAssignmentRequestDto
        {
            WorksheetId = worksheetId,
            StudentId = studentId,
            // Sabit geçmiş tarih: atama aktif, zamana bağlı kırılganlık yok.
            StartAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        }, Owner);
        r.Success.ShouldBeTrue(r.Message);
    }

    private async Task<List<AssignedWorksheetDto>> ActiveAsync(World w, WorksheetKind? kind = null)
    {
        await using var ctx = _db.NewContext();
        return await NewAssignments(ctx).GetActiveAssignmentsForStudentAsync(
            new StudentProfileDto { Id = w.StudentId, GradeId = w.GradeId }, kind);
    }

    private async Task<int> AddInstanceAsync(int studentId, int worksheetId, int? checkCount = null, int? firstCorrect = null, int? firstTotal = null)
    {
        await using var ctx = _db.NewContext();
        var instance = new WorksheetInstance
        {
            StudentId = studentId,
            WorksheetId = worksheetId,
            Status = WorksheetInstanceStatus.Started,
            StartTime = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            MaxDurationSeconds = 0,
            CheckCount = checkCount ?? 0,
            FirstCheckedAt = checkCount is > 0 ? new DateTime(2020, 1, 2, 0, 1, 0, DateTimeKind.Utc) : null,
            FirstCheckCorrectCount = firstCorrect,
            FirstCheckTotalCount = firstTotal,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();
        return instance.Id;
    }

    // ---------------------------------------------------------------- aktif atamalar: kind

    [Fact]
    public async Task GetActiveAssignmentsForStudentAsync_ActivityAssigned_ReturnsItWithKindActivity()
    {
        var w = await SeedWorldAsync();
        await AssignAsync(w.ActivityWorksheetId, w.StudentId);

        var items = await ActiveAsync(w);

        items.ShouldHaveSingleItem().Kind.ShouldBe(WorksheetKind.Activity);
    }

    [Fact]
    public async Task GetActiveAssignmentsForStudentAsync_KindOmitted_ReturnsTestsAndActivities()
    {
        var w = await SeedWorldAsync();
        await AssignAsync(w.TestWorksheetId, w.StudentId);
        await AssignAsync(w.ActivityWorksheetId, w.StudentId);

        var items = await ActiveAsync(w, kind: null);

        items.Count.ShouldBe(2);
        items.Select(i => i.Kind).OrderBy(k => k).ShouldBe(new[] { WorksheetKind.Test, WorksheetKind.Activity }.OrderBy(k => k));
    }

    [Theory]
    [InlineData(WorksheetKind.Test, "Test")]
    [InlineData(WorksheetKind.Activity, "Etkinlik")]
    public async Task GetActiveAssignmentsForStudentAsync_KindFilter_ReturnsOnlyThatKind(WorksheetKind kind, string expectedName)
    {
        var w = await SeedWorldAsync();
        await AssignAsync(w.TestWorksheetId, w.StudentId);
        await AssignAsync(w.ActivityWorksheetId, w.StudentId);

        var items = await ActiveAsync(w, kind);

        var only = items.ShouldHaveSingleItem();
        only.Kind.ShouldBe(kind);
        only.Name.ShouldBe(expectedName);
    }

    // ---------------------------------------------------------------- öğretmen atama özeti

    [Fact]
    public async Task GetWorksheetAssignmentsForTeacherAsync_ActivityWorksheet_ReturnsKindAndFirstCheckCounts()
    {
        var w = await SeedWorldAsync();
        await AssignAsync(w.ActivityWorksheetId, w.StudentId);
        var instanceId = await AddInstanceAsync(w.StudentId, w.ActivityWorksheetId, checkCount: 2, firstCorrect: 7, firstTotal: 10);

        await using var ctx = _db.NewContext();
        var view = await NewAssignments(ctx).GetWorksheetAssignmentsForTeacherAsync(w.ActivityWorksheetId, SchoolScope.Unrestricted(Owner));

        view.WorksheetKind.ShouldBe(WorksheetKind.Activity);
        var row = view.Assignments.ShouldHaveSingleItem().Students.ShouldHaveSingleItem();
        row.InstanceId.ShouldBe(instanceId);
        row.FirstCheckCorrectCount.ShouldBe(7);
        row.FirstCheckTotalCount.ShouldBe(10);
    }

    [Fact]
    public async Task GetWorksheetAssignmentsForTeacherAsync_ActivityWithoutCheck_FirstCheckCountsAreNull()
    {
        var w = await SeedWorldAsync();
        await AssignAsync(w.ActivityWorksheetId, w.StudentId);
        await AddInstanceAsync(w.StudentId, w.ActivityWorksheetId, checkCount: 0);

        await using var ctx = _db.NewContext();
        var view = await NewAssignments(ctx).GetWorksheetAssignmentsForTeacherAsync(w.ActivityWorksheetId, SchoolScope.Unrestricted(Owner));

        var row = view.Assignments.ShouldHaveSingleItem().Students.ShouldHaveSingleItem();
        row.FirstCheckCorrectCount.ShouldBeNull();
        row.FirstCheckTotalCount.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetAssignmentsForTeacherAsync_TestWorksheet_KindIsTestAndFirstCheckCountsAreNull()
    {
        var w = await SeedWorldAsync();
        await AssignAsync(w.TestWorksheetId, w.StudentId);
        // Test oturumuna (kontrol kavramı yok) yanlışlıkla yazılmış sayaçlar bile yanıta taşınmaz.
        await AddInstanceAsync(w.StudentId, w.TestWorksheetId, checkCount: 0, firstCorrect: 4, firstTotal: 5);

        await using var ctx = _db.NewContext();
        var view = await NewAssignments(ctx).GetWorksheetAssignmentsForTeacherAsync(w.TestWorksheetId, SchoolScope.Unrestricted(Owner));

        view.WorksheetKind.ShouldBe(WorksheetKind.Test);
        var row = view.Assignments.ShouldHaveSingleItem().Students.ShouldHaveSingleItem();
        row.FirstCheckCorrectCount.ShouldBeNull();
        row.FirstCheckTotalCount.ShouldBeNull();
    }

    // ---------------------------------------------------------------- uçtan uca: atama → aktif liste → takvim

    [Fact]
    public async Task ActivityAssignmentFlow_AssignThenActiveListThenCalendar_CarriesKindOne()
    {
        var w = await SeedWorldAsync();
        // Aktif atama için bitiş gelecekte olmalı (EndAt < şimdi ise atama aktif listede değildir); sabit uzak tarih.
        var deadline = new DateTime(2099, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = _db.NewContext())
        {
            var r = await NewAssignments(ctx).AssignAsUserAsync(ctx, new WorksheetAssignmentRequestDto
            {
                WorksheetId = w.ActivityWorksheetId,
                StudentId = w.StudentId,
                StartAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndAt = deadline,
            }, Owner);
            r.Success.ShouldBeTrue(r.Message);
        }

        var active = await ActiveAsync(w, WorksheetKind.Activity);
        active.ShouldHaveSingleItem().Kind.ShouldBe(WorksheetKind.Activity);

        await using var calendarCtx = _db.NewContext();
        var calendar = await new WorksheetCalendarService(calendarCtx, Substitute.For<IAuthApiClient>()).GetMyCalendarAsync(
            w.StudentId, StudentKeycloakId, null, null,
            new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2099, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        var ev = calendar.Events.Single(e => e.Kind == "assignment-deadline");
        ev.WorksheetKind.ShouldBe(WorksheetKind.Activity);
        ev.WorksheetId.ShouldBe(w.ActivityWorksheetId);
    }
}
