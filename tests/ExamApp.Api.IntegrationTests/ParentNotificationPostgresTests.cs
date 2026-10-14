using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #423 (epic #407 V5) — gerçek Postgres'te: bağlantı event'lerinin sub/kısa ad taşıması (HTTP uçlarıyla), "test tamamlandı"
/// event'inin <c>EndTest</c> transaction'ında (Npgsql execution strategy) yazılması ve gecikmiş ödev süpürücüsünün
/// LINQ'unun Npgsql'de çevrilip tek işaretçi/veli başına tek event üretmesi. SQLite birim testlerinin göremediği SQL çevirisi
/// ve transaction/tekil index davranışını kapatır.
/// </summary>
public class ParentNotificationPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private sealed record Seeded(int StudentId, int GradeId, int SchoolId, int WorksheetId, int Q1Correct, int[] ParentIds);

    private async Task<Seeded> SeedAsync(int studentUserId, params int[] parentUserIds)
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-{studentUserId}", FullName = "Ayşe Kaya" });
        foreach (var p in parentUserIds)
            directory.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}" });

        return await WithDbAsync(async db =>
        {
            var school = new School { Name = "Atatürk Ortaokulu" };
            var grade = new Grade { Name = "7. Sınıf" };
            db.AddRange(school, grade);
            await db.SaveChangesAsync();

            var student = new Student
            {
                UserId = studentUserId, StudentNumber = $"N{studentUserId}", SchoolId = school.Id,
                SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id
            };
            var worksheet = new Worksheet { Name = $"Kesirler {studentUserId}", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
            var q1 = new Question { Text = "Q1", Point = 10 };
            var q2 = new Question { Text = "Q2", Point = 10 };
            db.AddRange(student, worksheet, q1, q2);
            var parents = parentUserIds.Select(u => new Parent { UserId = u }).ToList();
            db.Parents.AddRange(parents);
            await db.SaveChangesAsync();

            var a1 = new Answer { QuestionId = q1.Id, Text = "doğru", Tag = "A", Order = 0 };
            db.Answers.Add(a1);
            await db.SaveChangesAsync();
            q1.CorrectAnswerId = a1.Id;
            db.TestQuestions.AddRange(
                new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q1.Id, Order = 1 },
                new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q2.Id, Order = 2 });
            await db.SaveChangesAsync();
            return new Seeded(student.Id, grade.Id, school.Id, worksheet.Id, a1.Id, parents.Select(p => p.Id).ToArray());
        });
    }

    private Task<HttpClient> StudentAsync(int userId) => ClientAsAsync(userId, "Student", $"kc-{userId}", "Student");

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    /// <summary>issue #436: veli-öncelikli bağlantı (doğrudan Active; ilk veli birincil — event'siz).</summary>
    private Task<int> LinkAsync(int studentUser, int parentUser) => SeedParentLinkAsync(parentUser, studentUser);

    /// <summary>
    /// issue #436: ikinci veli akışı HTTP üzerinden — birincil velinin kodu → redeem (Pending) → birincil veli onayı (Active +
    /// ParentLinkedEvent). İkinci velinin bağlantı id'sini döner.
    /// </summary>
    private async Task<int> LinkSecondParentAsync(int primaryLinkId, int primaryUser, int parentUser)
    {
        var primary = await ParentAsync(primaryUser);
        var codeResponse = await primary.PostAsync($"/api/parent-links/{primaryLinkId}/second-parent-code", null);
        codeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await codeResponse.Content.ReadAsStringAsync());
        var redeem = await (await ParentAsync(parentUser)).PostAsJsonAsync("/api/parent-links/redeem",
            new { code = doc.RootElement.GetProperty("code").GetString() });
        redeem.StatusCode.ShouldBe(HttpStatusCode.OK);
        var linkId = (await redeem.Content.ReadFromJsonAsync<LinkedChildDto>(Json))!.LinkId;
        (await primary.PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return linkId;
    }

    private Task<List<T>> EventsAsync<T>(Func<T, bool> where) where T : class
        => WithDbAsync(async db =>
        {
            var type = OutboxEventRegistry.NameFor<T>();
            return (await db.OutboxMessages.AsNoTracking().Where(o => o.Type == type).ToListAsync())
                .Select(o => JsonSerializer.Deserialize<T>(o.Content)!)
                .Where(where)
                .ToList();
        });

    [Fact]
    public async Task Link_and_unlink_events_carry_sub_and_short_names_but_no_email()
    {
        const int studentUser = 42301, parentUser = 42302, primaryUser = 42303;
        await SeedAsync(studentUser, parentUser, primaryUser);

        // #436: ikinci veli birincil velinin onayıyla bağlanır (event Active'e geçişte); birincil veli bağlantıyı koparır.
        var primaryLink = await LinkAsync(studentUser, primaryUser);
        var linkId = await LinkSecondParentAsync(primaryLink, primaryUser, parentUser);
        (await (await ParentAsync(primaryUser)).PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var linked = (await EventsAsync<ParentLinkedEvent>(e => e.LinkId == linkId)).ShouldHaveSingleItem();
        linked.ParentKeycloakId.ShouldBe($"kc-{parentUser}");
        linked.StudentKeycloakId.ShouldBe($"kc-{studentUser}");
        linked.StudentDisplayName.ShouldBe("Ayşe K.");
        var unlinked = (await EventsAsync<ParentUnlinkedEvent>(e => e.LinkId == linkId)).ShouldHaveSingleItem();
        unlinked.RevokedByRole.ShouldBe("PrimaryParent");
        unlinked.StudentKeycloakId.ShouldBe($"kc-{studentUser}");
        unlinked.ParentDisplayName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EndTest_writes_parent_events_in_the_completion_transaction_only_for_active_links()
    {
        const int studentUser = 42311, activeParent = 42312, pendingParent = 42313;
        var seeded = await SeedAsync(studentUser, activeParent, pendingParent);
        await LinkAsync(studentUser, activeParent);
        // Pending: ikinci veli isteği, birincil veli onaylamadı.
        await SeedParentLinkAsync(pendingParent, studentUser, active: false);

        // Test-tamamlandı bildirimi yalnız görünür bir atamanın penceresindeki oturum için gider (#423 m1).
        await WithDbAsync(async db =>
        {
            db.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = seeded.WorksheetId, StudentId = seeded.StudentId, StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync();
        });

        int instanceId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
            var start = await session.StartTestAsync(seeded.WorksheetId, new StudentProfileDto { Id = seeded.StudentId, GradeId = seeded.GradeId });
            start.Success.ShouldBeTrue();
            instanceId = start.InstanceId;
        }
        await WithDbAsync(async db =>
        {
            var row = await db.TestInstanceQuestions.Where(q => q.WorksheetInstanceId == instanceId).OrderBy(q => q.Id).FirstAsync();
            row.SelectedAnswerId = seeded.Q1Correct;
            await db.SaveChangesAsync();
        });

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
            (await session.EndTest(instanceId, studentUser)).Success.ShouldBeTrue();
            (await session.EndTest(instanceId, studentUser)).Success.ShouldBeTrue(); // idempotent
        }

        var events = await EventsAsync<ParentChildTestCompletedEvent>(e => e.TestInstanceId == instanceId);
        var evt = events.ShouldHaveSingleItem();
        evt.ParentUserId.ShouldBe(activeParent);
        evt.ParentKeycloakId.ShouldBe($"kc-{activeParent}");
        evt.Score.ShouldBe(50);
        evt.StudentDisplayName.ShouldBe("Ayşe K.");
    }

    [Fact]
    public async Task Overdue_sweep_translates_on_postgres_and_notifies_active_parents_once()
    {
        const int studentUser = 42321, parentA = 42322, parentB = 42323;
        var seeded = await SeedAsync(studentUser, parentA, parentB);
        await LinkAsync(studentUser, parentA);
        await LinkAsync(studentUser, parentB);

        // Bağlantılar bitiş anından ÖNCE aktifleşmiş olmalı (sonradan bağlanan veli eski gecikmeden bildirilmez).
        await WithDbAsync(db => db.ParentStudentLinks.Where(l => l.StudentId == seeded.StudentId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ActivatedAt, DateTime.UtcNow.AddDays(-5))
                .SetProperty(l => l.CreatedAt, DateTime.UtcNow.AddDays(-5).AddMinutes(-5))));

        var now = DateTime.UtcNow;
        var (studentAssignment, gradeAssignment) = await WithDbAsync(async db =>
        {
            // Aynı test iki kez atansa tek bildirim olur (#423): sınıf ataması için ikinci bir test.
            var second = new Worksheet { Name = $"Ikinci {studentUser}", Description = "", GradeId = seeded.GradeId, MaxDurationSeconds = 600 };
            db.Worksheets.Add(second);
            await db.SaveChangesAsync();
            var s = new WorksheetAssignment
            {
                WorksheetId = seeded.WorksheetId, StudentId = seeded.StudentId, StartAt = now.AddDays(-3), EndAt = now.AddHours(-2)
            };
            var g = new WorksheetAssignment
            {
                WorksheetId = second.Id, GradeId = seeded.GradeId, SchoolId = seeded.SchoolId, StartAt = now.AddDays(-3), EndAt = now.AddHours(-1)
            };
            db.WorksheetAssignments.AddRange(s, g);
            await db.SaveChangesAsync();
            return (s.Id, g.Id);
        });

        // Diğer testlerin verisi de taranır (paylaşılan DB); bu testin çiftleri en az 2 işlenmiş olmalı.
        for (var i = 0; i < 3; i++)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IParentHomeworkOverdueSweepJob>().SweepAsync();
        }

        var events = await EventsAsync<ParentHomeworkOverdueEvent>(e => e.StudentId == seeded.StudentId);
        events.Count.ShouldBe(4); // 2 atama × 2 Active veli, tekrar çalışmada çoğalmaz
        events.Select(e => (e.AssignmentId, e.ParentUserId)).Distinct().Count().ShouldBe(4);
        events.Select(e => e.AssignmentId).Distinct().OrderBy(i => i).ShouldBe(new[] { studentAssignment, gradeAssignment }.OrderBy(i => i));
        events.ShouldAllBe(e => e.StudentDisplayName == "Ayşe K." && e.ParentKeycloakId.StartsWith("kc-"));
        (await WithDbAsync(db => db.ParentHomeworkOverdueMarkers.CountAsync(m => m.StudentId == seeded.StudentId))).ShouldBe(2);
    }
}
