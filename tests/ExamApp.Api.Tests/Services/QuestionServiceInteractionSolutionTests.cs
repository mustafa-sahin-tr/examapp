using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #570: <see cref="QuestionService"/> yazma yolu — <c>dragDropLabeling</c> planı çözümsüz saklanır, çözüm ayrı
/// kolonda tutulur; eski istemcinin plan içindeki <c>solution</c>'ı ayrılır; güncellemede çözüm kaybolmaz; geçersiz çözüm
/// reddedilir. Kapsam: <c>CreateOrUpdateQuestion</c> (güncelleme) ve <c>SaveBulkQuestion</c> (toplu kayıt).
/// </summary>
public class QuestionServiceInteractionSolutionTests : IDisposable
{
    private const string DragDropType = "dragDropLabeling";
    private const string Solution = """{"placements":[{"zone":"z1","label":"l1"}]}""";
    private const string NewSolution = """{"placements":[{"zone":"z2","label":"l2"}]}""";

    private static readonly string PlanWithoutSolution = """{"labels":["l1","l2"],"zones":[{"id":"z1"}]}""";
    private static readonly string PlanWithEmbeddedSolution =
        """{"labels":["l1","l2"],"zones":[{"id":"z1"}],"solution":{"placements":[{"zone":"z1","label":"l1"}]}}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    public void Dispose() => _db.Dispose();

    private static void ShouldHaveNoSolutionKey(string? plan)
    {
        plan.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(plan!);
        doc.RootElement.TryGetProperty("solution", out _).ShouldBeFalse();
    }

    /// <summary>Düz bir dragDrop sorusu; çözüm kolonu verilebilir (mevcut çözüm senaryosu için).</summary>
    private async Task<int> SeedDragDropQuestionAsync(string? plan, string? solution, int createUserId = 5)
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        ctx.SetCurrentUser(createUserId); // CreateUserId SaveChanges'ta bağlamdan damgalanır
        var q = new Question
        {
            Text = "Etiketleri yerleştir", Point = 5, SubjectId = subject.Id, CreateUserId = createUserId,
            InteractionType = DragDropType, InteractionPlan = plan, InteractionSolution = solution,
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private static QuestionDto UpdateDto(int id, string? plan, string? explicitSolution = null) => new()
    {
        Id = id,
        Text = "Etiketleri yerleştir",
        Point = 5,
        InteractionType = DragDropType,
        InteractionPlan = plan,
        InteractionSolution = explicitSolution,
        Answers = new List<AnswerDto>(),
    };

    private async Task<(string? Plan, string? Solution)> ReadStoredAsync(int questionId)
    {
        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync(x => x.Id == questionId);
        return (q.InteractionPlan, q.InteractionSolution);
    }

    // ---- CreateOrUpdateQuestion (güncelleme) ----

    [Fact]
    public async Task CreateOrUpdate_UpdateWithPlanAndExplicitSolution_StoresPlanWithoutSolutionAndSolutionSeparately()
    {
        var id = await SeedDragDropQuestionAsync(plan: null, solution: null);

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, PlanWithoutSolution, Solution), actingUserId: 5);
            result.Success.ShouldBeTrue();
        }

        var (plan, solution) = await ReadStoredAsync(id);
        ShouldHaveNoSolutionKey(plan);
        plan.ShouldNotBeNull();
        solution.ShouldBe(Solution);
    }

    [Fact]
    public async Task CreateOrUpdate_UpdateWithLegacyPlanEmbeddingSolution_SplitsSolutionIntoColumn()
    {
        var id = await SeedDragDropQuestionAsync(plan: null, solution: null);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, PlanWithEmbeddedSolution), actingUserId: 5))
                .Success.ShouldBeTrue();

        var (plan, solution) = await ReadStoredAsync(id);
        ShouldHaveNoSolutionKey(plan);
        solution.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(solution!);
        doc.RootElement.GetProperty("placements")[0].GetProperty("label").GetString().ShouldBe("l1");
    }

    [Fact]
    public async Task CreateOrUpdate_UpdateWithoutSolution_KeepsExistingSolution()
    {
        var id = await SeedDragDropQuestionAsync(plan: PlanWithoutSolution, solution: Solution);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, PlanWithoutSolution), actingUserId: 5))
                .Success.ShouldBeTrue();

        var (plan, solution) = await ReadStoredAsync(id);
        ShouldHaveNoSolutionKey(plan);
        solution.ShouldBe(Solution);
    }

    [Fact]
    public async Task CreateOrUpdate_UpdateWithExplicitSolution_ReplacesExistingSolution()
    {
        var id = await SeedDragDropQuestionAsync(plan: PlanWithoutSolution, solution: Solution);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, PlanWithoutSolution, NewSolution), actingUserId: 5))
                .Success.ShouldBeTrue();

        (await ReadStoredAsync(id)).Solution.ShouldBe(NewSolution);
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("{broken")]
    public async Task CreateOrUpdate_UpdateWithInvalidExplicitSolution_ReturnsInvalidInputAndKeepsRow(string invalid)
    {
        var id = await SeedDragDropQuestionAsync(plan: PlanWithoutSolution, solution: Solution);

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, PlanWithoutSolution, invalid), actingUserId: 5);

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();

        var (plan, solution) = await ReadStoredAsync(id);
        plan.ShouldBe(PlanWithoutSolution);
        solution.ShouldBe(Solution);
    }

    [Fact]
    public async Task CreateOrUpdate_OwnerRoundTrip_OwnerSeesSavedSolutionAndAuthorshipIsKept()
    {
        var id = await SeedDragDropQuestionAsync(plan: PlanWithoutSolution, solution: Solution);

        // Sahibi çözümü tuvalde görür (okuma) ...
        await using (var read = _db.NewContext())
        {
            var before = await new QuestionQueryService(read).GetQuestionById(id, 5, solutionViewerIsAdmin: false);
            before!.InteractionSolution.ShouldBe(Solution);
        }

        // ... değiştirip kaydeder (yazma; plan çözümsüz, çözüm ayrı alanda) ...
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, PlanWithoutSolution, NewSolution), actingUserId: 5))
                .Success.ShouldBeTrue();

        // ... ve tekrar okuduğunda yeni çözümü görür; yazarlık (CreateUserId) değişmez.
        await using (var read = _db.NewContext())
        {
            var after = await new QuestionQueryService(read).GetQuestionById(id, 5, solutionViewerIsAdmin: false);
            after!.InteractionSolution.ShouldBe(NewSolution);
            ShouldHaveNoSolutionKey(after.InteractionPlan);
        }

        await using var check = _db.NewContext();
        (await check.Questions.AsNoTracking().SingleAsync(q => q.Id == id)).CreateUserId.ShouldBe(5);
    }

    // ---- SaveBulkQuestion (toplu kayıt) ----

    private static BulkQuestionDto DragDropBulk(string name, string? plan, string? explicitSolution = null) => new()
    {
        Name = name,
        InteractionType = DragDropType,
        InteractionPlan = plan,
        InteractionSolution = explicitSolution,
        Answers = new List<BulkAnswerDto>(),
    };

    private static BulkQuestionCreateDto Bulk(params BulkQuestionDto[] questions) => new()
    {
        ImageData = null!,
        Passages = new List<BulkPassageDto>(),
        Questions = questions.ToList(),
        Header = new HeaderInfo(),
    };

    [Fact]
    public async Task SaveBulk_ExplicitSolution_StoresPlanWithoutSolutionAndSolutionSeparately()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(Bulk(DragDropBulk("S1", PlanWithoutSolution, Solution)), actingUserId: 7);

        result.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync();
        ShouldHaveNoSolutionKey(q.InteractionPlan);
        q.InteractionSolution.ShouldBe(Solution);
        q.CreateUserId.ShouldBe(7);
    }

    [Fact]
    public async Task SaveBulk_LegacyPlanEmbeddingSolution_SplitsSolutionIntoColumn()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(Bulk(DragDropBulk("S1", PlanWithEmbeddedSolution)), actingUserId: 7);

        result.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync();
        ShouldHaveNoSolutionKey(q.InteractionPlan);
        q.InteractionSolution.ShouldNotBeNull();
        q.InteractionSolution!.ShouldContain("placements");
    }

    [Fact]
    public async Task SaveBulk_InvalidExplicitSolution_FailsTheWholeBatchAndCreatesNoQuestions()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(Bulk(
                DragDropBulk("iyi", PlanWithoutSolution, Solution),
                DragDropBulk("kötü", PlanWithoutSolution, "[1,2]")), actingUserId: 7);

        result.Success.ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }
}
