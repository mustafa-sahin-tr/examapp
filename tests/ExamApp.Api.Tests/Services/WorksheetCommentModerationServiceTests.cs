using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.WorksheetComments;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #305 (dilim A): okul kapsamı (kısıtlama), şikayet, gizleme/açma (yer tutucu + audit), moderatör listesi.
/// Aynı sınıfın ikinci dosyası — seed/yardımcılar <c>WorksheetCommentServiceTests.cs</c>'te.
/// </summary>
public partial class WorksheetCommentServiceTests
{
    private const int OtherSchoolStudentUser = 104; // aynı sınıf, başka okul
    private const int SchoollessStudentUser = 105;  // aynı sınıf, okulsuz

    private async Task AddStudentAsync(World w, int userId, int? schoolId)
    {
        await using var ctx = _db.NewContext();
        ctx.Students.Add(new Student { UserId = userId, StudentNumber = $"s{userId}", GradeId = w.GradeId, SchoolId = schoolId });
        await ctx.SaveChangesAsync();
    }

    private async Task SetTeacherSchoolAsync(int userId, int? schoolId)
    {
        await using var ctx = _db.NewContext();
        await ctx.Teachers.Where(t => t.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(t => t.SchoolId, schoolId));
    }

    private async Task<World> SeedSchoolsAsync(WorksheetTeacherSharing sharing = WorksheetTeacherSharing.PublicView)
    {
        var w = await SeedAsync(sharing: sharing);
        await AddStudentAsync(w, OtherSchoolStudentUser, w.OtherSchoolId);
        await AddStudentAsync(w, SchoollessStudentUser, null);
        return w;
    }

    private async Task<WorksheetCommentReportResultDto> ReportAsync(int worksheetId, int commentId, WorksheetCommentActor actor,
        string? reason = "spam", string? note = null)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).ReportAsync(worksheetId, commentId, new ReportWorksheetCommentDto { Reason = reason, Note = note }, actor);
    }

    private async Task<WorksheetCommentResultDto> HideAsync(int worksheetId, int commentId, WorksheetCommentActor actor,
        string? reason = "kişisel bilgi paylaşımı", bool hidden = true)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).SetHiddenAsync(worksheetId, commentId, hidden,
            hidden ? new HideWorksheetCommentDto { Reason = reason } : null, actor);
    }

    private async Task<WorksheetCommentPageResultDto> GetModeratorAsync(int worksheetId, WorksheetCommentActor actor)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).GetThreadAsync(worksheetId, new WorksheetCommentQueryDto { ModeratorView = true }, actor);
    }

    private async Task<WorksheetCommentReportsResultDto> ReportsAsync(int? worksheetId, WorksheetCommentActor actor, int page = 1, int pageSize = 20)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).GetReportsAsync(worksheetId, new WorksheetCommentReportsQueryDto { Page = page, PageSize = pageSize }, actor);
    }

    private static List<int> Ids(WorksheetCommentPageResultDto r)
    {
        r.Success.ShouldBeTrue(r.ErrorCode);
        return r.Page!.Items.Select(i => i.Id).OrderBy(x => x).ToList();
    }

    // ---- Okul kapsamı ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Author_school_is_pinned_at_write_time()
    {
        var w = await SeedSchoolsAsync();
        var a = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        var c = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "C"));
        var d = Created(await PostAsync(w.WorksheetId, Student(SchoollessStudentUser), "D"));
        var t = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));

        (await StoredCommentAsync(a)).AuthorSchoolId.ShouldBe(w.SchoolId);
        (await StoredCommentAsync(c)).AuthorSchoolId.ShouldBe(w.OtherSchoolId);
        (await StoredCommentAsync(d)).AuthorSchoolId.ShouldBeNull();
        (await StoredCommentAsync(t)).AuthorSchoolId.ShouldBe(w.SchoolId); // #305 Y1: öğretmen yorumunda da

        // Yazarın okulu sonradan değişse de yorum yazıldığı okulda kalır.
        await using (var ctx = _db.NewContext())
            await ctx.Students.Where(s => s.UserId == StudentAUser)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SchoolId, (int?)w.OtherSchoolId));
        (await StoredCommentAsync(a)).AuthorSchoolId.ShouldBe(w.SchoolId);
    }

    [Fact]
    public async Task Students_see_same_school_student_comments_their_own_and_every_teacher_comment()
    {
        var w = await SeedSchoolsAsync();
        var a = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        var c = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "C"));
        var d = Created(await PostAsync(w.WorksheetId, Student(SchoollessStudentUser), "D"));
        var t = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));

        Ids(await GetAsync(w.WorksheetId, Student(StudentBUser))).ShouldBe(new[] { a, t });
        Ids(await GetAsync(w.WorksheetId, Student(OtherSchoolStudentUser))).ShouldBe(new[] { c, t });
        // Okulsuz öğrenci: yalnız kendi yorumu + öğretmen yorumları; okulsuz yazarın yorumu başkasına görünmez.
        Ids(await GetAsync(w.WorksheetId, Student(SchoollessStudentUser))).ShouldBe(new[] { d, t });
        Ids(await GetAsync(w.WorksheetId, AdminReader)).ShouldBe(new[] { a, c, d, t });
    }

    [Fact]
    public async Task Out_of_school_teacher_sees_only_teacher_comments_on_a_public_worksheet()
    {
        var w = await SeedSchoolsAsync();
        var a = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        var c = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "C"));
        var t = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));

        // ForeignTeacher diğer okulda: o okulun öğrencisi C'yi görür, A'yı görmez.
        Ids(await GetAsync(w.WorksheetId, Teacher(ForeignTeacher))).ShouldBe(new[] { c, t });
        Ids(await GetAsync(w.WorksheetId, Teacher(Unrelated))).ShouldBe(new[] { a, t });
        Ids(await GetAsync(w.WorksheetId, Teacher(Unrelated, isAdmin: true))).ShouldBe(new[] { a, c, t });

        // Okulsuz (bağımsız) okul dışı öğretmen: yalnız öğretmen yorumları.
        await SetTeacherSchoolAsync(ForeignTeacher, null);
        Ids(await GetAsync(w.WorksheetId, Teacher(ForeignTeacher))).ShouldBe(new[] { t });
    }

    [Fact]
    public async Task Owner_outside_the_school_no_longer_sees_even_the_threads_pinned_to_them_as_owner()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A (atayan sorumlu)"));
        var rootB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B (sahip sorumlu)"));
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A B'ye cevap", parentId: rootB));
        await SetTeacherSchoolAsync(Owner, w.OtherSchoolId);

        // issue #326 (security O1): rootB'nin sabiti SAHİP kaynaklı → sahip okuldan ayrılınca geçersiz (eskiden #305'te
        // sahip okul dışına taşınsa da sorumlusu olduğu thread'i görmeye devam ederdi). Atama kaynaklı sabitler etkilenmez
        // (bkz. Assignment_pin_is_not_affected_when_the_assigner_changes_school).
        var page = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!;
        page.Items.ShouldBeEmpty();
        ShouldFail(await GetRepliesAsync(w.WorksheetId, rootA, Teacher(Owner)), WorksheetCommentErrorCodes.RootCommentNotFound, notFound: true);
        ShouldFail(await GetRepliesAsync(w.WorksheetId, rootB, Teacher(Owner)), WorksheetCommentErrorCodes.RootCommentNotFound, notFound: true);
    }

    [Fact]
    public async Task Independent_responsible_teacher_sees_their_threads_and_pinned_replies_on_teacher_roots()
    {
        var w = await SeedSchoolsAsync();
        await SetTeacherSchoolAsync(Assigner, null); // bağımsız tutor
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        var replyB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B cevap", parentId: rootA));
        var rootB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B kök"));
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        var replyOnAnnouncementA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A duyuruya", parentId: announcement));
        var replyOnAnnouncementB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B duyuruya", parentId: announcement));

        // Öğretmen köküne yazılan öğrenci reply'ına o öğrencinin sorumlu öğretmeni sabitlenir (bildirim alıcısı = görebilen).
        (await StoredCommentAsync(replyOnAnnouncementA)).ResponsibleTeacherUserId.ShouldBe(Assigner);
        (await StoredCommentAsync(replyOnAnnouncementB)).ResponsibleTeacherUserId.ShouldBe(Owner);

        var page = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!;
        page.Items.Select(i => i.Id).OrderBy(x => x).ShouldBe(new[] { rootA, announcement });
        page.Items.Single(i => i.Id == rootA).Replies.Select(r => r.Id).ShouldBe(new[] { replyB });
        var ann = page.Items.Single(i => i.Id == announcement);
        ann.Replies.Select(r => r.Id).ShouldBe(new[] { replyOnAnnouncementA });
        ann.ReplyCount.ShouldBe(1);
        page.Items.ShouldNotContain(i => i.Id == rootB);
    }

    [Fact]
    public async Task Reply_count_and_previews_follow_the_readers_school_scope()
    {
        var w = await SeedSchoolsAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        var fromA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A", parentId: announcement));
        var fromC = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "C", parentId: announcement));
        var fromOwner = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "hoca", parentId: announcement));

        var asB = (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Single();
        asB.ReplyCount.ShouldBe(2);
        asB.Replies.Select(r => r.Id).ShouldBe(new[] { fromA, fromOwner });
        (await GetRepliesAsync(w.WorksheetId, announcement, Student(StudentBUser))).Page!.Items.Select(r => r.Id)
            .ShouldBe(new[] { fromA, fromOwner });
        (await GetRepliesAsync(w.WorksheetId, announcement, Student(StudentBUser))).Page!.ReplyCount.ShouldBe(2);

        var asC = (await GetAsync(w.WorksheetId, Student(OtherSchoolStudentUser))).Page!.Items.Single();
        asC.ReplyCount.ShouldBe(2);
        asC.Replies.Select(r => r.Id).ShouldBe(new[] { fromC, fromOwner });

        // Sahip kök yazarı: kendi okulundaki reply'ları görür; issue #326 (O2): atamasız okul dışı öğrencinin (C) reply'ı
        // sahibe sabitlenmez → sahip görmez (duyuru köküne ayrı istisna yok). Admin hepsini.
        var asOwner = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single();
        asOwner.ReplyCount.ShouldBe(2);
        asOwner.Replies.Select(r => r.Id).ShouldBe(new[] { fromA, fromOwner });
        (await GetAsync(w.WorksheetId, AdminReader)).Page!.Items.Single().ReplyCount.ShouldBe(3);
    }

    [Fact]
    public async Task Preview_keeps_five_latest_in_scope_replies_even_when_newer_out_of_scope_ones_exist()
    {
        var w = await SeedSchoolsAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        var inScope = new List<int>();
        for (var i = 0; i < 5; i++)
            inScope.Add(Created(await PostAsync(w.WorksheetId, Student(StudentAUser), $"A{i}", parentId: announcement)));
        for (var i = 0; i < 5; i++)
            Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), $"C{i}", parentId: announcement));

        var thread = (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Single();
        thread.Replies.Select(r => r.Id).ShouldBe(inScope);
        thread.ReplyCount.ShouldBe(5);
    }

    [Fact]
    public async Task Student_cannot_reply_to_a_root_outside_their_scope()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        ShouldFail(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "x", parentId: rootA), WorksheetCommentErrorCodes.InvalidParent);
        ShouldFail(await PostAsync(w.WorksheetId, Student(SchoollessStudentUser), "x", parentId: rootA), WorksheetCommentErrorCodes.InvalidParent);
        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "aynı okul", parentId: rootA));
    }

    // ---- Şikayet --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reader_reports_once_and_a_repeat_is_idempotent()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        var first = await ReportAsync(w.WorksheetId, rootA, Student(StudentBUser), "personalInfo", "  telefon numarası yazmış  ");
        first.Success.ShouldBeTrue(first.ErrorCode);
        first.AlreadyReported.ShouldBeFalse();
        first.ReportedByMe.ShouldBeTrue();

        var again = await ReportAsync(w.WorksheetId, rootA, Student(StudentBUser), "spam");
        again.Success.ShouldBeTrue();
        again.AlreadyReported.ShouldBeTrue();

        (await ReportAsync(w.WorksheetId, rootA, Teacher(Unrelated), "ABUSE")).AlreadyReported.ShouldBeFalse(); // öğretmen de şikayet eder

        await using (var ctx = _db.NewContext())
        {
            var rows = await ctx.WorksheetCommentReports.Where(r => r.CommentId == rootA).OrderBy(r => r.Id).ToListAsync();
            rows.Count.ShouldBe(2);
            rows[0].Reason.ShouldBe(WorksheetCommentReportReason.PersonalInfo);
            rows[0].Note.ShouldBe("telefon numarası yazmış");
            rows[0].ReporterUserId.ShouldBe(StudentBUser);
            rows[0].ReporterKeycloakId.ShouldBe($"kc-{StudentBUser}");
            rows[1].Reason.ShouldBe(WorksheetCommentReportReason.Abuse);
        }

        var asB = (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Single();
        asB.ReportedByMe.ShouldBeTrue();
        asB.ReportCount.ShouldBeNull(); // yalnız moderatöre
        asB.CanModerate.ShouldBeFalse();
        (await GetAsync(w.WorksheetId, Student(StudentAUser))).Page!.Items.Single().ReportedByMe.ShouldBeFalse();

        var asOwner = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single();
        asOwner.CanModerate.ShouldBeTrue();
        asOwner.ReportCount.ShouldBe(2);
        var asAssigner = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single();
        asAssigner.CanModerate.ShouldBeTrue();
        asAssigner.ReportCount.ShouldBe(2);
        (await GetAsync(w.WorksheetId, Teacher(Unrelated))).Page!.Items.Single().ReportCount.ShouldBeNull();
    }

    [Fact]
    public async Task Report_rejects_own_comment_bad_input_and_out_of_scope_targets()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        ShouldFail(await ReportAsync(w.WorksheetId, rootA, Student(StudentAUser)), WorksheetCommentErrorCodes.CannotReportOwnComment, forbidden: true);
        ShouldFail(await ReportAsync(w.WorksheetId, rootA, Student(StudentBUser), "hate"), WorksheetCommentErrorCodes.InvalidReportReason);
        ShouldFail(await ReportAsync(w.WorksheetId, rootA, Student(StudentBUser), null), WorksheetCommentErrorCodes.InvalidReportReason);
        ShouldFail(await ReportAsync(w.WorksheetId, rootA, Student(StudentBUser), "other", new string('x', 501)),
            WorksheetCommentErrorCodes.ReportNoteTooLong);
        ShouldFail(await ReportAsync(w.WorksheetId, rootA, Student(StudentBUser), "other", "a\u0000b"),
            WorksheetCommentErrorCodes.ModerationTextInvalidCharacters);

        // Kapsam dışı / yok / başka worksheet → 404 (varlık sızdırılmaz); erişimi olmayan öğrenci → worksheet 404.
        ShouldFail(await ReportAsync(w.WorksheetId, rootA, Student(OtherSchoolStudentUser)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await ReportAsync(w.WorksheetId, 999_999, Student(StudentBUser)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await ReportAsync(w.OtherWorksheetId, rootA, Student(StudentBUser)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await ReportAsync(w.WorksheetId, rootA, Student(OutsiderUser)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

        await using var ctx = _db.NewContext();
        (await ctx.WorksheetCommentReports.CountAsync()).ShouldBe(0);
    }

    // ---- Gizleme / açma -------------------------------------------------------------------------------------

    [Fact]
    public async Task Hide_permission_matrix_owner_responsible_teacher_and_admin_only()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A (Assigner sorumlu)"));
        var replyB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B", parentId: rootA));
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "Assigner duyurusu"));

        ShouldFail(await HideAsync(w.WorksheetId, rootA, Student(StudentBUser)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        ShouldFail(await HideAsync(w.WorksheetId, rootA, Student(StudentAUser)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        ShouldFail(await HideAsync(w.WorksheetId, rootA, Teacher(Unrelated)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        // Kapsam dışı moderatör adayı: yorum "yok".
        ShouldFail(await HideAsync(w.WorksheetId, rootA, Teacher(ForeignTeacher)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);

        (await HideAsync(w.WorksheetId, rootA, Teacher(Assigner))).Success.ShouldBeTrue();        // thread'in sorumlusu
        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner), hidden: false)).Success.ShouldBeTrue(); // sahip
        (await HideAsync(w.WorksheetId, replyB, Teacher(Assigner))).Success.ShouldBeTrue();       // reply → kökün sorumlusu
        (await HideAsync(w.WorksheetId, replyB, AdminReader, hidden: false)).Success.ShouldBeTrue(); // admin
        (await HideAsync(w.WorksheetId, announcement, Teacher(Assigner))).Success.ShouldBeTrue(); // öğretmen kökünün yazarı
        ShouldFail(await HideAsync(w.WorksheetId, rootA, Teacher(Owner), reason: "   "), WorksheetCommentErrorCodes.HideReasonRequired);
        ShouldFail(await HideAsync(w.WorksheetId, rootA, Teacher(Owner), reason: new string('x', 501)), WorksheetCommentErrorCodes.HideReasonTooLong);
        ShouldFail(await HideAsync(w.WorksheetId, 999_999, Teacher(Owner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
    }

    [Fact]
    public async Task Hidden_root_is_a_placeholder_its_replies_stay_and_moderators_can_reveal_it()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "numaram 0555 555 55 55"));
        var replyB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B cevap", parentId: rootA));

        var hidden = await HideAsync(w.WorksheetId, rootA, Teacher(Owner), reason: "telefon numarası");
        hidden.Success.ShouldBeTrue(hidden.ErrorCode);
        hidden.Comment!.IsHidden.ShouldBeTrue();
        hidden.Comment.Body.ShouldBe("numaram 0555 555 55 55"); // yanıt moderatör görünümünde
        hidden.Comment.HiddenReason.ShouldBe("telefon numarası");

        var asB = (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Single();
        asB.IsHidden.ShouldBeTrue();
        asB.Body.ShouldBeNull();
        asB.AuthorDisplayName.ShouldBe("Kaldırıldı");
        asB.HiddenReason.ShouldBeNull();
        asB.HiddenAt.ShouldBeNull();
        asB.CanReply.ShouldBeFalse();
        asB.Replies.Single().Id.ShouldBe(replyB);
        asB.Replies.Single().Body.ShouldBe("B cevap");
        // Öğrenci moderatorView istese de gövdeyi göremez; yazar da.
        (await GetModeratorAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Single().Body.ShouldBeNull();
        (await GetModeratorAsync(w.WorksheetId, Student(StudentAUser))).Page!.Items.Single().Body.ShouldBeNull();

        var ownerDefault = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single();
        ownerDefault.Body.ShouldBeNull();
        ownerDefault.CanModerate.ShouldBeTrue();
        var ownerModerator = (await GetModeratorAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single();
        ownerModerator.Body.ShouldBe("numaram 0555 555 55 55");
        ownerModerator.AuthorDisplayName.ShouldBe("Ayşe Nur K.");
        ownerModerator.HiddenReason.ShouldBe("telefon numarası");
        ownerModerator.HiddenAt.ShouldNotBeNull();
        (await GetModeratorAsync(w.WorksheetId, Teacher(Unrelated))).Page!.Items.Single().Body.ShouldBeNull();

        // Gizli köke kimse yeni cevap yazamaz.
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentBUser), "x", parentId: rootA), WorksheetCommentErrorCodes.RootCommentHidden, forbidden: true);
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Assigner), "x", parentId: rootA), WorksheetCommentErrorCodes.RootCommentHidden, forbidden: true);
        (await GetRepliesAsync(w.WorksheetId, rootA, Teacher(Assigner))).Page!.CanReply.ShouldBeFalse();
    }

    [Fact]
    public async Task Hide_and_unhide_are_audited_without_the_reason_and_are_idempotent()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));

        (await HideAsync(w.WorksheetId, rootA, Teacher(Assigner), reason: "gizli neden 12345")).Success.ShouldBeTrue();
        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner), reason: "ikinci")).Success.ShouldBeTrue(); // zaten gizli: değişmez

        var stored = await StoredCommentAsync(rootA);
        stored.HiddenAt.ShouldNotBeNull();
        stored.HiddenByUserId.ShouldBe(Assigner);
        stored.HiddenReason.ShouldBe("gizli neden 12345");
        stored.IsDeleted.ShouldBeFalse(); // moderasyon gizlemesi soft-delete bayrağı değil

        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner), hidden: false)).Comment!.IsHidden.ShouldBeFalse();
        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner), hidden: false)).Success.ShouldBeTrue(); // zaten açık

        stored = await StoredCommentAsync(rootA);
        stored.HiddenAt.ShouldBeNull();
        stored.HiddenByUserId.ShouldBeNull();
        stored.HiddenReason.ShouldBeNull();
        (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Single().Body.ShouldBe("A");

        await using var ctx = _db.NewContext();
        var logs = await ctx.AdminUserActionLogs.OrderBy(l => l.Id).ToListAsync();
        logs.Count.ShouldBe(2);
        logs[0].Action.ShouldBe(AdminUserAction.CommentHidden);
        logs[0].ActorKeycloakId.ShouldBe($"kc-{Assigner}");
        logs[1].Action.ShouldBe(AdminUserAction.CommentUnhidden);
        logs[1].ActorKeycloakId.ShouldBe($"kc-{Owner}");
        logs.ShouldAllBe(l => l.TargetType == AdminUserTargetType.WorksheetComment && l.TargetId == rootA
            && l.Outcome == AdminUserActionOutcome.Succeeded);
    }

    // ---- Moderatör listesi ----------------------------------------------------------------------------------

    [Fact]
    public async Task Reports_list_is_scoped_to_what_the_caller_moderates_with_reason_counts_and_paging()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A (Assigner sorumlu)"));
        var rootB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B (sahip sorumlu)"));
        var clean = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "şikayetsiz"));

        await ReportAsync(w.WorksheetId, rootA, Student(StudentBUser), "abuse");
        await ReportAsync(w.WorksheetId, rootA, Teacher(Unrelated), "spam", "reklam linki");
        await ReportAsync(w.WorksheetId, rootB, Student(StudentAUser), "personalInfo", "adres");
        (await HideAsync(w.WorksheetId, rootB, Teacher(Owner))).Success.ShouldBeTrue();

        var owner = (await ReportsAsync(w.WorksheetId, Teacher(Owner))).Page!;
        owner.TotalCount.ShouldBe(2);
        owner.Items.Select(i => i.Comment.Id).ShouldBe(new[] { rootB, rootA }); // son şikayet önce
        owner.Items.ShouldNotContain(i => i.Comment.Id == clean);
        var hiddenItem = owner.Items[0];
        hiddenItem.Comment.IsHidden.ShouldBeTrue();
        hiddenItem.Comment.Body.ShouldBe("B (sahip sorumlu)"); // moderatör listesi gizli gövdeyi gösterir
        hiddenItem.ReportCount.ShouldBe(1);
        hiddenItem.Reasons.PersonalInfo.ShouldBe(1);
        hiddenItem.Notes.ShouldBe(new[] { "adres" });
        hiddenItem.WorksheetTitle.ShouldBe("Kesirler");
        var itemA = owner.Items[1];
        itemA.ReportCount.ShouldBe(2);
        itemA.Reasons.Abuse.ShouldBe(1);
        itemA.Reasons.Spam.ShouldBe(1);
        itemA.Notes.ShouldBe(new[] { "reklam linki" });
        itemA.Comment.ReportCount.ShouldBe(2);

        (await ReportsAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Select(i => i.Comment.Id).ShouldBe(new[] { rootA });
        (await ReportsAsync(w.WorksheetId, Teacher(Unrelated))).Page!.Items.ShouldBeEmpty();

        var paged = (await ReportsAsync(w.WorksheetId, Teacher(Owner), page: 2, pageSize: 1)).Page!;
        paged.TotalCount.ShouldBe(2);
        paged.Items.Single().Comment.Id.ShouldBe(rootA);

        // Global liste yalnız admin'e.
        (await ReportsAsync(null, AdminReader)).Page!.TotalCount.ShouldBe(2);
        ShouldFail(await ReportsAsync(null, Teacher(Owner)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
        ShouldFail(await ReportsAsync(w.WorksheetId, Student(StudentBUser)), WorksheetCommentErrorCodes.NotModerator, forbidden: true);
    }

    [Fact]
    public async Task Owner_outside_the_school_does_not_see_reports_on_out_of_scope_comments()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        await ReportAsync(w.WorksheetId, rootA, Student(StudentBUser), "spam");
        await SetTeacherSchoolAsync(Owner, w.OtherSchoolId);

        (await ReportsAsync(w.WorksheetId, Teacher(Owner))).Page!.TotalCount.ShouldBe(0);
        ShouldFail(await HideAsync(w.WorksheetId, rootA, Teacher(Owner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        (await ReportsAsync(w.WorksheetId, Teacher(Assigner))).Page!.TotalCount.ShouldBe(1);
    }

    // ---- Review düzeltmeleri: security Y1 (öğretmen kökü üzerinden okullar arası kanal) + O1 (reply'ın kökü) ------

    /// <summary>C (diğer okul) için ForeignTeacher (diğer okul) aktif atama yapar → C'nin ilgili öğretmeni ForeignTeacher.</summary>
    private async Task AssignOtherSchoolStudentToForeignTeacherAsync(World w)
    {
        await using var ctx = _db.NewContext();
        var c = await ctx.Students.SingleAsync(s => s.UserId == OtherSchoolStudentUser);
        ctx.SetCurrentUser(ForeignTeacher);
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = w.WorksheetId, StudentId = c.Id, StartAt = DateTime.UtcNow.AddDays(-1)
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<List<int>> CreatedRecipientsAsync() =>
        (await OutboxAsync()).Where(r => r.Type == CreatedType)
            .Select(r => System.Text.Json.JsonSerializer.Deserialize<ExamApp.Foundation.Contracts.WorksheetCommentCreatedEvent>(r.Json)!.RecipientUserId)
            .ToList();

    private async Task ClearOutboxAsync()
    {
        await using var ctx = _db.NewContext();
        await ctx.OutboxMessages.ExecuteDeleteAsync();
    }

    private async Task AddReportRowAsync(int commentId, int reporterUserId)
    {
        await using var ctx = _db.NewContext();
        ctx.WorksheetCommentReports.Add(new WorksheetCommentReport
        {
            CommentId = commentId, ReporterUserId = reporterUserId, ReporterKeycloakId = $"kc-{reporterUserId}",
            Reason = WorksheetCommentReportReason.Abuse
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Teacher_comments_pin_the_authors_school()
    {
        var w = await SeedSchoolsAsync();
        await SetTeacherSchoolAsync(Assigner, null); // bağımsız
        var owner = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        var tutor = Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "tutor duyurusu"));

        (await StoredCommentAsync(owner)).AuthorSchoolId.ShouldBe(w.SchoolId);
        (await StoredCommentAsync(tutor)).AuthorSchoolId.ShouldBeNull();
    }

    [Fact]
    public async Task Students_see_teacher_comments_only_from_their_school_the_owner_their_own_teacher_or_their_threads()
    {
        var w = await SeedSchoolsAsync();
        await AssignOtherSchoolStudentToForeignTeacherAsync(w);
        await SetTeacherSchoolAsync(Assigner, null); // bağımsız tutor, A'nın ilgili öğretmeni

        var ownerRoot = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "sahip duyurusu"));        // X okulu, sahip
        var tutorRoot = Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "tutor duyurusu"));     // okulsuz, A'nın öğretmeni
        var foreignRoot = Created(await PostAsync(w.WorksheetId, Teacher(ForeignTeacher), "Y duyurusu")); // Y okulu, C'nin öğretmeni
        var rootB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B sorusu"));           // sorumlu: sahip
        var ownerAnswer = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "cevap", parentId: rootB));

        Ids(await GetAsync(w.WorksheetId, Student(StudentAUser))).ShouldBe(new[] { ownerRoot, tutorRoot, rootB });
        Ids(await GetAsync(w.WorksheetId, Student(StudentBUser))).ShouldBe(new[] { ownerRoot, rootB });
        Ids(await GetAsync(w.WorksheetId, Student(OtherSchoolStudentUser))).ShouldBe(new[] { ownerRoot, foreignRoot });
        Ids(await GetAsync(w.WorksheetId, Student(SchoollessStudentUser))).ShouldBe(new[] { ownerRoot });
        // Öğrenci kökündeki öğretmen cevabı, kökü görene görünür.
        (await GetAsync(w.WorksheetId, Student(StudentAUser))).Page!.Items.Single(i => i.Id == rootB)
            .Replies.Select(r => r.Id).ShouldBe(new[] { ownerAnswer });
        // Öğretmenler tüm öğretmen yorumlarını görür (PO).
        Ids(await GetAsync(w.WorksheetId, Teacher(ForeignTeacher))).ShouldBe(new[] { ownerRoot, tutorRoot, foreignRoot });
    }

    [Fact]
    public async Task Root_author_teacher_does_not_see_get_notified_by_or_moderate_other_school_replies()
    {
        var w = await SeedSchoolsAsync();
        await AssignOtherSchoolStudentToForeignTeacherAsync(w);
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru")); // sahip, X okulu
        await ClearOutboxAsync();

        var fromC = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "C (Y okulu)", parentId: announcement));
        (await StoredCommentAsync(fromC)).ResponsibleTeacherUserId.ShouldBe(ForeignTeacher);

        // Bildirim: yalnız C'nin sabitlenmiş sorumlusu; okul dışı kök yazarı alıcı değil.
        (await CreatedRecipientsAsync()).ShouldBe(new[] { ForeignTeacher });

        // Görünürlük: kök yazarı (sahip) görmez; C'nin sorumlusu görür.
        var asOwner = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single();
        asOwner.ReplyCount.ShouldBe(0);
        asOwner.Replies.ShouldBeEmpty();
        (await GetAsync(w.WorksheetId, Teacher(ForeignTeacher))).Page!.Items.Single().Replies.Select(r => r.Id).ShouldBe(new[] { fromC });

        // Moderasyon: şikayet listesinde yok, gizleyemez (yorum "yok"); admin görür.
        await AddReportRowAsync(fromC, 424242);
        (await ReportsAsync(w.WorksheetId, Teacher(Owner))).Page!.TotalCount.ShouldBe(0);
        ShouldFail(await HideAsync(w.WorksheetId, fromC, Teacher(Owner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        (await ReportsAsync(null, AdminReader)).Page!.Items.Select(i => i.Comment.Id).ShouldBe(new[] { fromC });

        // Aynı okuldan reply'da kök yazarı hâlâ alıcı (B'nin sorumlusu da sahip → tek alıcı).
        await ClearOutboxAsync();
        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B (X okulu)", parentId: announcement));
        (await CreatedRecipientsAsync()).ShouldBe(new[] { Owner });
    }

    [Fact]
    public async Task Teacher_reply_under_an_out_of_scope_student_root_cannot_be_reported_or_listed()
    {
        var w = await SeedSchoolsAsync();
        await AssignOtherSchoolStudentToForeignTeacherAsync(w);
        var rootC = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "C sorusu")); // sorumlu: Foreign
        var answer = Created(await PostAsync(w.WorksheetId, Teacher(ForeignTeacher), "cevap", parentId: rootC));
        await AddReportRowAsync(answer, OtherSchoolStudentUser);

        // B (X okulu): öğretmen cevabı tek başına kapsamda olsa da kökü değil → 404.
        ShouldFail(await ReportAsync(w.WorksheetId, answer, Student(StudentBUser)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        // Sahip (X okulu, C kökünün sorumlusu değil): listede yok, gizleyemez.
        (await ReportsAsync(w.WorksheetId, Teacher(Owner))).Page!.TotalCount.ShouldBe(0);
        ShouldFail(await HideAsync(w.WorksheetId, answer, Teacher(Owner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        // Thread'in sorumlusu yönetir.
        (await ReportsAsync(w.WorksheetId, Teacher(ForeignTeacher))).Page!.Items.Select(i => i.Comment.Id).ShouldBe(new[] { answer });
    }

    [Fact]
    public async Task School_only_worksheet_is_readable_by_same_school_teachers_only_after_the_single_school_query()
    {
        var w = await SeedSchoolsAsync(WorksheetTeacherSharing.SchoolOnly);

        (await GetAsync(w.WorksheetId, Teacher(Unrelated))).Success.ShouldBeTrue();          // aynı okul
        ShouldFail(await GetAsync(w.WorksheetId, Teacher(ForeignTeacher)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
        (await GetAsync(w.WorksheetId, Teacher(ForeignTeacher, isAdmin: true))).Success.ShouldBeTrue();
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Hidden_comment_cannot_be_reported()
    {
        var w = await SeedSchoolsAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        (await HideAsync(w.WorksheetId, rootA, Teacher(Owner))).Success.ShouldBeTrue();

        ShouldFail(await ReportAsync(w.WorksheetId, rootA, Student(StudentBUser)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
    }

    [Fact]
    public async Task Reports_list_carries_question_order_resolved_in_one_batch()
    {
        var w = await SeedSchoolsAsync();
        await StartAsync(w, w.StudentA, answerQ1: true);
        var q1Root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "q1", questionId: w.Q1));
        var generalRoot = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "genel"));
        await ReportAsync(w.WorksheetId, q1Root, Student(StudentBUser));
        await ReportAsync(w.WorksheetId, generalRoot, Student(StudentBUser));

        var items = (await ReportsAsync(w.WorksheetId, Teacher(Owner))).Page!.Items;
        items.Single(i => i.Comment.Id == q1Root).Comment.QuestionOrder.ShouldBe(1);
        items.Single(i => i.Comment.Id == generalRoot).Comment.QuestionOrder.ShouldBeNull();

        await using var ctx = _db.NewContext();
        var numbers = await ExamApp.Api.Helpers.WorksheetQuestionNumbering.ResolveNumbersAsync(ctx,
            new[] { (w.WorksheetId, w.Q1), (w.WorksheetId, w.Q2), (w.WorksheetId, w.QForeign), (w.OtherWorksheetId, w.QForeign) });
        numbers[(w.WorksheetId, w.Q1)].ShouldBe(1);
        numbers[(w.WorksheetId, w.Q2)].ShouldBe(2);
        numbers.ContainsKey((w.WorksheetId, w.QForeign)).ShouldBeFalse();
        numbers[(w.OtherWorksheetId, w.QForeign)].ShouldBe(1);
    }
}
