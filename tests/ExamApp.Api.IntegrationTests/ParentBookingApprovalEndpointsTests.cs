using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Bookings;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #439 (epic #435) gerçek Postgres'te: veliye tabi öğrencinin talebi AwaitingParent açılır, öğretmene düşmez; Active veli
/// "Onay bekleyen ders talepleri"nde görür ve onaylar/reddeder (ilk karar kazanır, çok veli yarışında tek kazanan + tek event);
/// onaydan sonra eski öğretmen akışı; velisiz öğrenci 403 ParentRequired; muaf (yetişkin rolü de olan) hesap eski akış; veli
/// doğrudan talep; filtreli unique index AwaitingParent'i kapsar; süre aşımı süpürmesi (Postgres'te tarih/saat çevirisi).
/// </summary>
public class ParentBookingApprovalEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    // FakeUserDirectory Respawn ile sıfırlanmaz → benzersiz, büyük UserId.
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private static readonly DateOnly Future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);

    private sealed record World(
        int TeacherId, int TeacherUserId, int StudentId, int StudentUserId, int ParentUserId, int SecondParentUserId,
        int OtherParentUserId, int OrphanStudentUserId, int SlotId, int SlotId2);

    private async Task<World> SeedAsync()
    {
        var (teacherUser, studentUser, parentUser, parent2User, otherParentUser, otherStudentUser, orphanUser) =
            (NewUserId(), NewUserId(), NewUserId(), NewUserId(), NewUserId(), NewUserId(), NewUserId());
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = teacherUser, KeycloakId = $"kc-{teacherUser}", FullName = "Zeynep Hoca" });
        directory.Add(new() { Id = studentUser, KeycloakId = $"kc-{studentUser}", FullName = "Ayşe Kaya" });
        directory.Add(new() { Id = orphanUser, KeycloakId = $"kc-{orphanUser}", FullName = "Velisiz Öğrenci" });
        directory.Add(new() { Id = otherStudentUser, KeycloakId = $"kc-{otherStudentUser}", FullName = "Başka Öğrenci" });
        foreach (var p in new[] { parentUser, parent2User, otherParentUser })
            directory.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}" });

        var (teacherId, studentId, slotId, slotId2) = await WithDbAsync(async db =>
        {
            var teacher = new Teacher
            {
                UserId = teacherUser, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow.AddDays(-3),
                IsIndependentTutor = true, Bio = "t"
            };
            var student = new Student { UserId = studentUser, StudentNumber = $"S{studentUser}" };
            db.AddRange(teacher, student,
                new Student { UserId = otherStudentUser, StudentNumber = $"S{otherStudentUser}" },
                new Student { UserId = orphanUser, StudentNumber = $"S{orphanUser}" },
                new Parent { UserId = parentUser }, new Parent { UserId = parent2User }, new Parent { UserId = otherParentUser });
            await db.SaveChangesAsync();
            var slot = new TeacherAvailabilitySlot
            {
                TeacherId = teacher.Id, Date = Future, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), CreatedAt = DateTime.UtcNow
            };
            var slot2 = new TeacherAvailabilitySlot
            {
                TeacherId = teacher.Id, Date = Future, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(15, 0), CreatedAt = DateTime.UtcNow
            };
            db.TeacherAvailabilitySlots.AddRange(slot, slot2);
            await db.SaveChangesAsync();
            return (teacher.Id, student.Id, slot.Id, slot2.Id);
        });

        await SeedParentLinkAsync(parentUser, studentUser); // birincil
        await SeedParentLinkAsync(parent2User, studentUser); // ikinci Active veli
        await SeedParentLinkAsync(otherParentUser, otherStudentUser);

        return new World(teacherId, teacherUser, studentId, studentUser, parentUser, parent2User, otherParentUser, orphanUser,
            slotId, slotId2);
    }

    private Task<HttpClient> StudentAsync(int userId, params string[] roles)
        => ClientAsAsync(userId, "Student", $"kc-{userId}", roles.Length == 0 ? new[] { "Student" } : roles);

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    private async Task<HttpClient> TeacherAsync(int userId) => await ClientAsAsync(userId, "Teacher", $"kc-{userId}", "Teacher");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private Task<List<T>> EventsAsync<T>() => WithDbAsync(async db =>
    {
        var type = OutboxEventRegistry.NameFor<T>();
        return (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync())
            .Select(c => JsonSerializer.Deserialize<T>(c)!)
            .ToList();
    });

    private Task<Booking> BookingAsync(int id) => WithDbAsync(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id));

    [Fact]
    public async Task Request_waits_for_a_parent_then_enters_the_teacher_flow()
    {
        var w = await SeedAsync();
        var student = await StudentAsync(w.StudentUserId);
        var teacher = await TeacherAsync(w.TeacherUserId);
        var parent = await ParentAsync(w.ParentUserId);
        var secondParent = await ParentAsync(w.SecondParentUserId);
        var otherParent = await ParentAsync(w.OtherParentUserId);

        // 1) Öğrenci talebi → AwaitingParent.
        var create = await student.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = w.SlotId });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await JsonAsync(create);
        var id = created.GetProperty("objectId").GetInt32();
        created.GetProperty("booking").GetProperty("status").GetString().ShouldBe("AwaitingParent");
        created.GetProperty("booking").GetProperty("expiresAt").ValueKind.ShouldBe(JsonValueKind.String);

        // 2) Öğretmen görmez, karar veremez (404).
        var teacherList = await JsonAsync(await teacher.GetAsync("/api/booking/requests/teacher"));
        teacherList.GetProperty("items").GetArrayLength().ShouldBe(0);
        (await teacher.PostAsync($"/api/booking/requests/{id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldBeEmpty();
        var requested = (await EventsAsync<BookingParentApprovalRequestedEvent>()).ShouldHaveSingleItem();
        requested.Parents.Select(p => p.ParentUserId).OrderBy(x => x)
            .ShouldBe(new[] { w.ParentUserId, w.SecondParentUserId }.OrderBy(x => x));
        requested.StudentName.ShouldBe("Ayşe K."); // event adları kısa görünen ad (#439 review)

        // 3) Veli listesi: bağlı veliler görür (no-store), başka veli görmez ve karar veremez (404, sızdırmaz).
        var listResponse = await parent.GetAsync("/api/parent/booking-requests");
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        listResponse.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var item = (await JsonAsync(listResponse)).GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        item.GetProperty("id").GetInt32().ShouldBe(id);
        item.GetProperty("studentName").GetString().ShouldBe("Ayşe Kaya");
        item.GetProperty("teacherName").GetString().ShouldBe("Zeynep Hoca");
        (await JsonAsync(await otherParent.GetAsync("/api/parent/booking-requests"))).GetProperty("items").GetArrayLength().ShouldBe(0);

        var foreign = await otherParent.PostAsync($"/api/parent/booking-requests/{id}/approve", null);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var foreignBody = await JsonAsync(foreign);
        foreignBody.GetProperty("errorCode").GetString().ShouldBe("NotFound");
        string.Join(",", foreignBody.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)).ShouldBe("errorCode,message");

        // Yanlış rol 403, anonim 401.
        (await student.PostAsync($"/api/parent/booking-requests/{id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Anonymous().GetAsync("/api/parent/booking-requests")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // 4) İkinci veli onaylar → Pending; birincil veli geç kalır → 409 AlreadyDecided.
        var approve = await secondParent.PostAsync($"/api/parent/booking-requests/{id}/approve", null);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(approve)).GetProperty("booking").GetProperty("status").GetString().ShouldBe("Pending");
        var late = await parent.PostAsJsonAsync($"/api/parent/booking-requests/{id}/reject", new { note = "geç" });
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(late)).GetProperty("errorCode").GetString().ShouldBe(BookingErrorCodes.AlreadyDecided);

        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Pending);
        stored.ParentApprovedBy.ShouldBe(w.SecondParentUserId);
        stored.ParentApprovedAt.ShouldNotBeNull();

        // Veli yazması erişim kaydında (kaybeden geç karar yazılmaz).
        var audit = (await WithDbAsync(db => db.ParentAccessAudits.AsNoTracking().ToListAsync())).ShouldHaveSingleItem();
        audit.Endpoint.ShouldBe(ParentAccessEndpoints.ChildBookingApprove);
        audit.StudentId.ShouldBe(w.StudentId);
        audit.ResourceId.ShouldBe(id);

        // 5) Öğretmen akışı aynen: listede görünür, onaylar; karar velilere de gider.
        var teacherList2 = await JsonAsync(await teacher.GetAsync("/api/booking/requests/teacher"));
        teacherList2.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetInt32().ShouldBe(id);
        (await teacher.PostAsync($"/api/booking/requests/{id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldHaveSingleItem().TargetKeycloakId.ShouldBe($"kc-{w.TeacherUserId}");
        var parentDecision = (await EventsAsync<BookingParentDecisionEvent>()).ShouldHaveSingleItem();
        parentDecision.Decision.ShouldBe(BookingParentDecisions.Approved);
        parentDecision.DecidedByParentUserId.ShouldBe(w.SecondParentUserId);
        var teacherDecision = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        teacherDecision.Approved.ShouldBeTrue();
        teacherDecision.Parents.Select(p => p.KeycloakId).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(new[] { $"kc-{w.ParentUserId}", $"kc-{w.SecondParentUserId}" }.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Parent_rejection_with_note_closes_the_request_and_frees_the_slot()
    {
        var w = await SeedAsync();
        var student = await StudentAsync(w.StudentUserId);
        var id = (await JsonAsync(await student.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = w.SlotId })))
            .GetProperty("objectId").GetInt32();
        var parent = await ParentAsync(w.ParentUserId);

        (await parent.PostAsJsonAsync($"/api/parent/booking-requests/{id}/reject", new { note = new string('x', 501) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var reject = await parent.PostAsJsonAsync($"/api/parent/booking-requests/{id}/reject", new { note = "Sınav haftası" });
        reject.StatusCode.ShouldBe(HttpStatusCode.OK);

        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.ParentRejected);
        stored.ParentRejectedBy.ShouldBe(w.ParentUserId);
        stored.RejectionReason.ShouldBe("Sınav haftası");
        stored.DecisionAt.ShouldNotBeNull();
        var e = (await EventsAsync<BookingParentDecisionEvent>()).ShouldHaveSingleItem();
        e.Decision.ShouldBe(BookingParentDecisions.Rejected);
        e.Note.ShouldBe("Sınav haftası");
        e.StudentKeycloakId.ShouldBe($"kc-{w.StudentUserId}");

        // Öğrencinin kendi listesinde durumu görünür; slot yeniden alınabilir.
        var mine = await JsonAsync(await student.GetAsync("/api/booking/requests/student"));
        mine.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("status").GetString().ShouldBe("ParentRejected");
        (await student.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = w.SlotId })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Student_without_an_active_parent_gets_403_ParentRequired_and_exempt_accounts_keep_the_old_flow()
    {
        var w = await SeedAsync();

        var orphan = await StudentAsync(w.OrphanStudentUserId);
        var denied = await orphan.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = w.SlotId });
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonAsync(denied)).GetProperty("errorCode").GetString().ShouldBe(BookingErrorCodes.ParentRequired);
        (await WithDbAsync(db => db.Bookings.CountAsync())).ShouldBe(0);

        // Aynı velisiz hesap öğretmen rolünü de taşıyorsa (ParentRequirement: yetişkin rolü kazanır) eski akış.
        var exempt = await StudentAsync(w.OrphanStudentUserId, "Student", "Teacher");
        var ok = await exempt.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = w.SlotId });
        ok.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await JsonAsync(ok)).GetProperty("booking").GetProperty("status").GetString().ShouldBe("Pending");
        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldHaveSingleItem();
        (await EventsAsync<BookingParentApprovalRequestedEvent>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Parent_creates_a_request_for_the_child_directly_and_can_browse_tutors()
    {
        var w = await SeedAsync();
        var parent = await ParentAsync(w.ParentUserId);

        // Öğretmen seçimi uçları veliye açık.
        (await parent.GetAsync("/api/teacher/search")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await parent.GetAsync($"/api/teacher/{w.TeacherId}/public-profile")).StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        var slots = await parent.GetAsync($"/api/booking/teachers/{w.TeacherId}/slots");
        slots.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(slots)).GetProperty("items").GetArrayLength().ShouldBe(2);

        var create = await parent.PostAsJsonAsync($"/api/parent/children/{w.StudentId}/bookings", new { availabilitySlotId = w.SlotId });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        create.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = await JsonAsync(create);
        body.GetProperty("booking").GetProperty("status").GetString().ShouldBe("Pending");
        var stored = await BookingAsync(body.GetProperty("objectId").GetInt32());
        stored.StudentId.ShouldBe(w.StudentId);
        stored.ParentApprovedBy.ShouldBe(w.ParentUserId);
        stored.ParentApprovedAt.ShouldNotBeNull();
        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldHaveSingleItem().StudentName.ShouldBe("Ayşe K.");

        // Bağlı olmayan çocuk (IDOR) 404; aynı slot ikinci kez 409.
        var other = await ParentAsync(w.OtherParentUserId);
        var idor = await other.PostAsJsonAsync($"/api/parent/children/{w.StudentId}/bookings", new { availabilitySlotId = w.SlotId2 });
        idor.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JsonAsync(idor)).GetProperty("errorCode").GetString().ShouldBe("NotFound");
        (await parent.PostAsJsonAsync($"/api/parent/children/{w.StudentId}/bookings", new { availabilitySlotId = w.SlotId }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Parallel_parent_decisions_have_exactly_one_winner_and_one_event()
    {
        const int rounds = 6;
        for (var round = 0; round < rounds; round++)
        {
            var w = await SeedAsync();
            int id;
            using (var scope = Factory.Services.CreateScope())
            {
                var created = await scope.ServiceProvider.GetRequiredService<IBookingService>()
                    .CreateBookingAsync(w.StudentUserId, new CreateBookingDto { AvailabilitySlotId = w.SlotId }, requiresParent: true);
                created.Success.ShouldBeTrue(created.Message);
                id = created.ObjectId;
            }

            using var gate = new SemaphoreSlim(0, 2);
            async Task<BookingResultDto> Decide(int parentUserId, bool approve)
            {
                using var scope = Factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
                await gate.WaitAsync();
                return approve
                    ? await service.ParentApproveBookingAsync(parentUserId, id)
                    : await service.ParentRejectBookingAsync(parentUserId, id, "hayır");
            }

            var a = Task.Run(() => Decide(w.ParentUserId, approve: true));
            var b = Task.Run(() => Decide(w.SecondParentUserId, approve: false));
            gate.Release(2);
            var results = await Task.WhenAll(a, b);

            results.Count(r => r.Success).ShouldBe(1, $"round {round}");
            results.Single(r => !r.Success).ErrorCode.ShouldBe(BookingErrorCodes.AlreadyDecided, $"round {round}");
            var decisions = (await EventsAsync<BookingParentDecisionEvent>()).Where(e => e.BookingId == id).ToList();
            decisions.Count.ShouldBe(1, $"round {round}");
            var status = (await BookingAsync(id)).Status;
            status.ShouldBe(decisions[0].Decision == BookingParentDecisions.Approved ? BookingStatus.Pending : BookingStatus.ParentRejected);
            var teacherNotices = (await EventsAsync<BookingRequestCreatedEvent>()).Count(e => e.BookingId == id);
            teacherNotices.ShouldBe(status == BookingStatus.Pending ? 1 : 0, $"round {round}");
        }
    }

    [Fact]
    public async Task Open_request_limit_and_teacher_slot_delete_release_the_AwaitingParent_hold_on_postgres()
    {
        var w = await SeedAsync();
        var student = await StudentAsync(w.StudentUserId);
        var teacher = await TeacherAsync(w.TeacherUserId);

        var first = await student.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = w.SlotId });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await JsonAsync(first)).GetProperty("objectId").GetInt32();

        // Aynı öğretmen için ikinci açık AwaitingParent → 409 TooManyAwaitingParent.
        var second = await student.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = w.SlotId2 });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(second)).GetProperty("errorCode").GetString().ShouldBe(BookingErrorCodes.TooManyAwaitingParent);

        // Öğretmen yalnız AwaitingParent'in tuttuğu slotu silebilir: talep Expired + "öğretmen müsait değil" bildirimi.
        (await teacher.DeleteAsync($"/api/booking/slots/{w.SlotId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Expired);
        var notice = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        notice.BookingId.ShouldBe(id);
        notice.TeacherUnavailable.ShouldBeTrue();
        notice.Parents.Select(p => p.ParentUserId).OrderBy(x => x)
            .ShouldBe(new[] { w.ParentUserId, w.SecondParentUserId }.OrderBy(x => x));
        (await JsonAsync(await teacher.GetAsync("/api/booking/requests/teacher"))).GetProperty("items").GetArrayLength().ShouldBe(0);

        // Tavan boşaldı: aynı öğretmenden yeni talep açılabilir; öğretmene ulaşmış (Pending) talep slot silmeyi yine engeller.
        var again = await student.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = w.SlotId2 });
        again.StatusCode.ShouldBe(HttpStatusCode.Created);
        var againId = (await JsonAsync(again)).GetProperty("objectId").GetInt32();
        var parent = await ParentAsync(w.ParentUserId);
        (await parent.PostAsync($"/api/parent/booking-requests/{againId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var blocked = await teacher.DeleteAsync($"/api/booking/slots/{w.SlotId2}");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(blocked)).GetProperty("errorCode").GetString().ShouldBe(BookingErrorCodes.SlotHasActiveBooking);
    }

    private async Task<int> AwaitingRequestAsync(World w)
    {
        using var scope = Factory.Services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IBookingService>()
            .CreateBookingAsync(w.StudentUserId, new CreateBookingDto { AvailabilitySlotId = w.SlotId }, requiresParent: true);
        created.Success.ShouldBeTrue(created.Message);
        return created.ObjectId;
    }

    /// <summary>
    /// issue #439 re-review (yarış): veli onayı öğretmen müsaitlik advisory kilidini alır. Slot silme kilidi tutarken (talebi
    /// Expired yapıp slotu silmiş, henüz commit etmemiş) onay BEKLER; silme commit olunca onay slotu silinmiş görür → 409
    /// BookingRequestSlotDeleted, talep Pending olmaz, öğretmene bildirim gitmez. (Kilitsiz eski kod beklemeden Pending yazardı.)
    /// </summary>
    [Fact]
    public async Task Parent_approval_waits_for_an_uncommitted_slot_delete_and_is_then_refused()
    {
        var w = await SeedAsync();
        var id = await AwaitingRequestAsync(w);

        // Slot silme yolunun kilit altındaki hali (ham bağlantı: execution strategy kullanıcı transaction'ına izin vermez).
        await using var holder = new Npgsql.NpgsqlConnection(Factory.ConnectionString);
        await holder.OpenAsync();
        await using var tx = await holder.BeginTransactionAsync();
        await using (var cmd = new Npgsql.NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(323001, @t); " +
            "UPDATE \"Bookings\" SET \"Status\" = 4 WHERE \"Id\" = @b; " +
            "UPDATE \"TeacherAvailabilitySlots\" SET \"IsDeleted\" = TRUE WHERE \"Id\" = @s;", holder, tx))
        {
            cmd.Parameters.AddWithValue("t", w.TeacherId);
            cmd.Parameters.AddWithValue("b", id);
            cmd.Parameters.AddWithValue("s", w.SlotId);
            await cmd.ExecuteNonQueryAsync();
        }

        var approve = Task.Run(async () =>
        {
            using var scope = Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IBookingService>().ParentApproveBookingAsync(w.ParentUserId, id);
        });

        (await Task.WhenAny(approve, Task.Delay(TimeSpan.FromMilliseconds(750)))).ShouldNotBe(approve,
            "onay, slot silmenin tuttuğu müsaitlik kilidini beklemeli");
        await tx.CommitAsync();

        var result = await approve;
        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.RequestSlotDeleted);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Expired);
        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldBeEmpty();
        (await EventsAsync<BookingParentDecisionEvent>()).ShouldBeEmpty();
    }

    /// <summary>
    /// issue #439 re-review (yarış): slot silme ile veli onayı paralel — hangi sırada olursa olsun silinmiş slotta aktif (Pending)
    /// talep kalmaz ve yalnız bir taraf başarılı olur (#376 kuralı).
    /// </summary>
    [Fact]
    public async Task Parallel_slot_delete_and_parent_approval_never_leave_a_pending_request_on_a_deleted_slot()
    {
        const int rounds = 8;
        for (var round = 0; round < rounds; round++)
        {
            var w = await SeedAsync();
            var id = await AwaitingRequestAsync(w);
            using var gate = new SemaphoreSlim(0, 2);

            var delete = Task.Run(async () =>
            {
                using var scope = Factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
                await gate.WaitAsync();
                return await service.DeleteSlotAsync(w.TeacherUserId, w.SlotId);
            });
            var approve = Task.Run(async () =>
            {
                using var scope = Factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
                await gate.WaitAsync();
                return await service.ParentApproveBookingAsync(w.ParentUserId, id);
            });
            gate.Release(2);
            await Task.WhenAll(delete, approve);

            var deleted = (await delete).Success;
            var approved = (await approve).Success;
            (deleted ^ approved).ShouldBeTrue($"round {round}: tam olarak bir taraf kazanmalı (delete={deleted}, approve={approved})");

            var status = (await BookingAsync(id)).Status;
            var slotDeleted = await WithDbAsync(db => db.TeacherAvailabilitySlots.IgnoreQueryFilters()
                .Where(s => s.Id == w.SlotId).Select(s => s.IsDeleted).SingleAsync());
            if (deleted)
            {
                slotDeleted.ShouldBeTrue($"round {round}");
                status.ShouldBe(BookingStatus.Expired, $"round {round}");
                (await EventsAsync<BookingRequestCreatedEvent>()).Count(e => e.BookingId == id).ShouldBe(0, $"round {round}");
            }
            else
            {
                slotDeleted.ShouldBeFalse($"round {round}");
                status.ShouldBe(BookingStatus.Pending, $"round {round}");
                (await delete).ErrorCode.ShouldBe(BookingErrorCodes.SlotHasActiveBooking, $"round {round}");
            }
        }
    }

    [Fact]
    public async Task Filtered_unique_index_counts_AwaitingParent_as_an_active_booking()
    {
        var w = await SeedAsync();
        var otherStudentId = await WithDbAsync(db => db.Students.Where(s => s.UserId != w.StudentUserId).Select(s => s.Id).FirstAsync());

        await WithDbAsync(async db =>
        {
            db.Bookings.Add(new Booking
            {
                TeacherId = w.TeacherId, StudentId = w.StudentId, AvailabilitySlotId = w.SlotId,
                Status = BookingStatus.AwaitingParent, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });

        await Should.ThrowAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.Bookings.Add(new Booking
            {
                TeacherId = w.TeacherId, StudentId = otherStudentId, AvailabilitySlotId = w.SlotId,
                Status = BookingStatus.Pending, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }));

        // Expired / ParentRejected slotu tutmaz.
        await WithDbAsync(db => db.Bookings.Where(b => b.AvailabilitySlotId == w.SlotId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Expired)));
        await WithDbAsync(async db =>
        {
            db.Bookings.Add(new Booking
            {
                TeacherId = w.TeacherId, StudentId = otherStudentId, AvailabilitySlotId = w.SlotId,
                Status = BookingStatus.Pending, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Expiry_job_expires_old_and_started_requests_on_postgres()
    {
        var w = await SeedAsync();
        var now = DateTime.UtcNow;
        var (oldId, startedId, freshId, pendingId) = await WithDbAsync(async db =>
        {
            // Ders dün başladı (oluşturmadan yalnız 1 gün geçti → süre aşımı ders saatinden). Sabit dünkü tarih + sabit saat:
            // "şimdi"ye göre saat aritmetiği UTC 23:00–00:30 arasında gece yarısını aşıp testi kırıyordu (review #11).
            var started = new TeacherAvailabilitySlot
            {
                TeacherId = w.TeacherId, Date = DateOnly.FromDateTime(now).AddDays(-1), StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(11, 0), CreatedAt = now.AddDays(-2)
            };
            var spare = new TeacherAvailabilitySlot
            {
                TeacherId = w.TeacherId, Date = Future.AddDays(1), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), CreatedAt = now
            };
            db.TeacherAvailabilitySlots.AddRange(started, spare);
            await db.SaveChangesAsync();

            Booking B(int slotId, BookingStatus status, DateTime createdAt) => new()
            {
                TeacherId = w.TeacherId, StudentId = w.StudentId, AvailabilitySlotId = slotId, Status = status, CreatedAt = createdAt
            };
            var old = B(w.SlotId, BookingStatus.AwaitingParent, now.AddDays(-7).AddMinutes(-1));
            var startedBooking = B(started.Id, BookingStatus.AwaitingParent, now.AddDays(-1));
            var fresh = B(w.SlotId2, BookingStatus.AwaitingParent, now.AddDays(-6));
            var pending = B(spare.Id, BookingStatus.Pending, now.AddDays(-30));
            db.Bookings.AddRange(old, startedBooking, fresh, pending);
            await db.SaveChangesAsync();
            return (old.Id, startedBooking.Id, fresh.Id, pending.Id);
        });

        int expired;
        using (var scope = Factory.Services.CreateScope())
            expired = await scope.ServiceProvider.GetRequiredService<IAwaitingParentBookingExpiryJob>().ExpireAsync();
        int again;
        using (var scope = Factory.Services.CreateScope())
            again = await scope.ServiceProvider.GetRequiredService<IAwaitingParentBookingExpiryJob>().ExpireAsync();

        expired.ShouldBe(2);
        again.ShouldBe(0);
        (await BookingAsync(oldId)).Status.ShouldBe(BookingStatus.Expired);
        (await BookingAsync(startedId)).Status.ShouldBe(BookingStatus.Expired);
        (await BookingAsync(freshId)).Status.ShouldBe(BookingStatus.AwaitingParent);
        (await BookingAsync(pendingId)).Status.ShouldBe(BookingStatus.Pending);

        var events = await EventsAsync<BookingParentDecisionEvent>();
        events.Select(e => e.BookingId).OrderBy(x => x).ShouldBe(new[] { oldId, startedId }.OrderBy(x => x));
        events.ShouldAllBe(e => e.Decision == BookingParentDecisions.Expired && e.Parents.Count == 2);
    }
}
