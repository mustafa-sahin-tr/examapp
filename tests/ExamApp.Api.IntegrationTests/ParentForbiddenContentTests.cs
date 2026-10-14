using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #424 (epic #407 V6) — "veli asla görmez" listesi, test olarak. Çocuğun etrafına veliye GÖSTERİLMEMESİ gereken her şey
/// işaretli değerlerle tohumlanır (DM, worksheet yorumu, başka öğrencinin adı/id'si/puanı/rozeti, soru metni/görseli, cevap
/// anahtarı, doğru ve seçilen şık, e-posta/avatar/öğrenci no, ders ücreti, öğretmen biyografisi, ret nedeni, hatırlatma iç
/// alanları, görüşme odası) ve <see cref="ParentEndpointCatalog"/>'daki HER okuma ucu (sorgu varyantlarıyla) bağlı veli olarak
/// çağrılır. Serileştirilmiş yanıtlar üç katmanda taranır: yasak içerik (işaretler), yasak alan adları (JSON ağacının tamamı)
/// ve kimlik alanları (her <c>studentId</c> bu çocuğun, her <c>testInstanceId</c> bu çocuğun oturumu). Uç başına alan listesi
/// kilitleri (sözleşme) Parent*EndpointsTests'te kalır; bu tarama uçtan bağımsız, katalogla birlikte büyür.
/// </summary>
public class ParentForbiddenContentTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int RivalXp = 98765;

    private const int StudentUser = 49101, OtherStudentUser = 49102, ParentUser = 49103, TeacherUser = 49104, SecondParentUser = 49105,
        ThirdParentUser = 49106;

    /// <summary>Hiçbir veli yanıtında geçmemesi gereken içerik (büyük/küçük harf duyarsız).</summary>
    private static readonly string[] ForbiddenContent =
    [
        "GIZLI",            // DM, yorum, soru/şık metni, açıklama, ret nedeni, biyografi, hatırlatma iç alanları
        "gizli/",           // soru görseli yolu
        "secret",           // e-posta adresleri (öğrenci, rakip, öğretmen, veli)
        "@",                // hiçbir e-posta biçimi
        "avatar-",          // avatar yolları
        "Rakip",            // başka öğrencinin adı / rozeti
        $"P{StudentUser}",  // öğrenci numarası
        $"P{OtherStudentUser}",
        "kc-",              // Keycloak kimlikleri
        "booking-",         // Jitsi oda adı biçimi (booking-{id}-{hmac})
        "jitsi",
        "http",             // hiçbir bağlantı
        "987.65", "987,65", // ders ücreti
    ];

    /// <summary>Hiçbir veli yanıtında (herhangi bir derinlikte) bulunmaması gereken alan adları (büyük/küçük harf duyarsız).</summary>
    private static readonly HashSet<string> ForbiddenProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        // iletişim / kimlik
        "email", "emailMasked", "parentEmailMasked", "phone", "phoneNumber", "avatar", "userId", "studentUserId", "teacherUserId",
        "keycloakId", "studentNumber", "parentId", "teacherId",
        // mesaj / yorum
        "body", "comment", "comments", "commentId", "messages", "messageId", "conversationId", "directMessage", "directMessages",
        // soru içeriği / cevap anahtarı / seçilen şık
        "text", "subText", "imageUrl", "question", "questions", "answer", "answers", "answerKey", "correctAnswer",
        "correctAnswerId", "correctOption", "practiceCorrectAnswer", "selectedAnswer", "selectedAnswerId", "selectedOption",
        "isCorrect", "description",
        // ders gizli alanları
        "bookingId", "meetingUrl", "joinUrl", "roomName", "room", "videoUrl", "jitsi", "link", "price", "hourlyRate", "rate",
        "fee", "note", "notes", "rejectionReason", "bio",
        // hatırlatma iç alanları
        "hangfireJobId", "studentKeycloakId", "remindBeforeMinutes",
    };

    private sealed record Seeded(int StudentId, int OtherStudentId, int[] OwnInstanceIds, int PrimaryLinkId);

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static DateOnly LocalToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Istanbul));

    private async Task<Seeded> SeedAsync()
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = StudentUser, KeycloakId = $"kc-{StudentUser}", FullName = "Ayşe Kaya", Email = "child-secret@x.com", Avatar = "avatar-child.png" });
        directory.Add(new() { Id = OtherStudentUser, KeycloakId = $"kc-{OtherStudentUser}", FullName = "Rakip Öğrenci", Email = "rakip-secret@x.com", Avatar = "avatar-rakip.png" });
        directory.Add(new() { Id = TeacherUser, KeycloakId = $"kc-{TeacherUser}", FullName = "Zeynep Hoca", Email = "teacher-secret@x.com", Avatar = "avatar-teacher.png" });
        directory.Add(new() { Id = ParentUser, KeycloakId = $"kc-{ParentUser}", FullName = "Veli Bey", Email = "parent-secret@x.com" });
        directory.Add(new() { Id = SecondParentUser, KeycloakId = $"kc-{SecondParentUser}", FullName = "İkinci Veli", Email = "parent2-secret@x.com" });
        directory.Add(new() { Id = ThirdParentUser, KeycloakId = $"kc-{ThirdParentUser}", FullName = "Üçüncü Veli", Email = "parent3-secret@x.com" });

        var now = DateTime.UtcNow;
        return await WithDbAsync(async db =>
        {
            var school = new School { Name = "Atatürk Ortaokulu" };
            var grade = new Grade { Name = "7. Sınıf" };
            var subject = new Subject { Name = "Matematik" };
            db.AddRange(school, grade, subject);
            await db.SaveChangesAsync();
            var topic = new Topic { Name = "Kesir işlemleri", SubjectId = subject.Id, GradeId = grade.Id };
            db.Topics.Add(topic);

            Student NewStudent(int userId) => new()
            {
                UserId = userId, StudentNumber = $"P{userId}", SchoolId = school.Id, SchoolVerifiedAt = now, GradeId = grade.Id
            };
            var student = NewStudent(StudentUser);
            var other = NewStudent(OtherStudentUser);
            db.Students.AddRange(student, other);
            var parent = new Parent { UserId = ParentUser };
            var secondParent = new Parent { UserId = SecondParentUser };
            db.Parents.AddRange(parent, secondParent, new Parent { UserId = ThirdParentUser });
            var teacher = new Teacher
            {
                UserId = TeacherUser, IsIndependentTutor = true, HourlyRate = 987.65m, Bio = "GIZLI-BIO", AccountApprovedAt = now
            };
            db.Teachers.Add(teacher);
            await db.SaveChangesAsync();

            // issue #436: birincil veli (çocuğun hesabını açan) + birincil velinin onayladığı ikinci veli (Active, birincil değil).
            var primaryLink = new ParentStudentLink
            {
                ParentId = parent.Id, StudentId = student.Id, Status = ParentStudentLinkStatus.Active,
                Origin = ParentStudentLinkOrigin.ParentCreated, IsPrimary = true,
                CreatedAt = now.AddDays(-2), ActivatedAt = now.AddDays(-2)
            };
            db.ParentStudentLinks.AddRange(primaryLink, new ParentStudentLink
            {
                ParentId = secondParent.Id, StudentId = student.Id, Status = ParentStudentLinkStatus.Active,
                Origin = ParentStudentLinkOrigin.InviteCode, IsPrimary = false,
                CreatedAt = now.AddDays(-2), ActivatedAt = now.AddDays(-1)
            });

            // ---- ödevler: bitmiş (soru + şık + cevap anahtarı + seçilen şık), gecikmiş, açık
            var done = new Worksheet { Name = "Kesirler", Description = "GIZLI-ACIKLAMA", GradeId = grade.Id, SubjectId = subject.Id };
            var missed = new Worksheet { Name = "Oran", Description = "GIZLI-ACIKLAMA", GradeId = grade.Id };
            var open = new Worksheet { Name = "Ondalık", Description = "GIZLI-ACIKLAMA", GradeId = grade.Id };
            db.Worksheets.AddRange(done, missed, open);
            await db.SaveChangesAsync();
            var assignments = new[]
            {
                new WorksheetAssignment { WorksheetId = done.Id, StudentId = student.Id, StartAt = now.AddDays(-3), EndAt = now.AddDays(3) },
                new WorksheetAssignment { WorksheetId = missed.Id, StudentId = student.Id, StartAt = now.AddDays(-5), EndAt = now.AddDays(-1) },
                new WorksheetAssignment { WorksheetId = open.Id, GradeId = grade.Id, SchoolId = school.Id, StartAt = now.AddDays(-1) },
            };
            db.WorksheetAssignments.AddRange(assignments);

            var ownInstance = new WorksheetInstance
            {
                StudentId = student.Id, WorksheetId = done.Id, StartTime = now.AddMinutes(-30), EndTime = now.AddMinutes(-10),
                Status = WorksheetInstanceStatus.Completed
            };
            var othersInstance = new WorksheetInstance
            {
                StudentId = other.Id, WorksheetId = done.Id, StartTime = now.AddMinutes(-60), EndTime = now.AddMinutes(-40),
                Status = WorksheetInstanceStatus.Completed
            };
            db.TestInstances.AddRange(ownInstance, othersInstance);
            await db.SaveChangesAsync();
            var assignmentIds = assignments.Select(a => a.Id).ToList();
            await db.WorksheetAssignments.Where(a => assignmentIds.Contains(a.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.CreateUserId, TeacherUser));

            for (var i = 0; i < 3; i++)
            {
                var question = new Question
                {
                    Text = $"GIZLI-SORU-{i}", SubText = "GIZLI-ALT", ImageUrl = $"gizli/soru-{i}.png", Point = 1,
                    TopicId = topic.Id, PracticeCorrectAnswer = "GIZLI-PRATIK"
                };
                db.Questions.Add(question);
                await db.SaveChangesAsync();
                var right = new Answer { QuestionId = question.Id, Text = "GIZLI-DOGRU", Tag = "A", ImageUrl = "gizli/dogru.png" };
                var wrong = new Answer { QuestionId = question.Id, Text = "GIZLI-YANLIS", Tag = "B" };
                var wq = new WorksheetQuestion { TestId = done.Id, QuestionId = question.Id, Order = i + 1 };
                db.AddRange(right, wrong, wq);
                await db.SaveChangesAsync();
                question.CorrectAnswerId = right.Id;
                db.TestInstanceQuestions.AddRange(
                    new WorksheetInstanceQuestion
                    {
                        WorksheetInstanceId = ownInstance.Id, WorksheetQuestionId = wq.Id,
                        SelectedAnswerId = i == 0 ? right.Id : wrong.Id, IsCorrect = i == 0, UpdateTime = now.AddMinutes(-20)
                    },
                    new WorksheetInstanceQuestion
                    {
                        WorksheetInstanceId = othersInstance.Id, WorksheetQuestionId = wq.Id, SelectedAnswerId = right.Id,
                        IsCorrect = true, UpdateTime = now.AddMinutes(-50)
                    });
                await db.SaveChangesAsync();
            }

            // ---- yorumlar (öğrencinin sorusu + öğretmen cevabı) ve DM'ler
            var comment = new WorksheetComment
            {
                WorksheetId = done.Id, AuthorUserId = StudentUser, AuthorKeycloakId = $"kc-{StudentUser}",
                AuthorRole = WorksheetCommentAuthorRole.Student, Body = "GIZLI-YORUM", AuthorSchoolId = school.Id
            };
            db.WorksheetComments.Add(comment);
            await db.SaveChangesAsync();
            db.WorksheetComments.Add(new WorksheetComment
            {
                WorksheetId = done.Id, AuthorUserId = TeacherUser, AuthorKeycloakId = $"kc-{TeacherUser}",
                AuthorRole = WorksheetCommentAuthorRole.Teacher, Body = "GIZLI-YORUM-CEVAP", ParentCommentId = comment.Id
            });
            var conversation = new Conversation { StudentUserId = StudentUser, TeacherUserId = TeacherUser, LastMessageAt = now };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();
            db.DirectMessages.AddRange(
                new DirectMessage { ConversationId = conversation.Id, SenderUserId = StudentUser, SenderRole = DirectMessageSenderRole.Student, Body = "GIZLI-DM-OGRENCI" },
                new DirectMessage { ConversationId = conversation.Id, SenderUserId = TeacherUser, SenderRole = DirectMessageSenderRole.Teacher, Body = "GIZLI-DM-OGRETMEN" });

            // ---- puan / rozet (çocuk + rakip)
            db.StudentPoints.AddRange(new StudentPoint { StudentId = student.Id, XP = 1200 }, new StudentPoint { StudentId = other.Id, XP = RivalXp });
            db.StudentBadgeProjections.AddRange(
                new StudentBadgeProjection
                {
                    StudentId = student.Id, BadgeDefinitionId = Guid.NewGuid(), Name = "İlk Adım", Icon = "rocket_launch",
                    EarnedAtUtc = now.AddDays(-1), ReceivedAtUtc = now
                },
                new StudentBadgeProjection
                {
                    StudentId = other.Id, BadgeDefinitionId = Guid.NewGuid(), Name = "Rakip Rozeti", Icon = "star",
                    EarnedAtUtc = now.AddDays(-1), ReceivedAtUtc = now
                });

            // ---- program: hatırlatmalar + ders randevuları (onaylı / bekleyen / reddedilmiş, rakibinki)
            var day = LocalToday.AddDays(2);
            db.WorksheetReminders.AddRange(
                new WorksheetReminder
                {
                    WorksheetId = open.Id, StudentId = student.Id, ScheduledFor = day.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
                    Status = WorksheetReminderStatus.Pending, RemindBeforeMinutes = 4321, HangfireJobId = "GIZLI-JOB",
                    StudentKeycloakId = $"kc-{StudentUser}"
                },
                new WorksheetReminder
                {
                    WorksheetId = open.Id, StudentId = other.Id, ScheduledFor = day.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
                    Status = WorksheetReminderStatus.Pending, RemindBeforeMinutes = 4321, HangfireJobId = "GIZLI-JOB-RAKIP",
                    StudentKeycloakId = $"kc-{OtherStudentUser}"
                });
            await db.SaveChangesAsync();

            var lessons = new (int StudentId, int Hour, BookingStatus Status, string? Reason)[]
            {
                (student.Id, 10, BookingStatus.Approved, null),
                (student.Id, 12, BookingStatus.Pending, null),
                (student.Id, 14, BookingStatus.Rejected, "GIZLI-RET"),
                (other.Id, 16, BookingStatus.Approved, null),
            };
            foreach (var l in lessons)
            {
                var slot = new TeacherAvailabilitySlot
                {
                    TeacherId = teacher.Id, Date = day, StartTime = new TimeOnly(l.Hour, 0), EndTime = new TimeOnly(l.Hour + 1, 0),
                    CreatedAt = now
                };
                db.Add(slot);
                await db.SaveChangesAsync();
                db.Bookings.Add(new Booking
                {
                    TeacherId = teacher.Id, StudentId = l.StudentId, AvailabilitySlotId = slot.Id, Status = l.Status, CreatedAt = now,
                    DecisionAt = l.Status == BookingStatus.Pending ? null : now, RejectionReason = l.Reason
                });
                await db.SaveChangesAsync();
            }

            return new Seeded(student.Id, other.Id, [ownInstance.Id], primaryLink.Id);
        });
    }

    /// <summary>Katalogdaki uç başına çağrılacak yol(lar): sorgu parametreli uçlarda tüm görünürlük dalları.</summary>
    private static IEnumerable<string> UrlsFor(ParentReadEndpoint endpoint, Seeded seeded)
    {
        var url = endpoint.For(seeded.StudentId, seeded.OwnInstanceIds[0]);
        yield return url;
        if (endpoint.AuditEndpoint == ParentAccessEndpoints.ChildAssignments)
        {
            foreach (var status in new[] { "completed", "overdue", "pending" })
                yield return $"{url}?status={status}";
        }
        else if (endpoint.AuditEndpoint == ParentAccessEndpoints.ChildSchedule)
        {
            yield return $"{url}?from={LocalToday:yyyy-MM-dd}&to={LocalToday.AddDays(30):yyyy-MM-dd}";
        }
    }

    [Fact]
    public async Task No_parent_endpoint_returns_forbidden_fields_or_content()
    {
        var seeded = await SeedAsync();
        var parent = await ClientAsAsync(ParentUser, "Parent", $"kc-{ParentUser}", "Parent");
        var secondParent = await ClientAsAsync(SecondParentUser, "Parent", $"kc-{SecondParentUser}", "Parent");
        var thirdParent = await ClientAsAsync(ThirdParentUser, "Parent", $"kc-{ThirdParentUser}", "Parent");

        var payloads = new List<(string Url, string Json)>();

        // Veli için muaf tutulan YAZMA uçları da taranır (review). Issue #436: birincil veli ikinci veli kodu üretir, üçüncü veli
        // kodu kullanır → Pending taslak (öğrenci verisi yok); istek aşağıdaki okuma taramasında birincil velinin listesinde görünür.
        var codeResponse = await parent.PostAsync($"/api/parent-links/{seeded.PrimaryLinkId}/second-parent-code", null);
        codeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var codeJson = await codeResponse.Content.ReadAsStringAsync();
        payloads.Add(("POST /api/parent-links/{id}/second-parent-code", codeJson));
        string code;
        using (var codeDoc = JsonDocument.Parse(codeJson))
            code = codeDoc.RootElement.GetProperty("code").GetString()!;
        var redeem = await thirdParent.PostAsJsonAsync("/api/parent-links/redeem", new { code });
        redeem.StatusCode.ShouldBe(HttpStatusCode.OK);
        var redeemJson = await redeem.Content.ReadAsStringAsync();
        redeemJson.ShouldNotContain(code, Case.Insensitive); // davet kodu geri yansımaz
        payloads.Add(("POST /api/parent-links/redeem", redeemJson));
        int pendingLinkId;
        using (var redeemDoc = JsonDocument.Parse(redeemJson))
            pendingLinkId = redeemDoc.RootElement.GetProperty("linkId").GetInt32();
        foreach (var endpoint in ParentEndpointCatalog.ReadEndpoints)
        {
            foreach (var url in UrlsFor(endpoint, seeded))
            {
                var response = await parent.GetAsync(url);
                response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
                response.Headers.CacheControl!.NoStore.ShouldBeTrue(url);
                payloads.Add((url, await response.Content.ReadAsStringAsync()));
            }
        }

        // Birincil olmayan (ikinci) veli de listesini görür: diğer veliler / bekleyen istekler / maskeli e-posta YOK.
        var secondChildren = await secondParent.GetAsync("/api/parent-links/my-children");
        secondChildren.StatusCode.ShouldBe(HttpStatusCode.OK);
        payloads.Add(("GET /api/parent-links/my-children (second parent)", await secondChildren.Content.ReadAsStringAsync()));

        // Üçüncü veli kendi bekleyen isteğini iptal eder → 204, gövde yok.
        var revoke = await thirdParent.PostAsync($"/api/parent-links/{pendingLinkId}/revoke", null);
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var revokeBody = await revoke.Content.ReadAsStringAsync();
        revokeBody.ShouldBeEmpty();

        // Tarama gerçekten veri gördü (boş yanıtlarla yeşil geçmesin).
        string Payload(string fragment) => payloads.First(p => p.Url.Contains(fragment, StringComparison.Ordinal)).Json;
        Payload("/summary").ShouldContain("\"totalPoints\":1200");
        Payload("/assignments").ShouldContain("Kesirler");
        Payload("/test-results/").ShouldContain("Kesir işlemleri");
        Payload("/progress").ShouldContain("İlk Adım");
        Payload("schedule?from=").ShouldContain("Zeynep Hoca");
        Payload("/my-children").ShouldContain("Ayşe Kaya");
        Payload("/my-children").ShouldContain("İkinci Veli");
        Payload("/my-children").ShouldContain("Üçüncü Veli");
        Payload("my-children (second parent)").ShouldContain("Ayşe Kaya");
        Payload("my-children (second parent)").ShouldNotContain("Üçüncü Veli");

        foreach (var (url, raw) in payloads)
        {
            // Issue #436 istisnası: YALNIZ birincil velinin çocuk listesinde, bekleyen ikinci veli isteğinin MASKELİ e-postası
            // (birincil veli kimi onayladığını bilsin). Değer doğrulanıp taramadan önce çıkarılır.
            var json = url == "/api/parent-links/my-children" ? StripPrimaryParentMaskedEmails(raw) : raw;
            foreach (var marker in ForbiddenContent)
                json.ShouldNotContain(marker, Case.Insensitive, $"{url} yasak içerik taşıyor: '{marker}'");

            using var doc = JsonDocument.Parse(json);
            foreach (var (path, name, value) in Walk(doc.RootElement, "$"))
            {
                ForbiddenProperties.ShouldNotContain(name, $"{url} yasak alan döndü: {path}");

                // Başka öğrencinin id'si yok: her studentId bu çocuğun, her test oturumu bu çocuğun.
                if (name.Equals("studentId", StringComparison.OrdinalIgnoreCase) && value.ValueKind == JsonValueKind.Number)
                    value.GetInt32().ShouldBe(seeded.StudentId, $"{url}: {path}");
                if (name.Equals("testInstanceId", StringComparison.OrdinalIgnoreCase) && value.ValueKind == JsonValueKind.Number)
                    seeded.OwnInstanceIds.ShouldContain(value.GetInt32(), $"{url}: {path}");
                // Başka öğrencinin puanı hiçbir sayısal alanda yok (metin taraması zaman damgası kesirlerine takılmasın diye sayı olarak).
                if (value.ValueKind == JsonValueKind.Number)
                    value.GetDecimal().ShouldNotBe(RivalXp, $"{url}: {path}");
            }
        }
    }

    /// <summary>
    /// Birincil velinin <c>my-children</c> yanıtında <c>coParents[*].parentEmailMasked</c> yalnız Pending istekte ve maskeli
    /// (<c>x***@y***.tld</c>) olabilir; doğrulanıp kaldırılır, kalan JSON genel taramaya girer.
    /// </summary>
    private static string StripPrimaryParentMaskedEmails(string json)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsArray();
        var stripped = 0;
        foreach (var child in root)
        {
            if (child?["coParents"] is not System.Text.Json.Nodes.JsonArray coParents)
                continue;
            foreach (var coParent in coParents.OfType<System.Text.Json.Nodes.JsonObject>())
            {
                if (!coParent.TryGetPropertyValue("parentEmailMasked", out var masked))
                    continue;
                var value = masked?.GetValue<string>() ?? string.Empty;
                if (value.Length > 0)
                {
                    coParent["status"]!.GetValue<string>().ShouldBe("Pending", "maskeli e-posta yalnız bekleyen istekte");
                    value.ShouldContain("***");
                    value.ShouldNotContain("secret", Case.Insensitive);
                    stripped++;
                }
                coParent.Remove("parentEmailMasked");
            }
        }
        stripped.ShouldBe(1, "bekleyen ikinci veli isteği birincil velinin listesinde maskeli e-postayla görünmeli");
        return root.ToJsonString();
    }

    private static IEnumerable<(string Path, string Name, JsonElement Value)> Walk(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = $"{path}.{property.Name}";
                    yield return (childPath, property.Name, property.Value);
                    foreach (var nested in Walk(property.Value, childPath))
                        yield return nested;
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in Walk(item, $"{path}[{index}]"))
                        yield return nested;
                    index++;
                }
                break;
        }
    }
}
