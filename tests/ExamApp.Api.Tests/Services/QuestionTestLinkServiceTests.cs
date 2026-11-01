using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #472: <see cref="QuestionTestLinkService.LinkAsync"/> — bankadan teste soru bağlama: sıra (testin sonu, istek
/// sırası), testte olanı atlama, silinmiş üyelik, kullanılabilir soru kuralı (kendi / görebildiği damgalı / legacy kendi;
/// legacy yabancı ve görünmeyen hayır), hepsi-ya-da-hiç ve bağlamanın düzenleme yetkisi vermemesi. SQLite.
/// </summary>
public class QuestionTestLinkServiceTests : IDisposable
{
    private const int TeacherA = 1; // okul S1, hedef test sahibi
    private const int TeacherB = 2; // okul S1
    private const int TeacherC = 3; // okul S2
    private const int Admin = 9;

    private readonly TestDb _db = TestDb.Create();

    private readonly int _target, _otherA;
    private readonly int _qInTarget1, _qInTarget2, _qOwnFree, _qOwnFree2, _qLegacyOwn, _qRemoved;
    private readonly int _qBSchool, _qCPublic, _qCSchool, _qCPrivate, _qLegacyPublic, _qDeleted;

    public QuestionTestLinkServiceTests()
    {
        using var ctx = _db.NewContext();

        var s1 = new School { Name = "Okul 1" };
        var s2 = new School { Name = "Okul 2" };
        var grade = new Grade { Name = "5" };
        ctx.AddRange(s1, s2, grade);
        ctx.SaveChanges();
        ctx.Teachers.AddRange(
            new Teacher { UserId = TeacherA, SchoolId = s1.Id, AccountApprovedAt = DateTime.UtcNow },
            new Teacher { UserId = TeacherB, SchoolId = s1.Id, AccountApprovedAt = DateTime.UtcNow },
            new Teacher { UserId = TeacherC, SchoolId = s2.Id, AccountApprovedAt = DateTime.UtcNow });
        ctx.SaveChanges();

        var target = Add(ctx, TeacherA, new Worksheet { Name = "Hedef", Description = "", GradeId = grade.Id });
        var otherA = Add(ctx, TeacherA, new Worksheet { Name = "A diğer", Description = "", GradeId = grade.Id });
        var bSchool = Add(ctx, TeacherB, new Worksheet { Name = "B okul", Description = "", GradeId = grade.Id, TeacherSharing = WorksheetTeacherSharing.SchoolOnly });
        var cPublic = Add(ctx, TeacherC, new Worksheet { Name = "C açık", Description = "", GradeId = grade.Id, TeacherSharing = WorksheetTeacherSharing.PublicView });
        var cSchool = Add(ctx, TeacherC, new Worksheet { Name = "C okul", Description = "", GradeId = grade.Id, TeacherSharing = WorksheetTeacherSharing.SchoolOnly });
        var cPrivate = Add(ctx, TeacherC, new Worksheet { Name = "C özel", Description = "", GradeId = grade.Id });

        var qInTarget1 = Add(ctx, TeacherA, new Question { Text = "hedefte-1" });
        var qInTarget2 = Add(ctx, TeacherA, new Question { Text = "hedefte-2" });
        var qOwnFree = Add(ctx, TeacherA, new Question { Text = "bağsız" });
        var qOwnFree2 = Add(ctx, TeacherA, new Question { Text = "bağsız-2" });
        var qLegacyOwn = Add(ctx, 0, new Question { Text = "eski-a" });
        var qRemoved = Add(ctx, TeacherA, new Question { Text = "çıkarılmış" });
        var qBSchool = Add(ctx, TeacherB, new Question { Text = "b-okul" });
        var qCPublic = Add(ctx, TeacherC, new Question { Text = "c-açık" });
        var qCSchool = Add(ctx, TeacherC, new Question { Text = "c-okul" });
        var qCPrivate = Add(ctx, TeacherC, new Question { Text = "c-özel" });
        var qLegacyPublic = Add(ctx, 0, new Question { Text = "eski-açık" });
        var qDeleted = Add(ctx, TeacherA, new Question { Text = "silinmiş", IsDeleted = true });

        ctx.TestQuestions.AddRange(
            new WorksheetQuestion { TestId = target.Id, QuestionId = qInTarget1.Id, Order = 1 },
            new WorksheetQuestion { TestId = target.Id, QuestionId = qInTarget2.Id, Order = 5 },
            // Testten çıkarılmış (soft-delete) üyelik: testte sayılmaz, sıra numarası hesabına girmez.
            new WorksheetQuestion { TestId = target.Id, QuestionId = qRemoved.Id, Order = 9, IsDeleted = true },
            new WorksheetQuestion { TestId = otherA.Id, QuestionId = qLegacyOwn.Id, Order = 1 },
            new WorksheetQuestion { TestId = bSchool.Id, QuestionId = qBSchool.Id, Order = 1 },
            new WorksheetQuestion { TestId = cPublic.Id, QuestionId = qCPublic.Id, Order = 1 },
            new WorksheetQuestion { TestId = cPublic.Id, QuestionId = qLegacyPublic.Id, Order = 2 },
            new WorksheetQuestion { TestId = cSchool.Id, QuestionId = qCSchool.Id, Order = 1 },
            new WorksheetQuestion { TestId = cPrivate.Id, QuestionId = qCPrivate.Id, Order = 1 });
        ctx.SaveChanges();

        (_target, _otherA) = (target.Id, otherA.Id);
        (_qInTarget1, _qInTarget2, _qOwnFree, _qOwnFree2, _qLegacyOwn, _qRemoved) =
            (qInTarget1.Id, qInTarget2.Id, qOwnFree.Id, qOwnFree2.Id, qLegacyOwn.Id, qRemoved.Id);
        (_qBSchool, _qCPublic, _qCSchool, _qCPrivate, _qLegacyPublic, _qDeleted) =
            (qBSchool.Id, qCPublic.Id, qCSchool.Id, qCPrivate.Id, qLegacyPublic.Id, qDeleted.Id);
    }

    public void Dispose() => _db.Dispose();

    private static T Add<T>(AppDbContext ctx, int owner, T entity) where T : BaseEntity
    {
        ctx.SetCurrentUser(owner);
        ctx.Add(entity);
        ctx.SaveChanges();
        return entity;
    }

    private async Task<LinkBankQuestionsResultDto?> LinkAsync(int userId, bool isAdmin, params int[] ids)
    {
        await using var ctx = _db.NewContext();
        return await new QuestionTestLinkService(ctx).LinkAsync(_target, ids, userId, isAdmin);
    }

    /// <summary>Hedef testin canlı üyelikleri, sıra numarasına göre: (QuestionId, Order).</summary>
    private async Task<(int QuestionId, int Order)[]> TargetMembershipsAsync()
    {
        await using var ctx = _db.NewContext();
        var rows = await ctx.TestQuestions.AsNoTracking()
            .Where(tq => tq.TestId == _target)
            .OrderBy(tq => tq.Order)
            .Select(tq => new { tq.QuestionId, tq.Order })
            .ToListAsync();
        return rows.Select(r => (r.QuestionId, r.Order)).ToArray();
    }

    [Fact]
    public async Task LinkAsync_OwnQuestions_AppendsAfterLastOrderInRequestOrder()
    {
        var result = await LinkAsync(TeacherA, false, _qOwnFree2, _qOwnFree);

        result.ShouldNotBeNull();
        result.AddedQuestionIds.ShouldBe(new[] { _qOwnFree2, _qOwnFree });
        result.AlreadyInTestQuestionIds.ShouldBeEmpty();
        (await TargetMembershipsAsync()).ShouldBe(new[]
        {
            (_qInTarget1, 1), (_qInTarget2, 5), (_qOwnFree2, 6), (_qOwnFree, 7),
        });
    }

    [Fact]
    public async Task LinkAsync_QuestionAlreadyInTest_IsSkippedAndNotDuplicated()
    {
        var result = await LinkAsync(TeacherA, false, _qInTarget1, _qOwnFree, _qOwnFree);

        result.ShouldNotBeNull();
        result.AddedQuestionIds.ShouldBe(new[] { _qOwnFree });
        result.AlreadyInTestQuestionIds.ShouldBe(new[] { _qInTarget1 });
        (await TargetMembershipsAsync()).Count(m => m.QuestionId == _qInTarget1).ShouldBe(1);
        (await TargetMembershipsAsync()).Count(m => m.QuestionId == _qOwnFree).ShouldBe(1);
    }

    [Fact]
    public async Task LinkAsync_SecondCallWithSameQuestions_AddsNothing()
    {
        (await LinkAsync(TeacherA, false, _qOwnFree)).ShouldNotBeNull();
        var second = await LinkAsync(TeacherA, false, _qOwnFree);

        second.ShouldNotBeNull();
        second.AddedQuestionIds.ShouldBeEmpty();
        second.AlreadyInTestQuestionIds.ShouldBe(new[] { _qOwnFree });
        (await TargetMembershipsAsync()).Length.ShouldBe(3);
    }

    [Fact]
    public async Task LinkAsync_QuestionRemovedFromTestEarlier_CanBeAddedAgainAtTheEnd()
    {
        var result = await LinkAsync(TeacherA, false, _qRemoved);

        result.ShouldNotBeNull();
        result.AddedQuestionIds.ShouldBe(new[] { _qRemoved });
        (await TargetMembershipsAsync()).Last().ShouldBe((_qRemoved, 6));
    }

    [Fact]
    public async Task LinkAsync_EmptyTest_StartsOrderAtOne()
    {
        await using (var ctx = _db.NewContext())
        {
            await ctx.TestQuestions.Where(tq => tq.TestId == _target)
                .ExecuteUpdateAsync(s => s.SetProperty(tq => tq.IsDeleted, true));
        }

        (await LinkAsync(TeacherA, false, _qOwnFree)).ShouldNotBeNull();

        (await TargetMembershipsAsync()).ShouldBe(new[] { (_qOwnFree, 1) });
    }

    [Fact]
    public async Task LinkAsync_LegacyQuestionOwnedViaOriginalWorksheet_IsLinkable()
    {
        var result = await LinkAsync(TeacherA, false, _qLegacyOwn);

        result.ShouldNotBeNull();
        result.AddedQuestionIds.ShouldBe(new[] { _qLegacyOwn });
    }

    [Fact]
    public async Task LinkAsync_VisibleStampedQuestionsOfOthers_AreLinkable_PublicAndSameSchool()
    {
        var result = await LinkAsync(TeacherA, false, _qCPublic, _qBSchool);

        result.ShouldNotBeNull();
        result.AddedQuestionIds.ShouldBe(new[] { _qCPublic, _qBSchool });
    }

    [Fact]
    public async Task LinkAsync_LinkingOthersQuestion_DoesNotGrantModifyRight()
    {
        (await LinkAsync(TeacherA, false, _qCPublic)).ShouldNotBeNull();

        await using var ctx = _db.NewContext();
        (await new QuestionOwnershipGuard(ctx).CanModifyQuestionAsync(_qCPublic, TeacherA, false))
            .ShouldBe(QuestionAccessResult.Forbidden);
    }

    [Theory]
    [InlineData("private")]       // başka öğretmenin özel testinde
    [InlineData("otherSchool")]   // başka okulun SchoolOnly testinde
    [InlineData("legacyPublic")]  // sahipsiz (legacy) soru, açık testte görünür olsa da
    [InlineData("deleted")]
    [InlineData("missing")]
    public async Task LinkAsync_AnyUnlinkableQuestion_ReturnsNullAndWritesNothing(string kind)
    {
        var bad = kind switch
        {
            "private" => _qCPrivate,
            "otherSchool" => _qCSchool,
            "legacyPublic" => _qLegacyPublic,
            "deleted" => _qDeleted,
            _ => 987654,
        };

        var result = await LinkAsync(TeacherA, false, _qOwnFree, bad);

        result.ShouldBeNull();
        (await TargetMembershipsAsync()).Select(m => m.QuestionId).ShouldBe(new[] { _qInTarget1, _qInTarget2 });
    }

    [Fact]
    public async Task LinkAsync_LegacyPublicQuestion_WouldOtherwiseGrantModifyRight_SoItStaysUnmodifiable()
    {
        // Güvenlik gerekçesi: legacy soru hedef (orijinal) teste bağlansaydı A onun "sahibi" sayılırdı.
        (await LinkAsync(TeacherA, false, _qLegacyPublic)).ShouldBeNull();

        await using var ctx = _db.NewContext();
        (await new QuestionOwnershipGuard(ctx).CanModifyQuestionAsync(_qLegacyPublic, TeacherA, false))
            .ShouldBe(QuestionAccessResult.Forbidden);
    }

    [Fact]
    public async Task LinkAsync_Admin_CanLinkAnyStampedQuestionButNotMissingOrDeleted()
    {
        var result = await LinkAsync(Admin, true, _qCPrivate, _qBSchool);
        result.ShouldNotBeNull();
        result.AddedQuestionIds.ShouldBe(new[] { _qCPrivate, _qBSchool });

        (await LinkAsync(Admin, true, _qDeleted)).ShouldBeNull();
        (await LinkAsync(Admin, true, 987654)).ShouldBeNull();
    }

    [Fact]
    public async Task LinkAsync_AdminIntoTeachersOriginalTest_RejectsForeignLegacyQuestion_SoTeacherGainsNoModifyRight()
    {
        // Security review (#472, Orta): admin yolunda da legacy sahiplik kazandırılmaz.
        (await LinkAsync(Admin, true, _qLegacyPublic)).ShouldBeNull();

        await using var ctx = _db.NewContext();
        (await new QuestionOwnershipGuard(ctx).CanModifyQuestionAsync(_qLegacyPublic, TeacherA, false))
            .ShouldBe(QuestionAccessResult.Forbidden);
    }

    [Fact]
    public async Task LinkAsync_AdminIntoTeachersOriginalTest_AcceptsLegacyQuestionTheOwnerAlreadyOwns()
    {
        var result = await LinkAsync(Admin, true, _qLegacyOwn);

        result.ShouldNotBeNull();
        result.AddedQuestionIds.ShouldBe(new[] { _qLegacyOwn });
    }

    [Fact]
    public async Task LinkAsync_AdminIntoCopyWorksheet_AcceptsForeignLegacyQuestion()
    {
        // Kopya test legacy sahiplik vermez → yan etki yok.
        int copy;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherA);
            var ws = new Worksheet { Name = "A kopya", Description = "", GradeId = ctx.Grades.First().Id, SourceWorksheetId = _otherA };
            ctx.Add(ws);
            await ctx.SaveChangesAsync();
            copy = ws.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var result = await new QuestionTestLinkService(ctx).LinkAsync(copy, new[] { _qLegacyPublic }, Admin, true);
            result.ShouldNotBeNull();
            result.AddedQuestionIds.ShouldBe(new[] { _qLegacyPublic });
        }
    }

    [Fact]
    public async Task LinkAsync_MissingOrDeletedWorksheet_ReturnsNull()
    {
        await using (var ctx = _db.NewContext())
        {
            (await new QuestionTestLinkService(ctx).LinkAsync(987654, new[] { _qOwnFree }, Admin, true)).ShouldBeNull();
            await ctx.Worksheets.Where(w => w.Id == _target).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsDeleted, true));
        }

        (await LinkAsync(TeacherA, false, _qOwnFree)).ShouldBeNull();
    }

    [Fact]
    public async Task LinkAsync_QuestionVisibleOnlyThroughDeletedWorksheet_IsRejectedForTeacher()
    {
        await using (var ctx = _db.NewContext())
        {
            await ctx.Worksheets.Where(w => w.Name == "C açık").ExecuteUpdateAsync(s => s.SetProperty(w => w.IsDeleted, true));
        }

        (await LinkAsync(TeacherA, false, _qCPublic)).ShouldBeNull();
    }

    [Fact]
    public async Task LinkAsync_NoValidIdsOrAnonymousTeacher_ReturnsNull()
    {
        (await LinkAsync(TeacherA, false)).ShouldBeNull();
        (await LinkAsync(TeacherA, false, 0, -3)).ShouldBeNull();
        (await LinkAsync(0, false, _qOwnFree)).ShouldBeNull();
    }

    [Fact]
    public async Task LinkAsync_StampsCreateUserIdOnNewMemberships()
    {
        (await LinkAsync(TeacherA, false, _qOwnFree)).ShouldNotBeNull();

        await using var ctx = _db.NewContext();
        var row = await ctx.TestQuestions.AsNoTracking().SingleAsync(tq => tq.TestId == _target && tq.QuestionId == _qOwnFree);
        row.CreateUserId.ShouldBe(TeacherA);
    }

    [Fact]
    public async Task LinkAsync_DoesNotTouchOtherWorksheets()
    {
        (await LinkAsync(TeacherA, false, _qOwnFree)).ShouldNotBeNull();

        await using var ctx = _db.NewContext();
        (await ctx.TestQuestions.CountAsync(tq => tq.TestId == _otherA)).ShouldBe(1);
    }

    // ---------------------------------------------------------------- issue #685: yeniden sıralama

    private async Task<ReorderTestQuestionsResult> ReorderAsync(params int[] ids)
    {
        await using var ctx = _db.NewContext();
        return await new QuestionTestLinkService(ctx).ReorderAsync(_target, ids, TeacherA);
    }

    [Fact]
    public async Task ReorderAsync_RenumbersLiveItemsInRequestOrder_issue685()
    {
        (await ReorderAsync(_qInTarget2, _qInTarget1)).ShouldBe(ReorderTestQuestionsResult.Reordered);

        (await TargetMembershipsAsync()).ShouldBe(new[] { (_qInTarget2, 1), (_qInTarget1, 2) });
        await using var ctx = _db.NewContext();
        // Testten çıkarılmış üyeliğe dokunulmaz.
        (await ctx.TestQuestions.IgnoreQueryFilters().SingleAsync(tq => tq.TestId == _target && tq.QuestionId == _qRemoved)).Order.ShouldBe(9);
    }

    [Theory]
    [InlineData(false, true)]  // eksik
    [InlineData(true, true)]   // fazla (testte olmayan soru)
    [InlineData(true, false)]  // testten çıkarılmış madde
    public async Task ReorderAsync_ListNotMatchingTheTestItems_IsMismatchAndWritesNothing_issue685(bool includeBoth, bool foreign)
    {
        var ids = includeBoth ? new List<int> { _qInTarget1, _qInTarget2 } : new List<int> { _qInTarget2 };
        if (includeBoth)
            ids.Add(foreign ? _qOwnFree : _qRemoved);

        (await ReorderAsync(ids.ToArray())).ShouldBe(ReorderTestQuestionsResult.Mismatch);
        (await TargetMembershipsAsync()).ShouldBe(new[] { (_qInTarget1, 1), (_qInTarget2, 5) });
    }

    [Fact]
    public async Task ReorderAsync_ItemWhoseQuestionIsDeleted_IsNotExpectedAndGoesLast_issue685()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = _target, QuestionId = _qDeleted, Order = 2 });
            await ctx.SaveChangesAsync();
        }

        (await ReorderAsync(_qInTarget2, _qInTarget1)).ShouldBe(ReorderTestQuestionsResult.Reordered);
        (await TargetMembershipsAsync()).ShouldBe(new[] { (_qInTarget2, 1), (_qInTarget1, 2), (_qDeleted, 3) });
    }

    [Fact]
    public async Task ReorderAsync_UnknownTest_IsNotFound_issue685()
    {
        await using var ctx = _db.NewContext();
        (await new QuestionTestLinkService(ctx).ReorderAsync(987654, new[] { _qInTarget1 }, TeacherA))
            .ShouldBe(ReorderTestQuestionsResult.NotFound);
    }
}
