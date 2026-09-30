using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #305 (dilim B, security D1): bildirim hedefi gönderim anında yeniden doğrulanır — atama bitti/başkasına geçti,
/// öğretmen askıda/onaysız → bildirim GİTMEZ (yönlendirme yok); cevap yetkisi sabit kalır. issue #326 (D4): gizleme
/// <c>WorksheetCommentHiddenEvent</c>'i aynı transaction'da yazar. Seed/yardımcılar <c>WorksheetCommentServiceTests.cs</c>'te.
/// </summary>
public partial class WorksheetCommentServiceTests
{
    private static string HiddenType => OutboxEventRegistry.NameFor<WorksheetCommentHiddenEvent>();

    private async Task EndAssignmentAsync(int assignmentId)
    {
        await using var ctx = _db.NewContext();
        await ctx.WorksheetAssignments.Where(a => a.Id == assignmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.EndAt, (DateTime?)DateTime.UtcNow.AddMinutes(-1)));
    }

    private async Task SetTeacherAccountAsync(int userId, DateTime? approvedAt, DateTime? suspendedAt)
    {
        await using var ctx = _db.NewContext();
        await ctx.Teachers.Where(t => t.UserId == userId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.AccountApprovedAt, approvedAt)
            .SetProperty(t => t.AccountSuspendedAt, suspendedAt));
    }

    private async Task<List<WorksheetCommentCreatedEvent>> CreatedEventsAsync() =>
        (await OutboxAsync()).Where(r => r.Type == CreatedType)
            .Select(r => JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(r.Json)!).ToList();

    [Fact]
    public async Task Reply_after_the_assignment_ended_does_not_notify_the_pinned_teacher_or_anyone_else()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru")); // Assigner'a sabitlendi
        (await CreatedEventsAsync()).Single().RecipientUserId.ShouldBe(Assigner);

        await EndAssignmentAsync(w.AssignmentId); // sorumlu artık sahip (Owner) — Assigner ilişkisiz

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "ek not", parentId: root));

        var created = await CreatedEventsAsync();
        created.Count.ShouldBe(1); // yalnız ilk kök; ne eski öğretmene ne (yönlendirme) sahibe yeni bildirim
        created.ShouldNotContain(e => e.RecipientUserId == Owner);
    }

    [Fact]
    public async Task Reply_by_another_student_after_the_assignment_ended_does_not_notify_the_old_teacher_but_still_notifies_the_root_author()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A soruyor"));
        await EndAssignmentAsync(w.AssignmentId);

        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "bence şöyle", parentId: root));

        (await CreatedEventsAsync()).Count.ShouldBe(1); // yalnız ilk kök
        var replied = (await OutboxAsync()).Where(r => r.Type == RepliedType)
            .Select(r => JsonSerializer.Deserialize<WorksheetCommentRepliedEvent>(r.Json)!).Single();
        replied.RecipientUserId.ShouldBe(StudentAUser); // öğrenci→öğrenci bildirimi D1 kapsamı dışında
    }

    [Fact]
    public async Task Pinned_teacher_keeps_reply_authority_after_the_assignment_ended()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        await EndAssignmentAsync(w.AssignmentId);

        // Yalnız bildirim hedefi değişir; "bildirim alan öğretmen her zaman cevap yazabilir" kuralı (#105) sabit.
        var reply = await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: root);

        reply.Success.ShouldBeTrue(reply.ErrorCode);
    }

    [Fact]
    public async Task Still_valid_pinned_teacher_keeps_getting_reply_notifications()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "ek not", parentId: root));

        (await CreatedEventsAsync()).Select(e => e.RecipientUserId).ShouldBe(new[] { Assigner, Assigner });
    }

    [Fact]
    public async Task Suspended_teacher_gets_no_notification_for_new_roots_or_replies()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        await SetTeacherAccountAsync(Assigner, approvedAt: null, suspendedAt: DateTime.UtcNow);

        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "ek not", parentId: root));
        var newRoot = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "yeni soru"));

        reply.ShouldBeGreaterThan(0);
        newRoot.ShouldBeGreaterThan(0); // yorum her durumda kaydedilir
        (await CreatedEventsAsync()).Count.ShouldBe(1); // yalnız askıdan önceki ilk kök
    }

    [Fact]
    public async Task Not_approved_teacher_gets_no_notification()
    {
        var w = await SeedAsync();
        await SetTeacherAccountAsync(Assigner, approvedAt: null, suspendedAt: null);

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        (await OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Suspended_root_author_teacher_is_skipped_but_the_students_responsible_teacher_is_still_notified()
    {
        var w = await SeedAsync();
        // Sahip (Owner) duyuru kökü yazar; öğrenci A cevap yazar → alıcılar: kök yazarı Owner + öğrencinin öğretmeni Assigner.
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        await SetTeacherAccountAsync(Owner, approvedAt: null, suspendedAt: DateTime.UtcNow);

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru", parentId: announcement));

        (await CreatedEventsAsync()).Select(e => e.RecipientUserId).ShouldBe(new[] { Assigner });
    }

    [Fact]
    public async Task Suspended_teacher_is_not_redirected_to_the_current_responsible_teacher()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        await EndAssignmentAsync(w.AssignmentId);
        await SetTeacherAccountAsync(Assigner, approvedAt: null, suspendedAt: DateTime.UtcNow);

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "ek not", parentId: root));

        (await CreatedEventsAsync()).Count.ShouldBe(1);
    }

    // ---- issue #326 D4: gizleme event'i ----------------------------------------------------------------------

    [Fact]
    public async Task Hiding_a_comment_writes_a_hidden_event_with_only_ids_in_the_same_transaction()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "Ali Veli 5551234567", parentId: root));

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            var r = await NewService(ctx).SetHiddenAsync(w.WorksheetId, reply, true,
                new ExamApp.Api.Models.Dtos.WorksheetComments.HideWorksheetCommentDto { Reason = "kişisel bilgi" }, Teacher(Assigner));
            r.Success.ShouldBeTrue(r.ErrorCode);
        }

        var rows = (await OutboxAsync()).Where(r => r.Type == HiddenType).ToList();
        rows.Count.ShouldBe(1);
        var e = JsonSerializer.Deserialize<WorksheetCommentHiddenEvent>(rows[0].Json)!;
        e.EventId.ShouldNotBe(Guid.Empty);
        e.CommentId.ShouldBe(reply);
        e.RootCommentId.ShouldBe(root);
        e.WorksheetId.ShouldBe(w.WorksheetId);
        rows[0].Json.ShouldNotContain("5551234567");
        rows[0].Json.ShouldNotContain("kişisel bilgi");
    }

    [Fact]
    public async Task Repeated_hide_and_unhide_do_not_write_extra_hidden_events()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        (await HideAsync(w.WorksheetId, root, Teacher(Assigner))).Success.ShouldBeTrue();
        (await HideAsync(w.WorksheetId, root, Teacher(Assigner))).Success.ShouldBeTrue(); // zaten gizli: değişiklik yok
        (await HideAsync(w.WorksheetId, root, Teacher(Assigner), hidden: false)).Success.ShouldBeTrue();

        (await OutboxAsync()).Count(r => r.Type == HiddenType).ShouldBe(1); // unhide event yazmaz (metin nötr kalır)
        var e = JsonSerializer.Deserialize<WorksheetCommentHiddenEvent>((await OutboxAsync()).Single(r => r.Type == HiddenType).Json)!;
        e.CommentId.ShouldBe(root);
        e.RootCommentId.ShouldBe(root);
    }

    [Fact]
    public async Task Failed_hide_by_a_non_moderator_writes_no_event()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        var r = await HideAsync(w.WorksheetId, root, Teacher(Unrelated));

        r.Success.ShouldBeFalse();
        (await OutboxAsync()).Count(x => x.Type == HiddenType).ShouldBe(0);
    }

    [Fact]
    public void Hidden_event_is_registered_in_the_outbox_event_registry()
        => OutboxEventRegistry.Resolve(HiddenType).ShouldBe(typeof(WorksheetCommentHiddenEvent));
}
