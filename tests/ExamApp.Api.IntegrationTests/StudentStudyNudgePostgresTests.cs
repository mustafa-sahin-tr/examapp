using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services.Practice;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #519: the study nudge job over real Postgres (defaults: 3–30 inactive days, one per spell, one per 7 days, opt-out) and
/// the student's notification setting over HTTP.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StudentStudyNudgePostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int FirstUser = 7801;

    private sealed record Seeded(int Inactive, int NewSpell);

    private async Task<Seeded> SeedAsync() => await WithDbAsync(async db =>
    {
        var grade = new Grade { Name = "6. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        db.AddRange(grade, subject);
        await db.SaveChangesAsync();
        var q = new Question { Text = "N", Point = 5, DifficultyLevel = 3, SubjectId = subject.Id };
        db.Questions.Add(q);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var user = FirstUser;
        Student Add(double? answeredDaysAgo, bool optOut = false, double? nudgedDaysAgo = null)
        {
            var s = new Student
            {
                UserId = user, StudentNumber = $"S{user++}", SchoolName = "s", GradeId = grade.Id, StudyNudgeOptOut = optOut,
                LastStudyNudgeAtUtc = nudgedDaysAgo is { } n ? now.AddDays(-n) : null,
            };
            db.Students.Add(s);
            if (answeredDaysAgo is { } d)
            {
                var session = new PracticeSession { Student = s, GradeId = grade.Id, StartTime = now.AddDays(-d) };
                session.Questions.Add(new PracticeSessionQuestion { QuestionId = q.Id, ShownAt = now.AddDays(-d), AnsweredAt = now.AddDays(-d), IsCorrect = true, TimeTaken = 20 });
                db.PracticeSessions.Add(session);
            }
            return s;
        }

        var inactive = Add(4.2);
        Add(1);                                  // studied yesterday
        Add(40);                                 // left long ago
        Add(4, optOut: true);                    // turned it off
        Add(null);                               // never answered
        Add(5, nudgedDaysAgo: 2);                // nudged recently in this spell
        Add(8, nudgedDaysAgo: 7.5);              // nudged 7.5 days ago, no answer since: same spell
        var newSpell = Add(5, nudgedDaysAgo: 10); // nudged in an earlier spell, studied since
        await db.SaveChangesAsync();
        return new Seeded(inactive.Id, newSpell.Id);
    });

    private async Task<int> RunAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStudentStudyNudgeJob>().RunAsync();
    }

    [Fact]
    public async Task Only_students_inactive_3_to_30_days_once_per_spell_and_opted_in_are_nudged_once()
    {
        var s = await SeedAsync();

        (await RunAsync()).ShouldBe(2);
        (await RunAsync()).ShouldBe(0);

        var events = await WithDbAsync(async db => (await db.OutboxMessages.AsNoTracking()
                .Where(m => m.Type == OutboxEventRegistry.NameFor<StudentStudyNudgeEvent>()).ToListAsync())
            .Select(m => JsonSerializer.Deserialize<StudentStudyNudgeEvent>(m.Content)!).ToList());
        events.Select(e => e.StudentId).OrderBy(id => id).ShouldBe(new[] { s.Inactive, s.NewSpell }.OrderBy(id => id));
        events.Single(e => e.StudentId == s.Inactive).InactiveDays.ShouldBe(4);
        var stamped = await WithDbAsync(db => db.Students.Where(st => st.Id == s.Inactive).Select(st => st.LastStudyNudgeAtUtc).SingleAsync());
        stamped.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_student_turns_the_nudge_off_and_on()
    {
        await SeedAsync();
        var client = await ClientAsAsync(FirstUser, "Student", "kc-nudge-student", "Student");
        const string url = "/api/student/me/notification-settings";

        (await client.GetFromJsonAsync<JsonElement>(url, Json)).GetProperty("studyNudge").GetBoolean().ShouldBeTrue();
        (await client.PutAsJsonAsync(url, new { studyNudge = false })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<JsonElement>(url, Json)).GetProperty("studyNudge").GetBoolean().ShouldBeFalse();
        (await WithDbAsync(db => db.Students.Where(st => st.UserId == FirstUser).Select(st => st.StudyNudgeOptOut).SingleAsync())).ShouldBeTrue();

        (await RunAsync()).ShouldBe(1);

        var teacher = await ClientAsAsync(7899, "Teacher", "kc-nudge-teacher", "Teacher");
        (await teacher.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
