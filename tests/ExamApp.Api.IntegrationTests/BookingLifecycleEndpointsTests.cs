using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #315 gerçek Postgres'te: HTTP üzerinden iptal ve "öner → kabul" erteleme; koşullu UPDATE'lerin (ExecuteUpdate)
/// Postgres çevirisi, DTO alanları ve outbox'a yazılan <see cref="BookingLifecycleEvent"/> satırı.
/// </summary>
public class BookingLifecycleEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    // FakeUserDirectory Respawn ile sıfırlanmaz → benzersiz, büyük UserId.
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private static readonly DateOnly Future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);

    private sealed record World(int TeacherUserId, int StudentUserId, int ParentUserId, int SlotId, int SlotId2);

    private async Task<World> SeedAsync()
    {
        var (teacherUser, studentUser, parentUser) = (NewUserId(), NewUserId(), NewUserId());
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = teacherUser, KeycloakId = $"kc-{teacherUser}", FullName = "Zeynep Hoca" });
        directory.Add(new() { Id = studentUser, KeycloakId = $"kc-{studentUser}", FullName = "Ayşe Kaya" });
        directory.Add(new() { Id = parentUser, KeycloakId = $"kc-{parentUser}", FullName = $"Veli {parentUser}" });

        var (slotId, slotId2) = await WithDbAsync(async db =>
        {
            var teacher = new Teacher
            {
                UserId = teacherUser, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow.AddDays(-3),
                IsIndependentTutor = true, Bio = "t"
            };
            db.AddRange(teacher, new Student { UserId = studentUser, StudentNumber = $"S{studentUser}" }, new Parent { UserId = parentUser });
            await db.SaveChangesAsync();
            var s1 = new TeacherAvailabilitySlot
            {
                TeacherId = teacher.Id, Date = Future, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), CreatedAt = DateTime.UtcNow
            };
            var s2 = new TeacherAvailabilitySlot
            {
                TeacherId = teacher.Id, Date = Future, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(15, 0), CreatedAt = DateTime.UtcNow
            };
            db.TeacherAvailabilitySlots.AddRange(s1, s2);
            await db.SaveChangesAsync();
            return (s1.Id, s2.Id);
        });

        await SeedParentLinkAsync(parentUser, studentUser);
        return new World(teacherUser, studentUser, parentUser, slotId, slotId2);
    }

    private Task<HttpClient> StudentAsync(int userId) => ClientAsAsync(userId, "Student", $"kc-{userId}", "Student");

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    private Task<HttpClient> TeacherAsync(int userId) => ClientAsAsync(userId, "Teacher", $"kc-{userId}", "Teacher");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private Task<Booking> BookingAsync(int id) => WithDbAsync(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id));

    private Task<List<BookingLifecycleEvent>> LifecycleEventsAsync() => WithDbAsync(async db =>
    {
        var type = OutboxEventRegistry.NameFor<BookingLifecycleEvent>();
        return (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync())
            .Select(c => JsonSerializer.Deserialize<BookingLifecycleEvent>(c)!)
            .ToList();
    });

    /// <summary>Öğrenci talebi → veli onayı → öğretmen onayı: Approved randevu id'si.</summary>
    private async Task<int> ApprovedBookingAsync(World w)
    {
        var student = await StudentAsync(w.StudentUserId);
        var parent = await ParentAsync(w.ParentUserId);
        var teacher = await TeacherAsync(w.TeacherUserId);

        var create = await student.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = w.SlotId });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await JsonAsync(create)).GetProperty("objectId").GetInt32();

        (await parent.PostAsync($"/api/parent/booking-requests/{id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teacher.PostAsync($"/api/booking/requests/{id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Approved);
        return id;
    }

    [Fact]
    public async Task Student_cancels_an_approved_booking_over_http_and_the_outbox_row_is_written_once()
    {
        var w = await SeedAsync();
        var id = await ApprovedBookingAsync(w);
        var student = await StudentAsync(w.StudentUserId);

        var cancel = await student.PostAsJsonAsync($"/api/booking/requests/{id}/cancel", new { reason = "Sınavım var" });

        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(cancel)).GetProperty("booking").GetProperty("status").GetString().ShouldBe("Cancelled");
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Cancelled);
        stored.CancelledFromStatus.ShouldBe(BookingStatus.Approved);
        stored.CancelledByRole.ShouldBe(BookingParty.Student);
        stored.CancellationReason.ShouldBe("Sınavım var");

        var e = (await LifecycleEventsAsync()).Single(x => x.BookingId == id);
        e.Kind.ShouldBe(BookingLifecycleKinds.Cancelled);
        e.NotifyTeacher.ShouldBeTrue();
        e.NotifyStudent.ShouldBeFalse();
        e.Reason.ShouldBe("Sınavım var");
        e.Parents.Select(p => p.ParentUserId).ShouldBe(new[] { w.ParentUserId });
        e.Parents.Single().KeycloakId.ShouldBe($"kc-{w.ParentUserId}");

        // İkinci iptal: koşullu UPDATE 0 satır → 409, yeni outbox satırı yok.
        var again = await student.PostAsJsonAsync($"/api/booking/requests/{id}/cancel", new { reason = (string?)null });
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(again)).GetProperty("errorCode").GetString().ShouldBe(BookingErrorCodes.InvalidState);
        (await LifecycleEventsAsync()).Count(x => x.BookingId == id).ShouldBe(1);
    }

    [Fact]
    public async Task Student_proposes_and_teacher_accepts_a_reschedule_over_http_and_the_booking_moves()
    {
        var w = await SeedAsync();
        var id = await ApprovedBookingAsync(w);
        var student = await StudentAsync(w.StudentUserId);
        var teacher = await TeacherAsync(w.TeacherUserId);

        var propose = await student.PostAsJsonAsync($"/api/booking/requests/{id}/reschedule", new { availabilitySlotId = w.SlotId2 });

        propose.StatusCode.ShouldBe(HttpStatusCode.OK);
        var proposed = (await JsonAsync(propose)).GetProperty("booking");
        proposed.GetProperty("proposedSlotId").GetInt32().ShouldBe(w.SlotId2);
        proposed.GetProperty("canProposeReschedule").GetBoolean().ShouldBeFalse();
        (await BookingAsync(id)).AvailabilitySlotId.ShouldBe(w.SlotId, "öneri kabul edilene kadar randevu eski slotta");

        // Öneren taraf kendi önerisini kabul edemez → 403.
        (await student.PostAsJsonAsync($"/api/booking/requests/{id}/reschedule/respond", new { accept = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var accept = await teacher.PostAsJsonAsync($"/api/booking/requests/{id}/reschedule/respond", new { accept = true });

        accept.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Approved);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId2);
        stored.RescheduleCount.ShouldBe(1);
        stored.RescheduleProposalCount.ShouldBe(1);
        stored.ProposedSlotId.ShouldBeNull();

        var events = (await LifecycleEventsAsync()).Where(x => x.BookingId == id).ToList();
        events.Select(x => x.Kind).OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(new[] { BookingLifecycleKinds.RescheduleAccepted, BookingLifecycleKinds.RescheduleProposed });
        var accepted = events.Single(x => x.Kind == BookingLifecycleKinds.RescheduleAccepted);
        accepted.ActorRole.ShouldBe(BookingParty.Teacher);
        accepted.NotifyStudent.ShouldBeTrue();
        accepted.NotifyTeacher.ShouldBeFalse();
        accepted.NewStartTime.ShouldBe(new TimeOnly(14, 0));
        accepted.NewEndTime.ShouldBe(new TimeOnly(15, 0));
        accepted.Parents.Select(p => p.ParentUserId).ShouldBe(new[] { w.ParentUserId });
    }

    [Fact]
    public async Task Live_status_reports_cancellation_and_hides_bookings_from_strangers_issue_320()
    {
        var w = await SeedAsync();
        var id = await ApprovedBookingAsync(w);
        var student = await StudentAsync(w.StudentUserId);
        var teacher = await TeacherAsync(w.TeacherUserId);

        // Ders 10 gün sonra: pencere henüz açık değil (token üretilmez, yalnız durum).
        var before = await student.GetAsync($"/api/booking/requests/{id}/live-status");
        before.StatusCode.ShouldBe(HttpStatusCode.OK);
        var beforeJson = await JsonAsync(before);
        beforeJson.GetProperty("active").GetBoolean().ShouldBeFalse();
        beforeJson.GetProperty("errorCode").GetString().ShouldBe("WindowNotOpen");

        (await teacher.PostAsJsonAsync($"/api/booking/requests/{id}/cancel", new { reason = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await JsonAsync(await student.GetAsync($"/api/booking/requests/{id}/live-status"));
        after.GetProperty("active").GetBoolean().ShouldBeFalse();
        after.GetProperty("errorCode").GetString().ShouldBe("BookingNotApproved");

        var stranger = await StudentAsync(NewUserId());
        (await stranger.GetAsync($"/api/booking/requests/{id}/live-status")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Live_status_after_teacher_suspension_ends_the_call_for_both_sides_issue_320()
    {
        var w = await SeedAsync();
        var id = await ApprovedBookingAsync(w);
        var student = await StudentAsync(w.StudentUserId);
        var teacher = await TeacherAsync(w.TeacherUserId);
        (await teacher.GetAsync($"/api/booking/requests/{id}/live-status")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == w.TeacherUserId);
            t.AccountSuspendedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });

        // Öğrenci: nötr TeacherUnavailable (pencere kontrolünden önce gelir).
        var studentStatus = await JsonAsync(await student.GetAsync($"/api/booking/requests/{id}/live-status"));
        studentStatus.GetProperty("active").GetBoolean().ShouldBeFalse();
        studentStatus.GetProperty("errorCode").GetString().ShouldBe("TeacherUnavailable");

        // Askıdaki öğretmenin kendi yoklaması ApprovedTeacher policy'sinde 403 → istemci görüşmeyi kapatır.
        (await teacher.GetAsync($"/api/booking/requests/{id}/live-status")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
