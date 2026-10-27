using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #575: <see cref="QuestionService"/> kayıt yolunda <c>slotFill</c> tanım doğrulaması — şemaya uymayan plan ya da planla
/// tutarsız çözüm kayıttan (ve toplu kayıtta MinIO yüklemesinden) önce reddedilir; geçerli tanım çözümsüz plan + ayrı çözüm
/// olarak saklanır. Toplu kayıtta slotFill doğru şık istemez ve soru görseli (question-v2) kırpılmaz.
/// </summary>
public class QuestionServiceSlotFillTests : IDisposable
{
    private const string SlotType = "slotFill";

    private const string Plan =
        """{"version":1,"type":"slotFill","slots":[{"id":"s1","box":{"x":0,"y":0,"width":10,"height":3},"input":{"kind":"number","entry":"fraction"}},{"id":"s2","box":{"x":0,"y":4,"width":10,"height":3},"input":{"kind":"text"}}]}""";

    private const string Solution = """{"s1":{"value":"3/4","equivalence":true},"s2":{"accept":["İzmir"]}}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedQuestionAsync(string? plan = null, string? solution = null)
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Matematik" };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        ctx.SetCurrentUser(5);
        var q = new Question
        {
            Text = "Boşluğu doldur", Point = 5, SubjectId = subject.Id,
            InteractionType = SlotType, InteractionPlan = plan, InteractionSolution = solution,
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private static QuestionDto UpdateDto(int id, string? plan, string? solution, string type = SlotType) => new()
    {
        Id = id, Text = "Boşluğu doldur", Point = 5, InteractionType = type,
        InteractionPlan = plan, InteractionSolution = solution, Answers = new List<AnswerDto>(),
    };

    private static BulkQuestionDto Bulk(string name, string? plan, string? solution, int x = 0,
        List<BulkAnswerDto>? answers = null, string type = SlotType) => new()
    {
        Name = name, InteractionType = type, InteractionPlan = plan, InteractionSolution = solution,
        Answers = answers ?? new List<BulkAnswerDto>(), X = x, Y = 0, Width = 10, Height = 10,
    };

    private static BulkQuestionCreateDto BulkRequest(params BulkQuestionDto[] questions) => new()
    {
        ImageData = "data:image/png;base64," + Convert.ToBase64String(TestImages.Png(100, 100)),
        Passages = new List<BulkPassageDto>(),
        Questions = questions.ToList(),
        Header = new HeaderInfo(),
    };

    private async Task<(string? Plan, string? Solution)> ReadStoredAsync(int id)
    {
        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync(x => x.Id == id);
        return (q.InteractionPlan, q.InteractionSolution);
    }

    [Fact]
    public async Task CreateOrUpdate_ValidDefinition_IsStoredWithCanonicalType()
    {
        var id = await SeedQuestionAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, Plan, Solution, type: "SlotFill"), actingUserId: 5)).Success.ShouldBeTrue();

        (await ReadStoredAsync(id)).ShouldBe((Plan, Solution));
        await using var check = _db.NewContext();
        (await check.Questions.AsNoTracking().SingleAsync(x => x.Id == id)).InteractionType.ShouldBe(SlotType);
    }

    [Fact]
    public async Task CreateOrUpdate_LegacyPlanEmbeddingSolution_IsSplitAndValidated()
    {
        var id = await SeedQuestionAsync();
        var embedded = Plan[..^1] + ",\"solution\":" + Solution + "}";

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, embedded, null), actingUserId: 5)).Success.ShouldBeTrue();

        var (plan, solution) = await ReadStoredAsync(id);
        plan!.ShouldNotContain("solution");
        solution.ShouldBe(Solution);
    }

    [Fact]
    public async Task CreateOrUpdate_PlanWithoutSolution_IsStoredAsDraft()
    {
        var id = await SeedQuestionAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, Plan, null), actingUserId: 5)).Success.ShouldBeTrue();

        (await ReadStoredAsync(id)).ShouldBe((Plan, (string?)null));
    }

    [Theory]
    [InlineData(Plan, """{"s1":{"value":"3/4"}}""")]                                         // s2 çözümsüz
    [InlineData(Plan, """{"s1":{"value":"3/0"},"s2":{"accept":["İzmir"]}}""")]               // geçersiz sayı
    [InlineData(Plan, """{"s1":{"value":"3/4"},"s2":{"accept":["İzmir"]},"s9":{"accept":["x"]}}""")] // plan dışı kutu
    [InlineData(Plan, """{"s1":{"value":"3/4","secret":1},"s2":{"accept":["İzmir"]}}""")]    // bilinmeyen alan
    [InlineData("""{"version":1,"type":"slotFill","slots":[{"id":"s1","input":{"kind":"text"}}]}""", null)] // kutusuz
    [InlineData("""{"version":1,"type":"slotFill","slots":[{"id":"s1","box":{"x":0,"y":0,"width":1,"height":1},"input":{"kind":"text","hint":"İzmir"}}]}""", null)] // girdide gizli alan
    [InlineData(null, Solution)]                                                                // plansız
    public async Task CreateOrUpdate_InvalidDefinition_ReturnsInvalidInputAndKeepsRow(string? plan, string? solution)
    {
        var id = await SeedQuestionAsync(Plan, Solution);

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, plan, solution), actingUserId: 5);

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.Message.ShouldNotBeNullOrWhiteSpace();
        result.Message.ShouldNotBe("questions.interactionDefinition.invalid");
        (await ReadStoredAsync(id)).ShouldBe((Plan, Solution));
    }

    [Fact]
    public async Task CreateOrUpdate_PlanChangeLeavingExistingSolutionInconsistent_IsRejected()
    {
        var id = await SeedQuestionAsync(Plan, Solution);
        const string shrunk = """{"version":1,"type":"slotFill","slots":[{"id":"s1","box":{"x":0,"y":0,"width":10,"height":3},"input":{"kind":"number"}}]}""";

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, shrunk, null), actingUserId: 5);

        result.InvalidInput.ShouldBeTrue();
        (await ReadStoredAsync(id)).ShouldBe((Plan, Solution));
    }

    [Fact]
    public async Task SaveBulk_InvalidDefinition_FailsBeforeAnyUploadAndCreatesNothing()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(
                Bulk("iyi", Plan, Solution, x: 0),
                Bulk("bozuk", Plan, """{"s1":{"value":"3/4"}}""", x: 20)), actingUserId: 7);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("bozuk");
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default!, default!);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SaveBulk_ValidQuestion_NeedsNoCorrectAnswerAndKeepsFullHeight()
    {
        var answers = new List<BulkAnswerDto> { new() { Label = "x", X = 0, Y = 5, Width = 5, Height = 3, IsCorrect = false } };

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(Bulk("kutu", Plan, Solution, answers: answers, type: "SlotFill")), actingUserId: 7);

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync();
        q.InteractionType.ShouldBe(SlotType);
        q.InteractionPlan.ShouldBe(Plan);
        q.InteractionSolution.ShouldBe(Solution);
        q.SanitizedHeight.ShouldBe(10);
        q.CorrectAnswerId.ShouldBeNull();
    }
}
