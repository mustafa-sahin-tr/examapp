using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.ParentLinks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #421 (epic #407 V3) — veli ödev/test takibi gerçek Postgres'te: kodla bağlan → liste (kovalar, sıralama, filtre,
/// sayfalama, alan listesi kilitli) → bitmiş testin özeti (konu bazında sayılar; soru/şık/cevap anahtarı yok) +
/// Cache-Control no-store + ParentAccessAudit satırları. Yetki: başka veli, bekleyen bağlantı, başka çocuğun oturumu,
/// devam eden oturum, olmayan id → 404; yanlış rol → 403; anonim → 401; geçersiz filtre/sayfa → 400.
/// </summary>
public class ParentAssignmentEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int TeacherUser = 43190;

    private sealed record Seeded(int StudentId, int OtherStudentId, int GradeId, int SchoolId, int SubjectId);

    private async Task<Seeded> SeedAsync(int studentUserId, int otherStudentUserId, params int[] parentUserIds)
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-{studentUserId}", FullName = "Ayşe Kaya" });
        directory.Add(new() { Id = otherStudentUserId, KeycloakId = $"kc-{otherStudentUserId}", FullName = "Başka Öğrenci" });
        directory.Add(new() { Id = TeacherUser, KeycloakId = $"kc-{TeacherUser}", FullName = "Zeynep Öğretmen", Email = "teacher-secret@x.com" });
        foreach (var p in parentUserIds)
            directory.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}" });

        return await WithDbAsync(async db =>
        {
            var school = new School { Name = "Atatürk Ortaokulu" };
            var grade = new Grade { Name = "7. Sınıf" };
            var subject = new Subject { Name = "Matematik" };
            db.AddRange(school, grade, subject);
            await db.SaveChangesAsync();

            var student = new Student
            {
                UserId = studentUserId, StudentNumber = $"P{studentUserId}", SchoolId = school.Id,
                SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id
            };
            var other = new Student
            {
                UserId = otherStudentUserId, StudentNumber = $"P{otherStudentUserId}", SchoolId = school.Id,
                SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id
            };
            db.Students.AddRange(student, other);
            db.Parents.AddRange(parentUserIds.Select(u => new Parent { UserId = u }));
            await db.SaveChangesAsync();
            return new Seeded(student.Id, other.Id, grade.Id, school.Id, subject.Id);
        });
    }

    private Task<HttpClient> StudentAsync(int userId) => ClientAsAsync(userId, "Student", $"kc-{userId}", "Student");

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    /// <summary>
    /// issue #436: veli-öncelikli bağlantı (doğrudan Active; ilk veli birincil). <paramref name="approve"/> false → birincil
    /// velinin onayını bekleyen ikinci veli isteği (erişim vermez).
    /// </summary>
    private Task<int> LinkAsync(int studentUser, int parentUser, bool approve = true)
        => SeedParentLinkAsync(parentUser, studentUser, active: approve);

    private static string ListUrl(int studentId, string query = "") => $"/api/parent/children/{studentId}/assignments{query}";

    private static string ResultUrl(int studentId, int instanceId) => $"/api/parent/children/{studentId}/test-results/{instanceId}";

    private Task<List<ParentAccessAudit>> AuditsAsync()
        => WithDbAsync(db => db.ParentAccessAudits.AsNoTracking().OrderBy(a => a.Id).ToListAsync());

    private sealed record Fixture(int DoneWorksheet, int DoneInstance, int OpenWorksheet, int MissedWorksheet,
        int RunningInstance, int OthersInstance);

    /// <summary>Tamamlanmış (3 soru: doğru/yanlış/boş), açık sınıf ataması, kaçırılmış, devam eden; başka öğrencinin oturumu.</summary>
    private Task<Fixture> SeedWorkAsync(Seeded seeded, DateTime now) => WithDbAsync(async db =>
    {
        var done = new Worksheet { Name = "Kesirler", Description = "GIZLI-ACIKLAMA", GradeId = seeded.GradeId, SubjectId = seeded.SubjectId };
        var open = new Worksheet { Name = "Ondalık", Description = "", GradeId = seeded.GradeId };
        var missed = new Worksheet { Name = "Oran", Description = "", GradeId = seeded.GradeId };
        var running = new Worksheet { Name = "Yüzde", Description = "", GradeId = seeded.GradeId };
        db.AddRange(done, open, missed, running);
        var topic = new Topic { Name = "Kesir işlemleri", SubjectId = seeded.SubjectId, GradeId = seeded.GradeId };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var assignments = new[]
        {
            new WorksheetAssignment { WorksheetId = done.Id, StudentId = seeded.StudentId, StartAt = now.AddDays(-3), EndAt = now.AddDays(3) },
            new WorksheetAssignment { WorksheetId = open.Id, GradeId = seeded.GradeId, SchoolId = seeded.SchoolId, StartAt = now.AddDays(-1), EndAt = null },
            new WorksheetAssignment { WorksheetId = missed.Id, StudentId = seeded.StudentId, StartAt = now.AddDays(-5), EndAt = now.AddDays(-1) },
            new WorksheetAssignment { WorksheetId = running.Id, StudentId = seeded.StudentId, StartAt = now.AddDays(-1), EndAt = now.AddDays(1) }
        };
        db.WorksheetAssignments.AddRange(assignments);

        var doneInstance = new WorksheetInstance
        {
            StudentId = seeded.StudentId, WorksheetId = done.Id, StartTime = now.AddMinutes(-30),
            EndTime = now.AddMinutes(-10), Status = WorksheetInstanceStatus.Completed
        };
        var runningInstance = new WorksheetInstance
        {
            StudentId = seeded.StudentId, WorksheetId = running.Id, StartTime = now.AddMinutes(-5), Status = WorksheetInstanceStatus.Started
        };
        var othersInstance = new WorksheetInstance
        {
            StudentId = seeded.OtherStudentId, WorksheetId = done.Id, StartTime = now.AddMinutes(-60),
            EndTime = now.AddMinutes(-40), Status = WorksheetInstanceStatus.Completed
        };
        db.TestInstances.AddRange(doneInstance, runningInstance, othersInstance);
        await db.SaveChangesAsync();

        // Audit hook CreateUserId'yi o anki kullanıcıyla yazar; atayan öğretmeni sabitle.
        var ids = assignments.Select(a => a.Id).ToList();
        await db.WorksheetAssignments.Where(a => ids.Contains(a.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.CreateUserId, TeacherUser));

        for (var i = 0; i < 3; i++)
        {
            var question = new Question
            {
                Text = $"GIZLI-SORU-{i}", ImageUrl = $"gizli/soru-{i}.png", Point = 1, TopicId = i < 2 ? topic.Id : null
            };
            db.Questions.Add(question);
            await db.SaveChangesAsync();
            var right = new Answer { QuestionId = question.Id, Text = "GIZLI-DOGRU", Tag = "A" };
            var wrong = new Answer { QuestionId = question.Id, Text = "GIZLI-YANLIS", Tag = "B" };
            var wq = new WorksheetQuestion { TestId = done.Id, QuestionId = question.Id, Order = i + 1 };
            db.AddRange(right, wrong, wq);
            await db.SaveChangesAsync();
            question.CorrectAnswerId = right.Id;
            db.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
            {
                WorksheetInstanceId = doneInstance.Id,
                WorksheetQuestionId = wq.Id,
                SelectedAnswerId = i switch { 0 => right.Id, 1 => wrong.Id, _ => null }
            });
            await db.SaveChangesAsync();
        }

        return new Fixture(done.Id, doneInstance.Id, open.Id, missed.Id, runningInstance.Id, othersInstance.Id);
    });

    private static string[] Names(JsonElement obj) => obj.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Linked_parent_lists_assignments_and_opens_a_test_summary_without_content()
    {
        const int studentUser = 43101, otherStudentUser = 43102, parentUser = 43103;
        var seeded = await SeedAsync(studentUser, otherStudentUser, parentUser);
        var student = await StudentAsync(studentUser);
        var parent = await ParentAsync(parentUser);
        await LinkAsync(studentUser, parentUser);
        var now = DateTime.UtcNow;
        var fx = await SeedWorkAsync(seeded, now);

        // ---- liste
        var listResponse = await parent.GetAsync(ListUrl(seeded.StudentId));
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        listResponse.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var listJson = await listResponse.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(listJson))
        {
            var root = doc.RootElement;
            Names(root).ShouldBe(new[] { "counts", "items", "page", "pageSize", "status", "studentId", "totalCount" });
            root.GetProperty("totalCount").GetInt32().ShouldBe(4);
            root.GetProperty("page").GetInt32().ShouldBe(1);
            root.GetProperty("status").ValueKind.ShouldBe(JsonValueKind.Null);
            var counts = root.GetProperty("counts");
            counts.GetProperty("completed").GetInt32().ShouldBe(1);
            counts.GetProperty("pending").GetInt32().ShouldBe(2);
            counts.GetProperty("overdue").GetInt32().ShouldBe(1);

            var items = root.GetProperty("items").EnumerateArray().ToList();
            // Teslim tarihsiz en üstte, sonra en yeni teslim tarihi.
            items.Select(i => i.GetProperty("title").GetString()).ShouldBe(new[] { "Ondalık", "Kesirler", "Yüzde", "Oran" });
            foreach (var item in items)
            {
                Names(item).ShouldBe(new[]
                    { "deadline", "result", "startAt", "status", "subject", "teacherName", "testInstanceId", "title", "worksheetId" });
            }

            var done = items[1];
            done.GetProperty("status").GetString().ShouldBe("completed");
            done.GetProperty("subject").GetString().ShouldBe("Matematik");
            done.GetProperty("teacherName").GetString().ShouldBe("Zeynep Öğretmen");
            done.GetProperty("testInstanceId").GetInt32().ShouldBe(fx.DoneInstance);
            var result = done.GetProperty("result");
            Names(result).ShouldBe(new[] { "blankCount", "correctCount", "durationSeconds", "scorePercent", "totalCount", "wrongCount" });
            result.GetProperty("correctCount").GetInt32().ShouldBe(1);
            result.GetProperty("wrongCount").GetInt32().ShouldBe(1);
            result.GetProperty("blankCount").GetInt32().ShouldBe(1);
            result.GetProperty("scorePercent").GetInt32().ShouldBe(33);
            result.GetProperty("durationSeconds").GetInt32().ShouldBe(20 * 60);

            items[0].GetProperty("deadline").ValueKind.ShouldBe(JsonValueKind.Null);
            items[2].GetProperty("status").GetString().ShouldBe("pending"); // devam eden
            items[2].GetProperty("testInstanceId").ValueKind.ShouldBe(JsonValueKind.Null);
            items[3].GetProperty("status").GetString().ShouldBe("overdue");
        }

        foreach (var secret in new[] { "GIZLI", "gizli/", "teacher-secret", "Başka Öğrenci" })
            listJson.ShouldNotContain(secret);

        // ---- filtre + sayfa
        using (var doc = JsonDocument.Parse(await (await parent.GetAsync(ListUrl(seeded.StudentId, "?status=overdue&page=1"))).Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("status").GetString().ShouldBe("overdue");
            doc.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(1);
            doc.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("worksheetId").GetInt32().ShouldBe(fx.MissedWorksheet);
        }
        using (var doc = JsonDocument.Parse(await (await parent.GetAsync(ListUrl(seeded.StudentId, "?page=2"))).Content.ReadAsStringAsync()))
            doc.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);

        (await parent.GetAsync(ListUrl(seeded.StudentId, "?status=bogus"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await parent.GetAsync(ListUrl(seeded.StudentId, "?page=0"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // ---- test sonucu
        var resultResponse = await parent.GetAsync(ResultUrl(seeded.StudentId, fx.DoneInstance));
        resultResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        resultResponse.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var resultJson = await resultResponse.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(resultJson))
        {
            var root = doc.RootElement;
            Names(root).ShouldBe(new[]
                { "finishedAt", "outcome", "score", "startedAt", "studentId", "subject", "testInstanceId", "title", "topics", "worksheetId" });
            root.GetProperty("title").GetString().ShouldBe("Kesirler");
            root.GetProperty("outcome").GetString().ShouldBe("completed");
            // Başlangıç/bitiş saate kesilir; süre kesin.
            var startedAt = root.GetProperty("startedAt").GetDateTime().ToUniversalTime();
            (startedAt.Minute, startedAt.Second).ShouldBe((0, 0));
            root.GetProperty("score").GetProperty("durationSeconds").GetInt32().ShouldBe(20 * 60);
            root.GetProperty("score").GetProperty("correctCount").GetInt32().ShouldBe(1);
            root.GetProperty("score").GetProperty("totalCount").GetInt32().ShouldBe(3);
            var topics = root.GetProperty("topics").EnumerateArray().ToList();
            topics.Count.ShouldBe(2);
            Names(topics[0]).ShouldBe(new[] { "blankCount", "correctCount", "name", "topicId", "totalCount", "wrongCount" });
            topics[0].GetProperty("name").GetString().ShouldBe("Kesir işlemleri");
            topics[0].GetProperty("correctCount").GetInt32().ShouldBe(1);
            topics[0].GetProperty("wrongCount").GetInt32().ShouldBe(1);
            topics[1].GetProperty("topicId").ValueKind.ShouldBe(JsonValueKind.Null);
            topics[1].GetProperty("blankCount").GetInt32().ShouldBe(1);
        }

        foreach (var secret in new[] { "GIZLI", "gizli/", "answer", "Answer", "selected", "image" })
            resultJson.ShouldNotContain(secret);

        // ---- audit: uç başına bir satır (10 dk kova; sınırda 2 olabilir)
        var audits = await AuditsAsync();
        audits.Count(a => a.Endpoint == ParentAccessEndpoints.ChildAssignments).ShouldBeInRange(1, 2);
        audits.Count(a => a.Endpoint == ParentAccessEndpoints.ChildTestResult).ShouldBeInRange(1, 2);
        audits.ShouldAllBe(a => a.StudentId == seeded.StudentId);
        // Test sonucu satırı görülen oturumu kaynak olarak taşır; liste satırında kaynak yok.
        audits.Where(a => a.Endpoint == ParentAccessEndpoints.ChildTestResult).ShouldAllBe(a => a.ResourceId == fx.DoneInstance);
        audits.Where(a => a.Endpoint == ParentAccessEndpoints.ChildAssignments).ShouldAllBe(a => a.ResourceId == null);
    }

    [Fact]
    public async Task Idor_and_role_cases_are_404_or_403_without_leaking()
    {
        const int studentUser = 43111, otherStudentUser = 43112, parentUser = 43113, otherParentUser = 43114, pendingParentUser = 43115;
        var seeded = await SeedAsync(studentUser, otherStudentUser, parentUser, otherParentUser, pendingParentUser);
        var student = await StudentAsync(studentUser);
        var parent = await ParentAsync(parentUser);
        var otherParent = await ParentAsync(otherParentUser);
        var pendingParent = await ParentAsync(pendingParentUser);
        var teacher = await ClientAsAsync(43116, "Teacher", "kc-43116", "Teacher");

        var linkId = await LinkAsync(studentUser, parentUser);
        await LinkAsync(studentUser, pendingParentUser, approve: false);
        // Diğer veli kendi (başka) çocuğuna bağlı.
        await LinkAsync(otherStudentUser, otherParentUser);
        var fx = await SeedWorkAsync(seeded, DateTime.UtcNow);

        // Başka velinin çocuğu / bekleyen bağlantı → 404, audit yok.
        foreach (var client in new[] { otherParent, pendingParent })
        {
            var list = await client.GetAsync(ListUrl(seeded.StudentId));
            list.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            using (var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
                doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.NotFound);
            (await client.GetAsync(ResultUrl(seeded.StudentId, fx.DoneInstance))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        (await parent.GetAsync(ListUrl(999_999))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuditsAsync()).ShouldBeEmpty();

        // Kendi çocuğunun id'siyle başka çocuğun oturumu, devam eden oturum, olmayan oturum → 404.
        (await parent.GetAsync(ResultUrl(seeded.StudentId, fx.OthersInstance))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await parent.GetAsync(ResultUrl(seeded.StudentId, fx.RunningInstance))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await parent.GetAsync(ResultUrl(seeded.StudentId, 999_999))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // Kendi başına çözülen (atanmamış) test listenin kapsamında değil → 404.
        var selfInstance = await WithDbAsync(async db =>
        {
            var ws = new Worksheet { Name = "Serbest", Description = "", GradeId = seeded.GradeId };
            db.Worksheets.Add(ws);
            await db.SaveChangesAsync();
            var ti = new WorksheetInstance
            {
                StudentId = seeded.StudentId, WorksheetId = ws.Id, StartTime = DateTime.UtcNow.AddMinutes(-20),
                EndTime = DateTime.UtcNow.AddMinutes(-5), Status = WorksheetInstanceStatus.Completed
            };
            db.TestInstances.Add(ti);
            await db.SaveChangesAsync();
            return ti.Id;
        });
        (await parent.GetAsync(ResultUrl(seeded.StudentId, selfInstance))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // Diğer veli kendi çocuğunun id'siyle bizim öğrencinin oturumunu isteyemez.
        (await otherParent.GetAsync(ResultUrl(seeded.OtherStudentId, fx.DoneInstance))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Yanlış rol → 403; anonim → 401.
        foreach (var client in new[] { student, teacher })
        {
            (await client.GetAsync(ListUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.GetAsync(ResultUrl(seeded.StudentId, fx.DoneInstance))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        (await Anonymous().GetAsync(ListUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Yazma ucu yok.
        (await parent.PostAsync(ListUrl(seeded.StudentId), null)).StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        (await parent.DeleteAsync(ResultUrl(seeded.StudentId, fx.DoneInstance))).StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);

        // Bağlı veli erişir; bağlantı koparılınca erişim anında biter.
        (await parent.GetAsync(ResultUrl(seeded.StudentId, fx.DoneInstance))).StatusCode.ShouldBe(HttpStatusCode.OK);
        // #436: tek veli ayrılamaz (409); bağlantıyı admin koparınca erişim anında biter.
        (await parent.PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var admin = await ClientAsAsync(43199, "Admin", "kc-admin-43199", "Admin");
        (await admin.PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await parent.GetAsync(ListUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await parent.GetAsync(ResultUrl(seeded.StudentId, fx.DoneInstance))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
