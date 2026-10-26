using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #570 review: <see cref="QuestionService"/> yazma yolu katı — yinelenen anahtar (her derinlikte), bozuk JSON,
/// nesne olmayan plan/çözüm ve sınır aşımı kayıttan önce reddedilir; yanıt yerelleştirilmiş mesajdır (ham istisna değil);
/// toplu kayıtta reddedilen istek MinIO'ya hiçbir şey yüklemez ve hiçbir soru yazmaz.
/// </summary>
public class QuestionServiceInteractionSolutionStrictTests : IDisposable
{
    private const string DragDropType = "dragDropLabeling";
    private const string Solution = """{"placements":[{"zone":"z1","label":"l1"}]}""";
    private static readonly string PlanWithoutSolution = """{"labels":["l1","l2"],"zones":[{"id":"z1"}]}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedQuestionAsync(int owner)
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        ctx.SetCurrentUser(owner);
        var q = new Question
        {
            Text = "Etiketleri yerleştir", Point = 5, SubjectId = subject.Id,
            InteractionType = DragDropType, InteractionPlan = PlanWithoutSolution, InteractionSolution = Solution,
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private static QuestionDto UpdateDto(int id, string? plan, string? explicitSolution = null) => new()
    {
        Id = id, Text = "Etiketleri yerleştir", Point = 5, InteractionType = DragDropType,
        InteractionPlan = plan, InteractionSolution = explicitSolution, Answers = new List<AnswerDto>(),
    };

    private static BulkQuestionDto Bulk(string name, string? plan, string? explicitSolution = null, int x = 0) => new()
    {
        Name = name, InteractionType = DragDropType, InteractionPlan = plan, InteractionSolution = explicitSolution,
        Answers = new List<BulkAnswerDto>(), X = x, Y = 0, Width = 10, Height = 10,
    };

    private static BulkQuestionCreateDto BulkRequest(params BulkQuestionDto[] questions) => new()
    {
        ImageData = TestImagesBase64(),
        Passages = new List<BulkPassageDto>(),
        Questions = questions.ToList(),
        Header = new HeaderInfo(),
    };

    private static string TestImagesBase64() =>
        "data:image/png;base64," + Convert.ToBase64String(TestImages.Png(100, 100));

    private async Task<(string? Plan, string? Solution)> ReadStoredAsync(int id)
    {
        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync(x => x.Id == id);
        return (q.InteractionPlan, q.InteractionSolution);
    }

    // ---- update (tekil kayıt) ----

    [Theory]
    [InlineData("""{"a":1,"a":2}""", null)]                                      // üst düzey yinelenen anahtar
    [InlineData("""{"zones":[{"id":"z1","id":"z2"}]}""", null)]                  // derin yinelenen anahtar (plan)
    [InlineData("""{"labels":["l1"]}""", """{"placements":[{"z":1,"z":2}]}""")]  // derin yinelenen anahtar (açık çözüm)
    [InlineData("{broken", null)]                                                // bozuk JSON
    [InlineData("""[1,2]""", null)]                                              // nesne değil
    [InlineData("""{"labels":["l1"],"solution":"abc"}""", null)]                 // plandan çözüm nesne değil
    public async Task CreateOrUpdate_StrictlyInvalidPlanOrSolution_ReturnsInvalidInputAndKeepsRow(string plan, string? explicitSolution)
    {
        var id = await SeedQuestionAsync(owner: 5);

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, plan, explicitSolution), actingUserId: 5);

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.Message.ShouldNotContain("duplicate", Case.Insensitive);
        result.Message.ShouldNotContain("JsonException");
        result.Message.ShouldNotBeNullOrWhiteSpace();

        (await ReadStoredAsync(id)).ShouldBe((PlanWithoutSolution, Solution));
    }

    [Fact]
    public async Task CreateOrUpdate_PlanOverMaxPlanLength_ReturnsInvalidInputAndKeepsRow()
    {
        var id = await SeedQuestionAsync(owner: 5);
        var oversized = "{\"x\":\"" + new string('a', InteractionPlanSolution.MaxPlanLength) + "\"}";

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, oversized), actingUserId: 5);

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        (await ReadStoredAsync(id)).ShouldBe((PlanWithoutSolution, Solution));
    }

    // ---- bulk ----

    [Fact]
    public async Task SaveBulk_DuplicateKeyInOneQuestion_FailsBeforeAnyUploadAndCreatesNothing()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(
                Bulk("iyi", PlanWithoutSolution, Solution, x: 0),
                Bulk("bozuk", """{"zones":[{"id":"z1","id":"z2"}]}""", x: 20)), actingUserId: 7);

        result.Success.ShouldBeFalse();
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default, default);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("""[1,2]""")]
    public async Task SaveBulk_InvalidOrNonObjectPlan_FailsBeforeAnyUploadAndCreatesNothing(string plan)
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(Bulk("bozuk", plan, x: 0)), actingUserId: 7);

        result.Success.ShouldBeFalse();
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default, default);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SaveBulk_NonObjectSolutionSplitOutOfPlan_FailsBeforeAnyUploadAndCreatesNothing()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(Bulk("bozuk", """{"labels":["a"],"solution":"abc"}""")),
                actingUserId: 7);

        result.Success.ShouldBeFalse();
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default, default);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SaveBulk_ValidQuestionsWithExplicitSolution_UploadsAndStoresStrippedPlan()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(
                Bulk("iyi", """{"labels":["a"],"solution":"abc"}""", Solution, x: 0)), actingUserId: 7);

        result.Success.ShouldBeTrue();
        await _minio.Received().UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync();
        q.InteractionPlan.ShouldBe("""{"labels":["a"]}""");
        q.InteractionSolution.ShouldBe(Solution);
    }
}
