using ExamApp.Api.Data;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #570 review: tek sahiplik kaynağı (<see cref="QuestionOwnershipGuard.OwnedBy"/>). Yazma yetkisi
/// (<see cref="QuestionOwnershipGuard.CanModifyQuestionAsync"/>) ile çözüm görünürlüğü (<see cref="QuestionQueryService"/>)
/// her senaryoda aynı kararı verir; bulunamayan soru her ikisinde de yok sayılır.
/// </summary>
public class QuestionOwnershipVisibilityAgreementTests : IDisposable
{
    private const int Owner = 5;          // CreateUserId sahibi; legacy'de orijinal testin sahibi
    private const int CopyOwner = 6;      // yalnız kopya testte soruyu tutan öğretmen
    private const int Stranger = 7;
    private const int Admin = 9;
    private const int MissingId = 987654;
    private const string Solution = """{"placements":[{"zone":"z1","label":"l1"}]}""";

    public enum Scenario { CreatedByOwner, LegacyOriginalOwner, LegacyCopyOnly, SoftDeleted, Missing }

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private async Task<int> BuildAsync(Scenario scenario)
    {
        if (scenario == Scenario.Missing) return MissingId;

        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        var grade = new Grade { Name = "5" };
        ctx.AddRange(subject, grade);
        await ctx.SaveChangesAsync();

        // CreateUserId SaveChanges'ta bağlam kullanıcısından damgalanır; legacy için 0.
        ctx.SetCurrentUser(scenario == Scenario.CreatedByOwner || scenario == Scenario.SoftDeleted ? Owner : 0);
        var q = new Question
        {
            Text = "Etiketleri yerleştir", Point = 5, SubjectId = subject.Id,
            InteractionType = "dragDropLabeling", InteractionPlan = """{"labels":["l1"]}""", InteractionSolution = Solution,
            IsDeleted = scenario == Scenario.SoftDeleted,
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(Owner);
        var original = new Worksheet { Name = "Orijinal", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(original);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(CopyOwner);
        var copy = new Worksheet { Name = "Kopya", Description = "", GradeId = grade.Id, SourceWorksheetId = original.Id };
        ctx.Worksheets.Add(copy);
        await ctx.SaveChangesAsync();

        switch (scenario)
        {
            case Scenario.LegacyOriginalOwner:
                ctx.TestQuestions.Add(new WorksheetQuestion { TestId = original.Id, QuestionId = q.Id, Order = 1 });
                break;
            case Scenario.LegacyCopyOnly:
                ctx.TestQuestions.Add(new WorksheetQuestion { TestId = copy.Id, QuestionId = q.Id, Order = 1 });
                break;
        }
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    [Theory]
    [InlineData(Scenario.CreatedByOwner, Owner, false, true)]
    [InlineData(Scenario.CreatedByOwner, Admin, true, true)]
    [InlineData(Scenario.CreatedByOwner, CopyOwner, false, false)]
    [InlineData(Scenario.CreatedByOwner, Stranger, false, false)]
    [InlineData(Scenario.LegacyOriginalOwner, Owner, false, true)]
    [InlineData(Scenario.LegacyOriginalOwner, CopyOwner, false, false)]
    [InlineData(Scenario.LegacyOriginalOwner, Stranger, false, false)]
    [InlineData(Scenario.LegacyCopyOnly, CopyOwner, false, false)]
    [InlineData(Scenario.LegacyCopyOnly, Owner, false, false)]
    [InlineData(Scenario.LegacyCopyOnly, Admin, true, true)]
    [InlineData(Scenario.SoftDeleted, Owner, false, false)] // soft-delete global filtresi: iki okuma da NotFound
    [InlineData(Scenario.SoftDeleted, Stranger, false, false)]
    public async Task WriteGuardAndSolutionVisibility_AgreeOnEveryScenario(Scenario scenario, int viewer, bool isAdmin, bool expectedAllowed)
    {
        var questionId = await BuildAsync(scenario);

        QuestionAccessResult guard;
        await using (var guardCtx = _db.NewContext())
            guard = await new QuestionOwnershipGuard(guardCtx).CanModifyQuestionAsync(questionId, viewer, isAdmin);

        QuestionDto? dto;
        await using (var queryCtx = _db.NewContext())
            dto = await new QuestionQueryService(queryCtx).GetQuestionById(questionId, viewer, isAdmin);

        var visible = dto?.InteractionSolution != null;
        var allowed = guard == QuestionAccessResult.Allowed;

        visible.ShouldBe(allowed, $"scenario={scenario} viewer={viewer} guard={guard}");
        allowed.ShouldBe(expectedAllowed, $"scenario={scenario} viewer={viewer} guard={guard}");
    }

    [Fact]
    public async Task WriteGuardAndSolutionVisibility_MissingQuestion_AreBothNotFoundAndHideSolution()
    {
        var questionId = await BuildAsync(Scenario.Missing);

        QuestionAccessResult guard;
        await using (var guardCtx = _db.NewContext())
            guard = await new QuestionOwnershipGuard(guardCtx).CanModifyQuestionAsync(questionId, Owner, isAdmin: false);

        QuestionDto? dto;
        await using (var queryCtx = _db.NewContext())
            dto = await new QuestionQueryService(queryCtx).GetQuestionById(questionId, Owner, solutionViewerIsAdmin: false);

        guard.ShouldBe(QuestionAccessResult.NotFound);
        dto.ShouldBeNull();
    }
}
