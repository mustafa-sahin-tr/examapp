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
/// Issue #422 (epic #407 V4) — veli "Puan ve rozetler" + "Program" gerçek Postgres'te: kodla bağlan → progress (XP, seviye,
/// haftalık puan, rozetler GÜNE kesilmiş, yalnız kendi sırası) → schedule (planlar yerel gün, randevular öğretmen adı + saat
/// + durum; reddedilenler, bağlantı/ücret/not yok) + Cache-Control no-store + ParentAccessAudit satırları. Yetki: başka veli, bekleyen
/// bağlantı → 404; yanlış rol → 403; anonim → 401; geçersiz aralık → 400; koparınca 404; yazma ucu yok.
/// </summary>
public class ParentProgressEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int TeacherUser = 47190;

    private sealed record Seeded(int StudentId, int OtherStudentId, int SchoolId);

    private async Task<Seeded> SeedAsync(int studentUserId, int otherStudentUserId, params int[] parentUserIds)
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-{studentUserId}", FullName = "Ayşe Kaya" });
        directory.Add(new() { Id = otherStudentUserId, KeycloakId = $"kc-{otherStudentUserId}", FullName = "Rakip Öğrenci" });
        directory.Add(new() { Id = TeacherUser, KeycloakId = $"kc-{TeacherUser}", FullName = "Zeynep Hoca", Email = "teacher-secret@x.com" });
        foreach (var p in parentUserIds)
            directory.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}" });

        return await WithDbAsync(async db =>
        {
            var school = new School { Name = "Atatürk Ortaokulu" };
            db.Schools.Add(school);
            await db.SaveChangesAsync();

            var student = new Student
            {
                UserId = studentUserId, StudentNumber = $"P{studentUserId}", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow
            };
            var other = new Student
            {
                UserId = otherStudentUserId, StudentNumber = $"P{otherStudentUserId}", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow
            };
            db.Students.AddRange(student, other);
            db.Parents.AddRange(parentUserIds.Select(u => new Parent { UserId = u }));
            await db.SaveChangesAsync();
            db.StudentPoints.AddRange(
                new StudentPoint { StudentId = student.Id, XP = 2500 },
                new StudentPoint { StudentId = other.Id, XP = 98765 });
            await db.SaveChangesAsync();
            return new Seeded(student.Id, other.Id, school.Id);
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

    private static string ProgressUrl(int studentId) => $"/api/parent/children/{studentId}/progress";

    private static string ScheduleUrl(int studentId, string query = "") => $"/api/parent/children/{studentId}/schedule{query}";

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    /// <summary>Bugün (Europe/Istanbul) — aralık sınırı (±1 yıl) bugüne göre olduğundan sabit tarih kullanılamaz.</summary>
    private static DateOnly LocalToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Istanbul));

    /// <summary>Gelecek haftanın Pazartesi'si — program verisi bu haftaya (Pazartesi = gün 4, Pazar = gün 10) yerleşir.</summary>
    private static readonly DateOnly M = NextMonday();

    private static DateOnly NextMonday()
    {
        var today = LocalToday;
        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7) + 7);
    }

    /// <summary>Eski sabit takvimdeki "gün" numarası (4 = Pazartesi) → gerçek tarih.</summary>
    private static DateOnly Day(int d) => M.AddDays(d - 4);

    private static DateTime At(int d, int hour, int minute = 0) => Day(d).ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static string Week => $"?from={Iso(M)}&to={Iso(M.AddDays(6))}";

    /// <summary>Planlar (biri iptal, biri hafta dışı) + randevular (onaylı/bekleyen/reddedilmiş + başka öğrencinin).</summary>
    private Task SeedScheduleAsync(Seeded seeded) => WithDbAsync(async db =>
    {
        var grade = new Grade { Name = "7. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        db.AddRange(grade, subject);
        await db.SaveChangesAsync();
        var names = new[] { "Kesirler", "Oran", "İptal", "Dışarıda" };
        var worksheets = names.Select(n => new Worksheet
        {
            Name = n, Description = "GIZLI-ACIKLAMA", GradeId = grade.Id, SubjectId = n == "Kesirler" ? subject.Id : null
        }).ToList();
        db.Worksheets.AddRange(worksheets);
        var teacher = new Teacher { UserId = TeacherUser, IsIndependentTutor = true, HourlyRate = 987.65m, AccountApprovedAt = DateTime.UtcNow };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();

        WorksheetReminder Reminder(int index, int studentId, DateTime at, WorksheetReminderStatus status) => new()
        {
            WorksheetId = worksheets[index].Id, StudentId = studentId, ScheduledFor = at, Status = status, RemindBeforeMinutes = 4321,
            HangfireJobId = "GIZLI-JOB", StudentKeycloakId = "GIZLI-KC"
        };
        db.WorksheetReminders.AddRange(
            Reminder(0, seeded.StudentId, At(5, 15, 0), WorksheetReminderStatus.Pending),
            // 22:30Z = Istanbul 01:30 → planlanan yerel gün = gün 7.
            Reminder(1, seeded.StudentId, At(6, 22, 30), WorksheetReminderStatus.Sent),
            Reminder(2, seeded.StudentId, At(7, 10, 0), WorksheetReminderStatus.Cancelled),
            Reminder(3, seeded.StudentId, At(12, 10, 0), WorksheetReminderStatus.Pending),
            Reminder(0, seeded.OtherStudentId, At(5, 15, 0), WorksheetReminderStatus.Pending));

        var lessons = new (int StudentId, int Day, int Hour, BookingStatus Status, string? Reason)[]
        {
            (seeded.StudentId, 5, 14, BookingStatus.Approved, null),
            (seeded.StudentId, 7, 9, BookingStatus.Pending, null),
            (seeded.StudentId, 8, 9, BookingStatus.Rejected, "GIZLI-RET"),
            (seeded.OtherStudentId, 6, 16, BookingStatus.Approved, null)
        };
        foreach (var l in lessons)
        {
            var slot = new TeacherAvailabilitySlot
            {
                TeacherId = teacher.Id, Date = Day(l.Day), StartTime = new TimeOnly(l.Hour, 0),
                EndTime = new TimeOnly(l.Hour + 1, 0), CreatedAt = DateTime.UtcNow
            };
            db.Add(slot);
            await db.SaveChangesAsync();
            db.Bookings.Add(new Booking
            {
                TeacherId = teacher.Id, StudentId = l.StudentId, AvailabilitySlotId = slot.Id, Status = l.Status,
                CreatedAt = DateTime.UtcNow, DecisionAt = l.Status == BookingStatus.Pending ? null : DateTime.UtcNow, RejectionReason = l.Reason
            });
            await db.SaveChangesAsync();
        }
    });

    private Task<List<ParentAccessAudit>> AuditsAsync()
        => WithDbAsync(db => db.ParentAccessAudits.AsNoTracking().OrderBy(a => a.Id).ToListAsync());

    private static string[] Names(JsonElement obj) => obj.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Linked_parent_reads_progress_and_schedule_without_other_students_or_lesson_secrets()
    {
        const int studentUser = 47101, otherStudentUser = 47102, parentUser = 47103;
        var seeded = await SeedAsync(studentUser, otherStudentUser, parentUser);
        var student = await StudentAsync(studentUser);
        var parent = await ParentAsync(parentUser);
        await LinkAsync(studentUser, parentUser);
        await SeedScheduleAsync(seeded);
        // Projeksiyonlar (BadgeService event'leriyle beslenir; consumer'lar LeaderboardPointsSyncTests'te uçtan uca).
        var today = LocalToday;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        await WithDbAsync(async db =>
        {
            db.StudentBadgeProjections.AddRange(
                new StudentBadgeProjection
                {
                    StudentId = seeded.StudentId, BadgeDefinitionId = Guid.NewGuid(), Name = "İlk Adım", Icon = "rocket_launch",
                    EarnedAtUtc = new DateTime(2026, 9, 1, 8, 15, 0, DateTimeKind.Utc), ReceivedAtUtc = DateTime.UtcNow
                },
                new StudentBadgeProjection
                {
                    StudentId = seeded.StudentId, BadgeDefinitionId = Guid.NewGuid(), Name = "Seri", Icon = null,
                    EarnedAtUtc = new DateTime(2026, 10, 5, 22, 30, 0, DateTimeKind.Utc), ReceivedAtUtc = DateTime.UtcNow
                },
                new StudentBadgeProjection
                {
                    StudentId = seeded.OtherStudentId, BadgeDefinitionId = Guid.NewGuid(), Name = "Rakip Rozeti", Icon = "star",
                    EarnedAtUtc = DateTime.UtcNow, ReceivedAtUtc = DateTime.UtcNow
                });
            db.StudentDailyXps.AddRange(
                new StudentDailyXp { StudentId = seeded.StudentId, Day = monday.AddDays(-1), Xp = 500 }, // geçen hafta
                new StudentDailyXp { StudentId = seeded.StudentId, Day = monday, Xp = 100 },
                new StudentDailyXp { StudentId = seeded.StudentId, Day = today.AddDays(1), Xp = 9000 }, // ileri tarihli → sayılmaz
                new StudentDailyXp { StudentId = seeded.OtherStudentId, Day = today, Xp = 4444 });
            if (today != monday)
                db.StudentDailyXps.Add(new StudentDailyXp { StudentId = seeded.StudentId, Day = today, Xp = 40 });
            await db.SaveChangesAsync();
        });
        var expectedWeekly = today == monday ? 100 : 140;

        // ---- progress
        var response = await parent.GetAsync(ProgressUrl(seeded.StudentId));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var json = await response.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            Names(root).ShouldBe(new[] { "badges", "level", "ranks", "studentId", "totalXp", "weekStart", "weeklyXp" });
            root.GetProperty("totalXp").GetInt32().ShouldBe(2500);
            root.GetProperty("level").GetInt32().ShouldBe(8);
            root.GetProperty("weeklyXp").GetInt32().ShouldBe(expectedWeekly);
            root.GetProperty("weekStart").GetString().ShouldBe(Iso(monday));
            var badgeItems = root.GetProperty("badges").EnumerateArray().ToList();
            badgeItems.Select(b => b.GetProperty("name").GetString()).ShouldBe(new[] { "Seri", "İlk Adım" });
            Names(badgeItems[0]).ShouldBe(new[] { "earnedOn", "icon", "name" });
            badgeItems[0].GetProperty("earnedOn").GetString().ShouldBe("2026-10-06"); // yerel güne kesilmiş, saat yok
            var ranks = root.GetProperty("ranks").EnumerateArray().ToList();
            ranks.Select(r => (r.GetProperty("scope").GetString(), r.GetProperty("rank").GetInt32(), r.GetProperty("totalCount").GetInt32()))
                .ShouldBe(new[] { ("global", 2, 2), ("school", 2, 2) });
            Names(ranks[0]).ShouldBe(new[] { "rank", "scope", "totalCount" });
        }
        foreach (var secret in new[] { "98765", "4444", "Rakip", otherStudentUser.ToString(), "fullName", "avatar", "userId" })
            json.ShouldNotContain(secret, Case.Sensitive);

        // ---- schedule
        var scheduleResponse = await parent.GetAsync(ScheduleUrl(seeded.StudentId, Week));
        scheduleResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        scheduleResponse.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var scheduleJson = await scheduleResponse.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(scheduleJson))
        {
            var root = doc.RootElement;
            Names(root).ShouldBe(new[] { "from", "lessons", "plans", "studentId", "to" });
            root.GetProperty("from").GetString().ShouldBe(Iso(M));
            root.GetProperty("to").GetString().ShouldBe(Iso(M.AddDays(6)));

            var plans = root.GetProperty("plans").EnumerateArray().ToList();
            plans.Select(p => (p.GetProperty("title").GetString(), p.GetProperty("plannedOn").GetString()))
                .ShouldBe(new[] { ("Kesirler", Iso(Day(5))), ("Oran", Iso(Day(7))) });
            Names(plans[0]).ShouldBe(new[] { "plannedOn", "subject", "title" });
            plans[0].GetProperty("subject").GetString().ShouldBe("Matematik");

            var lessons = root.GetProperty("lessons").EnumerateArray().ToList();
            // Reddedilen talep veliye gösterilmez.
            lessons.Select(l => (l.GetProperty("status").GetString(), l.GetProperty("startsOn").GetString()))
                .ShouldBe(new[] { ("approved", Iso(Day(5))), ("pending", Iso(Day(7))) });
            Names(lessons[0]).ShouldBe(new[] { "endAt", "startAt", "startsOn", "status", "teacherName" });
            lessons[0].GetProperty("teacherName").GetString().ShouldBe("Zeynep Hoca");
            lessons[0].GetProperty("startAt").GetDateTime().ToUniversalTime().ShouldBe(At(5, 14, 0));
        }
        foreach (var secret in new[] { "GIZLI", "987", "4321", "teacher-secret", "http", "jitsi", "meet", "url", "link", "price", "rate", "reason", "note", "bookingId" })
            scheduleJson.ShouldNotContain(secret, Case.Insensitive);

        // Varsayılan aralık (bu hafta) da çalışır.
        using (var doc = JsonDocument.Parse(await (await parent.GetAsync(ScheduleUrl(seeded.StudentId))).Content.ReadAsStringAsync()))
        {
            var from = DateOnly.Parse(doc.RootElement.GetProperty("from").GetString()!);
            var to = DateOnly.Parse(doc.RootElement.GetProperty("to").GetString()!);
            from.DayOfWeek.ShouldBe(DayOfWeek.Monday);
            (to.DayNumber - from.DayNumber).ShouldBe(6);
        }

        // Geçersiz aralık → 400 (alan bazlı hata): 32 gün, ters, tek uç, biçim, bugünden 1 yıldan uzak, uç yıllar (500 değil).
        var invalid = new[]
        {
            $"?from={Iso(M)}&to={Iso(M.AddDays(31))}",
            $"?from={Iso(M.AddDays(6))}&to={Iso(M)}",
            $"?from={Iso(M)}",
            $"?from={M:dd.MM.yyyy}&to={Iso(M.AddDays(6))}",
            $"?from={Iso(LocalToday.AddYears(-1).AddDays(-1))}&to={Iso(LocalToday.AddYears(-1).AddDays(3))}",
            $"?from={Iso(LocalToday.AddYears(1).AddDays(-3))}&to={Iso(LocalToday.AddYears(1).AddDays(1))}",
            "?from=9999-12-01&to=9999-12-31",
            "?from=0001-01-01&to=0001-01-02"
        };
        foreach (var query in invalid)
        {
            var bad = await parent.GetAsync(ScheduleUrl(seeded.StudentId, query));
            bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using var doc = JsonDocument.Parse(await bad.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("errors").GetProperty("range").GetArrayLength().ShouldBe(1);
        }
        (await parent.GetAsync(ScheduleUrl(seeded.StudentId, $"?from={Iso(M)}&to={Iso(M.AddDays(30))}"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // ---- audit: uç başına bir satır (10 dk kova; sınırda 2 olabilir), kaynak yok
        var audits = await AuditsAsync();
        audits.Count(a => a.Endpoint == ParentAccessEndpoints.ChildProgress).ShouldBeInRange(1, 2);
        audits.Count(a => a.Endpoint == ParentAccessEndpoints.ChildSchedule).ShouldBeInRange(1, 2);
        audits.ShouldAllBe(a => a.StudentId == seeded.StudentId && a.ResourceId == null);
    }

    [Fact]
    public async Task Idor_role_and_revoke_cases_are_404_403_or_401()
    {
        const int studentUser = 47111, otherStudentUser = 47112, parentUser = 47113, otherParentUser = 47114, pendingParentUser = 47115;
        var seeded = await SeedAsync(studentUser, otherStudentUser, parentUser, otherParentUser, pendingParentUser);
        var student = await StudentAsync(studentUser);
        var parent = await ParentAsync(parentUser);
        var otherParent = await ParentAsync(otherParentUser);
        var pendingParent = await ParentAsync(pendingParentUser);
        var teacher = await ClientAsAsync(47116, "Teacher", "kc-47116", "Teacher");

        var linkId = await LinkAsync(studentUser, parentUser);
        await LinkAsync(studentUser, pendingParentUser, approve: false);
        await LinkAsync(otherStudentUser, otherParentUser);

        // Başka velinin çocuğu / bekleyen bağlantı / olmayan öğrenci → 404, audit yok.
        foreach (var client in new[] { otherParent, pendingParent })
        {
            foreach (var url in new[] { ProgressUrl(seeded.StudentId), ScheduleUrl(seeded.StudentId, Week) })
            {
                var response = await client.GetAsync(url);
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.NotFound);
            }
        }
        (await parent.GetAsync(ProgressUrl(999_999))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await parent.GetAsync(ScheduleUrl(999_999))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuditsAsync()).ShouldBeEmpty();

        // Yanlış rol → 403; anonim → 401.
        foreach (var client in new[] { student, teacher })
        {
            (await client.GetAsync(ProgressUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.GetAsync(ScheduleUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        (await Anonymous().GetAsync(ProgressUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Anonymous().GetAsync(ScheduleUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Yazma ucu yok.
        (await parent.PostAsync(ProgressUrl(seeded.StudentId), null)).StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        (await parent.PutAsync(ScheduleUrl(seeded.StudentId), null)).StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        (await parent.DeleteAsync(ScheduleUrl(seeded.StudentId))).StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);

        // Bağlı veli erişir; bağlantı koparılınca erişim anında biter.
        (await parent.GetAsync(ProgressUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await parent.GetAsync(ScheduleUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.OK);
        // #436: tek veli ayrılamaz (409); bağlantıyı admin koparınca erişim anında biter.
        (await parent.PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var admin = await ClientAsAsync(47199, "Admin", "kc-admin-47199", "Admin");
        (await admin.PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await parent.GetAsync(ProgressUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await parent.GetAsync(ScheduleUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
