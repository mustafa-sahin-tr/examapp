using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.WorksheetComments;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #335 (#326 D1): yorum moderasyon geçmişi — gizle/aç olayları (neden korunur, sıralı, idempotent), reddedilen
/// moderasyon denemeleri (yalnız id'ler, hız sınırı, best-effort yazım) ve geçmiş okuma ucunun yetki matrisi.
/// Seed/yardımcılar <c>WorksheetCommentServiceTests.cs</c> ve <c>WorksheetCommentModerationServiceTests.cs</c>'te.
/// </summary>
public partial class WorksheetCommentServiceTests
{
    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Moderasyon olayı eklenirken SaveChanges'ı patlatır (reddedilen deneme yazımı hatası simülasyonu).</summary>
    private sealed class FailModerationEventSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<WorksheetCommentModerationEvent>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated moderation event write failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private WorksheetCommentService ClockedService(AppDbContext ctx, TimeProvider clock) =>
        new(ctx, new WorksheetResponsibleTeacherResolver(ctx), _authApi, clock: clock);

    private async Task<List<WorksheetCommentModerationEvent>> ModerationEventsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.WorksheetCommentModerationEvents.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
    }

    private async Task<WorksheetCommentModerationHistoryResultDto> HistoryAsync(int worksheetId, int commentId,
        WorksheetCommentActor actor, int page = 1, int pageSize = 20)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).GetModerationHistoryAsync(worksheetId, commentId,
            new WorksheetCommentModerationHistoryQueryDto { Page = page, PageSize = pageSize }, actor);
    }

    // ---- Gizle / aç olayları --------------------------------------------------------------------------------

    [Fact]
    public async Task Hide_writes_a_moderation_event_with_the_reason_and_scope_evidence()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        (await HideAsync(w.WorksheetId, rootA, Teacher(Assigner), reason: "telefon numarası")).Success.ShouldBeTrue();

        var e = (await ModerationEventsAsync()).ShouldHaveSingleItem();
        e.Action.ShouldBe(WorksheetCommentModerationAction.Hide);
        e.WorksheetCommentId.ShouldBe(rootA);
        e.WorksheetId.ShouldBe(w.WorksheetId);
        e.ActorUserId.ShouldBe(Assigner);
        e.ActorRole.ShouldBe(WorksheetCommentModerationActorRoles.Teacher);
        e.ActorSchoolId.ShouldBe(w.SchoolId);
        e.CommentAuthorSchoolId.ShouldBe(w.SchoolId);
        e.Reason.ShouldBe("telefon numarası");
        e.CreatedAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        // AdminUserActionLog korunur (yeni tablo onu ikame etmez).
        await using var ctx = _db.NewContext();
        (await ctx.AdminUserActionLogs.SingleAsync()).Action.ShouldBe(AdminUserAction.CommentHidden);
    }

    [Fact]
    public async Task Unhide_event_keeps_the_previous_reason_that_the_comment_no_longer_has()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner), reason: "hakaret")).Success.ShouldBeTrue();
        (await HideAsync(w.WorksheetId, rootA, AdminReader, hidden: false)).Success.ShouldBeTrue();

        (await StoredCommentAsync(rootA)).HiddenReason.ShouldBeNull();
        var events = await ModerationEventsAsync();
        events.Count.ShouldBe(2);
        events[1].Action.ShouldBe(WorksheetCommentModerationAction.Unhide);
        events[1].Reason.ShouldBe("hakaret");
        events[1].ActorUserId.ShouldBe(AdminReader.UserId);
        events[1].ActorRole.ShouldBe(WorksheetCommentModerationActorRoles.Admin);
        events[1].ActorSchoolId.ShouldBeNull(); // admin'in öğretmen/öğrenci satırı yok
        events[1].CommentAuthorSchoolId.ShouldBe(w.SchoolId);
    }

    [Fact]
    public async Task Hide_unhide_hide_cycle_produces_three_ordered_events_and_repeats_write_nothing()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner), reason: "ilk neden")).Success.ShouldBeTrue();
        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner), reason: "tekrar")).Success.ShouldBeTrue();   // zaten gizli
        (await HideAsync(w.WorksheetId, rootA, Teacher(Assigner), hidden: false)).Success.ShouldBeTrue();
        (await HideAsync(w.WorksheetId, rootA, Teacher(Assigner), hidden: false)).Success.ShouldBeTrue();    // zaten açık
        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner, isAdmin: true), reason: "ikinci neden")).Success.ShouldBeTrue();

        var events = await ModerationEventsAsync();
        events.Select(e => (e.Action, e.Reason, e.ActorUserId)).ShouldBe(new[]
        {
            (WorksheetCommentModerationAction.Hide, (string?)"ilk neden", Owner),
            (WorksheetCommentModerationAction.Unhide, (string?)"ilk neden", Assigner),
            (WorksheetCommentModerationAction.Hide, (string?)"ikinci neden", Owner)
        });
        events[2].ActorRole.ShouldBe(WorksheetCommentModerationActorRoles.AdminTeacher);
        events.Select(e => e.CreatedAt).ShouldBeInOrder(SortDirection.Ascending);

        await using var ctx = _db.NewContext();
        (await ctx.AdminUserActionLogs.CountAsync()).ShouldBe(3);
    }

    // ---- Reddedilen denemeler -------------------------------------------------------------------------------

    [Fact]
    public async Task Not_moderator_rejections_stay_403_and_record_id_only_denied_events()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "numaram 0555 555 55 55"));

        ShouldFail(await HideAsync(w.WorksheetId, rootA, Student(StudentBUser)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        ShouldFail(await HideAsync(w.WorksheetId, rootA, Teacher(Unrelated), hidden: false), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        ShouldFail(await ReportsAsync(w.WorksheetId, Student(StudentBUser)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        ShouldFail(await ReportsAsync(null, Teacher(Owner)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);

        var events = await ModerationEventsAsync();
        events.Count.ShouldBe(4);
        events.ShouldAllBe(e => e.Action == WorksheetCommentModerationAction.DeniedNotModerator && e.Reason == null);

        // Erken ret (öğrenci; worksheet/yorum doğrulanmadan): id'ler yazılmaz (review D1).
        events[0].ShouldSatisfyAllConditions(
            e => e.WorksheetCommentId.ShouldBeNull(),
            e => e.WorksheetId.ShouldBeNull(),
            e => e.ActorUserId.ShouldBe(StudentBUser),
            e => e.ActorRole.ShouldBe(WorksheetCommentModerationActorRoles.Student),
            e => e.ActorSchoolId.ShouldBe(w.SchoolId),
            e => e.CommentAuthorSchoolId.ShouldBeNull());
        // Doğrulanmış (kapsamdaki, var olan) yorumda moderatör olmayan öğretmen: id'ler + kapsam kanıtı.
        events[1].ShouldSatisfyAllConditions(
            e => e.WorksheetCommentId.ShouldBe(rootA),
            e => e.WorksheetId.ShouldBe(w.WorksheetId),
            e => e.ActorUserId.ShouldBe(Unrelated),
            e => e.ActorRole.ShouldBe(WorksheetCommentModerationActorRoles.Teacher),
            e => e.ActorSchoolId.ShouldBe(w.SchoolId),
            e => e.CommentAuthorSchoolId.ShouldBe(w.SchoolId));
        // Rapor listesi: öğrenci erken ret, global listede öğretmen — ikisinde de id yok.
        events[2].ShouldSatisfyAllConditions(
            e => e.WorksheetCommentId.ShouldBeNull(),
            e => e.WorksheetId.ShouldBeNull(),
            e => e.ActorUserId.ShouldBe(StudentBUser));
        events[3].ShouldSatisfyAllConditions(
            e => e.WorksheetCommentId.ShouldBeNull(),
            e => e.WorksheetId.ShouldBeNull(),
            e => e.ActorUserId.ShouldBe(Owner));

        // PII yok: tabloda yorum gövdesi / ad taşıyabilecek tek serbest metin alanı Reason (reddedilen denemede null).
        typeof(WorksheetCommentModerationEvent).GetProperties().Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name).OrderBy(n => n).ShouldBe(new[] { "ActorRole", "Reason" });

        // Reddedilen denemeler yorumu değiştirmez ve AdminUserActionLog'a gizle/aç yazmaz.
        (await StoredCommentAsync(rootA)).HiddenAt.ShouldBeNull();
        await using var ctx = _db.NewContext();
        (await ctx.AdminUserActionLogs.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Admin_reader_report_is_403_but_not_a_denied_moderation_attempt()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        // Admin moderatördür (doğrudan gizler); şikayet edememesi reddedilen moderasyon denemesi sayılmaz (review 7).
        ShouldFail(await ReportAsync(w.WorksheetId, rootA, AdminReader), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        (await ModerationEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Denied_events_are_rate_limited_per_actor_and_comment_per_rolling_minute()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        var rootB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B"));
        var t0 = DateTimeOffset.UtcNow;
        var clock = new MutableClock(t0);

        async Task DenyAsync(int commentId)
        {
            await using var ctx = _db.NewContext();
            var r = await ClockedService(ctx, clock).SetHiddenAsync(w.WorksheetId, commentId, true,
                new HideWorksheetCommentDto { Reason = "x" }, Teacher(Unrelated));
            ShouldFail(r, WorksheetCommentErrorCodes.NotModerator, forbidden: true); // sınırı aşınca da 403
        }

        async Task DenyReportListAsync()
        {
            await using var ctx = _db.NewContext();
            var r = await ClockedService(ctx, clock).GetReportsAsync(null, new WorksheetCommentReportsQueryDto(), Teacher(Unrelated));
            ShouldFail(r, WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        }

        async Task<int> CountForAsync(int? commentId) =>
            (await ModerationEventsAsync()).Count(e => e.WorksheetCommentId == commentId);

        // t0, t0+1s, …, t0+9s → 10 olay; t0+10s'deki 11. deneme yazılmaz.
        for (var i = 0; i < WorksheetCommentService.DeniedEventsPerWindow + 1; i++)
        {
            clock.Now = t0.AddSeconds(i);
            await DenyAsync(rootA);
        }
        (await CountForAsync(rootA)).ShouldBe(10);

        // Kova (aktör, yorum) başına: başka yorum ve rapor listesi (yorum null) ayrı sayılır.
        await DenyAsync(rootB);
        await DenyReportListAsync();
        (await CountForAsync(rootB)).ShouldBe(1);
        (await CountForAsync(null)).ShouldBe(1);

        // Kayan pencere "son 60 sn" (CreatedAt > now - 1 dk): t0+59s'de t0..t0+9s hâlâ içeride (10) → yazılmaz.
        clock.Now = t0.AddSeconds(59);
        await DenyAsync(rootA);
        (await CountForAsync(rootA)).ShouldBe(10);

        // Tam t0+60s: t0'daki olay pencereden çıkar (sayım 9) → yeniden yazılır.
        clock.Now = t0.AddSeconds(60);
        await DenyAsync(rootA);
        (await CountForAsync(rootA)).ShouldBe(11);
    }

    private async Task SeedDeniedRowsAsync(int actorUserId, IEnumerable<(int? CommentId, DateTime At)> rows)
    {
        await using var ctx = _db.NewContext();
        ctx.WorksheetCommentModerationEvents.AddRange(rows.Select(r => new WorksheetCommentModerationEvent
        {
            Action = WorksheetCommentModerationAction.DeniedNotModerator,
            WorksheetCommentId = r.CommentId,
            ActorUserId = actorUserId,
            ActorRole = WorksheetCommentModerationActorRoles.Teacher,
            CreatedAt = r.At
        }));
        await ctx.SaveChangesAsync();
    }

    private async Task<int> DeniedCountAsync(int actorUserId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.WorksheetCommentModerationEvents.CountAsync(m => m.ActorUserId == actorUserId
            && m.Action == WorksheetCommentModerationAction.DeniedNotModerator);
    }

    private async Task DenyHideAsync(World w, int commentId, TimeProvider clock)
    {
        await using var ctx = _db.NewContext();
        ShouldFail(await ClockedService(ctx, clock).SetHiddenAsync(w.WorksheetId, commentId, true,
            new HideWorksheetCommentDto { Reason = "x" }, Teacher(Unrelated)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
    }

    [Fact]
    public async Task Denied_events_have_a_per_actor_cap_of_twenty_per_minute_across_comments()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        var rootB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B"));
        var now = DateTimeOffset.UtcNow;
        var clock = new MutableClock(now);

        // Son dakikada 19 farklı yorumda (yorum başına 1 — yorum kovası dolmaz) olay.
        await SeedDeniedRowsAsync(Unrelated, Enumerable.Range(0, 19)
            .Select(i => ((int?)(900_000 + i), now.UtcDateTime.AddSeconds(-30))));

        await DenyHideAsync(w, rootA, clock);
        (await DeniedCountAsync(Unrelated)).ShouldBe(20); // 20. yazıldı
        await DenyHideAsync(w, rootB, clock);
        (await DeniedCountAsync(Unrelated)).ShouldBe(20); // aktör/dakika sınırı: yazılmadı (rootB kovası boş olsa da)

        // Başka aktör etkilenmez.
        ShouldFail(await HideAsync(w.WorksheetId, rootA, Student(StudentBUser)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        (await DeniedCountAsync(StudentBUser)).ShouldBe(1);

        // Dakika geçince yeniden.
        clock.Now = now.AddSeconds(31);
        await DenyHideAsync(w, rootB, clock);
        (await DeniedCountAsync(Unrelated)).ShouldBe(21);
    }

    [Fact]
    public async Task Denied_events_have_a_per_actor_cap_of_two_hundred_per_day()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        var now = DateTimeOffset.UtcNow;
        var clock = new MutableClock(now);

        // Son 24 saate yayılmış 199 olay (dakikalık sınırlara takılmayacak kadar seyrek); biri 23 saat 59 dk önce.
        var oldest = now.UtcDateTime.AddHours(-24).AddSeconds(30);
        await SeedDeniedRowsAsync(Unrelated, Enumerable.Range(0, 199)
            .Select(i => ((int?)null, oldest.AddMinutes(i * 7))));

        await DenyHideAsync(w, rootA, clock);
        (await DeniedCountAsync(Unrelated)).ShouldBe(200);
        clock.Now = now.AddSeconds(5);
        await DenyHideAsync(w, rootA, clock);
        (await DeniedCountAsync(Unrelated)).ShouldBe(200); // 24 saat sınırı

        // En eski olay 24 saat penceresinden çıkınca yeniden yazılır.
        clock.Now = now.AddSeconds(31);
        await DenyHideAsync(w, rootA, clock);
        (await DeniedCountAsync(Unrelated)).ShouldBe(201);
    }

    [Fact]
    public async Task Denied_event_write_failure_does_not_change_the_403()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        await using (var ctx = _db.NewContext(new FailModerationEventSave()))
        {
            var service = NewService(ctx);
            ShouldFail(await service.SetHiddenAsync(w.WorksheetId, rootA, true, new HideWorksheetCommentDto { Reason = "x" }, Teacher(Unrelated)),
                WorksheetCommentErrorCodes.NotModerator, forbidden: true);
            ShouldFail(await service.GetReportsAsync(null, new WorksheetCommentReportsQueryDto(), Teacher(Owner)),
                WorksheetCommentErrorCodes.NotModerator, forbidden: true);

            // Başarısız olay tracker'da kalmaz: aynı scope'taki sonraki işlem etkilenmez.
            ctx.ChangeTracker.Entries<WorksheetCommentModerationEvent>().ShouldBeEmpty();
        }

        (await ModerationEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Denied_event_is_skipped_when_the_context_has_unrelated_pending_changes()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        await using (var ctx = _db.NewContext())
        {
            // Aynı scope'ta kaydedilmemiş başka bir değişiklik: best-effort SaveChanges onu commit etmemeli (review 6).
            ctx.Schools.Add(new School { Name = "yarım kalan" });
            ShouldFail(await NewService(ctx).SetHiddenAsync(w.WorksheetId, rootA, true, new HideWorksheetCommentDto { Reason = "x" },
                Teacher(Unrelated)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        }

        (await ModerationEventsAsync()).ShouldBeEmpty();
        await using var read = _db.NewContext();
        (await read.Schools.AnyAsync(s => s.Name == "yarım kalan")).ShouldBeFalse();
    }

    // ---- Geçmiş okuma ucu -----------------------------------------------------------------------------------

    [Fact]
    public async Task History_admin_sees_every_event_moderator_teacher_only_hide_and_unhide()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A (Assigner sorumlu)"));

        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner), reason: "kişisel bilgi")).Success.ShouldBeTrue();
        ShouldFail(await HideAsync(w.WorksheetId, rootA, Teacher(Unrelated), hidden: false), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        (await HideAsync(w.WorksheetId, rootA, Teacher(Assigner), hidden: false)).Success.ShouldBeTrue();

        var admin = await HistoryAsync(w.WorksheetId, rootA, AdminReader);
        admin.Success.ShouldBeTrue(admin.ErrorCode);
        admin.Page!.TotalCount.ShouldBe(3);
        admin.Page.Items.Select(i => i.Action).ShouldBe(new[] { "Unhide", "DeniedNotModerator", "Hide" }); // yeniden eskiye
        admin.Page.Items[0].ShouldSatisfyAllConditions(
            i => i.Reason.ShouldBe("kişisel bilgi"),
            i => i.ActorUserId.ShouldBe(Assigner),
            i => i.ActorRole.ShouldBe("Teacher"),
            i => i.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc));
        admin.Page.Items[1].Reason.ShouldBeNull();
        admin.Page.Items[1].ActorUserId.ShouldBe(Unrelated);
        admin.Page.Items[2].ActorUserId.ShouldBe(Owner);
        admin.Page.Items[2].Reason.ShouldBe("kişisel bilgi");

        // Admin rollü öğretmen de admin görünümü alır.
        (await HistoryAsync(w.WorksheetId, rootA, Teacher(Unrelated, isAdmin: true))).Page!.TotalCount.ShouldBe(3);

        var paged = (await HistoryAsync(w.WorksheetId, rootA, AdminReader, page: 2, pageSize: 1)).Page!;
        paged.TotalCount.ShouldBe(3);
        paged.Page.ShouldBe(2);
        paged.PageSize.ShouldBe(1);
        paged.Items.Single().Action.ShouldBe("DeniedNotModerator");
        (await HistoryAsync(w.WorksheetId, rootA, AdminReader, pageSize: 500)).Page!.PageSize.ShouldBe(WorksheetCommentLimits.MaxPageSize);
        var farPage = (await HistoryAsync(w.WorksheetId, rootA, AdminReader, page: int.MaxValue, pageSize: 50)).Page!;
        farPage.Page.ShouldBe(WorksheetCommentService.MaxPageNumber); // taşma yok
        farPage.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task History_hides_other_moderators_identity_and_reason_from_non_admin_moderators()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A (Assigner sorumlu)"));

        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner), reason: "sahibin nedeni")).Success.ShouldBeTrue();
        (await HideAsync(w.WorksheetId, rootA, Teacher(Assigner), hidden: false)).Success.ShouldBeTrue();

        // Sahip: kendi Hide'ı tam; atayanın Unhide'ında kimlik ve (sahibin yazdığı) neden gizli, rol görünür.
        var owner = (await HistoryAsync(w.WorksheetId, rootA, Teacher(Owner))).Page!;
        owner.TotalCount.ShouldBe(2);
        owner.Items[0].ShouldSatisfyAllConditions(
            i => i.Action.ShouldBe("Unhide"),
            i => i.ActorUserId.ShouldBeNull(),
            i => i.ActorRole.ShouldBe("Teacher"),
            i => i.Reason.ShouldBeNull());
        owner.Items[1].ShouldSatisfyAllConditions(
            i => i.Action.ShouldBe("Hide"),
            i => i.ActorUserId.ShouldBe(Owner),
            i => i.Reason.ShouldBe("sahibin nedeni"));

        // Atayan: kendi Unhide'ı tam (kaldırdığı neden dahil), sahibin Hide'ı anonim.
        var assigner = (await HistoryAsync(w.WorksheetId, rootA, Teacher(Assigner))).Page!;
        assigner.Items.Select(i => (i.Action, i.ActorUserId, i.Reason)).ShouldBe(new[]
        {
            ("Unhide", (int?)Assigner, (string?)"sahibin nedeni"),
            ("Hide", (int?)null, (string?)null)
        });

        // Admin her şeyi görür.
        (await HistoryAsync(w.WorksheetId, rootA, AdminReader)).Page!.Items
            .ShouldAllBe(i => i.ActorUserId != null && i.Reason == "sahibin nedeni");
    }

    [Fact]
    public async Task History_is_forbidden_to_non_moderators_and_hidden_from_out_of_scope_teachers()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner))).Success.ShouldBeTrue();

        ShouldFail(await HistoryAsync(w.WorksheetId, rootA, Teacher(Unrelated)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        ShouldFail(await HistoryAsync(w.WorksheetId, rootA, Student(StudentAUser)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        // Kapsam dışı öğretmen: yorum "yok" (varlık sızdırılmaz); yok / başka worksheet de 404.
        ShouldFail(await HistoryAsync(w.WorksheetId, rootA, Teacher(ForeignTeacher)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await HistoryAsync(w.WorksheetId, 999_999, AdminReader), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await HistoryAsync(w.OtherWorksheetId, rootA, AdminReader), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await HistoryAsync(999_999, rootA, AdminReader), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

        // Geçmiş çağrısının reddi de reddedilen deneme: doğrulanmış yorumda id'li, öğrencinin erken reddinde id'siz.
        var denied = (await ModerationEventsAsync()).Where(e => e.Action == WorksheetCommentModerationAction.DeniedNotModerator).ToList();
        denied.Select(e => (e.ActorUserId, e.WorksheetCommentId, e.WorksheetId)).ShouldBe(new[]
        {
            (Unrelated, (int?)rootA, (int?)w.WorksheetId),
            (StudentAUser, (int?)null, (int?)null)
        });
    }
}
