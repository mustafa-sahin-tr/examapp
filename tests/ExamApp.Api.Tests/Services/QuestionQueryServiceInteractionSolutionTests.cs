using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #570: soru okuma (<see cref="QuestionQueryService.GetQuestionById"/>, <see cref="QuestionQueryService.GetQuestionByTestId"/>)
/// — plan her zaman çözümsüz; etkileşim çözümü yalnız sorunun sahibine (CreateUserId, ya da legacy'de orijinal testin
/// sahibine) ya da admine döner. Sahiplik kuralı <c>QuestionOwnershipGuard.CanModifyQuestionAsync</c> ile aynıdır.
/// </summary>
public class QuestionQueryServiceInteractionSolutionTests : IDisposable
{
    private const int Owner = 5;
    private const int OtherTeacher = 6;
    private const int Admin = 9;
    private const string Solution = """{"placements":[{"zone":"z1","label":"l1"}]}""";
    private static readonly string PlanWithoutSolution = """{"labels":["l1","l2"],"zones":[{"id":"z1"}]}""";
    private static readonly string PlanWithEmbeddedSolution =
        """{"labels":["l1","l2"],"zones":[{"id":"z1"}],"solution":{"placements":[{"zone":"z1","label":"l1"}]}}""";

    private readonly TestDb _db = TestDb.Create();

    private static QuestionQueryService NewService(AppDbContext ctx) => new(ctx);

    public void Dispose() => _db.Dispose();

    private static void ShouldHaveNoSolutionKey(string? plan)
    {
        plan.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(plan!);
        doc.RootElement.TryGetProperty("solution", out _).ShouldBeFalse();
    }

    /// <summary>Tek seferlik ders + sınıf (FK); varsa mevcut olan kullanılır.</summary>
    private static async Task<(int SubjectId, int GradeId)> EnsureBaseAsync(AppDbContext ctx)
    {
        var subject = await ctx.Subjects.FirstOrDefaultAsync() ?? new Subject { Name = "Fen" };
        var grade = await ctx.Grades.FirstOrDefaultAsync() ?? new Grade { Name = "5" };
        if (subject.Id == 0) ctx.Subjects.Add(subject);
        if (grade.Id == 0) ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        return (subject.Id, grade.Id);
    }

    /// <summary>
    /// Soru ekler. <c>CreateUserId</c> SaveChanges'ta bağlamdaki kullanıcıdan damgalanır (AppDbContext.ApplyAuditInfo),
    /// bu yüzden sahip <see cref="AppDbContext.SetCurrentUser"/> ile verilir; <c>null</c> = legacy (0).
    /// </summary>
    private async Task<Question> AddQuestionAsync(AppDbContext ctx, int? createUserId, string plan, string? solutionColumn)
    {
        var (subjectId, _) = await EnsureBaseAsync(ctx);
        ctx.SetCurrentUser(createUserId ?? 0);
        var q = new Question
        {
            Text = "Etiketleri yerleştir", Point = 5, SubjectId = subjectId,
            InteractionType = "dragDropLabeling", InteractionPlan = plan, InteractionSolution = solutionColumn,
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q;
    }

    /// <summary>Testi (sahibi <paramref name="ownerUserId"/>) ekler; <paramref name="sourceWorksheetId"/> doluysa kopya.</summary>
    private static async Task<Worksheet> AddWorksheetAsync(AppDbContext ctx, int ownerUserId, int? sourceWorksheetId = null)
    {
        var (_, gradeId) = await EnsureBaseAsync(ctx);
        ctx.SetCurrentUser(ownerUserId);
        var ws = new Worksheet { Name = "W", Description = "", GradeId = gradeId, SourceWorksheetId = sourceWorksheetId };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        return ws;
    }

    private static void LinkToTest(AppDbContext ctx, Question q, Worksheet ws) =>
        ctx.TestQuestions.Add(new WorksheetQuestion { TestId = ws.Id, QuestionId = q.Id, Order = 1 });

    // ---- GetQuestionById ----

    [Fact]
    public async Task GetQuestionById_OwnerViewer_ReturnsSolutionAndPlanWithoutSolutionKey()
    {
        int id;
        await using (var ctx = _db.NewContext())
            id = (await AddQuestionAsync(ctx, Owner, PlanWithoutSolution, Solution)).Id;

        await using var read = _db.NewContext();
        var dto = await NewService(read).GetQuestionById(id, Owner, solutionViewerIsAdmin: false);

        dto.ShouldNotBeNull();
        ShouldHaveNoSolutionKey(dto!.InteractionPlan);
        dto.InteractionSolution.ShouldBe(Solution);
    }

    [Fact]
    public async Task GetQuestionById_NonOwnerTeacher_ReturnsPlanWithoutSolutionAndNoSolution()
    {
        int id;
        await using (var ctx = _db.NewContext())
            id = (await AddQuestionAsync(ctx, Owner, PlanWithoutSolution, Solution)).Id;

        await using var read = _db.NewContext();
        var dto = await NewService(read).GetQuestionById(id, OtherTeacher, solutionViewerIsAdmin: false);

        dto.ShouldNotBeNull();
        ShouldHaveNoSolutionKey(dto!.InteractionPlan);
        dto.InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task GetQuestionById_AdminViewer_ReturnsSolutionEvenWhenNotOwner()
    {
        int id;
        await using (var ctx = _db.NewContext())
            id = (await AddQuestionAsync(ctx, Owner, PlanWithoutSolution, Solution)).Id;

        await using var read = _db.NewContext();
        var dto = await NewService(read).GetQuestionById(id, Admin, solutionViewerIsAdmin: true);

        dto!.InteractionSolution.ShouldBe(Solution);
    }

    [Fact]
    public async Task GetQuestionById_NoViewerArguments_ReturnsNoSolution()
    {
        int id;
        await using (var ctx = _db.NewContext())
            id = (await AddQuestionAsync(ctx, Owner, PlanWithoutSolution, Solution)).Id;

        await using var read = _db.NewContext();
        var dto = await NewService(read).GetQuestionById(id);

        dto!.InteractionSolution.ShouldBeNull();
        ShouldHaveNoSolutionKey(dto.InteractionPlan);
    }

    [Fact]
    public async Task GetQuestionById_LegacyQuestionViewedByOriginalWorksheetOwner_ReturnsSolution()
    {
        int id;
        await using (var ctx = _db.NewContext())
        {
            var q = await AddQuestionAsync(ctx, createUserId: null, PlanWithoutSolution, Solution);
            var original = await AddWorksheetAsync(ctx, Owner);
            LinkToTest(ctx, q, original);
            await ctx.SaveChangesAsync();
            id = q.Id;
        }

        await using var read = _db.NewContext();
        (await NewService(read).GetQuestionById(id, Owner))!.InteractionSolution.ShouldBe(Solution);
    }

    [Fact]
    public async Task GetQuestionById_LegacyQuestionOnlyInCopyOfAnotherTeacher_HidesSolutionFromCopyOwner()
    {
        int id;
        await using (var ctx = _db.NewContext())
        {
            var q = await AddQuestionAsync(ctx, createUserId: null, PlanWithoutSolution, Solution);
            var original = await AddWorksheetAsync(ctx, Owner);
            var copy = await AddWorksheetAsync(ctx, OtherTeacher, sourceWorksheetId: original.Id);
            LinkToTest(ctx, q, copy);
            await ctx.SaveChangesAsync();
            id = q.Id;
        }

        // Kopya sahibi soruyu görebilir ama çözümünü alamaz: legacy sahiplik yalnız orijinal (kopya olmayan) testten türer.
        await using var read = _db.NewContext();
        (await NewService(read).GetQuestionById(id, OtherTeacher))!.InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task GetQuestionById_LegacyEmbeddedSolution_OwnerGetsSplitSolutionAndNonOwnerGetsNone()
    {
        int id;
        await using (var ctx = _db.NewContext())
            id = (await AddQuestionAsync(ctx, Owner, PlanWithEmbeddedSolution, solutionColumn: null)).Id;

        await using (var read = _db.NewContext())
        {
            var owner = await NewService(read).GetQuestionById(id, Owner);
            ShouldHaveNoSolutionKey(owner!.InteractionPlan);
            owner.InteractionSolution.ShouldNotBeNull();
            owner.InteractionSolution!.ShouldContain("placements");
        }

        await using var other = _db.NewContext();
        var nonOwner = await NewService(other).GetQuestionById(id, OtherTeacher);
        ShouldHaveNoSolutionKey(nonOwner!.InteractionPlan);
        nonOwner.InteractionSolution.ShouldBeNull();
    }

    // ---- GetQuestionByTestId ----

    [Fact]
    public async Task GetQuestionByTestId_MixedOwnership_ReturnsSolutionOnlyForOwnedQuestions()
    {
        int testId;
        int ownedId, otherOwnedId;
        await using (var ctx = _db.NewContext())
        {
            var ownedByViewer = await AddQuestionAsync(ctx, Owner, PlanWithoutSolution, Solution);
            var ownedByOther = await AddQuestionAsync(ctx, OtherTeacher, PlanWithoutSolution, Solution);
            var ws = await AddWorksheetAsync(ctx, Owner);
            LinkToTest(ctx, ownedByViewer, ws);
            LinkToTest(ctx, ownedByOther, ws);
            await ctx.SaveChangesAsync();
            (testId, ownedId, otherOwnedId) = (ws.Id, ownedByViewer.Id, ownedByOther.Id);
        }

        await using var read = _db.NewContext();
        var list = await NewService(read).GetQuestionByTestId(testId, Owner, solutionViewerIsAdmin: false);

        list.Count.ShouldBe(2);
        list.Single(q => q.Id == ownedId).InteractionSolution.ShouldBe(Solution);
        list.Single(q => q.Id == otherOwnedId).InteractionSolution.ShouldBeNull();
        list.ShouldAllBe(q => !q.InteractionPlan!.Contains("\"solution\""));
    }

    [Fact]
    public async Task GetQuestionByTestId_NonOwnerViewer_ReturnsNoSolutionForAnyQuestion()
    {
        int testId;
        await using (var ctx = _db.NewContext())
        {
            var q = await AddQuestionAsync(ctx, Owner, PlanWithEmbeddedSolution, solutionColumn: Solution);
            var ws = await AddWorksheetAsync(ctx, Owner);
            LinkToTest(ctx, q, ws);
            await ctx.SaveChangesAsync();
            testId = ws.Id;
        }

        await using var read = _db.NewContext();
        var list = await NewService(read).GetQuestionByTestId(testId, OtherTeacher, solutionViewerIsAdmin: false);

        list.ShouldHaveSingleItem().InteractionSolution.ShouldBeNull();
        ShouldHaveNoSolutionKey(list[0].InteractionPlan);
    }

    [Fact]
    public async Task GetQuestionByTestId_AdminViewer_ReturnsSolutionForEveryQuestion()
    {
        int testId;
        await using (var ctx = _db.NewContext())
        {
            var q1 = await AddQuestionAsync(ctx, Owner, PlanWithoutSolution, Solution);
            var q2 = await AddQuestionAsync(ctx, OtherTeacher, PlanWithoutSolution, Solution);
            var ws = await AddWorksheetAsync(ctx, Owner);
            LinkToTest(ctx, q1, ws);
            LinkToTest(ctx, q2, ws);
            await ctx.SaveChangesAsync();
            testId = ws.Id;
        }

        await using var read = _db.NewContext();
        var list = await NewService(read).GetQuestionByTestId(testId, Admin, solutionViewerIsAdmin: true);

        list.Count.ShouldBe(2);
        list.ShouldAllBe(q => q.InteractionSolution == Solution);
    }
}
