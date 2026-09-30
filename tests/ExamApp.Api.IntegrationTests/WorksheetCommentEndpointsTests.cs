using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #105 (dilim 1): GET/POST /api/worksheet/{id}/comments — gerçek PostgreSQL üzerinde (migration, timestamptz cursor,
/// string enum kolonu, CommentsEnabled default/sentinel) uçtan uca: yetki kapıları, ilgili öğretmen, JSON sözleşmesi,
/// rate limit.
/// </summary>
public class WorksheetCommentEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    // Respawn FakeUserDirectory'yi sıfırlamaz — benzersiz, büyük UserId'ler.
    private const int OwnerId = 105_001;
    private const int AssignerId = 105_002;
    private const int UnapprovedTeacherId = 105_003;
    private const int StudentAUserId = 105_101;
    private const int StudentBUserId = 105_102;
    private const int OutsiderUserId = 105_103;

    private static string Url(int worksheetId, string query = "") => $"/api/worksheet/{worksheetId}/comments{query}";

    private sealed record Seed(int WorksheetId, int Q1, int Q2, int Wq1, int StudentA, int AnswerQ1, int SchoolId = 0, int OtherSchoolId = 0);

    private async Task<Seed> SeedAsync(bool commentsEnabled = true)
    {
        Factory.Services.GetRequiredService<FakeUserDirectory>().Add(new UserLookupResultDto
        {
            Id = StudentAUserId, FullName = "Ayşe Kaya", Email = "ayse@mail.local", KeycloakId = "kc-105-a"
        });
        Factory.Services.GetRequiredService<FakeUserDirectory>().Add(new UserLookupResultDto
        {
            Id = AssignerId, FullName = "Ata Hoca", Email = "ata@mail.local", KeycloakId = "kc-105-t"
        });

        // issue #305 (okul kapsamı): öğrenciler ve öğretmenler aynı okulda — thread okul içinde paylaşılır.
        var (schoolId, otherSchoolId) = await WithDbAsync(async db =>
        {
            var school = new School { Name = "Okul 105" };
            var other = new School { Name = "Diğer Okul 105" };
            db.Schools.AddRange(school, other);
            await db.SaveChangesAsync();
            return (school.Id, other.Id);
        });
        await SeedApprovedTeacherAsync(OwnerId, schoolId);
        await SeedApprovedTeacherAsync(AssignerId, schoolId);
        await WithDbAsync(async db =>
        {
            db.Teachers.Add(new Teacher { UserId = UnapprovedTeacherId, AccountApprovedAt = null });
            await db.SaveChangesAsync();
        });

        return await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "5" };
            var otherGrade = new Grade { Name = "6" };
            db.AddRange(grade, otherGrade);
            await db.SaveChangesAsync();

            db.SetCurrentUser(OwnerId);
            var ws = new Worksheet
            {
                Name = "Kesirler", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, CommentsEnabled = commentsEnabled
            };
            var q1 = new Question { Text = "Q1" };
            var q2 = new Question { Text = "Q2" };
            db.AddRange(ws, q1, q2);
            await db.SaveChangesAsync();
            var answer = new Answer { QuestionId = q1.Id, Text = "A", Tag = "A", Order = 0 };
            db.Answers.Add(answer);
            var wq1 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q1.Id, Order = 1 };
            db.TestQuestions.AddRange(wq1, new WorksheetQuestion { TestId = ws.Id, QuestionId = q2.Id, Order = 2 });
            await db.SaveChangesAsync();

            db.SetCurrentUser(0);
            var a = new Student { UserId = StudentAUserId, StudentNumber = "A", GradeId = grade.Id, SchoolId = schoolId };
            var b = new Student { UserId = StudentBUserId, StudentNumber = "B", GradeId = grade.Id, SchoolId = schoolId };
            var outsider = new Student { UserId = OutsiderUserId, StudentNumber = "C", GradeId = otherGrade.Id, SchoolId = schoolId };
            db.AddRange(a, b, outsider);
            await db.SaveChangesAsync();

            db.SetCurrentUser(AssignerId);
            db.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = a.Id, StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync();

            return new Seed(ws.Id, q1.Id, q2.Id, wq1.Id, a.Id, answer.Id, schoolId, otherSchoolId);
        });
    }

    private Task<HttpClient> StudentA() => ClientAsAsync(StudentAUserId, "Student", "kc-105-a", "Student");
    private Task<HttpClient> StudentB() => ClientAsAsync(StudentBUserId, "Student", "kc-105-b", "Student");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Student_comment_teacher_reply_and_public_visibility_end_to_end()
    {
        var seed = await SeedAsync();
        var a = await StudentA();

        // Worksheet seviyesi: çözme şartı yok → 201 + sözleşme.
        var created = await a.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "  Hocam 3. soruyu anlamadım  " });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var comment = await JsonOf(created);
        var rootId = comment.GetProperty("id").GetInt32();
        comment.GetProperty("worksheetId").GetInt32().ShouldBe(seed.WorksheetId);
        comment.GetProperty("questionId").ValueKind.ShouldBe(JsonValueKind.Null);
        comment.GetProperty("parentCommentId").ValueKind.ShouldBe(JsonValueKind.Null);
        comment.GetProperty("authorRole").GetString().ShouldBe("Student");
        comment.GetProperty("isMine").GetBoolean().ShouldBeTrue();
        comment.GetProperty("body").GetString().ShouldBe("Hocam 3. soruyu anlamadım");
        comment.TryGetProperty("authorUserId", out _).ShouldBeFalse();
        comment.TryGetProperty("authorKeycloakId", out _).ShouldBeFalse();

        // Soru bazlı: başlatılmamış → 403 WorksheetNotStarted.
        var notStarted = await a.PostAsJsonAsync(Url(seed.WorksheetId), new { questionId = seed.Q1, body = "?" });
        notStarted.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonOf(notStarted)).GetProperty("errorCode").GetString().ShouldBe("WorksheetNotStarted");

        // Sahip ilgili öğretmen değil (atama var) → 403; atamayı yapan cevaplar → 201.
        var owner = await ClientAsAsync(OwnerId, "Teacher", "kc-105-owner", "Teacher");
        var ownerReply = await owner.PostAsJsonAsync(Url(seed.WorksheetId), new { parentCommentId = rootId, body = "sahip" });
        ownerReply.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonOf(ownerReply)).GetProperty("errorCode").GetString().ShouldBe("NotResponsibleTeacher");

        var assigner = await ClientAsAsync(AssignerId, "Teacher", "kc-105-t", "Teacher");
        var reply = await assigner.PostAsJsonAsync(Url(seed.WorksheetId), new { parentCommentId = rootId, body = "Payda eşitle." });
        reply.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await JsonOf(reply)).GetProperty("authorRole").GetString().ShouldBe("Teacher");

        // Reply'a reply → 400 InvalidParent.
        var replyId = (await JsonOf(reply)).GetProperty("id").GetInt32();
        var nested = await a.PostAsJsonAsync(Url(seed.WorksheetId), new { parentCommentId = replyId, body = "nested" });
        nested.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonOf(nested)).GetProperty("errorCode").GetString().ShouldBe("InvalidParent");

        // Erişimi olan başka öğrenci tüm thread'i görür.
        var page = await JsonOf(await (await StudentB()).GetAsync(Url(seed.WorksheetId)));
        page.GetProperty("canWrite").GetBoolean().ShouldBeTrue();
        page.GetProperty("lockReason").ValueKind.ShouldBe(JsonValueKind.Null);
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
        var thread = page.GetProperty("items")[0];
        thread.GetProperty("id").GetInt32().ShouldBe(rootId);
        thread.GetProperty("authorDisplayName").GetString().ShouldBe("Ayşe K.");
        thread.GetProperty("isMine").GetBoolean().ShouldBeFalse();
        thread.GetProperty("canReply").GetBoolean().ShouldBeTrue();
        var replies = thread.GetProperty("replies");
        replies.GetArrayLength().ShouldBe(1);
        replies[0].GetProperty("authorDisplayName").GetString().ShouldBe("Ata Hoca");
        page.GetRawText().ShouldNotContain("mail.local");
        page.GetRawText().ShouldNotContain("kc-105");

        // Öğretmen thread bazında canReply.
        var asAssigner = await JsonOf(await assigner.GetAsync(Url(seed.WorksheetId)));
        asAssigner.GetProperty("items")[0].GetProperty("canReply").GetBoolean().ShouldBeTrue();
        var asOwner = await JsonOf(await owner.GetAsync(Url(seed.WorksheetId)));
        asOwner.GetProperty("items")[0].GetProperty("canReply").GetBoolean().ShouldBeFalse();

        // Erişimi olmayan öğrenci → 404 WorksheetNotFound (issue #305: yok olan worksheet'ten ayırt edilemez).
        var outsider = await ClientAsAsync(OutsiderUserId, "Student", "kc-105-c", "Student");
        var denied = await outsider.GetAsync(Url(seed.WorksheetId));
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JsonOf(denied)).GetProperty("errorCode").GetString().ShouldBe("WorksheetNotFound");
        var missingWs = await outsider.GetAsync(Url(999_999));
        missingWs.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JsonOf(missingWs)).GetRawText().ShouldBe((await JsonOf(denied)).GetRawText());
    }

    [Fact]
    public async Task Answered_question_opens_the_question_thread()
    {
        var seed = await SeedAsync();
        await WithDbAsync(async db =>
        {
            db.TestInstances.Add(new WorksheetInstance
            {
                StudentId = seed.StudentA, WorksheetId = seed.WorksheetId, StartTime = DateTime.UtcNow,
                Status = WorksheetInstanceStatus.Started,
                WorksheetInstanceQuestions = new List<WorksheetInstanceQuestion>
                {
                    new() { WorksheetQuestionId = seed.Wq1, SelectedAnswerId = seed.AnswerQ1 }
                }
            });
            await db.SaveChangesAsync();
        });
        var a = await StudentA();

        var q2Page = await JsonOf(await a.GetAsync(Url(seed.WorksheetId, $"?questionId={seed.Q2}")));
        q2Page.GetProperty("canWrite").GetBoolean().ShouldBeFalse();
        q2Page.GetProperty("lockReason").GetString().ShouldBe("question-not-answered");

        var posted = await a.PostAsJsonAsync(Url(seed.WorksheetId), new { questionId = seed.Q1, body = "neden A?" });
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await JsonOf(posted)).GetProperty("questionOrder").GetInt32().ShouldBe(1); // issue #309
        var q1Page = await JsonOf(await a.GetAsync(Url(seed.WorksheetId, $"?questionId={seed.Q1}")));
        q1Page.GetProperty("items").GetArrayLength().ShouldBe(1);
        q1Page.GetProperty("items")[0].GetProperty("questionOrder").GetInt32().ShouldBe(1);
        q1Page.GetProperty("questionOrder").GetInt32().ShouldBe(1);
        q1Page.GetProperty("studentCommentsSummary").ValueKind.ShouldBe(JsonValueKind.Null); // öğrencide yok
        (await JsonOf(await a.GetAsync(Url(seed.WorksheetId)))).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Disabled_worksheet_is_persisted_and_locks_students_only()
    {
        var seed = await SeedAsync(commentsEnabled: false);
        (await WithDbAsync(db => db.Worksheets.Where(w => w.Id == seed.WorksheetId).Select(w => w.CommentsEnabled).SingleAsync()))
            .ShouldBeFalse();

        var b = await StudentB();
        var page = await JsonOf(await b.GetAsync(Url(seed.WorksheetId)));
        page.GetProperty("canWrite").GetBoolean().ShouldBeFalse();
        page.GetProperty("lockReason").GetString().ShouldBe("comments-disabled");
        var post = await b.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "x" });
        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonOf(post)).GetProperty("errorCode").GetString().ShouldBe("CommentsDisabled");

        var owner = await ClientAsAsync(OwnerId, "Teacher", "kc-105-owner-2", "Teacher");
        (await owner.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "duyuru" })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cursor_paging_round_trips_postgres_timestamps()
    {
        var seed = await SeedAsync();
        var ids = await WithDbAsync(async db =>
        {
            var list = new List<int>();
            for (var i = 0; i < 5; i++)
            {
                var c = new WorksheetComment
                {
                    WorksheetId = seed.WorksheetId, AuthorUserId = StudentBUserId, AuthorKeycloakId = "kc-105-b",
                    AuthorRole = WorksheetCommentAuthorRole.Student, Body = $"kök {i}"
                };
                db.WorksheetComments.Add(c);
                await db.SaveChangesAsync();
                list.Add(c.Id);
            }
            return list;
        });

        var b = await StudentB();
        var seen = new List<int>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var query = "?take=2" + (cursor == null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await JsonOf(await b.GetAsync(Url(seed.WorksheetId, query)));
            foreach (var item in page.GetProperty("items").EnumerateArray())
                seen.Add(item.GetProperty("id").GetInt32());
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
            pages++;
        } while (cursor != null && pages < 10);

        pages.ShouldBe(3);
        seen.ShouldBe(Enumerable.Reverse(ids).ToList());

        var bad = await b.GetAsync(Url(seed.WorksheetId, "?cursor=%%%"));
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Retired_worksheet_thread_is_readable_only_by_students_who_solved_it()
    {
        var seed = await SeedAsync();
        var b = await StudentB();
        (await b.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "soru" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        await WithDbAsync(async db =>
        {
            var ws = await db.Worksheets.SingleAsync(w => w.Id == seed.WorksheetId);
            ws.IsDeleted = true;
            await db.SaveChangesAsync();
        });

        // Instance'ı yok (yalnızca grade uyumuyla erişiyordu) → retired'da kapalı (404, #305).
        var denied = await b.GetAsync(Url(seed.WorksheetId));
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JsonOf(denied)).GetProperty("errorCode").GetString().ShouldBe("WorksheetNotFound");

        await WithDbAsync(async db =>
        {
            var studentB = await db.Students.SingleAsync(s => s.UserId == StudentBUserId);
            db.TestInstances.Add(new WorksheetInstance
            {
                StudentId = studentB.Id, WorksheetId = seed.WorksheetId, StartTime = DateTime.UtcNow,
                Status = WorksheetInstanceStatus.Completed
            });
            await db.SaveChangesAsync();
        });

        var page = await JsonOf(await b.GetAsync(Url(seed.WorksheetId)));
        page.GetProperty("items").GetArrayLength().ShouldBe(1);
        var owner = await ClientAsAsync(OwnerId, "Teacher", "kc-105-owner-retired", "Teacher");
        (await JsonOf(await owner.GetAsync(Url(seed.WorksheetId)))).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Thread_list_caps_replies_and_the_replies_endpoint_pages_the_rest()
    {
        var seed = await SeedAsync();
        var a = await ClientAsAsync(StudentAUserId, "Student", $"kc-105-rep-a-{Guid.NewGuid():N}", "Student");
        var root = (await JsonOf(await a.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "kök" }))).GetProperty("id").GetInt32();
        var replyIds = await WithDbAsync(async db =>
        {
            var list = new List<int>();
            for (var i = 0; i < 7; i++)
            {
                var c = new WorksheetComment
                {
                    WorksheetId = seed.WorksheetId, ParentCommentId = root, AuthorUserId = StudentBUserId,
                    AuthorKeycloakId = "kc-105-b", AuthorRole = WorksheetCommentAuthorRole.Student, Body = $"reply {i}",
                    AuthorSchoolId = seed.SchoolId // #305: yazma yolu yazarın okulunu sabitler
                };
                db.WorksheetComments.Add(c);
                await db.SaveChangesAsync();
                list.Add(c.Id);
            }
            return list;
        });

        // Pinned responsible teacher (kayıtta).
        (await WithDbAsync(db => db.WorksheetComments.Where(c => c.Id == root).Select(c => c.ResponsibleTeacherUserId).SingleAsync()))
            .ShouldBe(AssignerId);

        var thread = (await JsonOf(await a.GetAsync(Url(seed.WorksheetId)))).GetProperty("items")[0];
        thread.GetProperty("replyCount").GetInt32().ShouldBe(7);
        thread.GetProperty("replies").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ShouldBe(replyIds.Skip(2));

        var seen = new List<int>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var query = "?take=3" + (cursor == null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await a.GetAsync($"{Url(seed.WorksheetId)}/{root}/replies{query}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var page = await JsonOf(response);
            page.GetProperty("replyCount").GetInt32().ShouldBe(7);
            page.GetProperty("canReply").GetBoolean().ShouldBeTrue();
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()));
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
            pages++;
        } while (cursor != null && pages < 10);

        pages.ShouldBe(3);
        seen.ShouldBe(replyIds);

        var notRoot = await a.GetAsync($"{Url(seed.WorksheetId)}/{replyIds[0]}/replies");
        notRoot.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JsonOf(notRoot)).GetProperty("errorCode").GetString().ShouldBe("RootCommentNotFound");
    }

    [Fact]
    public async Task Comment_reads_are_rate_limited_per_user_across_both_read_endpoints()
    {
        var seed = await SeedAsync();
        var b = await ClientAsAsync(StudentBUserId, "Student", $"kc-105-rrl-{Guid.NewGuid():N}", "Student");
        var root = (await JsonOf(await b.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "kök" }))).GetProperty("id").GetInt32();

        for (var i = 0; i < 30; i++)
            (await b.GetAsync(Url(seed.WorksheetId))).StatusCode.ShouldBe(HttpStatusCode.OK);
        for (var i = 0; i < 30; i++)
            (await b.GetAsync($"{Url(seed.WorksheetId)}/{root}/replies")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var limitedThread = await b.GetAsync(Url(seed.WorksheetId));
        await ShouldBeRateLimitedJsonAsync(limitedThread);
        await ShouldBeRateLimitedJsonAsync(await b.GetAsync($"{Url(seed.WorksheetId)}/{root}/replies"));
    }

    /// <summary>issue #309: 429 gövdesi diğer hatalarla aynı JSON biçiminde + Retry-After başlığı korunur.</summary>
    private static async Task ShouldBeRateLimitedJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.ShouldNotBeNull();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var body = await JsonOf(response);
        body.GetProperty("errorCode").GetString().ShouldBe("RateLimited");
        body.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Teacher_thread_carries_the_student_comments_summary_scoped_to_visible_assignments()
    {
        var seed = await SeedAsync(commentsEnabled: false);
        await WithDbAsync(async db =>
        {
            db.SetCurrentUser(AssignerId);
            var own = await db.WorksheetAssignments.SingleAsync(x => x.WorksheetId == seed.WorksheetId);
            own.CommentsEnabledOverride = true;
            db.SetCurrentUser(OwnerId);
            db.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = seed.WorksheetId, StudentId = seed.StudentA, StartAt = DateTime.UtcNow.AddDays(-1),
                CommentsEnabledOverride = false
            });
            await db.SaveChangesAsync();
        });

        var owner = await ClientAsAsync(OwnerId, "Teacher", "kc-105-owner", "Teacher");
        var ownerSummary = (await JsonOf(await owner.GetAsync(Url(seed.WorksheetId)))).GetProperty("studentCommentsSummary");
        ownerSummary.GetProperty("worksheetDefault").GetBoolean().ShouldBeFalse();
        // Sahip yalnız kendi oluşturduğu atamaları sayar (security L1): Assigner'ın true'su sızmaz.
        ownerSummary.GetProperty("assignmentOverrides").GetProperty("enabled").GetInt32().ShouldBe(0);
        ownerSummary.GetProperty("assignmentOverrides").GetProperty("disabled").GetInt32().ShouldBe(1);

        var assigner = await ClientAsAsync(AssignerId, "Teacher", "kc-105-t", "Teacher");
        var assignerSummary = (await JsonOf(await assigner.GetAsync(Url(seed.WorksheetId)))).GetProperty("studentCommentsSummary");
        assignerSummary.GetProperty("assignmentOverrides").GetProperty("enabled").GetInt32().ShouldBe(1);
        assignerSummary.GetProperty("assignmentOverrides").GetProperty("disabled").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Unapproved_teacher_and_unknown_worksheet_are_rejected()
    {
        var seed = await SeedAsync();

        var unapproved = await ClientAsAsync(UnapprovedTeacherId, "Teacher", "kc-105-unapproved", "Teacher");
        (await unapproved.GetAsync(Url(seed.WorksheetId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await unapproved.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var missing = await (await StudentB()).GetAsync(Url(999_999));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JsonOf(missing)).GetProperty("errorCode").GetString().ShouldBe("WorksheetNotFound");

        (await Anonymous().GetAsync(Url(seed.WorksheetId))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Comment_writes_are_rate_limited_per_user()
    {
        var seed = await SeedAsync();
        var sub = $"kc-105-rl-{Guid.NewGuid():N}";
        var b = await ClientAsAsync(StudentBUserId, "Student", sub, "Student");

        for (var i = 0; i < 5; i++)
            (await b.PostAsJsonAsync(Url(seed.WorksheetId), new { body = $"yorum {i}" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var limited = await b.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "fazla" });
        await ShouldBeRateLimitedJsonAsync(limited);

        // Okuma limitli değil; başka kullanıcının kovası ayrı.
        (await b.GetAsync(Url(seed.WorksheetId))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var a = await ClientAsAsync(StudentAUserId, "Student", $"kc-105-rl-{Guid.NewGuid():N}", "Student");
        (await a.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "ben" })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // ---- issue #305: moderasyon, okul kapsamı --------------------------------------------------------------

    private const int OtherSchoolStudentUserId = 105_104;
    private const int AdminUserId = 105_900;

    private static string Sub(string name) => $"kc-305-{name}-{Guid.NewGuid():N}";

    private static List<int> ItemIds(JsonElement page) => page.GetProperty("items").EnumerateArray()
        .Select(i => i.GetProperty("id").GetInt32()).OrderBy(x => x).ToList();

    [Fact]
    public async Task Report_hide_unhide_and_moderator_lists_end_to_end()
    {
        var seed = await SeedAsync();
        var a = await ClientAsAsync(StudentAUserId, "Student", Sub("a"), "Student");
        var b = await ClientAsAsync(StudentBUserId, "Student", Sub("b"), "Student");
        var assigner = await ClientAsAsync(AssignerId, "Teacher", Sub("t"), "Teacher");
        var owner = await ClientAsAsync(OwnerId, "Teacher", Sub("owner"), "Teacher");

        var rootId = (await JsonOf(await a.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "numaram 0555 555 55 55" })))
            .GetProperty("id").GetInt32();
        var replyId = (await JsonOf(await b.PostAsJsonAsync(Url(seed.WorksheetId), new { parentCommentId = rootId, body = "B" })))
            .GetProperty("id").GetInt32();

        // Şikayet: 200, tekrar idempotent; kendi yorumu 403; geçersiz neden 400.
        var reported = await b.PostAsJsonAsync($"{Url(seed.WorksheetId)}/{rootId}/report", new { reason = "personalInfo", note = "telefon" });
        reported.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(reported)).GetProperty("alreadyReported").GetBoolean().ShouldBeFalse();
        var again = await b.PostAsJsonAsync($"{Url(seed.WorksheetId)}/{rootId}/report", new { reason = "spam" });
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(again)).GetProperty("alreadyReported").GetBoolean().ShouldBeTrue();
        var own = await a.PostAsJsonAsync($"{Url(seed.WorksheetId)}/{rootId}/report", new { reason = "spam" });
        own.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonOf(own)).GetProperty("errorCode").GetString().ShouldBe("CannotReportOwnComment");
        var bad = await a.PostAsJsonAsync($"{Url(seed.WorksheetId)}/{replyId}/report", new { reason = "nope" });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonOf(bad)).GetProperty("errorCode").GetString().ShouldBe("InvalidReportReason");

        var asB = (await JsonOf(await b.GetAsync(Url(seed.WorksheetId)))).GetProperty("items")[0];
        asB.GetProperty("reportedByMe").GetBoolean().ShouldBeTrue();
        asB.GetProperty("reportCount").ValueKind.ShouldBe(JsonValueKind.Null);
        asB.GetProperty("canModerate").GetBoolean().ShouldBeFalse();
        asB.GetProperty("isHidden").GetBoolean().ShouldBeFalse();

        // Öğrenci gizleyemez / moderatör listesini göremez (rol kapısı).
        (await b.PostAsJsonAsync($"{Url(seed.WorksheetId)}/{rootId}/hide", new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await b.GetAsync($"{Url(seed.WorksheetId)}/reports")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Sorumlu öğretmen (atayan) gizler: yanıt moderatör görünümünde.
        var hide = await assigner.PostAsJsonAsync($"{Url(seed.WorksheetId)}/{rootId}/hide", new { reason = "telefon numarası" });
        hide.StatusCode.ShouldBe(HttpStatusCode.OK);
        var hidden = await JsonOf(hide);
        hidden.GetProperty("isHidden").GetBoolean().ShouldBeTrue();
        hidden.GetProperty("body").GetString().ShouldBe("numaram 0555 555 55 55");
        hidden.GetProperty("hiddenReason").GetString().ShouldBe("telefon numarası");
        hidden.GetProperty("canModerate").GetBoolean().ShouldBeTrue();
        hidden.GetProperty("reportCount").GetInt32().ShouldBe(1);

        // Öğrenci: "kaldırıldı" yer tutucu, reply duruyor, yeni reply 403.
        var placeholder = (await JsonOf(await b.GetAsync(Url(seed.WorksheetId, "?moderatorView=true")))).GetProperty("items")[0];
        placeholder.GetProperty("isHidden").GetBoolean().ShouldBeTrue();
        placeholder.GetProperty("body").ValueKind.ShouldBe(JsonValueKind.Null);
        placeholder.GetProperty("authorDisplayName").GetString().ShouldBe("Kaldırıldı");
        placeholder.GetProperty("hiddenReason").ValueKind.ShouldBe(JsonValueKind.Null);
        placeholder.GetProperty("canReply").GetBoolean().ShouldBeFalse();
        placeholder.GetProperty("replies")[0].GetProperty("id").GetInt32().ShouldBe(replyId);
        placeholder.GetRawText().ShouldNotContain("0555");
        var blocked = await b.PostAsJsonAsync(Url(seed.WorksheetId), new { parentCommentId = rootId, body = "yine" });
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonOf(blocked)).GetProperty("errorCode").GetString().ShouldBe("RootCommentHidden");

        // Sahip moderatör görünümünde gövdeyi görür; varsayılan görünümde görmez.
        (await JsonOf(await owner.GetAsync(Url(seed.WorksheetId)))).GetProperty("items")[0].GetProperty("body").ValueKind
            .ShouldBe(JsonValueKind.Null);
        (await JsonOf(await owner.GetAsync(Url(seed.WorksheetId, "?moderatorView=true")))).GetProperty("items")[0]
            .GetProperty("body").GetString().ShouldBe("numaram 0555 555 55 55");

        // Moderatör listesi (worksheet) + global admin listesi.
        var reports = await JsonOf(await owner.GetAsync($"{Url(seed.WorksheetId)}/reports?page=1&pageSize=10"));
        reports.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var item = reports.GetProperty("items")[0];
        item.GetProperty("comment").GetProperty("id").GetInt32().ShouldBe(rootId);
        item.GetProperty("reportCount").GetInt32().ShouldBe(1);
        item.GetProperty("reasons").GetProperty("personalInfo").GetInt32().ShouldBe(1);
        item.GetProperty("notes")[0].GetString().ShouldBe("telefon");
        item.GetProperty("worksheetTitle").GetString().ShouldBe("Kesirler");
        item.GetRawText().ShouldNotContain("kc-305");

        var admin = await ClientAsAsync(AdminUserId, "Admin", Sub("admin"), "Admin");
        var global = await admin.GetAsync("/api/admin/comments/reports");
        global.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(global)).GetProperty("totalCount").GetInt32().ShouldBe(1);
        (await owner.GetAsync("/api/admin/comments/reports")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Açma + audit (neden audit'e yazılmaz — tabloda böyle bir kolon yok).
        var unhide = await owner.PostAsync($"{Url(seed.WorksheetId)}/{rootId}/unhide", null);
        unhide.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(unhide)).GetProperty("isHidden").GetBoolean().ShouldBeFalse();
        var logs = await WithDbAsync(db => db.AdminUserActionLogs
            .Where(l => l.TargetType == AdminUserTargetType.WorksheetComment && l.TargetId == rootId)
            .OrderBy(l => l.Id).ToListAsync());
        logs.Select(l => l.Action).ShouldBe(new[] { AdminUserAction.CommentHidden, AdminUserAction.CommentUnhidden });
        logs.ShouldAllBe(l => l.Outcome == AdminUserActionOutcome.Succeeded);
        var stored = await WithDbAsync(db => db.WorksheetComments.SingleAsync(c => c.Id == rootId));
        stored.HiddenAt.ShouldBeNull();
        stored.HiddenReason.ShouldBeNull();
        stored.AuthorSchoolId.ShouldBe(seed.SchoolId);
    }

    [Fact]
    public async Task Student_comments_are_limited_to_the_authors_school()
    {
        var seed = await SeedAsync();
        await WithDbAsync(async db =>
        {
            var gradeId = await db.Students.Where(s => s.UserId == StudentAUserId).Select(s => s.GradeId).SingleAsync();
            db.Students.Add(new Student { UserId = OtherSchoolStudentUserId, StudentNumber = "D", GradeId = gradeId, SchoolId = seed.OtherSchoolId });
            await db.SaveChangesAsync();
        });
        var a = await ClientAsAsync(StudentAUserId, "Student", Sub("a"), "Student");
        var b = await ClientAsAsync(StudentBUserId, "Student", Sub("b"), "Student");
        var other = await ClientAsAsync(OtherSchoolStudentUserId, "Student", Sub("d"), "Student");
        var owner = await ClientAsAsync(OwnerId, "Teacher", Sub("owner"), "Teacher");

        var rootA = (await JsonOf(await a.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "A" }))).GetProperty("id").GetInt32();
        var rootD = (await JsonOf(await other.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "D" }))).GetProperty("id").GetInt32();
        var announcement = (await JsonOf(await owner.PostAsJsonAsync(Url(seed.WorksheetId), new { body = "duyuru" }))).GetProperty("id").GetInt32();
        (await a.PostAsJsonAsync(Url(seed.WorksheetId), new { parentCommentId = announcement, body = "A cevap" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await other.PostAsJsonAsync(Url(seed.WorksheetId), new { parentCommentId = announcement, body = "D cevap" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var asB = await JsonOf(await b.GetAsync(Url(seed.WorksheetId)));
        ItemIds(asB).ShouldBe(new[] { rootA, announcement }.OrderBy(x => x).ToList());
        asB.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == announcement)
            .GetProperty("replyCount").GetInt32().ShouldBe(1);
        ItemIds(await JsonOf(await other.GetAsync(Url(seed.WorksheetId)))).ShouldBe(new[] { rootD, announcement }.OrderBy(x => x).ToList());

        // Kapsam dışı köke cevap: 400 InvalidParent (var olduğu sızmaz); replies ucu ve şikayet 404.
        var cross = await other.PostAsJsonAsync(Url(seed.WorksheetId), new { parentCommentId = rootA, body = "x" });
        cross.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonOf(cross)).GetProperty("errorCode").GetString().ShouldBe("InvalidParent");
        (await other.GetAsync($"{Url(seed.WorksheetId)}/{rootA}/replies")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync($"{Url(seed.WorksheetId)}/{rootA}/report", new { reason = "spam" })).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // Atayan öğretmen (okul içi, yalnız A'nın sorumlusu): diğer okulun kökünü ve o öğrencinin duyuru cevabını görmez.
        var assigner = await ClientAsAsync(AssignerId, "Teacher", Sub("t"), "Teacher");
        var asAssigner = await JsonOf(await assigner.GetAsync(Url(seed.WorksheetId)));
        ItemIds(asAssigner).ShouldBe(new[] { rootA, announcement }.OrderBy(x => x).ToList());
        asAssigner.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == announcement)
            .GetProperty("replyCount").GetInt32().ShouldBe(1);

        // Sahip: D'nin kökünün sorumlusu (atama yok → sahip) olduğu için onu da görür; kendi duyurusunun tüm cevaplarını görür.
        var asOwner = await JsonOf(await owner.GetAsync(Url(seed.WorksheetId)));
        ItemIds(asOwner).ShouldBe(new[] { rootA, rootD, announcement }.OrderBy(x => x).ToList());
        asOwner.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == announcement)
            .GetProperty("replyCount").GetInt32().ShouldBe(2);
    }
}
