using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.DirectMessages;
using ExamApp.Api.Services.DirectMessages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #106 (dilim a) gerçek PostgreSQL'de: CanMessage IQueryable'ının SQL çevirisi (okul / öğrenci ve sınıf hedefli atama /
/// engel / onay alt sorguları), çift başına tek konuşmanın eşzamanlı ilk mesaj yarışında korunması (tekil index + yeniden
/// okuma), filtreli tekil index'lerle şikayet idempotentliği ve uçların rol kapıları (gateway: /api/exam/direct-messages/...).
/// </summary>
public class DirectMessagePostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    // FakeUserDirectory Respawn ile sıfırlanmaz → benzersiz, büyük UserId.
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private sealed record Seed(int SchoolId, int StudentUserId, int StudentId, int SameSchoolTeacher, int AssignerTeacher,
        int GradeAssignerTeacher, int BlockingTeacher, int UnrelatedTeacher, int UnapprovedTeacher, Dictionary<int, int> TeacherIds);

    private async Task<Seed> SeedAsync()
    {
        var student = NewUserId();
        var same = NewUserId();
        var assigner = NewUserId();
        var gradeAssigner = NewUserId();
        var blocking = NewUserId();
        var unrelated = NewUserId();
        var unapproved = NewUserId();
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        foreach (var (id, name) in new[] { (student, "Ayşe Öğrenci"), (same, "Selin Okul"), (assigner, "Ata Atayan"),
                     (gradeAssigner, "Gül Sınıf"), (blocking, "Bülent Engel"), (unrelated, "Ufuk İlgisiz"), (unapproved, "Onur Onaysız") })
            directory.Add(new UserLookupResultDto { Id = id, FullName = name, KeycloakId = $"kc-{id}" });

        return await WithDbAsync(async db =>
        {
            var school = new School { Name = $"DM Okul {student}" };
            var other = new School { Name = $"DM Diğer {student}" };
            var grade = new Grade { Name = $"G{student}" };
            db.AddRange(school, other, grade);
            await db.SaveChangesAsync();

            var now = DateTime.UtcNow;
            Teacher T(int userId, int? schoolId, bool approved = true) =>
                new() { UserId = userId, SchoolId = schoolId, AccountApprovedAt = approved ? now.AddDays(-5) : null };
            var teachers = new[]
            {
                T(same, school.Id), T(assigner, other.Id), T(gradeAssigner, null), T(blocking, school.Id), T(unrelated, other.Id),
                T(unapproved, school.Id, approved: false)
            };
            db.Teachers.AddRange(teachers);
            var s = new Student { UserId = student, StudentNumber = $"S{student}", SchoolId = school.Id, GradeId = grade.Id };
            db.Students.Add(s);
            var ws = new Worksheet { Name = "DM", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
            db.Worksheets.Add(ws);
            await db.SaveChangesAsync();

            db.SetCurrentUser(assigner);
            db.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws.Id, StudentId = s.Id, StartAt = now.AddDays(-1), EndAt = now.AddDays(3) });
            await db.SaveChangesAsync();
            db.SetCurrentUser(gradeAssigner);
            db.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = grade.Id, SchoolId = school.Id, StartAt = now.AddDays(-1), EndAt = null
            });
            await db.SaveChangesAsync();
            db.SetCurrentUser(blocking);
            db.DirectMessageBlocks.Add(new DirectMessageBlock { TeacherUserId = blocking, StudentUserId = student });
            await db.SaveChangesAsync();

            return new Seed(school.Id, student, s.Id, same, assigner, gradeAssigner, blocking, unrelated, unapproved,
                teachers.ToDictionary(t => t.UserId, t => t.Id));
        });
    }

    private Task<HttpClient> StudentClientAsync(Seed seed)
        => ClientAsWithSchoolAsync(seed.StudentUserId, "Student", $"kc-{seed.StudentUserId}", seed.SchoolId, "Student");

    [Fact]
    public async Task Messageable_teacher_list_translates_to_sql_with_relations_and_excludes_blocked_unapproved_unrelated()
    {
        var seed = await SeedAsync();
        using var client = await StudentClientAsync(seed);

        var response = await client.GetAsync("/api/direct-messages/teachers?pageSize=50");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = (await response.Content.ReadFromJsonAsync<MessageableTeacherPageDto>(Json))!;

        // Varsayılan yapılandırma: DirectMessaging:AllowSameSchoolMessaging=false (#361) → yalnız atama yolu.
        page.Items.Select(i => (i.TeacherId, i.Relation, i.FullName)).ShouldBe(new[]
        {
            (seed.TeacherIds[seed.AssignerTeacher], "assignment", "Ata Atayan"),
            (seed.TeacherIds[seed.GradeAssignerTeacher], "assignment", "Gül Sınıf"),
        }, ignoreOrder: true);
        page.TotalCount.ShouldBe(2);

        var search = (await client.GetFromJsonAsync<MessageableTeacherPageDto>("/api/direct-messages/teachers?search=gül", Json))!;
        search.Items.Single().TeacherId.ShouldBe(seed.TeacherIds[seed.GradeAssignerTeacher]);

        // Gönderme aynı kuralı Postgres'te uygular: ilişkisiz / engelleyen / onaysız → aynı nötr 403.
        foreach (var teacher in new[] { seed.UnrelatedTeacher, seed.BlockingTeacher, seed.UnapprovedTeacher, seed.SameSchoolTeacher })
        {
            var denied = await client.PostAsJsonAsync($"/api/direct-messages/teachers/{seed.TeacherIds[teacher]}/messages", new { body = "selam" });
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            using var body = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("errorCode").GetString().ShouldBe(DirectMessageErrorCodes.CannotMessageTeacher);
        }

        var sent = await client.PostAsJsonAsync($"/api/direct-messages/teachers/{seed.TeacherIds[seed.GradeAssignerTeacher]}/messages", new { body = "selam" });
        sent.StatusCode.ShouldBe(HttpStatusCode.Created, await sent.Content.ReadAsStringAsync());
        var result = (await sent.Content.ReadFromJsonAsync<SendDirectMessageResultDto>(Json))!;
        result.ConversationCreated.ShouldBeTrue();
        result.DirectMessage!.Body.ShouldBe("selam");
    }

    [Fact]
    public async Task Concurrent_first_messages_create_a_single_conversation()
    {
        const int rounds = 5;
        const int parallel = 6;
        for (var round = 0; round < rounds; round++)
        {
            var seed = await SeedAsync();
            using var gate = new SemaphoreSlim(0, parallel);
            var tasks = Enumerable.Range(0, parallel).Select(i => Task.Run(async () =>
            {
                using var scope = Factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IDirectMessageService>();
                await gate.WaitAsync();
                return await service.SendToTeacherAsync(
                    new DirectMessageActor(seed.StudentUserId, $"kc-{seed.StudentUserId}", DirectMessageActorKind.Student),
                    seed.TeacherIds[seed.AssignerTeacher], new SendDirectMessageDto { Body = $"m{i}" });
            })).ToList();
            gate.Release(parallel);
            var results = await Task.WhenAll(tasks);

            results.ShouldAllBe(r => r.Success, $"round {round}");
            results.Select(r => r.ConversationId).Distinct().Count().ShouldBe(1, $"round {round}");
            results.Count(r => r.ConversationCreated).ShouldBe(1, $"round {round}");

            var (conversations, messages) = await WithDbAsync(async db => (
                await db.Conversations.CountAsync(c => c.StudentUserId == seed.StudentUserId),
                await db.DirectMessages.CountAsync(m => m.SenderUserId == seed.StudentUserId)));
            conversations.ShouldBe(1, $"round {round}");
            messages.ShouldBe(parallel, $"round {round}");
        }
    }

    [Fact]
    public async Task Teacher_inbox_report_idempotency_and_admin_list_over_http()
    {
        var seed = await SeedAsync();
        using var student = await StudentClientAsync(seed);
        var sent = await student.PostAsJsonAsync($"/api/direct-messages/teachers/{seed.TeacherIds[seed.AssignerTeacher]}/messages", new { body = "soru" });
        var conversationId = (await sent.Content.ReadFromJsonAsync<SendDirectMessageResultDto>(Json))!.ConversationId;

        using var teacher = await ClientAsWithSchoolAsync(seed.AssignerTeacher, "Teacher", $"kc-{seed.AssignerTeacher}", null, "Teacher");
        var inbox = (await teacher.GetFromJsonAsync<ConversationPageDto>("/api/direct-messages/inbox?filter=unread", Json))!;
        inbox.Items.Single().ConversationId.ShouldBe(conversationId);
        inbox.Items.Single().UnreadCount.ShouldBe(1);
        inbox.Items.Single().CounterpartName.ShouldBe("Ayşe Öğrenci");

        var thread = (await teacher.GetFromJsonAsync<ConversationMessagesDto>($"/api/direct-messages/conversations/{conversationId}/messages", Json))!;
        var messageId = thread.Items.Single().Id;
        // GET yan etkisiz; okundu ayrı uçla (ExecuteUpdate + audit alanları Postgres'te).
        (await teacher.GetFromJsonAsync<ConversationPageDto>("/api/direct-messages/inbox?filter=unread", Json))!.Items.Count.ShouldBe(1);
        var read = await teacher.PostAsJsonAsync($"/api/direct-messages/conversations/{conversationId}/read", new { upToMessageId = messageId });
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        (await read.Content.ReadFromJsonAsync<MarkDirectMessagesReadResultDto>(Json))!.MarkedCount.ShouldBe(1);
        (await teacher.GetFromJsonAsync<ConversationPageDto>("/api/direct-messages/inbox?filter=unread", Json))!.Items.ShouldBeEmpty();
        // Taraf olmayan öğretmen: 404 (oracle yok).
        using var outsider = await ClientAsWithSchoolAsync(seed.UnrelatedTeacher, "Teacher", $"kc-{seed.UnrelatedTeacher}", null, "Teacher");
        (await outsider.GetAsync($"/api/direct-messages/conversations/{conversationId}/messages")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await teacher.PostAsync($"/api/direct-messages/conversations/{conversationId}/block", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teacher.GetFromJsonAsync<ConversationPageDto>("/api/direct-messages/inbox?filter=blocked", Json))!.Items.Count.ShouldBe(1);
        var reply = await teacher.PostAsJsonAsync($"/api/direct-messages/conversations/{conversationId}/messages", new { body = "cevap" });
        reply.StatusCode.ShouldBe(HttpStatusCode.Created); // engel cevabı kapatmaz
        (await student.PostAsJsonAsync($"/api/direct-messages/conversations/{conversationId}/messages", new { body = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PostAsync($"/api/direct-messages/conversations/{conversationId}/block", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var first = await (await teacher.PostAsJsonAsync($"/api/direct-messages/conversations/{conversationId}/report",
            new { messageId, reason = "abuse", note = "not" })).Content.ReadFromJsonAsync<DirectMessageReportResultDto>(Json);
        var second = await (await teacher.PostAsJsonAsync($"/api/direct-messages/conversations/{conversationId}/report",
            new { messageId, reason = "spam" })).Content.ReadFromJsonAsync<DirectMessageReportResultDto>(Json);
        first!.AlreadyReported.ShouldBeFalse();
        second!.AlreadyReported.ShouldBeTrue();
        second.ReportId.ShouldBe(first.ReportId);

        (await student.GetAsync("/api/admin/direct-messages/reports")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.GetAsync("/api/admin/direct-messages/reports")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var adminId = NewUserId();
        using var admin = await ClientAsAsync(adminId, "Admin", $"kc-admin-{adminId}", "Admin");
        var reports = await admin.GetAsync("/api/admin/direct-messages/reports");
        reports.StatusCode.ShouldBe(HttpStatusCode.OK, await reports.Content.ReadAsStringAsync());
        var reportPage = (await reports.Content.ReadFromJsonAsync<DirectMessageReportPageDto>(Json))!;
        var item = reportPage.Items.Single(i => i.ReportId == first.ReportId);
        (item.MessageBody, item.Reason, item.Status, item.ReporterRole).ShouldBe(("soru", "abuse", "Open", "Teacher"));

        // security O3: admin şikayet listesi erişimi AdminDataAccessLogs'a yazılır.
        var audit = await WithDbAsync(db => db.AdminDataAccessLogs.AsNoTracking()
            .Where(a => a.ActorKeycloakId == $"kc-admin-{adminId}").ToListAsync());
        audit.Count.ShouldBe(1);
        (audit[0].Resource, audit[0].Outcome, audit[0].ReturnedCount)
            .ShouldBe((AdminDataAccessResource.DirectMessageReports, AdminDataAccessOutcome.Served, reportPage.Items.Count));
    }
}
