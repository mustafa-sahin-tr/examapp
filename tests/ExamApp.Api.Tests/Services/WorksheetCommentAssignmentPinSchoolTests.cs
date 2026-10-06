using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.WorksheetComments;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #334 (#326 takip, PO seçenek a): ATAMA kaynaklı sabit de öğretmenin GÜNCEL okulu yazarın okuluyla aynıyken geçerli;
/// tek istisna öğretmen VE yazar okulsuz (bağımsız). Okul dışına çıkan atayan thread'i göremez, okuyamaz (doğrudan Id ile de),
/// cevap yazamaz, moderasyon yapamaz, bildirim almaz. Aynı okulda kalınca #105 kuralı (atama bitse de cevap) değişmez.
/// Seed: <c>WorksheetCommentServiceTests.cs</c> + <c>SeedSchoolsAsync</c> (Owner/Assigner/StudentA/StudentB okul X'te;
/// Assigner → StudentA öğrenci hedefli aktif atama).
/// </summary>
public partial class WorksheetCommentServiceTests
{
    private async Task SetStudentSchoolAsync(int userId, int? schoolId)
    {
        await using var ctx = _db.NewContext();
        await ctx.Students.Where(s => s.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SchoolId, schoolId).SetProperty(x => x.SchoolVerifiedAt, (schoolId) == null ? (DateTime?)null : DateTime.UtcNow));
    }

    /// <summary>Atayanın (Assigner) thread'e hiçbir yoldan erişemediğini doğrular (liste, Id ile okuma, cevap, gizle/aç, şikayet).</summary>
    private async Task AssignerHasNoAccessAsync(World w, int root, int reply)
    {
        var asAssigner = Teacher(Assigner);
        (await GetAsync(w.WorksheetId, asAssigner)).Page!.Items.ShouldNotContain(i => i.Id == root);
        (await GetModeratorAsync(w.WorksheetId, asAssigner)).Page!.Items.ShouldNotContain(i => i.Id == root);

        // IDOR: listede görünmeyen thread'e doğrudan Id ile her yol "yok" döner.
        ShouldFail(await GetRepliesAsync(w.WorksheetId, root, asAssigner), WorksheetCommentErrorCodes.RootCommentNotFound, notFound: true);
        ShouldFail(await PostAsync(w.WorksheetId, asAssigner, "cevap", parentId: root), WorksheetCommentErrorCodes.InvalidParent);
        ShouldFail(await HideAsync(w.WorksheetId, root, asAssigner), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await HideAsync(w.WorksheetId, root, asAssigner, hidden: false), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await HideAsync(w.WorksheetId, reply, asAssigner), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await ReportAsync(w.WorksheetId, root, asAssigner), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        (await ReportsAsync(w.WorksheetId, asAssigner)).Page!.Items.ShouldNotContain(i => i.Comment.Id == root);
    }

    // ---- KK1 + KK2: okullu atayan başka okula / bağımsıza geçer ---------------------------------------------------------

    [Theory]
    [InlineData(false)] // okul B'ye taşındı
    [InlineData(true)]  // bağımsıza geçti (okulsuz) — okullu yazarın yorumuna erişim kapanır
    public async Task Assigner_who_leaves_the_students_school_loses_the_assignment_pinned_thread_on_every_path(bool becameIndependent)
    {
        var w = await SeedSchoolsAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru")); // Assigner'a (Assignment) sabit
        (await SourceOfAsync(root)).ShouldBe(ResponsibleTeacherSource.Assignment);
        (await ReportAsync(w.WorksheetId, root, Student(StudentBUser))).Success.ShouldBeTrue();
        (await ReportsAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.ShouldContain(i => i.Comment.Id == root);

        await SetTeacherSchoolAsync(Assigner, becameIndependent ? null : w.OtherSchoolId);
        await ClearOutboxAsync();
        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "ek not", parentId: root));

        await AssignerHasNoAccessAsync(w, root, reply);
        (await CreatedRecipientsAsync()).ShouldNotContain(Assigner, "kapsam dışı sabit öğretmene bildirim üretilmez");

        // Okulun sahibi thread'i yönetmeye devam eder; admin görür.
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single(i => i.Id == root).CanModerate.ShouldBeTrue();
        (await GetAsync(w.WorksheetId, AdminReader)).Page!.Items.ShouldContain(i => i.Id == root);

        // Koşul okuma anında: okula dönünce sabit yine geçerli (veri silinmedi).
        await SetTeacherSchoolAsync(Assigner, w.SchoolId);
        (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single(i => i.Id == root).CanReply.ShouldBeTrue();
    }

    [Fact]
    public async Task Assigner_who_leaves_the_school_no_longer_sees_the_assignment_pinned_reply_on_an_announcement()
    {
        var w = await SeedSchoolsAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A duyuruya", parentId: announcement));
        (await StoredCommentAsync(reply)).ResponsibleTeacherUserId.ShouldBe(Assigner);
        (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single(i => i.Id == announcement)
            .Replies.Select(r => r.Id).ShouldContain(reply);

        await SetTeacherSchoolAsync(Assigner, w.OtherSchoolId);

        var thread = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single(i => i.Id == announcement);
        thread.Replies.ShouldNotContain(r => r.Id == reply);
        thread.ReplyCount.ShouldBe(0);
        (await GetRepliesAsync(w.WorksheetId, announcement, Teacher(Assigner))).Page!.Items.ShouldNotContain(r => r.Id == reply);
        ShouldFail(await HideAsync(w.WorksheetId, reply, Teacher(Assigner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
    }

    // ---- KK2 (ikinci yarı) + KK4: bağımsız istisnası --------------------------------------------------------------------

    [Fact]
    public async Task Independent_assigner_and_schoolless_student_keep_the_assignment_relationship_on_every_path()
    {
        var w = await SeedSchoolsAsync();
        await SetTeacherSchoolAsync(Assigner, null);
        await SetStudentSchoolAsync(StudentAUser, null);
        await ClearOutboxAsync();

        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        (await PinOfAsync(root)).ShouldBe(Assigner);
        (await StoredCommentAsync(root)).AuthorSchoolId.ShouldBeNull();
        (await CreatedRecipientsAsync()).ShouldBe(new[] { Assigner });

        await ClearOutboxAsync();
        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "ek not", parentId: root));
        (await CreatedRecipientsAsync()).ShouldBe(new[] { Assigner }); // #329 yeniden doğrulaması geçer

        var thread = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single(i => i.Id == root);
        thread.CanReply.ShouldBeTrue();
        thread.CanModerate.ShouldBeTrue();
        thread.Replies.Select(r => r.Id).ShouldBe(new[] { reply });
        (await GetRepliesAsync(w.WorksheetId, root, Teacher(Assigner))).Page!.CanReply.ShouldBeTrue();
        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: root));
        (await ReportAsync(w.WorksheetId, root, Teacher(Unrelated))).Success.ShouldBeFalse("okul içi olmayan öğretmen göremez");
        (await HideAsync(w.WorksheetId, reply, Teacher(Assigner))).Success.ShouldBeTrue();
        (await HideAsync(w.WorksheetId, reply, Teacher(Assigner), hidden: false)).Success.ShouldBeTrue();
        await AddReportRowAsync(root, OtherSchoolStudentUser);
        (await ReportsAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Select(i => i.Comment.Id).ShouldBe(new[] { root });
    }

    // ---- KK5: okullu öğretmen + okulsuz öğrenci (null == null de, tek taraflı null da eşleşmez) ----------------------------

    /// <summary>Atama kaynaklı sabit, doğrudan yazılır (ör. eski kayıt) — okul koşulu okuma anında uygulanır.</summary>
    private async Task<int> InsertAssignmentPinnedRootAsync(World w, int? authorSchoolId)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(StudentAUser);
        var c = new WorksheetComment
        {
            WorksheetId = w.WorksheetId, AuthorUserId = StudentAUser, AuthorKeycloakId = $"kc-{StudentAUser}",
            AuthorRole = WorksheetCommentAuthorRole.Student, Body = "eski soru", ResponsibleTeacherUserId = Assigner,
            ResponsibleTeacherSource = ResponsibleTeacherSource.Assignment, AuthorSchoolId = authorSchoolId
        };
        ctx.WorksheetComments.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    [Theory]
    [InlineData(true, false)]  // okullu öğretmen, okulsuz yazar (tek taraflı null)
    [InlineData(false, true)]  // bağımsız öğretmen, okullu yazar (tek taraflı null)
    public async Task One_sided_schoolless_assignment_pin_grants_nothing(bool teacherHasSchool, bool authorHasSchool)
    {
        var w = await SeedSchoolsAsync();
        await SetTeacherSchoolAsync(Assigner, teacherHasSchool ? w.SchoolId : null);
        var root = await InsertAssignmentPinnedRootAsync(w, authorHasSchool ? w.SchoolId : null);

        (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.ShouldNotContain(i => i.Id == root);
        ShouldFail(await GetRepliesAsync(w.WorksheetId, root, Teacher(Assigner)), WorksheetCommentErrorCodes.RootCommentNotFound, notFound: true);
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: root), WorksheetCommentErrorCodes.InvalidParent);
        ShouldFail(await HideAsync(w.WorksheetId, root, Teacher(Assigner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
    }

    [Fact]
    public async Task School_assigner_gets_no_pin_and_no_notification_for_a_schoolless_assigned_student()
    {
        var w = await SeedSchoolsAsync();
        await SetStudentSchoolAsync(StudentAUser, null); // atama öğrenci hedefli → aktif kalır
        await ClearOutboxAsync();

        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        (await PinOfAsync(root)).ShouldBeNull("okullu atayan + okulsuz öğrenci; sahip de okullu → sorumlu yok");
        (await OutboxAsync()).ShouldBeEmpty();
        (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.ShouldNotContain(i => i.Id == root);
        (await GetAsync(w.WorksheetId, Student(StudentAUser))).Page!.CommentVisibility.ShouldBe(WorksheetCommentVisibilities.Self);
    }

    // ---- KK3: öğrenci başka okula geçer ----------------------------------------------------------------------------------

    [Fact]
    public async Task Student_who_moves_school_is_not_pinned_to_the_old_schools_assigner_but_old_threads_stay_with_them()
    {
        var w = await SeedSchoolsAsync();
        var oldRoot = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "X okulunda"));
        await SetStudentSchoolAsync(StudentAUser, w.OtherSchoolId); // öğrenci hedefli atama aktif kalır
        await ClearOutboxAsync();

        var newRoot = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "yeni okulda"));

        (await PinOfAsync(newRoot)).ShouldBeNull("ne atayan ne sahip öğrencinin yeni okulunda");
        (await StoredCommentAsync(newRoot)).AuthorSchoolId.ShouldBe(w.OtherSchoolId);
        (await OutboxAsync()).ShouldBeEmpty("T bildirim almaz");
        var asAssigner = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!;
        asAssigner.Items.ShouldNotContain(i => i.Id == newRoot);
        ShouldFail(await GetRepliesAsync(w.WorksheetId, newRoot, Teacher(Assigner)), WorksheetCommentErrorCodes.RootCommentNotFound, notFound: true);

        // T ile aynı okuldaki (yazıldığı okul X) eski yorum T'de kalır (okur, yönetir) — ama kök yazarı artık okul dışında:
        // cevap yazamaz (#334 review, okullar arası kanal yok).
        var old = asAssigner.Items.Single(i => i.Id == oldRoot);
        old.CanModerate.ShouldBeTrue();
        old.CanReply.ShouldBeFalse();
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Assigner), "eski thread'e cevap", parentId: oldRoot),
            WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);
    }

    // ---- #334 review (security Orta): taşınan öğrencinin eski köke yeni okulundan yazdığı cevap --------------------------

    [Fact]
    public async Task Moved_students_new_reply_on_an_old_root_is_not_exposed_to_the_old_schools_teacher()
    {
        var w = await SeedSchoolsAsync();
        var oldRoot = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "X okulunda"));
        await SetStudentSchoolAsync(StudentAUser, w.OtherSchoolId);
        await ClearOutboxAsync();

        var awayReply = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "Y okulundan ek", parentId: oldRoot));
        (await StoredCommentAsync(awayReply)).AuthorSchoolId.ShouldBe(w.OtherSchoolId);
        await AddReportRowAsync(awayReply, StudentBUser);
        (await OutboxAsync()).ShouldBeEmpty("kapsam dışı cevap için T'ye bildirim yok");

        // Kök görünür kalır; cevap listelenmez, Id ile gizlenemez/şikayet edilemez, rapor listesinde yok.
        var thread = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single(i => i.Id == oldRoot);
        thread.Replies.ShouldNotContain(r => r.Id == awayReply);
        thread.ReplyCount.ShouldBe(0);
        (await GetRepliesAsync(w.WorksheetId, oldRoot, Teacher(Assigner))).Page!.Items.ShouldNotContain(r => r.Id == awayReply);
        ShouldFail(await HideAsync(w.WorksheetId, awayReply, Teacher(Assigner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await ReportAsync(w.WorksheetId, awayReply, Teacher(Assigner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        (await ReportsAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.ShouldNotContain(i => i.Comment.Id == awayReply);

        // Cevap yazamaz → StudentReply bildirimi de üretilmez.
        thread.CanReply.ShouldBeFalse();
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: oldRoot),
            WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);
        (await GetRepliesAsync(w.WorksheetId, oldRoot, Teacher(Assigner))).Page!.CanReply.ShouldBeFalse();
        (await OutboxAsync()).ShouldBeEmpty();

        // Öğrenci X'e dönünce cevap yetkisi açılır; yokken Y okulundan yazılan cevap yazıldığı okulda kalır.
        await SetStudentSchoolAsync(StudentAUser, w.SchoolId);
        var back = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single(i => i.Id == oldRoot);
        back.CanReply.ShouldBeTrue();
        back.Replies.ShouldNotContain(r => r.Id == awayReply);
        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: oldRoot));
        (await OutboxAsync()).Select(r => r.Type).ShouldBe(new[] { RepliedType });
    }

    // ---- security Düşük-1: belirsiz okul (çoklu canlı satır) bağımsız istisnasına girmez -----------------------------------

    private async Task GiveAssignerASecondLiveSchoollessRowAsync()
    {
        await using var ctx = _db.NewContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Teachers_UserId\""); // #259 index'siz ortam
        await ctx.Teachers.Where(t => t.UserId == Assigner).ExecuteUpdateAsync(s => s.SetProperty(t => t.SchoolId, (int?)null));
        ctx.Teachers.Add(new Teacher { UserId = Assigner, SchoolId = null, AccountApprovedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Ambiguous_teacher_school_does_not_qualify_for_the_independent_exception()
    {
        var w = await SeedSchoolsAsync();
        await SetStudentSchoolAsync(StudentAUser, null);
        var legacy = await InsertAssignmentPinnedRootAsync(w, authorSchoolId: null);
        await GiveAssignerASecondLiveSchoollessRowAsync();
        await ClearOutboxAsync();

        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        (await PinOfAsync(root)).ShouldBeNull("belirsiz okul 'okulsuz' sayılmaz → atama sabiti yok");
        (await OutboxAsync()).ShouldBeEmpty();
        var page = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!;
        page.Items.ShouldNotContain(i => i.Id == root);
        page.Items.ShouldNotContain(i => i.Id == legacy, "mevcut okulsuz+okulsuz sabit belirsiz öğretmene açılmaz");
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: legacy), WorksheetCommentErrorCodes.InvalidParent);
    }

    [Fact]
    public void Ambiguous_school_never_matches_even_another_ambiguous_school()
    {
        var ambiguous = WorksheetCommentPinRule.ForRule(new ExamApp.Api.Helpers.UserSchool(null, true));
        ambiguous.ShouldBe(WorksheetCommentPinRule.AmbiguousSchoolId);
        WorksheetCommentPinRule.ForRule(new ExamApp.Api.Helpers.UserSchool(null, false)).ShouldBeNull();
        WorksheetCommentPinRule.ForRule(new ExamApp.Api.Helpers.UserSchool(5, false)).ShouldBe(5);
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Assignment, ambiguous, null).ShouldBeFalse();
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Assignment, ambiguous, ambiguous).ShouldBeFalse();
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Owner, 5, ambiguous).ShouldBeFalse();
        ResponsibleTeacherRule.Decide(new ResponsibleTeacherWorksheet(1, Owner, null), new RelevantAssignment(1, Assigner, null),
            ambiguous, ambiguous, ambiguous).ShouldBeNull();
    }

    // ---- CR-5: EF parametreleştirmesi -------------------------------------------------------------------------------------

    /// <summary>
    /// Öğretmen id'si/okulu SQL'e sabit (literal) olarak değil parametre olarak girmeli (sorgu planı/önbellek kullanıcı başına
    /// çoğalmasın). EF Core 10.0 (SQLite sağlayıcısı) ile doğrulandı: sabit nesnenin üye erişimi parametreye çıkarılır.
    /// SQLite'ın ToQueryString'i parametre değerlerini başta ".param set" satırları olarak yazar; SQL gövdesi ayrıca denetlenir.
    /// </summary>
    [Fact]
    public async Task Pin_expression_is_parameterized()
    {
        await using var ctx = _db.NewContext();
        const int teacherId = 987_654;
        const int school = 43_210;

        foreach (var query in new[]
                 {
                     ctx.WorksheetComments.Where(WorksheetCommentPinRule.HoldsOn(c => c, teacherId, school)).ToQueryString(),
                     ctx.WorksheetComments.Where(WorksheetCommentPinRule.HoldsForReply(teacherId, school)).ToQueryString()
                 })
        {
            var body = string.Join("\n", query.Split('\n').Where(l => !l.TrimStart().StartsWith(".param", StringComparison.Ordinal)));
            body.ShouldContain("@");
            body.ShouldNotContain(teacherId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            body.ShouldNotContain(school.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public async Task Same_school_owner_takes_over_new_comments_when_the_assigner_left_the_school()
    {
        var w = await SeedSchoolsAsync();
        await SetTeacherSchoolAsync(Assigner, w.OtherSchoolId);
        await ClearOutboxAsync();

        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        (await PinOfAsync(root)).ShouldBe(Owner);
        (await SourceOfAsync(root)).ShouldBe(ResponsibleTeacherSource.Owner);
        (await CreatedRecipientsAsync()).ShouldBe(new[] { Owner });
        (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.ShouldNotContain(i => i.Id == root);
    }

    // ---- #329 yeniden doğrulaması: çözümleyici öğretmeni verse de eski okulun sabiti geçersiz --------------------------------

    [Fact]
    public async Task Teacher_and_student_moving_together_keep_the_new_relationship_but_not_the_old_schools_thread()
    {
        var w = await SeedSchoolsAsync();
        var oldRoot = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "X okulunda"));
        await SetTeacherSchoolAsync(Assigner, w.OtherSchoolId);
        await SetStudentSchoolAsync(StudentAUser, w.OtherSchoolId);
        await ClearOutboxAsync();

        // Güncel çözümleyici yine Assigner'ı verir (ikisi de Y okulunda), ama eski kök X okulunda yazıldı → sabit geçersiz:
        // cevapta bildirim yok, thread görünmez.
        var oldReply = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "eski thread'e ek", parentId: oldRoot));
        (await CreatedRecipientsAsync()).ShouldBeEmpty();
        await AssignerHasNoAccessAsync(w, oldRoot, oldReply);

        // Yeni okulda yazılan yorum aynı ilişkiyle Assigner'a sabitlenir.
        var newRoot = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "Y okulunda"));
        (await PinOfAsync(newRoot)).ShouldBe(Assigner);
        (await CreatedRecipientsAsync()).ShouldBe(new[] { Assigner });
        (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single(i => i.Id == newRoot).CanReply.ShouldBeTrue();
    }

    // ---- #105 regresyonu: aynı okulda kalınca atama bitse de sabit öğretmen yetkili ------------------------------------------

    [Fact]
    public async Task Same_school_pinned_assigner_keeps_reading_replying_and_moderating_after_the_assignment_ended()
    {
        var w = await SeedSchoolsAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B katkı", parentId: root));
        await AddReportRowAsync(root, StudentBUser);
        await EndAssignmentAsync(w.AssignmentId);

        var thread = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single(i => i.Id == root);
        thread.CanReply.ShouldBeTrue();
        thread.CanModerate.ShouldBeTrue();
        thread.Replies.Select(r => r.Id).ShouldBe(new[] { reply });
        (await GetRepliesAsync(w.WorksheetId, root, Teacher(Assigner))).Page!.Items.Select(r => r.Id).ShouldBe(new[] { reply });
        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: root));
        (await HideAsync(w.WorksheetId, reply, Teacher(Assigner))).Success.ShouldBeTrue();
        (await ReportsAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Select(i => i.Comment.Id).ShouldBe(new[] { root });
    }

    // ---- Tek kural: SQL ifadesi ile bellek içi karar her kombinasyonda aynı -------------------------------------------------

    [Fact]
    public async Task Sql_pin_expression_matches_the_in_memory_rule_for_every_combination()
    {
        var w = await SeedSchoolsAsync();
        int?[] schools = { null, w.SchoolId, w.OtherSchoolId };
        ResponsibleTeacherSource?[] sources =
            { null, ResponsibleTeacherSource.Assignment, ResponsibleTeacherSource.Owner, ResponsibleTeacherSource.CopyOwner };
        int[] pinnedTeachers = { Assigner, Owner };

        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentAUser);
            foreach (var source in sources)
            foreach (var authorSchool in schools)
            foreach (var pinned in pinnedTeachers)
            {
                var root = new WorksheetComment
                {
                    WorksheetId = w.WorksheetId, AuthorUserId = StudentAUser, AuthorKeycloakId = "kc-101",
                    AuthorRole = WorksheetCommentAuthorRole.Student, Body = "k", ResponsibleTeacherUserId = pinned,
                    ResponsibleTeacherSource = source, AuthorSchoolId = authorSchool
                };
                ctx.WorksheetComments.Add(root);
                ctx.WorksheetComments.Add(new WorksheetComment
                {
                    WorksheetId = w.WorksheetId, AuthorUserId = StudentBUser, AuthorKeycloakId = "kc-102",
                    AuthorRole = WorksheetCommentAuthorRole.Student, Body = "r", ParentComment = root
                });
            }
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var all = await read.WorksheetComments.AsNoTracking().ToListAsync();
        var byId = all.ToDictionary(c => c.Id);
        var checkedCases = 0;
        foreach (var teacherSchool in schools)
        {
            var sqlRoots = await read.WorksheetComments
                .Where(WorksheetCommentPinRule.HoldsOn(c => c, Assigner, teacherSchool)).Select(c => c.Id).ToListAsync();
            var sqlReplies = await read.WorksheetComments
                .Where(c => c.ParentComment != null)
                .Where(WorksheetCommentPinRule.HoldsOn(c => c.ParentComment, Assigner, teacherSchool)).Select(c => c.Id).ToListAsync();

            all.Where(c => WorksheetCommentPinRule.Holds(c, Assigner, teacherSchool)).Select(c => c.Id)
                .OrderBy(x => x).ShouldBe(sqlRoots.OrderBy(x => x), $"teacherSchool={teacherSchool}");
            all.Where(c => c.ParentCommentId is { } p && WorksheetCommentPinRule.Holds(byId[p], Assigner, teacherSchool))
                .Select(c => c.Id).OrderBy(x => x).ShouldBe(sqlReplies.OrderBy(x => x), $"reply, teacherSchool={teacherSchool}");
            checkedCases++;
        }
        checkedCases.ShouldBe(3);

        // Kuralın kendisi (beklenen tablo): yalnız aynı okul ya da okulsuz+okulsuz+Assignment.
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Assignment, 1, 1).ShouldBeTrue();
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Assignment, null, null).ShouldBeTrue();
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Assignment, 1, null).ShouldBeFalse();
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Assignment, null, 1).ShouldBeFalse();
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Assignment, 1, 2).ShouldBeFalse();
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.Owner, null, null).ShouldBeFalse();
        WorksheetCommentPinRule.SchoolAllows(ResponsibleTeacherSource.CopyOwner, 1, 1).ShouldBeTrue();
        WorksheetCommentPinRule.SchoolAllows(null, null, null).ShouldBeFalse();
    }
}
