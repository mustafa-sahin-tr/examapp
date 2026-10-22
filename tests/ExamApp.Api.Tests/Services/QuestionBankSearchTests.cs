using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #471: <see cref="QuestionQueryService.SearchBankAsync"/> — kapsam (mine/all), öğretmen/admin görünürlüğü
/// (okul, özel/açık paylaşım, silinmiş üyelik, legacy sahiplik), filtreler, sayfalama, TestCount ve IsOwn. SQLite,
/// sabit tohum verisi; beklentiler küme olarak karşılaştırılır (sıra bağımsız), sıra yalnız "id desc" testinde denetlenir.
/// </summary>
public class QuestionBankSearchTests : IDisposable
{
    private const int TeacherA = 1;        // okul S1; kendi özel/açık/kopya testleri
    private const int TeacherB = 2;        // okul S1 (A ile aynı okul); SchoolOnly test
    private const int TeacherC = 3;        // okul S2
    private const int TeacherNoSchool = 4; // öğretmen kaydı yok → okulsuz
    private const int Admin = 9;

    private readonly TestDb _db = TestDb.Create();

    private readonly int _g5, _g6, _mat, _t5, _t6, _st1, _st2;
    private readonly int _qA1, _qA2, _qLegacyA, _qAPub, _qB1, _qC1, _qC2, _qC3, _qLegacyPublic, _qCopyLegacy, _qOrphan;
    private readonly int _qDeleted;
    private readonly int[] _live;

    public QuestionBankSearchTests()
    {
        using var ctx = _db.NewContext();

        var s1 = new School { Name = "Okul 1" };
        var s2 = new School { Name = "Okul 2" };
        var g5 = new Grade { Name = "5" };
        var g6 = new Grade { Name = "6" };
        var mat = new Subject { Name = "Matematik" };
        ctx.AddRange(s1, s2, g5, g6, mat);
        ctx.SaveChanges();

        var t5 = new Topic { Name = "Kesirler", SubjectId = mat.Id, GradeId = g5.Id };
        var t6 = new Topic { Name = "Oranlar", SubjectId = mat.Id, GradeId = g6.Id };
        ctx.AddRange(t5, t6);
        ctx.SaveChanges();

        var st1 = new SubTopic { Name = "ST1", TopicId = t5.Id };
        var st2 = new SubTopic { Name = "ST2", TopicId = t5.Id };
        ctx.AddRange(st1, st2);
        ctx.Teachers.AddRange(
            new Teacher { UserId = TeacherA, SchoolId = s1.Id, AccountApprovedAt = DateTime.UtcNow },
            new Teacher { UserId = TeacherB, SchoolId = s1.Id, AccountApprovedAt = DateTime.UtcNow },
            new Teacher { UserId = TeacherC, SchoolId = s2.Id, AccountApprovedAt = DateTime.UtcNow });
        ctx.SaveChanges();

        // Testler. Sahip = SetCurrentUser; 0 → eski (legacy) davranış.
        var wsAPrivate = Add(ctx, TeacherA, new Worksheet { Name = "A özel", Description = "", GradeId = g5.Id });
        var wsAPublic = Add(ctx, TeacherA, new Worksheet { Name = "A açık", Description = "", GradeId = g5.Id, TeacherSharing = WorksheetTeacherSharing.PublicView });
        var wsBSchool = Add(ctx, TeacherB, new Worksheet { Name = "B okul", Description = "", GradeId = g5.Id, TeacherSharing = WorksheetTeacherSharing.SchoolOnly });
        var wsCPublic = Add(ctx, TeacherC, new Worksheet { Name = "C açık", Description = "", GradeId = g5.Id, TeacherSharing = WorksheetTeacherSharing.PublicView });
        var wsCSchool = Add(ctx, TeacherC, new Worksheet { Name = "C okul", Description = "", GradeId = g5.Id, TeacherSharing = WorksheetTeacherSharing.SchoolOnly });
        var wsCPrivate = Add(ctx, TeacherC, new Worksheet { Name = "C özel", Description = "", GradeId = g5.Id });
        var wsACopy = Add(ctx, TeacherA, new Worksheet { Name = "A kopya", Description = "", GradeId = g5.Id, SourceWorksheetId = wsCPublic.Id });
        var wsOwnerless = Add(ctx, 0, new Worksheet { Name = "Sahipsiz", Description = "", GradeId = g5.Id, TeacherSharing = WorksheetTeacherSharing.PublicView });

        // Sorular.
        var qA1 = Add(ctx, TeacherA, new Question { Text = "a1", SubjectId = mat.Id, TopicId = t5.Id, DifficultyLevel = 3, ClassificationSource = ClassificationSource.AI });
        var qA2 = Add(ctx, TeacherA, new Question { Text = "a2", SubjectId = mat.Id, DifficultyLevel = 7 });
        var qLegacyA = Add(ctx, 0, new Question { Text = "eski-a", DifficultyLevel = 1 });
        var qAPub = Add(ctx, TeacherA, new Question { Text = "a-açık", SubjectId = mat.Id, TopicId = t6.Id, DifficultyLevel = 5, ClassificationSource = ClassificationSource.Human });
        var qB1 = Add(ctx, TeacherB, new Question { Text = "b1", SubjectId = mat.Id, TopicId = t5.Id, DifficultyLevel = 5, ClassificationSource = ClassificationSource.Human });
        var qC1 = Add(ctx, TeacherC, new Question { Text = "c1", SubjectId = mat.Id, TopicId = t6.Id, DifficultyLevel = 2, ClassificationSource = ClassificationSource.AI });
        var qC2 = Add(ctx, TeacherC, new Question { Text = "c2", DifficultyLevel = 4 });
        var qC3 = Add(ctx, TeacherC, new Question { Text = "c3", DifficultyLevel = 9 });
        var qLegacyPublic = Add(ctx, 0, new Question { Text = "eski-açık", DifficultyLevel = 1 });
        var qCopyLegacy = Add(ctx, 0, new Question { Text = "eski-kopya", DifficultyLevel = 1 });
        var qOrphan = Add(ctx, 0, new Question { Text = "sahipsiz", DifficultyLevel = 1 });
        var qDeleted = Add(ctx, TeacherA, new Question { Text = "silinmiş", IsDeleted = true });

        var answer = new Answer { QuestionId = qA1.Id, Text = "doğru" };
        ctx.Add(answer);
        ctx.SaveChanges();
        qA1.CorrectAnswerId = answer.Id;
        ctx.SaveChanges();

        ctx.QuestionSubTopics.AddRange(
            new QuestionSubTopic { QuestionId = qA1.Id, SubTopicId = st1.Id },
            new QuestionSubTopic { QuestionId = qB1.Id, SubTopicId = st1.Id },
            new QuestionSubTopic { QuestionId = qB1.Id, SubTopicId = st2.Id });

        // Üyelikler (WorksheetQuestion). IsDeleted=true üyelik sayılmaz ve görünürlük vermez.
        ctx.TestQuestions.AddRange(
            Link(wsAPrivate, qA1), Link(wsAPublic, qA1, deleted: true),
            Link(wsAPrivate, qA2),
            Link(wsAPrivate, qLegacyA),
            Link(wsAPublic, qAPub),
            Link(wsBSchool, qB1), Link(wsCSchool, qB1),
            Link(wsCPublic, qC1),
            Link(wsCSchool, qC2),
            Link(wsCPrivate, qC3),
            Link(wsCPublic, qLegacyPublic),
            Link(wsACopy, qCopyLegacy),
            Link(wsOwnerless, qOrphan),
            Link(wsAPrivate, qDeleted));
        ctx.SaveChanges();

        // Eski soru: CreateUserId null (sayılabilir legacy biçim).
        ctx.Questions.Where(q => q.Id == qLegacyA.Id)
            .ExecuteUpdate(s => s.SetProperty(q => q.CreateUserId, (int?)null));

        (_g5, _g6, _mat, _t5, _t6, _st1, _st2) = (g5.Id, g6.Id, mat.Id, t5.Id, t6.Id, st1.Id, st2.Id);
        (_qA1, _qA2, _qLegacyA, _qAPub, _qB1, _qC1, _qC2, _qC3) = (qA1.Id, qA2.Id, qLegacyA.Id, qAPub.Id, qB1.Id, qC1.Id, qC2.Id, qC3.Id);
        (_qLegacyPublic, _qCopyLegacy, _qOrphan, _qDeleted) = (qLegacyPublic.Id, qCopyLegacy.Id, qOrphan.Id, qDeleted.Id);
        _live = new[] { _qA1, _qA2, _qLegacyA, _qAPub, _qB1, _qC1, _qC2, _qC3, _qLegacyPublic, _qCopyLegacy, _qOrphan };
    }

    public void Dispose() => _db.Dispose();

    // ---------------- yardımcılar ----------------

    private static T Add<T>(AppDbContext ctx, int owner, T entity) where T : BaseEntity
    {
        ctx.SetCurrentUser(owner);
        ctx.Add(entity);
        ctx.SaveChanges();
        return entity;
    }

    private static WorksheetQuestion Link(Worksheet ws, Question q, bool deleted = false) =>
        new() { TestId = ws.Id, QuestionId = q.Id, Order = 1, IsDeleted = deleted };

    private async Task<QuestionBankPageDto> SearchAsync(int userId, bool isAdmin, QuestionBankQuery? filter = null)
    {
        await using var ctx = _db.NewContext();
        return await new QuestionQueryService(ctx).SearchBankAsync(filter ?? new QuestionBankQuery(), userId, isAdmin);
    }

    private static int[] Sorted(IEnumerable<int> ids) => ids.OrderBy(i => i).ToArray();

    private static int[] IdsOf(QuestionBankPageDto page) => Sorted(page.Items.Select(i => i.Id));

    private static int[] Expect(params int[] ids) => Sorted(ids);

    private int[] AllLiveIds => Sorted(_live);

    // ---------------- kapsam: mine ----------------

    [Fact]
    public async Task SearchBankAsync_ScopeMineDefault_ReturnsOnlyCallersOwnQuestions()
    {
        var page = await SearchAsync(TeacherA, isAdmin: false);

        IdsOf(page).ShouldBe(Expect(_qA1, _qA2, _qLegacyA, _qAPub));
        page.Total.ShouldBe(4);
    }

    [Fact]
    public async Task SearchBankAsync_ScopeMineForTeacherB_ReturnsOnlyTheirOwnQuestion()
    {
        var page = await SearchAsync(TeacherB, isAdmin: false);

        IdsOf(page).ShouldBe(Expect(_qB1));
    }

    [Fact]
    public async Task SearchBankAsync_ScopeMineForAdmin_ReturnsNothingWhenAdminOwnsNoQuestions()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "mine" });

        page.Total.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchBankAsync_NonAdminWithZeroUserId_ReturnsEmptyPage()
    {
        var page = await SearchAsync(0, isAdmin: false, new QuestionBankQuery { Scope = "all" });

        page.Total.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    // ---------------- kapsam: all (görünürlük) ----------------

    [Fact]
    public async Task SearchBankAsync_ScopeAllForTeacherA_ReturnsOwnAndVisibleQuestionsOnly()
    {
        var page = await SearchAsync(TeacherA, isAdmin: false, new QuestionBankQuery { Scope = "all" });

        // Kendi sorular + aynı okul SchoolOnly (B) + açık (C) testlerdeki sorular + kendi kopyası.
        // Dışarıda: C'nin özel/okul-dışı SchoolOnly sorusu (qC2, qC3), sahipsiz testteki soru (qOrphan), silinmiş soru.
        IdsOf(page).ShouldBe(Expect(_qA1, _qA2, _qLegacyA, _qAPub, _qCopyLegacy, _qB1, _qC1, _qLegacyPublic));
        page.Total.ShouldBe(8);
    }

    [Fact]
    public async Task SearchBankAsync_ScopeAllForTeacherC_HidesPrivateOtherSchoolAndDeletedMembershipQuestions()
    {
        var page = await SearchAsync(TeacherC, isAdmin: false, new QuestionBankQuery { Scope = "all" });

        // C'nin kendi soruları + C'nin SchoolOnly testindeki qB1 + açık testler (qAPub, qLegacyPublic).
        // qA1: A'nın açık testindeki üyelik SİLİNMİŞ, özel testi C'ye kapalı → görünmez.
        // qA2 / qLegacyA / qCopyLegacy: A'nın özel ya da kopya testinde → görünmez.
        IdsOf(page).ShouldBe(Expect(_qC1, _qC2, _qC3, _qB1, _qAPub, _qLegacyPublic));
        page.Items.Select(i => i.Id).ShouldNotContain(_qA1);
        page.Items.Select(i => i.Id).ShouldNotContain(_qCopyLegacy);
    }

    [Fact]
    public async Task SearchBankAsync_ScopeAllForTeacherWithoutSchool_ShowsOnlyPublicVisibility()
    {
        var page = await SearchAsync(TeacherNoSchool, isAdmin: false, new QuestionBankQuery { Scope = "all" });

        // Okulsuz: yalnız açık (PublicView) testlerdeki sorular. SchoolOnly testteki qB1/qC2 görünmez.
        IdsOf(page).ShouldBe(Expect(_qAPub, _qC1, _qLegacyPublic));
    }

    [Fact]
    public async Task SearchBankAsync_ScopeAll_ExcludesQuestionsOnlyInOwnerlessWorksheets()
    {
        var page = await SearchAsync(TeacherA, isAdmin: false, new QuestionBankQuery { Scope = "all" });

        page.Items.Select(i => i.Id).ShouldNotContain(_qOrphan);
    }

    [Fact]
    public async Task SearchBankAsync_ScopeAllForAdmin_ReturnsEveryLiveQuestionIncludingOwnerless()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all" });

        IdsOf(page).ShouldBe(AllLiveIds);
        page.Total.ShouldBe(_live.Length);
        page.Items.Select(i => i.Id).ShouldNotContain(_qDeleted);
    }

    [Fact]
    public async Task SearchBankAsync_ScopeAllScopeValueIsCaseInsensitive_BehavesLikeAll()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "ALL" });

        page.Total.ShouldBe(_live.Length);
    }

    // ---------------- IsOwn / TestCount ----------------

    [Fact]
    public async Task SearchBankAsync_IsOwn_LegacyQuestionOwnedOnlyThroughOriginalWorksheet()
    {
        var page = await SearchAsync(TeacherA, isAdmin: false, new QuestionBankQuery { Scope = "all" });
        var byId = page.Items.ToDictionary(i => i.Id);

        byId[_qLegacyA].IsOwn.ShouldBeTrue();   // A'nın özgün (kopya olmayan) testinde → sahip
        byId[_qAPub].IsOwn.ShouldBeTrue();
        byId[_qCopyLegacy].IsOwn.ShouldBeFalse(); // yalnız A'nın KOPYASINDA → sahip değil
        byId[_qLegacyPublic].IsOwn.ShouldBeFalse();
        byId[_qB1].IsOwn.ShouldBeFalse();
    }

    [Fact]
    public async Task SearchBankAsync_TestCountForTeacherA_CountsOnlyViewableNonDeletedMemberships()
    {
        var page = await SearchAsync(TeacherA, isAdmin: false, new QuestionBankQuery { Scope = "all" });
        var byId = page.Items.ToDictionary(i => i.Id);

        byId[_qB1].TestCount.ShouldBe(1);   // B'nin SchoolOnly testi görünür, C'ninki (farklı okul) değil
        byId[_qA1].TestCount.ShouldBe(1);   // A'nın özel testi; açık testteki üyelik silinmiş → sayılmaz
        byId[_qC1].TestCount.ShouldBe(1);
    }

    [Fact]
    public async Task SearchBankAsync_TestCountForTeacherC_ExcludesWorksheetsCallerCannotView()
    {
        var page = await SearchAsync(TeacherC, isAdmin: false, new QuestionBankQuery { Scope = "all" });
        var byId = page.Items.ToDictionary(i => i.Id);

        byId[_qB1].TestCount.ShouldBe(1); // yalnız C'nin SchoolOnly testi
    }

    [Fact]
    public async Task SearchBankAsync_TestCountForAdmin_CountsEveryLiveMembership()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all" });
        var byId = page.Items.ToDictionary(i => i.Id);

        byId[_qB1].TestCount.ShouldBe(2);
        byId[_qA1].TestCount.ShouldBe(1);
        byId[_qCopyLegacy].TestCount.ShouldBe(1);
    }

    // ---------------- filtreler ----------------

    [Fact]
    public async Task SearchBankAsync_FilterByGradeId_ReturnsQuestionsWhoseTopicIsInThatGrade()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", GradeId = _g6 });

        IdsOf(page).ShouldBe(Expect(_qAPub, _qC1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterBySubjectId_ReturnsQuestionsOfThatSubject()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", SubjectId = _mat });

        IdsOf(page).ShouldBe(Expect(_qA1, _qA2, _qAPub, _qB1, _qC1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterByTopicId_ReturnsQuestionsOfThatTopic()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", TopicId = _t5 });

        IdsOf(page).ShouldBe(Expect(_qA1, _qB1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterBySubTopicIds_MatchesAnyOfTheGivenSubTopics()
    {
        var onlySt2 = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", SubTopicIds = new[] { _st2, 999999 } });
        var st1 = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", SubTopicIds = new[] { _st1 } });

        IdsOf(onlySt2).ShouldBe(Expect(_qB1));
        IdsOf(st1).ShouldBe(Expect(_qA1, _qB1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterByDifficultyRange_IsInclusiveOnBothEnds()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", MinDifficulty = 5, MaxDifficulty = 7 });

        IdsOf(page).ShouldBe(Expect(_qA2, _qAPub, _qB1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterBySourceAI_ReturnsOnlyAiClassifiedQuestions()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Source = "AI" });

        IdsOf(page).ShouldBe(Expect(_qA1, _qC1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterBySourceHuman_ReturnsOnlyHumanClassifiedQuestions()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Source = "human" });

        // Kaynağı boş (null) sorular ne AI ne Human filtresine uyar.
        IdsOf(page).ShouldBe(Expect(_qAPub, _qB1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterByClassifiedTrue_RequiresBothSubjectAndTopic()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Classified = true });

        IdsOf(page).ShouldBe(Expect(_qA1, _qAPub, _qB1, _qC1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterByClassifiedFalse_IncludesQuestionsMissingSubjectOrTopic()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Classified = false });

        // qA2: ders var, konu yok → sınıflandırılmamış sayılır.
        IdsOf(page).ShouldBe(Expect(_qA2, _qLegacyA, _qC2, _qC3, _qLegacyPublic, _qCopyLegacy, _qOrphan));
    }

    [Fact]
    public async Task SearchBankAsync_FilterByHasCorrectAnswerTrue_ReturnsOnlyAnsweredQuestions()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", HasCorrectAnswer = true });

        IdsOf(page).ShouldBe(Expect(_qA1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterByHasCorrectAnswerFalse_ReturnsAllOtherQuestions()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", HasCorrectAnswer = false });

        IdsOf(page).ShouldBe(Sorted(_live.Where(id => id != _qA1)));
    }

    [Fact]
    public async Task SearchBankAsync_CombinedFilters_AreAppliedTogether()
    {
        var aiClassifiedMin1 = await SearchAsync(Admin, isAdmin: true,
            new QuestionBankQuery { Scope = "all", Source = "AI", Classified = true, MinDifficulty = 1 });
        var grade5Human = await SearchAsync(Admin, isAdmin: true,
            new QuestionBankQuery { Scope = "all", GradeId = _g5, Source = "Human" });

        IdsOf(aiClassifiedMin1).ShouldBe(Expect(_qA1, _qC1));
        IdsOf(grade5Human).ShouldBe(Expect(_qB1));
    }

    [Fact]
    public async Task SearchBankAsync_FilterAppliedWithinCallerVisibility_NeverWidensScope()
    {
        // Filtre görünürlüğün İÇİNDE süzer: B'nin SchoolOnly sorusu (qB1, aynı okul) A'ya görünür ve Human olduğu için gelir;
        // C'nin AI sınıflı qC1'i Source=Human ile elenir; A'nın göremediği Human sorular (örn. C'nin özel testi) hiç gelmez.
        var page = await SearchAsync(TeacherA, isAdmin: false, new QuestionBankQuery { Scope = "all", Source = "Human" });

        IdsOf(page).ShouldBe(Expect(_qAPub, _qB1));
    }

    // ---------------- sayfalama ----------------

    [Fact]
    public async Task SearchBankAsync_Paging_ReturnsNewestFirstWithFullTotal()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Take = 3 });

        var newestThree = _live.OrderByDescending(id => id).Take(3).ToArray();
        page.Items.Select(i => i.Id).ToArray().ShouldBe(newestThree);
        page.Total.ShouldBe(_live.Length);
        page.Take.ShouldBe(3);
        page.Skip.ShouldBe(0);
    }

    [Fact]
    public async Task SearchBankAsync_Paging_LastPageReturnsRemainderOnly()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Skip = 9, Take = 5 });

        page.Items.Count.ShouldBe(_live.Length - 9);
        page.Total.ShouldBe(_live.Length);
        page.Skip.ShouldBe(9);
    }

    [Fact]
    public async Task SearchBankAsync_Paging_SkipBeyondTotal_ReturnsEmptyItemsButKeepsTotal()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Skip = _live.Length });

        page.Items.ShouldBeEmpty();
        page.Total.ShouldBe(_live.Length);
    }

    [Fact]
    public async Task SearchBankAsync_Paging_ZeroTakeUsesDefaultPageSize()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Take = 0 });

        page.Take.ShouldBe(QuestionBankQuery.DefaultTake);
        page.Items.Count.ShouldBe(_live.Length);
    }

    [Fact]
    public async Task SearchBankAsync_Paging_TakeAboveMaximum_IsCappedAtMaxTake()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Take = 500 });

        page.Take.ShouldBe(QuestionBankQuery.MaxTake);
    }

    [Fact]
    public async Task SearchBankAsync_Paging_NegativeSkip_IsClampedToZero()
    {
        // Controller IsValid ile reddeder; servis katmanı da güvenli varsayılana düşer.
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all", Skip = -5 });

        page.Skip.ShouldBe(0);
        page.Items.Count.ShouldBe(_live.Length);
    }

    // ---------------- projeksiyon ----------------

    [Fact]
    public async Task SearchBankAsync_Projection_MapsClassificationNamesAndSubTopics()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all" });
        var b1 = page.Items.Single(i => i.Id == _qB1);

        b1.SubjectName.ShouldBe("Matematik");
        b1.TopicName.ShouldBe("Kesirler");
        b1.GradeId.ShouldBe(_g5);
        b1.SubTopics.Select(s => s.Name).OrderBy(n => n).ToArray().ShouldBe(new[] { "ST1", "ST2" });
        b1.DifficultyLevel.ShouldBe(5);
        b1.ClassificationSource.ShouldBe(ClassificationSource.Human);
        b1.IsClassified.ShouldBeTrue();
        b1.HasCorrectAnswer.ShouldBeFalse();
    }

    [Fact]
    public async Task SearchBankAsync_Projection_UnclassifiedQuestionHasNullTopicGradeAndSource()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all" });
        var a2 = page.Items.Single(i => i.Id == _qA2);

        a2.SubjectName.ShouldBe("Matematik");
        a2.TopicId.ShouldBeNull();
        a2.TopicName.ShouldBeNull();
        a2.GradeId.ShouldBeNull();
        a2.ClassificationSource.ShouldBeNull();
        a2.IsClassified.ShouldBeFalse();
    }

    [Fact]
    public async Task SearchBankAsync_Projection_AnsweredQuestionReportsCorrectAnswerFlag()
    {
        var page = await SearchAsync(Admin, isAdmin: true, new QuestionBankQuery { Scope = "all" });
        var a1 = page.Items.Single(i => i.Id == _qA1);

        a1.HasCorrectAnswer.ShouldBeTrue();
        a1.ClassificationSource.ShouldBe(ClassificationSource.AI);
        a1.GradeId.ShouldBe(_g5);
    }
}
