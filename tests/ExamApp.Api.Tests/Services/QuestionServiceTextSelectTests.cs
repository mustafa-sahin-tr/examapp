using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #582: <see cref="QuestionService"/> kayıt yolunda <c>textSelect</c> tanım doğrulaması — şemaya uymayan plan ya da planla
/// tutarsız çözüm kayıttan (ve toplu kayıtta MinIO yüklemesinden) önce reddedilir; geçerli tanım çözümsüz plan + ayrı çözüm
/// olarak, token sırası korunarak saklanır. Toplu kayıtta textSelect doğru şık istemez ve soru görseli kırpılmaz.
/// </summary>
public class QuestionServiceTextSelectTests : IDisposable
{
    private const string TextType = "textSelect";

    private const string Plan =
        """{"version":1,"type":"textSelect","mode":"correct","tokens":[{"id":"t1","text":"Sonun","line":0,"boxes":[{"x":0,"y":0,"width":50,"height":10}]},{"id":"t2","text":"da","line":0,"boxes":[{"x":60,"y":0,"width":20,"height":10}]},{"id":"t3","text":"geldi.","line":1,"boxes":[{"x":0,"y":20,"width":60,"height":10}]}]}""";

    private const string Solution = """{"targets":[{"tokenIds":["t1","t2"],"accept":["Sonunda"]}]}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedQuestionAsync(string? plan = null, string? solution = null)
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Türkçe" };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        ctx.SetCurrentUser(5);
        var q = new Question
        {
            Text = "Yanlışı bul", Point = 5, SubjectId = subject.Id,
            InteractionType = TextType, InteractionPlan = plan, InteractionSolution = solution,
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private static QuestionDto UpdateDto(int id, string? plan, string? solution, string type = TextType) => new()
    {
        Id = id, Text = "Yanlışı bul", Point = 5, InteractionType = type,
        InteractionPlan = plan, InteractionSolution = solution, Answers = new List<AnswerDto>(),
    };

    private static BulkQuestionDto Bulk(string name, string? plan, string? solution, int x = 0,
        List<BulkAnswerDto>? answers = null, string type = TextType) => new()
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
    public async Task CreateOrUpdate_ValidDefinition_IsStoredUnchangedWithCanonicalType()
    {
        var id = await SeedQuestionAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, Plan, Solution, type: "TextSelect"), actingUserId: 5)).Success.ShouldBeTrue();

        (await ReadStoredAsync(id)).ShouldBe((Plan, Solution));    // token sırası/id'leri korunur (karıştırma yok)
        await using var check = _db.NewContext();
        (await check.Questions.AsNoTracking().SingleAsync(x => x.Id == id)).InteractionType.ShouldBe(TextType);
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
    [InlineData(Plan, """{"targets":[{"tokenIds":["t1","t2"]}]}""")]                              // correct'te kabul yok
    [InlineData(Plan, """{"targets":[{"tokenIds":["t9"],"accept":["x"]}]}""")]                    // plan dışı token
    [InlineData(Plan, """{"targets":[{"tokenIds":["t1"],"accept":["x"],"secret":1}]}""")]         // bilinmeyen alan
    [InlineData("""{"version":1,"type":"textSelect","mode":"correct","tokens":[{"id":"t1","text":"a","line":0}]}""", null)] // kutusuz
    [InlineData("""{"version":1,"type":"textSelect","mode":"select","tokens":[{"id":"t1","text":"a","line":0,"boxes":[{"x":0,"y":0,"width":5,"height":5}],"target":true}]}""", null)] // gizli alan
    [InlineData(null, Solution)]                                                                    // plansız
    public async Task CreateOrUpdate_InvalidDefinition_ReturnsInvalidInputAndKeepsRow(string? plan, string? solution)
    {
        var id = await SeedQuestionAsync(Plan, Solution);

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, plan, solution), actingUserId: 5);

        result.Success.ShouldBeFalse();
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
                Bulk("bozuk", Plan, """{"targets":[{"tokenIds":["t3"]}]}""", x: 20)), actingUserId: 7);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("bozuk");
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default!, default!);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SaveBulk_ValidQuestion_NeedsNoCorrectAnswerAndKeepsFullHeight()
    {
        var answers = new List<BulkAnswerDto> { new() { Label = "x", X = 0, Y = 5, Width = 5, Height = 3, IsCorrect = false } };

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(Bulk("metin", Plan, Solution, answers: answers, type: "TextSelect")), actingUserId: 7);

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync();
        q.InteractionType.ShouldBe(TextType);
        q.InteractionPlan.ShouldBe(Plan);
        q.InteractionSolution.ShouldBe(Solution);
        q.SanitizedHeight.ShouldBe(10);
        q.CorrectAnswerId.ShouldBeNull();
    }
}
