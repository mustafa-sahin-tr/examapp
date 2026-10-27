using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #576: <see cref="QuestionService"/> kayıt yolunda <c>dragPlace</c> / <c>match</c> tanım doğrulaması — şemaya uymayan
/// plan ya da planla tutarsız çözüm kayıttan (ve toplu kayıtta MinIO yüklemesinden) önce reddedilir; geçerli tanım çözümsüz plan
/// + ayrı çözüm ve kanonik tip olarak saklanır. Toplu kayıtta doğru şık istenmez, soru görseli kırpılmaz.
/// </summary>
public class QuestionServiceDragPlaceMatchTests : IDisposable
{
    public const string DragPlan =
        """{"version":1,"type":"dragPlace","mode":"place","reuse":false,"targets":[{"id":"t1","label":"Erime","capacity":2},{"id":"t2","label":"Donma","capacity":2}],"cards":[{"id":"c1","label":"Buz"},{"id":"c2","box":{"x":0,"y":0,"width":5,"height":5}},{"id":"c3","label":"Çeldirici"}]}""";

    public const string DragSolution = """{"t1":["c1"],"t2":["c2"]}""";

    public const string MatchPlan =
        """{"version":1,"type":"match","left":[{"id":"l1","label":"Kalem"},{"id":"l2","label":"Top"}],"right":[{"id":"r1","label":"Uzun"},{"id":"r2","label":"Yuvarlak"},{"id":"r3","label":"Kare"}],"prelinked":[{"leftId":"l1","rightId":"r1"}]}""";

    public const string MatchSolution = """{"l2":"r2"}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedQuestionAsync(string type, string? plan = null, string? solution = null)
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        ctx.SetCurrentUser(5);
        var q = new Question
        {
            Text = "Etkinlik", Point = 5, SubjectId = subject.Id,
            InteractionType = type, InteractionPlan = plan, InteractionSolution = solution,
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private static QuestionDto UpdateDto(int id, string type, string? plan, string? solution) => new()
    {
        Id = id, Text = "Etkinlik", Point = 5, InteractionType = type,
        InteractionPlan = plan, InteractionSolution = solution, Answers = new List<AnswerDto>(),
    };

    private static BulkQuestionDto Bulk(string name, string type, string? plan, string? solution, int x = 0,
        List<BulkAnswerDto>? answers = null) => new()
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

    private async Task<Question> ReadAsync(int id)
    {
        await using var check = _db.NewContext();
        return await check.Questions.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    [Theory]
    [InlineData("DragPlace", "dragPlace", DragPlan, DragSolution)]
    [InlineData("MATCH", "match", MatchPlan, MatchSolution)]
    public async Task CreateOrUpdate_ValidDefinition_IsStoredWithCanonicalType(string sentType, string canonical, string plan, string solution)
    {
        var id = await SeedQuestionAsync(canonical);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, sentType, plan, solution), actingUserId: 5)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        q.InteractionType.ShouldBe(canonical);
        AssertScrambled(canonical, plan, solution, q.InteractionPlan, q.InteractionSolution);
    }

    private static void AssertScrambled(string type, string authoredPlan, string? authoredSolution, string? storedPlan, string? storedSolution)
        => InteractionScrambleAssert.Scrambled(type, authoredPlan, authoredSolution, storedPlan, storedSolution);

    [Theory]
    [InlineData("dragPlace", DragPlan, DragSolution)]
    [InlineData("match", MatchPlan, MatchSolution)]
    public async Task CreateOrUpdate_SameStoredDefinitionResaved_IsNotReshuffled(string type, string plan, string solution)
    {
        var id = await SeedQuestionAsync(type);
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, type, plan, solution), actingUserId: 5)).Success.ShouldBeTrue();
        var first = await ReadAsync(id);

        // İstemci saklanan (karışık) tanımı aynen geri gönderir → dokunulmaz (kayıtlı cevapların id'leri geçerli kalır).
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, type, first.InteractionPlan, first.InteractionSolution), actingUserId: 5)).Success.ShouldBeTrue();
        var second = await ReadAsync(id);
        (second.InteractionPlan, second.InteractionSolution).ShouldBe((first.InteractionPlan, first.InteractionSolution));

        // Tanım değişince (yazarlık id'leriyle yeniden) tekrar karıştırılır.
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, type, plan, solution), actingUserId: 5)).Success.ShouldBeTrue();
        var third = await ReadAsync(id);
        AssertScrambled(type, plan, solution, third.InteractionPlan, third.InteractionSolution);
    }

    [Theory]
    [InlineData("dragPlace", DragPlan)]
    [InlineData("match", MatchPlan)]
    public async Task CreateOrUpdate_LegacyPlanEmbeddingSolution_IsSplitAndValidated(string type, string plan)
    {
        var solution = type == "match" ? MatchSolution : DragSolution;
        var id = await SeedQuestionAsync(type);
        var embedded = plan[..^1] + ",\"solution\":" + solution + "}";

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, type, embedded, null), actingUserId: 5)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        q.InteractionPlan!.ShouldNotContain("solution");
        AssertScrambled(type, plan, solution, q.InteractionPlan, q.InteractionSolution);
    }

    [Theory]
    [InlineData("dragPlace", DragPlan, """{"t1":["c1"]}""")]                                 // t2 çözümsüz
    [InlineData("dragPlace", DragPlan, """{"t1":["c1","c2","c3"],"t2":[]}""")]               // kapasite aşımı
    [InlineData("dragPlace", DragPlan, """{"t1":["c1"],"t2":["c1"]}""")]                     // tekrar kullanım kapalı
    [InlineData("dragPlace", """{"version":1,"type":"dragPlace","targets":[{"id":"t1","solution":["c1"]}],"cards":[{"id":"c1","label":"a"}]}""", null)] // gizli alan
    [InlineData("dragPlace", """{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1"}]}""", null)] // yüzsüz kart
    [InlineData("match", MatchPlan, """{"l1":"r1","l2":"r2"}""")]                            // örnek çift çözümde
    [InlineData("match", MatchPlan, """{"l2":"r1"}""")]                                      // örnek sağ
    [InlineData("match", MatchPlan, """{}""")]                                               // l2 çözümsüz
    [InlineData("match", """{"version":1,"type":"match","left":[{"id":"l1","label":"a","right":"r1"}],"right":[{"id":"r1","label":"b"}]}""", null)] // gizli alan
    [InlineData("match", null, MatchSolution)]                                               // plansız
    public async Task CreateOrUpdate_InvalidDefinition_ReturnsInvalidInputAndKeepsRow(string type, string? plan, string? solution)
    {
        var (seedPlan, seedSolution) = type == "match" ? (MatchPlan, MatchSolution) : (DragPlan, DragSolution);
        var id = await SeedQuestionAsync(type, seedPlan, seedSolution);

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, type, plan, solution), actingUserId: 5);

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.Message.ShouldNotBeNullOrWhiteSpace();
        var q = await ReadAsync(id);
        (q.InteractionPlan, q.InteractionSolution).ShouldBe((seedPlan, seedSolution));
    }

    [Fact]
    public async Task SaveBulk_InvalidDefinition_FailsBeforeAnyUploadAndCreatesNothing()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(
                Bulk("iyi", "dragPlace", DragPlan, DragSolution, x: 0),
                Bulk("bozuk", "match", MatchPlan, """{"l2":"r1"}""", x: 20)), actingUserId: 7);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("bozuk");
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default!, default!);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SaveBulk_ValidQuestions_NeedNoCorrectAnswerAndKeepFullHeight()
    {
        var answers = new List<BulkAnswerDto> { new() { Label = "x", X = 0, Y = 5, Width = 5, Height = 3, IsCorrect = false } };

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(BulkRequest(
                Bulk("yerleştir", "DragPlace", DragPlan, DragSolution, x: 0, answers: answers),
                Bulk("eşleştir", "Match", MatchPlan, MatchSolution, x: 20, answers: answers)), actingUserId: 7);

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var qs = await check.Questions.AsNoTracking().OrderBy(q => q.Id).ToListAsync();
        qs.Select(q => q.InteractionType).ShouldBe(new[] { "dragPlace", "match" });
        AssertScrambled("dragPlace", DragPlan, DragSolution, qs[0].InteractionPlan, qs[0].InteractionSolution);
        AssertScrambled("match", MatchPlan, MatchSolution, qs[1].InteractionPlan, qs[1].InteractionSolution);
        qs.ShouldAllBe(q => q.SanitizedHeight == 10 && q.CorrectAnswerId == null);
    }
}

/// <summary>review #576: kayıtta karıştırılmış dragPlace / match tanımı için ortak doğrulama.</summary>
internal static class InteractionScrambleAssert
{
    /// <summary>
    /// review #576 (H1/M1): saklanan tanım geçerli, kartlar / sağ öğeler opak id'li (yazarlık id'leri yok) ve aynı etiketler aynı
    /// hedefe / sola eşli (çözüm anlamı korunur).
    /// </summary>
    internal static void Scrambled(string type, string authoredPlan, string? authoredSolution, string? storedPlan, string? storedSolution)
    {
        storedPlan.ShouldNotBeNull();
        storedPlan.ShouldNotBe(authoredPlan);
        InteractionGraderRegistry.Default.Resolve(type).ValidateDefinition(storedPlan, storedSolution).ShouldBeTrue();
        var listKey = type == "match" ? "right" : "cards";
        static Dictionary<string, string> Labels(string plan, string key) =>
            System.Text.Json.Nodes.JsonNode.Parse(plan)![key]!.AsArray()
                .ToDictionary(n => n!["id"]!.GetValue<string>(), n => n!["label"]?.GetValue<string>() ?? n!["box"]!.ToJsonString());
        var before = Labels(authoredPlan, listKey);
        var after = Labels(storedPlan!, listKey);
        after.Keys.ShouldAllBe(k => System.Text.RegularExpressions.Regex.IsMatch(k, "^[a-z0-9]{8}$"));
        after.Values.OrderBy(v => v, StringComparer.Ordinal).ShouldBe(before.Values.OrderBy(v => v, StringComparer.Ordinal));
        if (authoredSolution == null)
        {
            storedSolution.ShouldBeNull();
            return;
        }
        // Çözümü etiketlere çevir: önce ve sonra aynı.
        string Canon(string solution, Dictionary<string, string> labels)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(solution)!.AsObject();
            return string.Join("|", root.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + (p.Value is System.Text.Json.Nodes.JsonArray a
                ? string.Join(",", a.Select(x => labels[x!.GetValue<string>()]).OrderBy(x => x, StringComparer.Ordinal))
                : labels[p.Value!.GetValue<string>()])));
        }
        Canon(storedSolution!, after).ShouldBe(Canon(authoredSolution, before));
    }
}
