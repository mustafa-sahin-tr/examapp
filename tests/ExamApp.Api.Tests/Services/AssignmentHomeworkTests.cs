using NSubstitute;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #1283 (#1071 S8): ödev olarak verilen test / etkinlik atamaları — bayrak doğrulaması, öğrenci listesinde otomatik durum
/// (oturum bitince tamamlandı, teslimde bitmediyse gecikmiş, yeniden çözülen etkinlik tamamlandı kalır), görünürlük ve öğretmen
/// sayıları. Veli gecikme bildirimi: <c>ParentNotificationProducersTests.Sweep_notifies_parents_for_overdue_assignment_homework</c>.
/// </summary>
public class AssignmentHomeworkTests : IDisposable
{
    private const int TeacherUser = 500;
    private const int OtherTeacherUser = 501;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public AssignmentHomeworkTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>()
                .Select(id => new UserLookupResultDto { Id = id, FullName = $"Ad{id} Soyad", KeycloakId = $"kc-{id}" })
                .ToList());
    }

    public void Dispose() => _db.Dispose();

    private sealed record World(int SchoolId, int GradeId, int Test, int Activity, int Other, int S1, int S2, int OtherSchoolStudent);

    // Öğrenci userId'leri: S1 = 1, S2 = 2, başka okul = 3.
    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var g = new Grade { Name = "6. Sınıf" };
        var school = new School { Name = "Okul" };
        var otherSchool = new School { Name = "Öbür Okul" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(g, school, otherSchool, subject);
        await ctx.SaveChangesAsync();
        ctx.SetCurrentUser(TeacherUser);
        var test = new Worksheet { Name = "Kesirler", Description = "", GradeId = g.Id, SubjectId = subject.Id };
        var activity = new Worksheet { Name = "Bulmaca", Description = "", GradeId = g.Id, SubjectId = subject.Id, Kind = WorksheetKind.Activity };
        var other = new Worksheet { Name = "Serbest", Description = "", GradeId = g.Id, SubjectId = subject.Id };
        ctx.Worksheets.AddRange(test, activity, other);
        var now = DateTime.UtcNow;
        Student S(int userId, int schoolId) => new()
        {
            UserId = userId, StudentNumber = $"n{userId}", SchoolName = "s", SchoolId = schoolId, SchoolVerifiedAt = now, GradeId = g.Id
        };
        var s1 = S(1, school.Id);
        var s2 = S(2, school.Id);
        var s3 = S(3, otherSchool.Id);
        ctx.Students.AddRange(s1, s2, s3);
        await ctx.SaveChangesAsync();
        return new World(school.Id, g.Id, test.Id, activity.Id, other.Id, s1.Id, s2.Id, s3.Id);
    }

    private async Task<int> AssignAsync(int worksheetId, DateTime? endAt, bool homework = true, int? studentId = null, int? gradeId = null,
        int? schoolId = null, int teacher = TeacherUser, DateTime? startAt = null)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(teacher);
        var a = new WorksheetAssignment
        {
            WorksheetId = worksheetId, StudentId = studentId, GradeId = gradeId, SchoolId = schoolId,
            StartAt = startAt ?? DateTime.UtcNow.AddDays(-3), EndAt = endAt, IsHomework = homework
        };
        ctx.WorksheetAssignments.Add(a);
        await ctx.SaveChangesAsync();
        return a.Id;
    }

    private async Task InstanceAsync(int studentId, int worksheetId, WorksheetInstanceStatus status, DateTime? firstCompletedAt = null)
    {
        await using var ctx = _db.NewContext();
        ctx.TestInstances.Add(new WorksheetInstance
        {
            StudentId = studentId, WorksheetId = worksheetId, Status = status, StartTime = DateTime.UtcNow.AddHours(-2),
            EndTime = status == WorksheetInstanceStatus.Started ? null : DateTime.UtcNow.AddHours(-1),
            FirstCompletedAt = firstCompletedAt
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<ExamApp.Api.Models.Dtos.Admin.StudentAssignmentHomeworkDto>> StudentListAsync(int studentUserId)
    {
        await using var ctx = _db.NewContext();
        return (await new AssignmentHomeworkService(ctx, new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx)), _authApi)
            .ListForStudentAsync(studentUserId))!;
    }

    [Fact]
    public async Task Student_list_resolves_status_from_the_session_automatically()
    {
        var w = await SeedAsync();
        await AssignAsync(w.Test, DateTime.UtcNow.AddDays(2), gradeId: w.GradeId, schoolId: w.SchoolId);
        await AssignAsync(w.Activity, DateTime.UtcNow.AddHours(-1), gradeId: w.GradeId, schoolId: w.SchoolId);

        var before = await StudentListAsync(1);
        before.Select(h => (h.WorksheetName, h.Status)).ShouldBe([("Bulmaca", "Overdue"), ("Kesirler", "Pending")]);
        before[1].TeacherName.ShouldBe($"Ad{TeacherUser} S.");
        before[1].SubjectName.ShouldBe("Matematik");
        before[0].WorksheetKind.ShouldBe("Activity");

        await InstanceAsync(w.S1, w.Test, WorksheetInstanceStatus.Started);
        var started = (await StudentListAsync(1)).Single(h => h.WorksheetId == w.Test);
        started.Status.ShouldBe("Pending");
        started.InProgress.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            var ti = ctx.TestInstances.Single(i => i.StudentId == w.S1 && i.WorksheetId == w.Test);
            ti.Status = WorksheetInstanceStatus.Completed;
            ti.EndTime = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }

        var done = (await StudentListAsync(1)).Single(h => h.WorksheetId == w.Test);
        done.Status.ShouldBe("Completed");
        done.CompletedAt.ShouldNotBeNull();
        (await StudentListAsync(2)).Single(h => h.WorksheetId == w.Test).Status.ShouldBe("Pending"); // diğer öğrenci etkilenmez
    }

    [Fact]
    public async Task Retaking_an_activity_that_was_completed_once_stays_completed()
    {
        var w = await SeedAsync();
        await AssignAsync(w.Activity, DateTime.UtcNow.AddHours(-1), studentId: w.S1);
        await InstanceAsync(w.S1, w.Activity, WorksheetInstanceStatus.Started, firstCompletedAt: DateTime.UtcNow.AddHours(-3));

        (await StudentListAsync(1)).ShouldHaveSingleItem().Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task Only_visible_started_homework_assignments_within_the_window_are_listed()
    {
        var w = await SeedAsync();
        await AssignAsync(w.Other, DateTime.UtcNow.AddDays(2), homework: false, gradeId: w.GradeId, schoolId: w.SchoolId); // ödev değil
        await AssignAsync(w.Test, DateTime.UtcNow.AddDays(-40), gradeId: w.GradeId, schoolId: w.SchoolId); // pencere dışı
        await AssignAsync(w.Activity, DateTime.UtcNow.AddDays(5), gradeId: w.GradeId, schoolId: w.SchoolId,
            startAt: DateTime.UtcNow.AddDays(1)); // henüz başlamadı
        var direct = await AssignAsync(w.Test, DateTime.UtcNow.AddDays(1), studentId: w.S2);

        (await StudentListAsync(1)).ShouldBeEmpty();
        (await StudentListAsync(2)).ShouldHaveSingleItem().AssignmentId.ShouldBe(direct);
        (await StudentListAsync(3)).ShouldBeEmpty(); // başka okul
    }

    [Fact]
    public async Task Student_list_is_null_for_a_non_student()
    {
        await SeedAsync();
        (await StudentListAsyncOrNull(TeacherUser)).ShouldBeNull();
    }

    private async Task<IReadOnlyList<ExamApp.Api.Models.Dtos.Admin.StudentAssignmentHomeworkDto>?> StudentListAsyncOrNull(int userId)
    {
        await using var ctx = _db.NewContext();
        return await new AssignmentHomeworkService(ctx, new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx)), _authApi)
            .ListForStudentAsync(userId);
    }

    [Fact]
    public async Task Teacher_list_shows_own_homework_with_progress_counts()
    {
        var w = await SeedAsync();
        var grade = await AssignAsync(w.Test, DateTime.UtcNow.AddMinutes(-30), gradeId: w.GradeId, schoolId: w.SchoolId);
        var single = await AssignAsync(w.Activity, DateTime.UtcNow.AddDays(3), studentId: w.S2);
        await AssignAsync(w.Other, DateTime.UtcNow.AddDays(3), homework: false, gradeId: w.GradeId, schoolId: w.SchoolId);
        await AssignAsync(w.Other, DateTime.UtcNow.AddDays(4), studentId: w.S1, teacher: OtherTeacherUser);
        await InstanceAsync(w.S1, w.Test, WorksheetInstanceStatus.Completed);

        await using var ctx = _db.NewContext();
        var list = await new AssignmentHomeworkService(ctx, new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx)), _authApi)
            .ListForTeacherAsync(SchoolScope.For(TeacherUser, w.SchoolId));

        list.Select(h => h.AssignmentId).ShouldBe([grade, single]);
        var g = list[0];
        (g.WorksheetName, g.TargetType, g.IsPastDue, g.StudentCount, g.CompletedCount, g.OverdueCount)
            .ShouldBe(("Kesirler", "Grade", true, 2, 1, 1));
        var s = list[1];
        (s.WorksheetKind, s.TargetType, s.IsPastDue, s.StudentCount, s.NotStartedCount).ShouldBe(("Activity", "Student", false, 1, 1));
    }

    [Theory]
    [InlineData(false, false)] // teslim tarihi yok
    [InlineData(true, true)] // admin platform geneli sınıf ataması
    public async Task Homework_flag_requires_a_due_date_and_is_not_for_platform_wide_assignments(bool withEnd, bool admin)
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx)).AssignWorksheetAsync(
            new WorksheetAssignmentRequestDto
            {
                WorksheetId = w.Test, GradeId = w.GradeId, StartAt = DateTime.UtcNow,
                EndAt = withEnd ? DateTime.UtcNow.AddDays(2) : null, IsHomework = true
            },
            admin ? SchoolScope.Unrestricted(1) : SchoolScope.For(TeacherUser, w.SchoolId));

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("teslim");
        ctx.WorksheetAssignments.ShouldBeEmpty();
    }
}
