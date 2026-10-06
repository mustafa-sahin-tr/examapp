using ExamApp.Api.Data;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #402: <see cref="QuestionOwnershipGuard"/> okuma/paragraf kuralları (SQLite). Görüntüleme
/// <c>WorksheetAccess.CanView</c> ile aynı; paragraf bağlama yalnız oluşturana (legacy paragrafta bağlı sorunun sahibine).
/// </summary>
public class QuestionOwnershipGuardReadTests : IDisposable
{
    private const int Owner = 1;
    private const int Other = 2;

    private readonly TestDb _db = TestDb.Create();
    private readonly int _privateWs, _publicWs, _legacyWs, _privateQ, _ownPassage, _legacyPassage, _legacyQ;

    public QuestionOwnershipGuardReadTests()
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        ctx.SaveChanges();

        // Legacy (sahipsiz) kayıtlar: test, paragraf, soru — CreateUserId 0.
        var legacyWs = new Worksheet { Name = "L", Description = "", GradeId = grade.Id, TeacherSharing = WorksheetTeacherSharing.PublicView };
        var legacyPassage = new Passage { Title = "legacy" };
        ctx.AddRange(legacyWs, legacyPassage);
        ctx.SaveChanges();
        var legacyQ = new Question { Text = "lq", PassageId = legacyPassage.Id };
        ctx.Questions.Add(legacyQ);
        ctx.SaveChanges();

        ctx.SetCurrentUser(Owner);
        var privateWs = new Worksheet { Name = "P", Description = "", GradeId = grade.Id };
        var publicWs = new Worksheet { Name = "Pub", Description = "", GradeId = grade.Id, TeacherSharing = WorksheetTeacherSharing.PublicAssignable };
        var ownPassage = new Passage { Title = "own" };
        var privateQ = new Question { Text = "q" };
        ctx.AddRange(privateWs, publicWs, ownPassage, privateQ);
        ctx.SaveChanges();
        ctx.TestQuestions.AddRange(
            new WorksheetQuestion { TestId = privateWs.Id, QuestionId = privateQ.Id, Order = 1 },
            // Legacy soru, Owner'ın orijinal testinde → Owner legacy sorunun (ve paragrafının) sahibi sayılır.
            new WorksheetQuestion { TestId = privateWs.Id, QuestionId = legacyQ.Id, Order = 2 },
            new WorksheetQuestion { TestId = legacyWs.Id, QuestionId = privateQ.Id, Order = 1 });
        ctx.SaveChanges();

        (_privateWs, _publicWs, _legacyWs, _privateQ, _ownPassage, _legacyPassage, _legacyQ) =
            (privateWs.Id, publicWs.Id, legacyWs.Id, privateQ.Id, ownPassage.Id, legacyPassage.Id, legacyQ.Id);
    }

    public void Dispose() => _db.Dispose();

    private QuestionOwnershipGuard NewGuard(AppDbContext ctx) => new(ctx);

    [Fact]
    public async Task Worksheet_view_follows_CanView()
    {
        await using var ctx = _db.NewContext();
        var guard = NewGuard(ctx);

        (await guard.CanViewWorksheetAsync(_privateWs, Owner, false)).ShouldBe(QuestionAccessResult.Allowed);
        (await guard.CanViewWorksheetAsync(_privateWs, Other, false)).ShouldBe(QuestionAccessResult.Forbidden);
        (await guard.CanViewWorksheetAsync(_publicWs, Other, false)).ShouldBe(QuestionAccessResult.Allowed);
        // Legacy (sahipsiz) public test yalnız admin'e görünür.
        (await guard.CanViewWorksheetAsync(_legacyWs, Other, false)).ShouldBe(QuestionAccessResult.Forbidden);
        (await guard.CanViewWorksheetAsync(_legacyWs, 99, true)).ShouldBe(QuestionAccessResult.Allowed);
        (await guard.CanViewWorksheetAsync(987654, Owner, false)).ShouldBe(QuestionAccessResult.NotFound);
        (await guard.CanViewWorksheetAsync(_privateWs, 0, false)).ShouldBe(QuestionAccessResult.Forbidden);
    }

    [Fact]
    public async Task Question_view_needs_ownership_or_a_visible_owned_worksheet()
    {
        await using var ctx = _db.NewContext();
        var guard = NewGuard(ctx);

        (await guard.CanViewQuestionAsync(_privateQ, Owner, false)).ShouldBe(QuestionAccessResult.Allowed);
        // Soru yalnız private testte ve legacy (sahipsiz) public testte → başka öğretmene görünmez.
        (await guard.CanViewQuestionAsync(_privateQ, Other, false)).ShouldBe(QuestionAccessResult.Forbidden);
        (await guard.CanViewQuestionAsync(_privateQ, 99, true)).ShouldBe(QuestionAccessResult.Allowed);
        (await guard.CanViewQuestionAsync(987654, Owner, false)).ShouldBe(QuestionAccessResult.NotFound);

        await using (var w = _db.NewContext())
        {
            w.TestQuestions.Add(new WorksheetQuestion { TestId = _publicWs, QuestionId = _privateQ, Order = 1 });
            await w.SaveChangesAsync();
        }
        await using var ctx2 = _db.NewContext();
        (await NewGuard(ctx2).CanViewQuestionAsync(_privateQ, Other, false)).ShouldBe(QuestionAccessResult.Allowed);
    }

    [Fact]
    public async Task Passage_linking_is_owner_admin_or_already_linked()
    {
        await using var ctx = _db.NewContext();
        var guard = NewGuard(ctx);

        (await guard.CanUsePassageAsync(_ownPassage, Owner, false)).ShouldBe(QuestionAccessResult.Allowed);
        (await guard.CanUsePassageAsync(_ownPassage, Other, false)).ShouldBe(QuestionAccessResult.Forbidden);
        (await guard.CanUsePassageAsync(_ownPassage, 99, true)).ShouldBe(QuestionAccessResult.Allowed);
        (await guard.CanUsePassageAsync(987654, Owner, false)).ShouldBe(QuestionAccessResult.NotFound);

        // Legacy paragraf: bağlı (legacy) sorunun türetilmiş sahibi kullanabilir, başkası kullanamaz.
        (await guard.CanUsePassageAsync(_legacyPassage, Owner, false)).ShouldBe(QuestionAccessResult.Allowed);
        (await guard.CanUsePassageAsync(_legacyPassage, Other, false)).ShouldBe(QuestionAccessResult.Forbidden);
        // Zaten bu soruya bağlı paragrafı düzenlemede geri göndermek serbest.
        (await guard.CanUsePassageAsync(_legacyPassage, Other, false, questionId: _legacyQ)).ShouldBe(QuestionAccessResult.Allowed);
    }
}
