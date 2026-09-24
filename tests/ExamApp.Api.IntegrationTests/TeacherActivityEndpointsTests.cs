using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #56: GET /api/teacher/own-activity-summary ve /api/teacher/students-activity-summary — gerçek PostgreSQL
/// üzerinde (GroupBy/koşullu Sum çevirisi dahil) uçtan uca: kimlik token'dan (path'te teacherId yok), days sınırı,
/// rol kapısı ve yalnızca öğretmenin kendi sınavlarına atanan öğrencilerin verisi.
/// </summary>
public class TeacherActivityEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string OwnUrl = "/api/teacher/own-activity-summary";
    private const string StudentsUrl = "/api/teacher/students-activity-summary";

    // Respawn FakeUserDirectory'yi sıfırlamaz — benzersiz, büyük UserId'ler.
    private const int OwnerId = 56_001;
    private const int OtherTeacherId = 56_002;
    private const int StudentAUserId = 56_101;
    private const int StudentBUserId = 56_102;

    private sealed record Seed(int StudentA, int StudentB);

    /// <summary>
    /// Sahip (okulsuz/bağımsız → direkt atamalar Approved Booking ile kapsamda) iki öğrencisini kendi worksheet'ine atar:
    /// B 3 soru (2 doğru), A 1 soru (1 doğru) çözer. Diğer öğretmen de bağımsızdır ve A ile Approved Booking'i vardır
    /// (A onun kapsamında gerçek bir öğrenci); A'yı kendi worksheet'ine atar ve A orada 10 soru çözer.
    /// </summary>
    private async Task<Seed> SeedAsync()
        => await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "8" };
            db.Grades.Add(grade);
            await db.SaveChangesAsync();

            var owner = new Teacher { UserId = OwnerId, SchoolId = null, IsIndependentTutor = true, AccountApprovedAt = DateTime.UtcNow };
            var otherTeacher = new Teacher { UserId = OtherTeacherId, SchoolId = null, IsIndependentTutor = true, AccountApprovedAt = DateTime.UtcNow };
            var a = new Student { UserId = StudentAUserId, StudentNumber = "A1", SchoolName = "S", GradeId = grade.Id };
            var b = new Student { UserId = StudentBUserId, StudentNumber = "B1", SchoolName = "S", GradeId = grade.Id };
            db.AddRange(owner, otherTeacher, a, b);
            await db.SaveChangesAsync();

            var hour = 8;
            foreach (var (teacher, student) in new[] { (owner, a), (owner, b), (otherTeacher, a) })
            {
                db.Bookings.Add(new Booking
                {
                    TeacherId = teacher.Id, StudentId = student.Id, Status = BookingStatus.Approved, CreatedAt = DateTime.UtcNow,
                    AvailabilitySlot = new TeacherAvailabilitySlot
                    {
                        TeacherId = teacher.Id, Date = new DateOnly(2026, 10, 1),
                        StartTime = new TimeOnly(hour, 0), EndTime = new TimeOnly(hour + 1, 0), CreatedAt = DateTime.UtcNow,
                    },
                });
                hour++;
            }

            db.SetCurrentUser(OwnerId);
            var ownWs = new Worksheet { Name = "Sahibin testi", Description = "", GradeId = grade.Id };
            db.Worksheets.Add(ownWs);
            await db.SaveChangesAsync();
            db.WorksheetAssignments.AddRange(
                new WorksheetAssignment { WorksheetId = ownWs.Id, StudentId = a.Id, StartAt = DateTime.UtcNow.AddDays(-1) },
                new WorksheetAssignment { WorksheetId = ownWs.Id, StudentId = b.Id, StartAt = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();

            db.SetCurrentUser(OtherTeacherId);
            var otherWs = new Worksheet { Name = "Diğer öğretmenin", Description = "", GradeId = grade.Id };
            db.Worksheets.Add(otherWs);
            await db.SaveChangesAsync();
            db.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = otherWs.Id, StudentId = a.Id, StartAt = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();

            await AddAnswersAsync(db, ownWs.Id, b.Id, count: 3, correct: 2, timeEach: 30);
            await AddAnswersAsync(db, ownWs.Id, a.Id, count: 1, correct: 1, timeEach: 40);
            await AddAnswersAsync(db, otherWs.Id, a.Id, count: 10, correct: 10, timeEach: 5);

            return new Seed(a.Id, b.Id);
        });

    private static async Task AddAnswersAsync(AppDbContext db, int worksheetId, int studentId, int count, int correct, int timeEach)
    {
        var instance = new WorksheetInstance
        {
            StudentId = studentId, WorksheetId = worksheetId, StartTime = DateTime.UtcNow, Status = WorksheetInstanceStatus.Started,
        };
        db.Add(instance);
        await db.SaveChangesAsync();

        for (var i = 0; i < count; i++)
        {
            var question = new Question { Text = $"q-{worksheetId}-{studentId}-{i}", Point = 1 };
            db.Questions.Add(question);
            await db.SaveChangesAsync();
            var answer = new Answer { QuestionId = question.Id, Text = "A", Tag = "A" };
            var wq = new WorksheetQuestion { TestId = worksheetId, QuestionId = question.Id, Order = i + 1 };
            db.AddRange(answer, wq);
            await db.SaveChangesAsync();

            db.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
            {
                WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id, SelectedAnswerId = answer.Id,
                IsCorrect = i < correct, TimeTaken = timeEach, UpdateTime = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Activity_endpoints_reject_anonymous_callers()
    {
        (await Anonymous().GetAsync(OwnUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Anonymous().GetAsync(StudentsUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Activity_endpoints_are_forbidden_for_non_teacher_roles()
    {
        var student = await ClientAsAsync(56_900, "Student", "kc-56-student", "Student");

        (await student.GetAsync(OwnUrl)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.GetAsync(StudentsUrl)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task Days_outside_1_to_90_is_rejected_with_400(int days)
    {
        var teacher = await ClientAsAsync(OwnerId, "Teacher", "kc-56-owner", "Teacher");

        (await teacher.GetAsync($"{OwnUrl}?days={days}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.GetAsync($"{StudentsUrl}?days={days}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Owner_sees_own_activity_and_only_their_assigned_students_ranked_by_questions_solved()
    {
        var seed = await SeedAsync();
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new UserLookupResultDto { Id = StudentAUserId, FullName = "Ayşe A." });
        directory.Add(new UserLookupResultDto { Id = StudentBUserId, FullName = "Bora B." });

        var owner = await ClientAsAsync(OwnerId, "Teacher", "kc-56-owner", "Teacher");

        var own = await owner.GetFromJsonAsync<TeacherOwnActivitySummaryDto>(OwnUrl, Json);
        own!.WorksheetsCreated.ShouldBe(1);
        own.AssignmentsCreated.ShouldBe(2);
        own.ActiveStudents.ShouldBe(2);

        var students = await owner.GetFromJsonAsync<TeacherStudentsActivitySummaryDto>($"{StudentsUrl}?days=7", Json);
        students!.TotalQuestionsSolved.ShouldBe(4);   // diğer öğretmenin sınavındaki 10 cevap hariç
        students.TotalCorrectCount.ShouldBe(3);
        students.TotalTimeSeconds.ShouldBe(3 * 30 + 40);
        students.TopStudents.Select(s => s.StudentId).ShouldBe(new[] { seed.StudentB, seed.StudentA });
        students.TopStudents[0].StudentName.ShouldBe("Bora B.");
        students.TopStudents[0].QuestionsSolved.ShouldBe(3);
        students.TopStudents[0].CorrectCount.ShouldBe(2);
        students.TopStudents[0].TimeSeconds.ShouldBe(90);
    }

    [Fact]
    public async Task Each_teacher_sees_only_their_own_numbers_for_a_shared_student()
    {
        var seed = await SeedAsync();

        // A her iki öğretmenin kapsamında. Diğer öğretmen A'nın yalnız KENDİ worksheet'indeki 10 cevabını görür;
        // sahibin worksheet'indeki cevaplar (A: 1, B: 3) ve B görünmez. Uçta teacherId yok — kimlik token'dan;
        // sahte teacherId sorgu parametresi yok sayılır.
        var other = await ClientAsAsync(OtherTeacherId, "Teacher", "kc-56-other", "Teacher");

        var own = await other.GetFromJsonAsync<TeacherOwnActivitySummaryDto>($"{OwnUrl}?teacherId={OwnerId}", Json);
        own!.WorksheetsCreated.ShouldBe(1);
        own.AssignmentsCreated.ShouldBe(1);
        own.ActiveStudents.ShouldBe(1);

        var students = await other.GetFromJsonAsync<TeacherStudentsActivitySummaryDto>($"{StudentsUrl}?teacherId={OwnerId}", Json);
        students!.TotalQuestionsSolved.ShouldBe(10);
        students.TotalCorrectCount.ShouldBe(10);
        students.TotalTimeSeconds.ShouldBe(50);
        var row = students.TopStudents.ShouldHaveSingleItem();
        row.StudentId.ShouldBe(seed.StudentA);
        row.QuestionsSolved.ShouldBe(10);
        students.TopStudents.ShouldNotContain(r => r.StudentId == seed.StudentB);
    }
}
