using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #700: koç kendi okulunun ya da özel ders verdiği öğrenciyi görür (#696 kuralı), başkası 404. Hedefi öğrenci ve veli
/// görür; not yalnız yazan öğretmenindir.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CoachEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int SchoolTeacherUser = 70001;
    private const int SecondSchoolTeacherUser = 70002;
    private const int OtherSchoolTeacherUser = 70003;
    private const int TutorUser = 70004;
    private const int PendingTutorUser = 70005;
    private const int StudentUser = 70006;
    private const int UnverifiedStudentUser = 70007;
    private const int ParentUser = 70008;

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static DateOnly LocalToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Istanbul));
    private static DateOnly ThisWeek => LocalToday.AddDays(-(((int)LocalToday.DayOfWeek + 6) % 7));

    private sealed record Seeded(int StudentId, int UnverifiedStudentId, int SchoolId, int OtherSchoolId);

    private static string Base(int studentId) => $"/api/teacher/coach/students/{studentId}";

    private Task<HttpClient> TeacherAsync(int userId, int? schoolId) => ClientAsWithSchoolAsync(userId, "Teacher", $"kc-{userId}", schoolId, "Teacher");

    private async Task<Seeded> SeedAsync()
    {
        var (schoolId, otherSchoolId) = await WithDbAsync(async db =>
        {
            var school = new School { Name = "Okul A" };
            var other = new School { Name = "Okul B" };
            db.AddRange(school, other);
            await db.SaveChangesAsync();
            return (school.Id, other.Id);
        });
        await SeedApprovedTeacherAsync(SchoolTeacherUser, schoolId);
        await SeedApprovedTeacherAsync(SecondSchoolTeacherUser, schoolId);
        await SeedApprovedTeacherAsync(OtherSchoolTeacherUser, otherSchoolId);
        await SeedApprovedTeacherAsync(TutorUser, otherSchoolId);
        await SeedApprovedTeacherAsync(PendingTutorUser);

        return await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "8. Sınıf" };
            db.Add(grade);
            await db.SaveChangesAsync();
            var student = new Student { UserId = StudentUser, StudentNumber = "P70006", SchoolName = "s", GradeId = grade.Id, SchoolId = schoolId, SchoolVerifiedAt = DateTime.UtcNow };
            var unverified = new Student { UserId = UnverifiedStudentUser, StudentNumber = "P70007", SchoolName = "s", GradeId = grade.Id, SchoolId = schoolId };
            db.AddRange(student, unverified);
            db.Parents.Add(new Parent { UserId = ParentUser });
            await db.SaveChangesAsync();

            var tutorId = await db.Teachers.Where(t => t.UserId == TutorUser).Select(t => t.Id).SingleAsync();
            var pendingTutorId = await db.Teachers.Where(t => t.UserId == PendingTutorUser).Select(t => t.Id).SingleAsync();
            AddBooking(db, tutorId, student.Id, BookingStatus.Approved);
            AddBooking(db, pendingTutorId, student.Id, BookingStatus.Pending);
            await db.SaveChangesAsync();
            return new Seeded(student.Id, unverified.Id, schoolId, otherSchoolId);
        });
    }

    private static void AddBooking(AppDbContext db, int teacherId, int studentId, BookingStatus status)
    {
        var slot = new TeacherAvailabilitySlot { TeacherId = teacherId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), CreatedAt = DateTime.UtcNow };
        db.Add(new Booking { TeacherId = teacherId, StudentId = studentId, AvailabilitySlot = slot, Status = status, CreatedAt = DateTime.UtcNow });
    }

    [Fact]
    public async Task School_teacher_and_tutor_see_the_student_and_their_relationship()
    {
        var seeded = await SeedAsync();

        var school = await TeacherAsync(SchoolTeacherUser, seeded.SchoolId);
        (await school.GetAsync($"{Base(seeded.StudentId)}/overview")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var schoolList = await school.GetFromJsonAsync<List<CoachStudentDto>>("/api/teacher/coach/students", Json);
        schoolList!.Single(s => s.Id == seeded.StudentId).Relationship.ShouldBe("School");
        schoolList.ShouldNotContain(s => s.Id == seeded.UnverifiedStudentId);

        var tutor = await TeacherAsync(TutorUser, seeded.OtherSchoolId);
        (await tutor.GetAsync($"{Base(seeded.StudentId)}/overview")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var tutorList = await tutor.GetFromJsonAsync<List<CoachStudentDto>>("/api/teacher/coach/students", Json);
        tutorList!.Single(s => s.Id == seeded.StudentId).Relationship.ShouldBe("PrivateLesson");
    }

    [Fact]
    public async Task Teachers_without_a_relationship_get_404_on_every_endpoint()
    {
        var seeded = await SeedAsync();
        var outsiders = new[]
        {
            (await TeacherAsync(OtherSchoolTeacherUser, seeded.OtherSchoolId), seeded.StudentId),
            (await TeacherAsync(PendingTutorUser, null), seeded.StudentId),
            (await TeacherAsync(SchoolTeacherUser, seeded.SchoolId), seeded.UnverifiedStudentId),
        };

        foreach (var (client, studentId) in outsiders)
        {
            (await client.GetAsync($"{Base(studentId)}/overview")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync($"{Base(studentId)}/goals")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync($"{Base(studentId)}/notes")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.PostAsJsonAsync($"{Base(studentId)}/goals", new SaveCoachGoalDto { Title = "Hedef" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.PostAsJsonAsync($"{Base(studentId)}/notes", new SaveCoachNoteDto { Body = "Not" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var otherList = await (await TeacherAsync(OtherSchoolTeacherUser, seeded.OtherSchoolId)).GetFromJsonAsync<List<CoachStudentDto>>("/api/teacher/coach/students", Json);
        otherList!.ShouldNotContain(s => s.Id == seeded.StudentId);
    }

    [Theory]
    [InlineData("Parent", ParentUser)]
    [InlineData("Student", StudentUser)]
    public async Task Other_roles_cannot_use_the_teacher_endpoints(string role, int userId)
    {
        var seeded = await SeedAsync();
        var client = await ClientAsAsync(userId, role, $"kc-{userId}", role);

        (await client.GetAsync($"{Base(seeded.StudentId)}/overview")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{Base(seeded.StudentId)}/notes")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Goal_reaches_the_student_and_parent_but_only_its_author_can_change_it()
    {
        var seeded = await SeedAsync();
        await SeedParentLinkAsync(ParentUser, StudentUser);
        var author = await TeacherAsync(SchoolTeacherUser, seeded.SchoolId);
        var colleague = await TeacherAsync(SecondSchoolTeacherUser, seeded.SchoolId);

        var created = await author.PostAsJsonAsync($"{Base(seeded.StudentId)}/goals", new SaveCoachGoalDto { Title = "40 paragraf sorusu <script>x()</script>" });
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var goal = (await created.Content.ReadFromJsonAsync<CoachGoalDto>(Json))!;
        goal.Title.ShouldBe("40 paragraf sorusu ");
        goal.WeekStart.ShouldBe(ThisWeek);

        var student = await ClientAsAsync(StudentUser, "Student", $"kc-{StudentUser}", "Student");
        (await student.GetFromJsonAsync<List<StudentCoachGoalDto>>("/api/student/me/coach-goals", Json))!.Single().Title.ShouldBe(goal.Title);

        var parent = await ClientAsAsync(ParentUser, "Parent", $"kc-{ParentUser}", "Parent");
        var summary = await parent.GetFromJsonAsync<ParentCoachSummaryDto>($"/api/parent/children/{seeded.StudentId}/coach-summary", Json);
        summary!.Goals.Single().Title.ShouldBe(goal.Title);

        (await colleague.GetFromJsonAsync<List<CoachGoalDto>>($"{Base(seeded.StudentId)}/goals", Json))!.ShouldBeEmpty();
        (await colleague.PutAsJsonAsync($"{Base(seeded.StudentId)}/goals/{goal.Id}", new SaveCoachGoalDto { Title = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await colleague.DeleteAsync($"{Base(seeded.StudentId)}/goals/{goal.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await author.DeleteAsync($"{Base(seeded.StudentId)}/goals/{goal.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await student.GetFromJsonAsync<List<StudentCoachGoalDto>>("/api/student/me/coach-goals", Json))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Notes_are_private_to_the_teacher_who_wrote_them()
    {
        var seeded = await SeedAsync();
        await SeedParentLinkAsync(ParentUser, StudentUser);
        var author = await TeacherAsync(SchoolTeacherUser, seeded.SchoolId);
        var colleague = await TeacherAsync(SecondSchoolTeacherUser, seeded.SchoolId);
        await author.PostAsJsonAsync($"{Base(seeded.StudentId)}/goals", new SaveCoachGoalDto { Title = "Hedef" });

        var created = await author.PostAsJsonAsync($"{Base(seeded.StudentId)}/notes", new SaveCoachNoteDto { Body = "GIZLI-KOC-NOTU" });
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var note = (await created.Content.ReadFromJsonAsync<CoachNoteDto>(Json))!;

        (await author.GetFromJsonAsync<List<CoachNoteDto>>($"{Base(seeded.StudentId)}/notes", Json))!.Single().Body.ShouldBe("GIZLI-KOC-NOTU");
        (await colleague.GetFromJsonAsync<List<CoachNoteDto>>($"{Base(seeded.StudentId)}/notes", Json))!.ShouldBeEmpty();
        (await colleague.PutAsJsonAsync($"{Base(seeded.StudentId)}/notes/{note.Id}", new SaveCoachNoteDto { Body = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await colleague.DeleteAsync($"{Base(seeded.StudentId)}/notes/{note.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await colleague.GetStringAsync($"{Base(seeded.StudentId)}/overview")).ShouldNotContain("GIZLI");

        var student = await ClientAsAsync(StudentUser, "Student", $"kc-{StudentUser}", "Student");
        (await student.GetStringAsync("/api/student/me/coach-goals")).ShouldNotContain("GIZLI");
        var parent = await ClientAsAsync(ParentUser, "Parent", $"kc-{ParentUser}", "Parent");
        (await parent.GetStringAsync($"/api/parent/children/{seeded.StudentId}/coach-summary")).ShouldNotContain("GIZLI");

        var updated = await author.PutAsJsonAsync($"{Base(seeded.StudentId)}/notes/{note.Id}", new SaveCoachNoteDto { Body = "Yeni not" });
        (await updated.Content.ReadFromJsonAsync<CoachNoteDto>(Json))!.Body.ShouldBe("Yeni not");
        (await author.DeleteAsync($"{Base(seeded.StudentId)}/notes/{note.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await WithDbAsync(async db => (await db.CoachNotes.CountAsync()).ShouldBe(0));
    }

    [Fact]
    public async Task Goal_or_note_of_another_student_cannot_be_reached_through_a_student_the_teacher_coaches()
    {
        var seeded = await SeedAsync();
        var secondStudentId = await WithDbAsync(async db =>
        {
            var gradeId = await db.Students.Where(s => s.Id == seeded.StudentId).Select(s => s.GradeId).SingleAsync();
            var second = new Student { UserId = 70009, StudentNumber = "P70009", SchoolName = "s", GradeId = gradeId, SchoolId = seeded.SchoolId, SchoolVerifiedAt = DateTime.UtcNow };
            db.Add(second);
            await db.SaveChangesAsync();
            return second.Id;
        });
        var client = await TeacherAsync(SchoolTeacherUser, seeded.SchoolId);
        var goal = await (await client.PostAsJsonAsync($"{Base(seeded.StudentId)}/goals", new SaveCoachGoalDto { Title = "Hedef" })).Content.ReadFromJsonAsync<CoachGoalDto>(Json);
        var note = await (await client.PostAsJsonAsync($"{Base(seeded.StudentId)}/notes", new SaveCoachNoteDto { Body = "Not" })).Content.ReadFromJsonAsync<CoachNoteDto>(Json);

        (await client.PutAsJsonAsync($"{Base(secondStudentId)}/goals/{goal!.Id}", new SaveCoachGoalDto { Title = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"{Base(secondStudentId)}/goals/{goal.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"{Base(secondStudentId)}/notes/{note!.Id}", new SaveCoachNoteDto { Body = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"{Base(secondStudentId)}/notes/{note.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<List<CoachNoteDto>>($"{Base(secondStudentId)}/notes", Json))!.ShouldBeEmpty();
        await WithDbAsync(async db => (await db.CoachNotes.SingleAsync()).Body.ShouldBe("Not"));
    }

    [Fact]
    public async Task Invalid_goals_are_rejected()
    {
        var seeded = await SeedAsync();
        var client = await TeacherAsync(SchoolTeacherUser, seeded.SchoolId);
        var url = $"{Base(seeded.StudentId)}/goals";

        (await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = "x", WeekStart = ThisWeek.AddDays(1) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = "x", WeekStart = ThisWeek.AddDays(-7) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = "x", WeekStart = ThisWeek.AddDays(35) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = "<script>x()</script>" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = new string('a', CoachWeeklyGoal.MaxTitleLength + 1) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = "<a href=\"https://example.com\">tıkla</a>" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = new string('&', CoachWeeklyGoal.MaxTitleLength) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"{Base(seeded.StudentId)}/notes", new SaveCoachNoteDto { Body = new string('&', CoachNote.MaxBodyLength) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var plain = await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = "2 < 3 & 4" });
        plain.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await plain.Content.ReadFromJsonAsync<CoachGoalDto>(Json))!.Title.ShouldBe("2 &lt; 3 &amp; 4");

        var nextWeek = await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = "Gelecek hafta", WeekStart = ThisWeek.AddDays(7) });
        nextWeek.StatusCode.ShouldBe(HttpStatusCode.OK);

        for (var i = 0; i < 9; i++)
            (await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = $"Hedef {i}" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(url, new SaveCoachGoalDto { Title = "Fazla" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Overview_counts_this_weeks_planned_tests()
    {
        var seeded = await SeedAsync();
        await WithDbAsync(async db =>
        {
            var gradeId = await db.Students.Where(s => s.Id == seeded.StudentId).Select(s => s.GradeId!.Value).SingleAsync();
            var done = new Worksheet { Name = "Biten", Description = "", GradeId = gradeId };
            var open = new Worksheet { Name = "Bekleyen", Description = "", GradeId = gradeId };
            var cancelled = new Worksheet { Name = "İptal", Description = "", GradeId = gradeId };
            db.AddRange(done, open, cancelled);
            await db.SaveChangesAsync();
            var at = DateTime.UtcNow;
            db.WorksheetReminders.AddRange(
                new WorksheetReminder { WorksheetId = done.Id, StudentId = seeded.StudentId, ScheduledFor = at, Status = WorksheetReminderStatus.Sent },
                new WorksheetReminder { WorksheetId = open.Id, StudentId = seeded.StudentId, ScheduledFor = at, Status = WorksheetReminderStatus.Pending },
                new WorksheetReminder { WorksheetId = cancelled.Id, StudentId = seeded.StudentId, ScheduledFor = at, Status = WorksheetReminderStatus.Cancelled });
            db.TestInstances.Add(new WorksheetInstance { StudentId = seeded.StudentId, WorksheetId = done.Id, StartTime = at.AddMinutes(-30), EndTime = at, Status = WorksheetInstanceStatus.Completed });
            await db.SaveChangesAsync();
        });
        var client = await TeacherAsync(SchoolTeacherUser, seeded.SchoolId);

        var overview = await client.GetFromJsonAsync<CoachOverviewDto>($"{Base(seeded.StudentId)}/overview", Json);

        (overview!.Plan.Planned, overview.Plan.Completed, overview.Plan.WeekStart).ShouldBe((2, 1, ThisWeek));
        overview.ActiveMistakeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Parent_reads_a_linked_child_with_an_audit_row_and_others_get_404()
    {
        var seeded = await SeedAsync();
        await SeedParentLinkAsync(ParentUser, StudentUser);
        var client = await ClientAsAsync(ParentUser, "Parent", $"kc-{ParentUser}", "Parent");

        (await client.GetAsync($"/api/parent/children/{seeded.StudentId}/coach-summary")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/parent/children/{seeded.UnverifiedStudentId}/coach-summary")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await WithDbAsync(async db =>
            (await db.ParentAccessAudits.AsNoTracking().CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildCoachSummary)).ShouldBe(1));
    }
}
