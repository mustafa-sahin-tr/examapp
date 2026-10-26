using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #574: <see cref="QuestionService"/> kayıt yolunda <c>matrixChoice</c> tanım doğrulaması — şemaya uymayan plan ya da
/// planla tutarsız çözüm kayıttan (ve toplu kayıtta MinIO yüklemesinden) önce reddedilir; geçerli tanım çözümsüz plan + ayrı
/// çözüm olarak saklanır. Toplu kayıtta matrixChoice doğru şık istemez ve soru görseli (question-v2) kırpılmaz.
/// </summary>
public class QuestionServiceMatrixChoiceTests : IDisposable
{
    private const string MatrixType = "matrixChoice";

    private const string Plan =
        """{"version":1,"type":"matrixChoice","multiPerRow":false,"columns":[{"id":"cD","label":"D"},{"id":"cY","label":"Y"}],"rows":[{"id":"r1","box":{"x":0,"y":0,"width":10,"height":3}},{"id":"r2","box":{"x":0,"y":4,"width":10,"height":3}}]}""";

    private const string Solution = """{"r1":["cD"],"r2":["cY"]}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedQuestionAsync(string? plan = null, string? solution = null)
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        ctx.SetCurrentUser(5);
        var q = new Question
        {
            Text = "Doğru/yanlış", Point = 5, SubjectId = subject.Id,
            InteractionType = MatrixType, InteractionPlan = plan, InteractionSolution = solution,
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private static QuestionDto UpdateDto(int id, string? plan, string? solution, string type = MatrixType) => new()
    {
        Id = id, Text = "Doğru/yanlış", Point = 5, InteractionType = type,
        InteractionPlan = plan, InteractionSolution = solution, Answers = new List<AnswerDto>(),
    };

    private static BulkQuestionDto Bulk(string name, string? plan, string? solution, int x = 0,
        List<BulkAnswerDto>? answers = null, string type = MatrixType) => new()
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

    // ---- update (tekil kayıt) ----

    [Fact]
    public async Task CreateOrUpdate_ValidMatrixPlanAndSolution_IsStored()
    {
        var id = await SeedQuestionAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, Plan, Solution), actingUserId: 5)).Success.ShouldBeTrue();

        (await ReadStoredAsync(id)).ShouldBe((Plan, Solution));
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
    [InlineData(Plan, """{"r1":["cD"]}""")]                          // r2 çözümsüz
    [InlineData(Plan, """{"r1":["cD","cY"],"r2":["cY"]}""")]         // tek seçimli satırda iki kolon
    [InlineData(Plan, """{"r1":["cX"],"r2":["cY"]}""")]              // plan dışı kolon
    [InlineData(Plan, """{"r1":["cD"],"r2":["cY"],"r9":["cD"]}""")]  // plan dışı satır
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"cD"}],"rows":[{"id":"r1","box":{"x":0,"y":0,"width":1,"height":1}}]}""", null)] // tek kolon
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"cD"},{"id":"cY"}],"rows":[{"id":"r1"}]}""", null)] // kutusuz satır
    [InlineData(null, Solution)]                                      // plansız
    public async Task CreateOrUpdate_InvalidMatrixDefinition_ReturnsInvalidInputAndKeepsRow(string? plan, string? solution)
    {
        var id = await SeedQuestionAsync(Plan, Solution);

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, plan, solution), actingUserId: 5);

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.Message.ShouldNotBeNullOrWhiteSpace();
        result.Message.ShouldNotBe("questions.interactionDefinition.invalid"); // anahtar değil, çevrilmiş mesaj
        (await ReadStoredAsync(id)).ShouldBe((Plan, Solution));
    }

    [Fact]
    public async Task CreateOrUpdate_PlanChangeLeavingExistingSolutionInconsistent_IsRejected()
    {
        // Mevcut çözüm r1+r2'yi kapsıyor; yeni plan r2'yi siliyor ve çözüm gönderilmiyor → eski çözüm planla tutarsız.
        var id = await SeedQuestionAsync(Plan, Solution);
        const string shrunk = """{"version":1,"type":"matrixChoice","columns":[{"id":"cD"},{"id":"cY"}],"rows":[{"id":"r1","box":{"x":0,"y":0,"width":10,"height":3}}]}""";

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, shrunk, null), actingUserId: 5);

        result.InvalidInput.ShouldBeTrue();
        (await ReadStoredAsync(id)).ShouldBe((Plan, Solution));
    }

    [Fact]
    public async Task CreateOrUpdate_DragDropTypeIsNotSubjectToMatrixRules()
    {
        // Diğer tiplerin kayıt davranışı değişmez (ValidateDefinition varsayılanı true).
        var id = await SeedQuestionAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(
                UpdateDto(id, """{"labels":["l1"]}""", """{"placements":[]}""", type: "dragDropLabeling"), actingUserId: 5))
                .Success.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateOrUpdate_NonCanonicalTypeCasing_IsStoredCanonically()
    {
        var id = await SeedQuestionAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, Plan, Solution, type: "MatrixChoice"), actingUserId: 5))
                .Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Questions.AsNoTracking().SingleAsync(x => x.Id == id)).InteractionType.ShouldBe(MatrixType);
    }

    [Fact]
    public async Task CreateOrUpdate_PlanWithUnknownField_ReturnsInvalidInputAndKeepsRow()
    {
        var id = await SeedQuestionAsync(Plan, Solution);
        var smuggled = Plan.Replace("\"label\":\"D\"", "\"label\":\"D\",\"hint\":\"cevap D\"");
        smuggled.ShouldNotBe(Plan);

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, smuggled, Solution), actingUserId: 5);

        result.InvalidInput.ShouldBeTrue();
        (await ReadStoredAsync(id)).ShouldBe((Plan, Solution));
    }

    // ---- bulk ----

    [Fact]
    public async Task SaveBulk_InvalidMatrixDefinition_FailsBeforeAnyUploadAndCreatesNothing()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(
                Bulk("iyi", Plan, Solution, x: 0),
                Bulk("bozuk", Plan, """{"r1":["cD"]}""", x: 20)), actingUserId: 7);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("bozuk");
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default!, default!);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SaveBulk_ValidMatrixQuestion_NeedsNoCorrectAnswerAndKeepsFullHeight()
    {
        // Şık kutusu (y=5) MCQ'da question-v2'yi 5 px'e kırpardı; matrixChoice'ta hedefler görselin içinde, tam yükseklik kalır.
        var answers = new List<BulkAnswerDto> { new() { Label = "x", X = 0, Y = 5, Width = 5, Height = 3, IsCorrect = false } };

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(Bulk("matris", Plan, Solution, answers: answers, type: "MatrixChoice")), actingUserId: 7);

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync();
        q.InteractionType.ShouldBe(MatrixType);
        q.InteractionPlan.ShouldBe(Plan);
        q.InteractionSolution.ShouldBe(Solution);
        q.SanitizedHeight.ShouldBe(10);
        q.CorrectAnswerId.ShouldBeNull();
    }

    [Fact]
    public async Task SaveBulk_McqWithoutCorrectAnswer_StillFails()
    {
        // Regresyon: "etkileşimli" kararı yalnız MCQ dışı kontrolcüsü olan tipler için; MCQ'da doğru şık hâlâ zorunlu.
        var answers = new List<BulkAnswerDto> { new() { Label = "A", X = 0, Y = 5, Width = 5, Height = 3, IsCorrect = false } };

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(Bulk("mcq", null, null, answers: answers, type: "mcq")), actingUserId: 7);

        result.Success.ShouldBeFalse();
    }
}
