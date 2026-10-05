using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.WorksheetComments;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #326 (O2, PO kararı c): atama yoksa sahip YALNIZ öğrenciyle aynı okuldaysa sorumlu öğretmen. Okul dışı sahip,
/// atamasız öğrencinin kökünü görmez, bildirim almaz, cevap yazamaz, moderasyon yapamaz; duyuru köküne yazılan okul dışı
/// atamasız cevap da sahibe görünmez. Thread yanıtında öğrenci için <c>CommentVisibility</c>. Seed:
/// <c>WorksheetCommentServiceTests.cs</c> (sahip Okul X; StudentA Assigner atamalı, StudentB atamasız aynı okul) +
/// <c>SeedSchoolsAsync</c> (104 başka okul, 105 okulsuz — ikisi de atamasız).
/// </summary>
public partial class WorksheetCommentServiceTests
{
    private async Task<int?> PinOfAsync(int commentId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.WorksheetComments.Where(c => c.Id == commentId).Select(c => c.ResponsibleTeacherUserId).SingleAsync();
    }

    [Fact]
    public async Task Out_of_school_unassigned_students_root_has_no_responsible_teacher_and_stays_inside_their_school()
    {
        var w = await SeedSchoolsAsync();
        await ClearOutboxAsync();

        var root = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "okul dışından soru"));

        (await PinOfAsync(root)).ShouldBeNull("sahip öğrencinin okulunda değil → sabitleme yok");
        (await OutboxAsync()).ShouldBeEmpty("sahibe (ve kimseye) öğretmen bildirimi gitmez");

        // Sahip görmez; cevap yazamaz (kök "yok"); moderasyon yapamaz (404).
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.ShouldNotContain(i => i.Id == root);
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Owner), "cevap", parentId: root), WorksheetCommentErrorCodes.InvalidParent);
        ShouldFail(await HideAsync(w.WorksheetId, root, Teacher(Owner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        (await GetRepliesAsync(w.WorksheetId, root, Teacher(Owner))).NotFound.ShouldBeTrue();

        // Yorum okul içi görünür: öğrencinin okulundaki öğretmen görür (ama sorumlu olmadığı için cevaplayamaz); admin görür.
        var foreignView = (await GetAsync(w.WorksheetId, Teacher(ForeignTeacher))).Page!;
        var thread = foreignView.Items.Single(i => i.Id == root);
        thread.CanReply.ShouldBeFalse();
        (await GetAsync(w.WorksheetId, AdminReader)).Page!.Items.ShouldContain(i => i.Id == root);
    }

    [Fact]
    public async Task Same_school_unassigned_student_still_falls_back_to_the_owner()
    {
        var w = await SeedSchoolsAsync();
        await ClearOutboxAsync();

        var root = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "aynı okuldan soru"));

        (await PinOfAsync(root)).ShouldBe(Owner);
        (await CreatedRecipientsAsync()).ShouldBe(new[] { Owner });
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single(i => i.Id == root).CanReply.ShouldBeTrue();
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "cevap", parentId: root));
    }

    [Fact]
    public async Task Schoolless_unassigned_student_gets_no_owner_fallback()
    {
        var w = await SeedSchoolsAsync();
        await SetTeacherSchoolAsync(Owner, null); // bağımsız sahip + okulsuz öğrenci: null == null aynı okul değil
        await ClearOutboxAsync();

        var root = Created(await PostAsync(w.WorksheetId, Student(SchoollessStudentUser), "soru"));

        (await PinOfAsync(root)).ShouldBeNull();
        (await OutboxAsync()).ShouldBeEmpty();
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.ShouldNotContain(i => i.Id == root);
    }

    [Fact]
    public async Task Out_of_school_unassigned_reply_on_the_owners_announcement_is_hidden_from_the_owner_and_not_notified()
    {
        var w = await SeedSchoolsAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        await ClearOutboxAsync();

        var reply = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "okul dışı cevap", parentId: announcement));

        (await PinOfAsync(reply)).ShouldBeNull();
        (await OutboxAsync()).ShouldBeEmpty("ne kök yazarı (okul dışı) ne sorumlu öğretmen (yok) bildirim alır");
        var asOwner = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single(i => i.Id == announcement);
        asOwner.Replies.ShouldNotContain(r => r.Id == reply);
        asOwner.ReplyCount.ShouldBe(0);
        ShouldFail(await HideAsync(w.WorksheetId, reply, Teacher(Owner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);

        // Cevaplayan öğrenci kendi cevabını ve duyuruyu görmeye devam eder.
        var asStudent = (await GetAsync(w.WorksheetId, Student(OtherSchoolStudentUser))).Page!.Items.Single(i => i.Id == announcement);
        asStudent.Replies.Select(r => r.Id).ShouldBe(new[] { reply });
    }

    [Fact]
    public async Task Owner_who_leaves_the_students_school_is_no_longer_notified_on_replies_to_an_old_pinned_root()
    {
        var w = await SeedSchoolsAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "soru")); // aynı okul → Owner'a sabit
        await SetTeacherSchoolAsync(Owner, w.OtherSchoolId);
        await ClearOutboxAsync();

        // D1 yeniden doğrulaması güncel kuralla: sahip artık öğrencinin okulunda değil → sorumlu değil → bildirim yok.
        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "ek not", parentId: root));

        (await OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Owner_moderator_reports_list_excludes_out_of_school_unassigned_threads()
    {
        var w = await SeedSchoolsAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "okul dışı"));
        (await ReportAsync(w.WorksheetId, root, Teacher(ForeignTeacher))).Success.ShouldBeTrue();

        var ownerList = (await ReportsAsync(w.WorksheetId, Teacher(Owner))).Page!;
        ownerList.Items.ShouldNotContain(i => i.Comment.Id == root);
        (await ReportsAsync(w.WorksheetId, AdminReader)).Page!.Items.ShouldContain(i => i.Comment.Id == root);
    }

    // ---- Thread yanıtı: CommentVisibility --------------------------------------------------------------------------

    [Fact]
    public async Task Thread_page_tells_the_student_who_will_see_a_new_comment()
    {
        var w = await SeedSchoolsAsync();

        (await GetAsync(w.WorksheetId, Student(StudentAUser))).Page!.CommentVisibility
            .ShouldBe(WorksheetCommentVisibilities.Teacher, "atamalı, okullu");
        (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.CommentVisibility
            .ShouldBe(WorksheetCommentVisibilities.Teacher, "atamasız ama sahip aynı okulda");
        (await GetAsync(w.WorksheetId, Student(OtherSchoolStudentUser))).Page!.CommentVisibility
            .ShouldBe(WorksheetCommentVisibilities.School, "atamasız, sahip okul dışı → yalnız okul içi");
        (await GetAsync(w.WorksheetId, Student(SchoollessStudentUser))).Page!.CommentVisibility
            .ShouldBe(WorksheetCommentVisibilities.Self, "okulsuz, sorumlu yok → yalnız kendisi");

        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.CommentVisibility.ShouldBeNull();
        (await GetAsync(w.WorksheetId, AdminReader)).Page!.CommentVisibility.ShouldBeNull();
    }

    /// <summary>issue #334: okulsuz öğrenciye atama — yalnız atayan da okulsuzsa (bağımsız istisnası) sorumlu öğretmen olur.</summary>
    [Theory]
    [InlineData(true)]  // bağımsız atayan → SelfAndTeacher, sabit atayanda
    [InlineData(false)] // okullu atayan → atama olsa bile sorumlu yok (Self), sabit yok
    public async Task Schoolless_student_with_an_assignment_has_a_teacher_only_if_the_assigner_is_independent(bool independentAssigner)
    {
        var w = await SeedSchoolsAsync();
        if (independentAssigner)
            await SetTeacherSchoolAsync(Assigner, null);
        await using (var ctx = _db.NewContext())
        {
            var studentId = await ctx.Students.Where(s => s.UserId == SchoollessStudentUser).Select(s => s.Id).SingleAsync();
            ctx.SetCurrentUser(Assigner);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = w.WorksheetId, StudentId = studentId, StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await ctx.SaveChangesAsync();
        }

        (await GetAsync(w.WorksheetId, Student(SchoollessStudentUser))).Page!.CommentVisibility
            .ShouldBe(independentAssigner ? WorksheetCommentVisibilities.SelfAndTeacher : WorksheetCommentVisibilities.Self);

        var root = Created(await PostAsync(w.WorksheetId, Student(SchoollessStudentUser), "soru"));
        if (independentAssigner)
        {
            (await PinOfAsync(root)).ShouldBe(Assigner, "okulsuz öğretmen + okulsuz öğrenci: atama ilişkisi korunur");
            (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.ShouldContain(i => i.Id == root);
        }
        else
        {
            (await PinOfAsync(root)).ShouldBeNull("okullu atayan + okulsuz öğrenci: eşleşme yok");
            (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.ShouldNotContain(i => i.Id == root);
        }
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.ShouldNotContain(i => i.Id == root);
    }

    [Fact]
    public void Visibility_value_is_a_function_of_school_and_responsible_teacher()
    {
        WorksheetCommentVisibilities.For(1, true).ShouldBe("teacher");
        WorksheetCommentVisibilities.For(1, false).ShouldBe("school");
        WorksheetCommentVisibilities.For(null, true).ShouldBe("self-and-teacher");
        WorksheetCommentVisibilities.For(null, false).ShouldBe("self");
    }

    // ---- D3: okuyucu okulu tek kaynaktan ---------------------------------------------------------------------------

    [Fact]
    public async Task Student_reader_school_comes_from_the_live_row_not_an_old_deleted_one()
    {
        var w = await SeedSchoolsAsync();
        var fromC = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "C okulunda"));
        var fromB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "X okulunda"));

        // StudentB'nin satırı silinip C'nin okulunda yeni canlı satır açılır (transfer): artık C'nin okulundan okur.
        await using (var ctx = _db.NewContext())
        {
            await ctx.Students.Where(s => s.UserId == StudentBUser).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            ctx.Students.Add(new Student { UserId = StudentBUser, StudentNumber = "b2", GradeId = w.GradeId, SchoolId = w.OtherSchoolId });
            await ctx.SaveChangesAsync();
        }

        var ids = (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Select(i => i.Id).ToList();
        ids.ShouldContain(fromC);
        ids.ShouldContain(fromB, "kendi yorumu");
    }

    // ---- issue #326 (security O1): sabitleme kaynağı + sahip kaynaklıda güncel okul koşulu ---------------------------

    private async Task<ResponsibleTeacherSource?> SourceOfAsync(int commentId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.WorksheetComments.Where(c => c.Id == commentId).Select(c => c.ResponsibleTeacherSource).SingleAsync();
    }

    [Fact]
    public async Task Pin_source_is_stored_with_the_pin()
    {
        var w = await SeedSchoolsAsync();

        var assigned = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "atamalı"));
        var owned = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "atamasız aynı okul"));
        var none = Created(await PostAsync(w.WorksheetId, Student(OtherSchoolStudentUser), "okul dışı"));
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        var replyOnAnnouncement = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "cevap", parentId: announcement));
        var replyOnStudentRoot = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "katkı", parentId: assigned));

        (await SourceOfAsync(assigned)).ShouldBe(ResponsibleTeacherSource.Assignment);
        (await SourceOfAsync(owned)).ShouldBe(ResponsibleTeacherSource.Owner);
        (await SourceOfAsync(none)).ShouldBeNull();
        (await SourceOfAsync(announcement)).ShouldBeNull();
        (await SourceOfAsync(replyOnAnnouncement)).ShouldBe(ResponsibleTeacherSource.Owner);
        (await SourceOfAsync(replyOnStudentRoot)).ShouldBeNull("öğrenci kökündeki reply'a sabit yazılmaz");
    }

    [Theory]
    [InlineData(true)]  // sahip bağımsıza geçti (okulsuz)
    [InlineData(false)] // sahip başka okula taşındı
    public async Task Owner_who_leaves_the_school_loses_the_owner_pinned_thread(bool becameIndependent)
    {
        var w = await SeedSchoolsAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "soru")); // Owner'a (Owner kaynaklı) sabit
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.ShouldContain(i => i.Id == root);

        await SetTeacherSchoolAsync(Owner, becameIndependent ? null : w.OtherSchoolId);
        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "ek not", parentId: root));

        // Görmez (kök ve yeni reply), cevap yazamaz, moderasyon yapamaz, bildirim almaz.
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.ShouldNotContain(i => i.Id == root);
        (await GetRepliesAsync(w.WorksheetId, root, Teacher(Owner))).NotFound.ShouldBeTrue();
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Owner), "cevap", parentId: root), WorksheetCommentErrorCodes.InvalidParent);
        ShouldFail(await HideAsync(w.WorksheetId, root, Teacher(Owner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        ShouldFail(await HideAsync(w.WorksheetId, reply, Teacher(Owner)), WorksheetCommentErrorCodes.CommentNotFound, notFound: true);
        (await CreatedRecipientsAsync()).Count(r => r == Owner).ShouldBe(1, "yalnız kök yazıldığında (sahip o an aynı okuldaydı)");

        // Okula dönünce sabit yine geçerli (veri silinmedi, koşul okuma anında).
        await SetTeacherSchoolAsync(Owner, w.SchoolId);
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single(i => i.Id == root).CanReply.ShouldBeTrue();
    }

    [Fact]
    public async Task Owner_who_leaves_the_school_no_longer_sees_owner_pinned_replies_on_the_announcement()
    {
        var w = await SeedSchoolsAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "cevap", parentId: announcement));

        await SetTeacherSchoolAsync(Owner, w.OtherSchoolId);

        var thread = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single(i => i.Id == announcement);
        thread.Replies.ShouldNotContain(r => r.Id == reply);
    }

    [Fact]
    public async Task Owner_who_leaves_the_school_drops_the_thread_from_the_moderation_list()
    {
        var w = await SeedSchoolsAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "soru"));
        (await ReportAsync(w.WorksheetId, root, Student(StudentAUser))).Success.ShouldBeTrue();
        (await ReportsAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.ShouldContain(i => i.Comment.Id == root);

        await SetTeacherSchoolAsync(Owner, w.OtherSchoolId);

        (await ReportsAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.ShouldNotContain(i => i.Comment.Id == root);
    }

    [Fact]
    public async Task Legacy_pin_without_a_source_is_treated_like_an_owner_pin()
    {
        var w = await SeedSchoolsAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "soru"));
        await using (var ctx = _db.NewContext())
            await ctx.WorksheetComments.Where(c => c.Id == root)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ResponsibleTeacherSource, (ResponsibleTeacherSource?)null));

        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single(i => i.Id == root).CanReply.ShouldBeTrue();
        await SetTeacherSchoolAsync(Owner, w.OtherSchoolId);
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.ShouldNotContain(i => i.Id == root);
    }
}
