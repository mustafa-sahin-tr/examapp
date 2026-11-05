using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>Issue #696: öğretmen kendi okulunun ya da özel ders verdiği öğrenciyi görür, başkasını 404; veli erişimi kayda geçer.</summary>
[Collection(IntegrationCollection.Name)]
public class LearningOutcomeMasteryEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int SchoolTeacherUser = 69601;
    private const int OtherSchoolTeacherUser = 69602;
    private const int TutorUser = 69603;
    private const int PendingTutorUser = 69604;
    private const int StudentUser = 69605;
    private const int UnverifiedStudentUser = 69606;
    private const int ParentUser = 69607;

    private sealed record Seeded(int StudentId, int UnverifiedStudentId, int OutcomeId, int SchoolId, int OtherSchoolId);

    private static string TeacherUrl(int studentId) => $"/api/teacher/students/{studentId}/learning-outcome-mastery";

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
        await SeedApprovedTeacherAsync(OtherSchoolTeacherUser, otherSchoolId);
        await SeedApprovedTeacherAsync(TutorUser, otherSchoolId);
        await SeedApprovedTeacherAsync(PendingTutorUser);

        return await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "5. Sınıf" };
            var subject = new Subject { Name = "Matematik" };
            db.AddRange(grade, subject);
            await db.SaveChangesAsync();
            var student = new Student { UserId = StudentUser, StudentNumber = "P69605", SchoolName = "s", GradeId = grade.Id, SchoolId = schoolId, SchoolVerifiedAt = DateTime.UtcNow };
            var unverified = new Student { UserId = UnverifiedStudentUser, StudentNumber = "P69606", SchoolName = "s", GradeId = grade.Id, SchoolId = schoolId };
            var outcome = new LearningOutcome { Code = "M.5.1", Description = "Kazanım ifadesi", GradeId = grade.Id, SubjectId = subject.Id, CurriculumVersion = CurriculumVersion.Maarif };
            db.AddRange(student, unverified, outcome);
            db.Parents.Add(new Parent { UserId = ParentUser });
            await db.SaveChangesAsync();

            var question = new Question { Text = "Soru", Point = 5, DifficultyLevel = 5, InteractionType = "mcq" };
            db.Questions.Add(question);
            await db.SaveChangesAsync();
            db.Add(new QuestionLearningOutcome { QuestionId = question.Id, LearningOutcomeId = outcome.Id });
            var at = DateTime.UtcNow.AddDays(-1);
            var session = new PracticeSession { StudentId = student.Id, GradeId = grade.Id, StartTime = at };
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = question.Id, ShownAt = at, AnsweredAt = at, IsCorrect = true, TimeTaken = 10 });
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync();

            var tutorId = await db.Teachers.Where(t => t.UserId == TutorUser).Select(t => t.Id).SingleAsync();
            var pendingTutorId = await db.Teachers.Where(t => t.UserId == PendingTutorUser).Select(t => t.Id).SingleAsync();
            AddBooking(db, tutorId, student.Id, BookingStatus.Approved);
            AddBooking(db, pendingTutorId, student.Id, BookingStatus.Pending);
            await db.SaveChangesAsync();
            return new Seeded(student.Id, unverified.Id, outcome.Id, schoolId, otherSchoolId);
        });
    }

    private static void AddBooking(AppDbContext db, int teacherId, int studentId, BookingStatus status)
    {
        var slot = new TeacherAvailabilitySlot { TeacherId = teacherId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), CreatedAt = DateTime.UtcNow };
        db.Add(new Booking { TeacherId = teacherId, StudentId = studentId, AvailabilitySlot = slot, Status = status, CreatedAt = DateTime.UtcNow });
    }

    [Fact]
    public async Task Student_reads_their_own_report_with_levels_and_rules()
    {
        var seeded = await SeedAsync();
        var client = await ClientAsAsync(StudentUser, "Student", $"kc-{StudentUser}", "Student");

        var report = await client.GetFromJsonAsync<LearningOutcomeMasteryDto>("/api/student/me/learning-outcome-mastery", Json);

        var item = report!.Items.Single(i => i.LearningOutcomeId == seeded.OutcomeId);
        (item.Level, item.IsTentative, item.Answered).ShouldBe((LearningOutcomeMasteryLevel.Proficient, true, 1));
        report.Rules.RecentAnswers.ShouldBe(20);
    }

    [Fact]
    public async Task Teacher_of_the_same_school_sees_the_student()
    {
        var seeded = await SeedAsync();
        var client = await TeacherAsync(SchoolTeacherUser, seeded.SchoolId);

        (await client.GetAsync(TeacherUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Teacher_with_an_approved_private_lesson_sees_the_student_from_another_school()
    {
        var seeded = await SeedAsync();
        var client = await TeacherAsync(TutorUser, seeded.OtherSchoolId);

        (await client.GetAsync(TeacherUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Other_teachers_get_404()
    {
        var seeded = await SeedAsync();

        (await (await TeacherAsync(OtherSchoolTeacherUser, seeded.OtherSchoolId)).GetAsync(TeacherUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await TeacherAsync(PendingTutorUser, null)).GetAsync(TeacherUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unverified_school_membership_gives_the_teacher_no_access()
    {
        var seeded = await SeedAsync();
        var client = await TeacherAsync(SchoolTeacherUser, seeded.SchoolId);

        (await client.GetAsync(TeacherUrl(seeded.UnverifiedStudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Parent", ParentUser)]
    [InlineData("Student", StudentUser)]
    public async Task Other_roles_cannot_use_the_teacher_endpoint(string role, int userId)
    {
        var seeded = await SeedAsync();
        var client = await ClientAsAsync(userId, role, $"kc-{userId}", role);

        (await client.GetAsync(TeacherUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalid_curriculum_is_rejected()
    {
        var seeded = await SeedAsync();
        var client = await TeacherAsync(SchoolTeacherUser, seeded.SchoolId);

        (await client.GetAsync(TeacherUrl(seeded.StudentId) + "?curriculumVersion=nope")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parent_reads_a_linked_child_with_an_audit_row_and_others_get_404()
    {
        var seeded = await SeedAsync();
        await SeedParentLinkAsync(ParentUser, StudentUser);
        var client = await ClientAsAsync(ParentUser, "Parent", $"kc-{ParentUser}", "Parent");

        (await client.GetAsync($"/api/parent/children/{seeded.StudentId}/learning-outcome-mastery")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/parent/children/{seeded.UnverifiedStudentId}/learning-outcome-mastery")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await WithDbAsync(async db =>
            (await db.ParentAccessAudits.AsNoTracking().CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildLearningOutcomeMastery)).ShouldBe(1));
    }
}
