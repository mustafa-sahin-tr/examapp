using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #826: testsiz (havuza kaydedilmiş) sorunun <see cref="Question.BankSharing"/>'i — havuz araması (scope=all),
/// görüntüleme ve teste bağlama aynı kuralı uygular; düzenleme yetkisi vermez. SQLite.
/// </summary>
public class QuestionBankSharingTests : IDisposable
{
    private const int Owner = 1;          // okul S1; testsiz soruların sahibi
    private const int SameSchool = 2;     // okul S1
    private const int OtherSchool = 3;    // okul S2
    private const int NoSchool = 4;       // öğretmen kaydı yok

    private readonly TestDb _db = TestDb.Create();
    private readonly int _qPrivate, _qSchool, _qEveryone, _wsSameSchool, _wsOtherSchool;

    public QuestionBankSharingTests()
    {
        using var ctx = _db.NewContext();
        var s1 = new School { Name = "Okul 1" };
        var s2 = new School { Name = "Okul 2" };
        var grade = new Grade { Name = "5" };
        ctx.AddRange(s1, s2, grade);
        ctx.SaveChanges();
        ctx.Teachers.AddRange(
            new Teacher { UserId = Owner, SchoolId = s1.Id, AccountApprovedAt = DateTime.UtcNow },
            new Teacher { UserId = SameSchool, SchoolId = s1.Id, AccountApprovedAt = DateTime.UtcNow },
            new Teacher { UserId = OtherSchool, SchoolId = s2.Id, AccountApprovedAt = DateTime.UtcNow });
        ctx.SaveChanges();

        _qPrivate = Add(ctx, Owner, new Question { Text = "özel" }).Id;
        _qSchool = Add(ctx, Owner, new Question { Text = "okul", BankSharing = QuestionBankSharing.School }).Id;
        _qEveryone = Add(ctx, Owner, new Question { Text = "herkes", BankSharing = QuestionBankSharing.Everyone }).Id;
        _wsSameSchool = Add(ctx, SameSchool, new Worksheet { Name = "B", Description = "", GradeId = grade.Id }).Id;
        _wsOtherSchool = Add(ctx, OtherSchool, new Worksheet { Name = "C", Description = "", GradeId = grade.Id }).Id;
    }

    public void Dispose() => _db.Dispose();

    private static T Add<T>(AppDbContext ctx, int owner, T entity) where T : BaseEntity
    {
        ctx.SetCurrentUser(owner);
        ctx.Add(entity);
        ctx.SaveChanges();
        return entity;
    }

    private async Task<int[]> AllScopeIdsAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        var page = await new QuestionQueryService(ctx).SearchBankAsync(new QuestionBankQuery { Scope = "all" }, userId, isAdmin: false);
        return page.Items.Select(i => i.Id).OrderBy(i => i).ToArray();
    }

    [Fact]
    public async Task Bank_all_scope_shows_everyone_and_same_school_questions()
    {
        (await AllScopeIdsAsync(SameSchool)).ShouldBe(new[] { _qSchool, _qEveryone });
        (await AllScopeIdsAsync(OtherSchool)).ShouldBe(new[] { _qEveryone });
        (await AllScopeIdsAsync(NoSchool)).ShouldBe(new[] { _qEveryone });
        (await AllScopeIdsAsync(Owner)).ShouldBe(new[] { _qPrivate, _qSchool, _qEveryone });
    }

    [Fact]
    public async Task Bank_mine_scope_does_not_include_shared_questions_of_others()
    {
        await using var ctx = _db.NewContext();
        var page = await new QuestionQueryService(ctx).SearchBankAsync(new QuestionBankQuery(), SameSchool, isAdmin: false);
        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task View_follows_sharing_but_edit_stays_with_the_owner()
    {
        await using var ctx = _db.NewContext();
        var guard = new QuestionOwnershipGuard(ctx);

        (await guard.CanViewQuestionAsync(_qEveryone, OtherSchool, false)).ShouldBe(QuestionAccessResult.Allowed);
        (await guard.CanViewQuestionAsync(_qSchool, SameSchool, false)).ShouldBe(QuestionAccessResult.Allowed);
        (await guard.CanViewQuestionAsync(_qSchool, OtherSchool, false)).ShouldBe(QuestionAccessResult.Forbidden);
        (await guard.CanViewQuestionAsync(_qPrivate, SameSchool, false)).ShouldBe(QuestionAccessResult.Forbidden);

        (await guard.CanModifyQuestionAsync(_qEveryone, OtherSchool, false)).ShouldBe(QuestionAccessResult.Forbidden);
    }

    [Fact]
    public async Task A_teacher_can_link_shared_questions_to_their_own_test()
    {
        await using (var ctx = _db.NewContext())
        {
            var result = await new QuestionTestLinkService(ctx).LinkAsync(_wsSameSchool, [_qSchool, _qEveryone], SameSchool, false);
            result.ShouldNotBeNull().AddedQuestionIds.ShouldBe(new[] { _qSchool, _qEveryone });
        }

        await using (var ctx = _db.NewContext())
        {
            // Başka okul: okul paylaşımlı soru bağlanamaz; bir soru bile bağlanamazsa hiçbir şey yazılmaz.
            (await new QuestionTestLinkService(ctx).LinkAsync(_wsOtherSchool, [_qSchool, _qEveryone], OtherSchool, false)).ShouldBeNull();
            (await new QuestionTestLinkService(ctx).LinkAsync(_wsOtherSchool, [_qPrivate], OtherSchool, false)).ShouldBeNull();
        }
    }
}
