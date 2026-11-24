using System.Net;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #511: <c>GET api/parent/children/{id}/activity</c> on real Postgres — local days, the summary's answer rule (skips out),
/// points from <c>StudentDailyXps</c>, the previous window, a counts-only contract and the audit row. The IDOR / role matrix runs
/// for this endpoint through <c>ParentEndpointCatalog</c>.
/// </summary>
public class ParentActivityEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int StudentUser = 51101, ParentUser = 51102, OtherParentUser = 51103;

    [Fact]
    public async Task Activity_counts_each_local_day_and_compares_with_the_window_before()
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        foreach (var id in new[] { StudentUser, ParentUser, OtherParentUser })
            directory.Add(new() { Id = id, KeycloakId = $"kc-{id}", FullName = $"Kişi {id}" });

        var calendar = LocalDayCalendar.Default;
        var today = calendar.Today;
        DateTime At(int daysAgo, int hour) => calendar.StartOfDayUtc(today.AddDays(-daysAgo)).AddHours(hour);

        var studentId = await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "V511 7. Sınıf" };
            db.Add(grade);
            await db.SaveChangesAsync();
            var student = new Student { UserId = StudentUser, StudentNumber = "V511", GradeId = grade.Id };
            db.Students.Add(student);
            db.Parents.AddRange(new Parent { UserId = ParentUser }, new Parent { UserId = OtherParentUser });
            await db.SaveChangesAsync();

            var session = new PracticeSession { StudentId = student.Id, GradeId = grade.Id, StartTime = At(20, 1) };
            // (days ago, local hour, correct, skipped). Window days=7 → today-6..today; previous today-13..today-7.
            var rows = new (int Ago, int Hour, bool Correct, bool Skipped)[]
            {
                (0, 0, true, false), (0, 0, false, false), (0, 0, false, true), // today: 2 answers, 1 correct, a skip
                (3, 23, true, false),                                          // late evening stays on its local day
                (8, 10, false, false),                                         // previous window
                (20, 10, true, false),                                         // outside both
            };
            foreach (var (ago, hour, correct, skipped) in rows)
            {
                var question = new Question { Text = "V511 gizli soru", Point = 1 };
                db.Questions.Add(question);
                await db.SaveChangesAsync();
                session.Questions.Add(new PracticeSessionQuestion
                {
                    QuestionId = question.Id, ShownAt = At(ago, hour), AnsweredAt = At(ago, hour), IsCorrect = correct,
                    IsSkipped = skipped, TimeTaken = 5,
                });
            }
            db.PracticeSessions.Add(session);
            db.StudentDailyXps.AddRange(
                new StudentDailyXp { StudentId = student.Id, Day = today, Xp = 30 },
                new StudentDailyXp { StudentId = student.Id, Day = today.AddDays(-10), Xp = 20 },
                new StudentDailyXp { StudentId = student.Id, Day = today.AddDays(-30), Xp = 99 });
            await db.SaveChangesAsync();
            return student.Id;
        });
        await SeedParentLinkAsync(ParentUser, StudentUser);

        var parent = await ClientAsAsync(ParentUser, "Parent", $"kc-{ParentUser}", "Parent");
        var url = $"/api/parent/children/{studentId}/activity?days=7";
        var response = await parent.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("V511 gizli soru");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(new[] { "current", "daily", "days", "from", "previous", "studentId", "to" });
        root.GetProperty("days").GetInt32().ShouldBe(7);
        DateOnly.Parse(root.GetProperty("to").GetString()!).ShouldBe(today);
        DateOnly.Parse(root.GetProperty("from").GetString()!).ShouldBe(today.AddDays(-6));

        var daily = root.GetProperty("daily").EnumerateArray().ToList();
        daily.Count.ShouldBe(7);
        daily[0].EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "day", "answered", "correct", "points" });
        var last = daily[^1];
        DateOnly.Parse(last.GetProperty("day").GetString()!).ShouldBe(today);
        last.GetProperty("answered").GetInt32().ShouldBe(2);
        last.GetProperty("correct").GetInt32().ShouldBe(1);
        last.GetProperty("points").GetInt32().ShouldBe(30);
        daily[3].GetProperty("answered").GetInt32().ShouldBe(1); // today-3, 23:00 local

        var current = root.GetProperty("current");
        current.GetProperty("answered").GetInt32().ShouldBe(3);
        current.GetProperty("correct").GetInt32().ShouldBe(2);
        current.GetProperty("activeDays").GetInt32().ShouldBe(2);
        current.GetProperty("points").GetInt32().ShouldBe(30);
        var previous = root.GetProperty("previous");
        previous.GetProperty("answered").GetInt32().ShouldBe(1);
        previous.GetProperty("correct").GetInt32().ShouldBe(0);
        previous.GetProperty("activeDays").GetInt32().ShouldBe(1);
        previous.GetProperty("points").GetInt32().ShouldBe(20);

        (await WithDbAsync(db => db.ParentAccessAudits.CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildActivity))).ShouldBe(1);

        // Range check before any data is read; another parent's child is 404.
        (await parent.GetAsync($"/api/parent/children/{studentId}/activity?days=6")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await parent.GetAsync($"/api/parent/children/{studentId}/activity?days=29")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var other = await ClientAsAsync(OtherParentUser, "Parent", $"kc-{OtherParentUser}", "Parent");
        (await other.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
