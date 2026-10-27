using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #582: <c>textSelect</c> etkinliği uçtan uca, üç modun pilot biçimiyle (Türkçe Ustası yazım yanlışı bul — select,
/// noktalama boşlukları — punctuation, yanlışı düzelterek yaz — correct): tanımlar Claude çıkarımıyla aynı tokenizer'dan geçer ve
/// kayıt doğrulamasından geçer; öğrenci token seçerek çözer, "Cevapları kontrol et" bulunan / eksik / fazla sayar. Geçersiz cevap
/// 400 olup hiçbir şey yazmaz; çözüm kontrol/oynatıcı yanıtlarında gitmez, yalnız "Doğru cevapları göster"den sonra döner.
/// </summary>
public class TestSessionServiceTextSelectActivityTests : IDisposable
{
    private const int OwnerUserId = 7582;

    private static string PlanOf(string mode, string extra, params string[] lines)
    {
        var tokens = TextSelectTokenizer.Tokenize(lines.Select((text, i) =>
            new TextSelectTokenizer.Line(text, new TextSelectTokenizer.TokenBox(0, i * 20, text.Length * 10, 18))).ToList());
        var json = tokens.Select(t => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = t.Id, ["text"] = t.Text, ["line"] = t.Line,
            ["boxes"] = t.Boxes.Select(b => new { x = b.X, y = b.Y, width = b.Width, height = b.Height }).ToArray(),
        }.Concat(t.Gap ? [new KeyValuePair<string, object>("gap", true)] : []).ToDictionary()));
        return "{\"version\":1,\"type\":\"textSelect\",\"mode\":\"" + mode + "\"" + extra + ",\"tokens\":[" + string.Join(",", json) + "]}";
    }

    // Select: "Sonun da eve geldi. Gökte ki bulutlar dolaş-" / "mış yatak ta." → hedefler t1+t2, t5+t6, t9+t10.
    private static readonly string SelectPlan = PlanOf("select", ",\"style\":\"circle\"",
        "Sonun da eve geldi. Gökte ki bulutlar dolaş-", "mış yatak ta.");

    private const string SelectSolution =
        """{"targets":[{"tokenIds":["t1","t2"]},{"tokenIds":["t5","t6"]},{"tokenIds":["t9","t10"]}]}""";

    // Punctuation: "Eyvah ( ) Ne yaptın ( )" / "Yarın gel ( )".
    private static readonly string PunctuationPlan = PlanOf("punctuation", ",\"palette\":[\".\",\",\",\"!\",\"?\"]",
        "Eyvah ( ) Ne yaptın ( )", "Yarın gel ( )");

    private const string PunctuationSolution = """{"gaps":{"g1":{"accept":["!"]},"g2":{"accept":["?"]},"g3":{"accept":["."]}}}""";

    // Correct: "Herkez buraya geldi." → t1 "Herkez" düzeltmesi "Herkes".
    private static readonly string CorrectPlan = PlanOf("correct", "", "Herkez buraya geldi.");

    private const string CorrectSolution = """{"targets":[{"tokenIds":["t1"],"accept":["Herkes"]}]}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record Seeded(int InstanceId, int SelectRow, int PunctuationRow, int CorrectRow);

    private async Task<Seeded> SeedAsync(bool withSolutions = true)
    {
        var grader = new TextSelectGrader();
        grader.ValidateDefinition(SelectPlan, SelectSolution).ShouldBeTrue();
        grader.ValidateDefinition(PunctuationPlan, PunctuationSolution).ShouldBeTrue();
        grader.ValidateDefinition(CorrectPlan, CorrectSolution).ShouldBeTrue();

        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = OwnerUserId, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Türkçe pilot", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, Kind = WorksheetKind.Activity };
        Question Q(string text, string plan, string solution) => new()
        {
            Text = text, Point = 10, InteractionType = TextSelectGrader.Type, InteractionPlan = plan,
            InteractionSolution = withSolutions ? solution : null,
        };
        var questions = new[]
        {
            Q("Yazım yanlışlarını bul", SelectPlan, SelectSolution),
            Q("Noktalama", PunctuationPlan, PunctuationSolution),
            Q("Düzelterek yaz", CorrectPlan, CorrectSolution),
        };
        ctx.AddRange(student, worksheet);
        ctx.Questions.AddRange(questions);
        await ctx.SaveChangesAsync();

        var wqs = questions.Select((q, i) => new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q.Id, Order = i + 1 }).ToList();
        ctx.TestQuestions.AddRange(wqs);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Started,
            StartTime = DateTime.UtcNow.AddMinutes(-5), MaxDurationSeconds = 600,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var rows = wqs.Select(wq => new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id }).ToList();
        ctx.TestInstanceQuestions.AddRange(rows);
        await ctx.SaveChangesAsync();
        return new Seeded(instance.Id, rows[0].Id, rows[1].Id, rows[2].Id);
    }

    private static string Payload(string[] selected, Dictionary<string, string>? values = null)
        => JsonSerializer.Serialize(new { version = 1, type = "textSelect", selected, values = values ?? new Dictionary<string, string>() });

    private static ActivityCheckRequestDto Request(params (int RowId, string? Payload)[] answers) => new()
    {
        Answers = answers.Select(a => new ActivityCheckAnswerDto { InstanceQuestionId = a.RowId, AnswerPayload = a.Payload }).ToList(),
    };

    private async Task<ActivityCheckResultDto> CheckAsync(int instanceId, ActivityCheckRequestDto request)
    {
        await using var ctx = _db.NewContext();
        return await new TestSessionService(ctx).CheckActivityAsync(instanceId, request, OwnerUserId);
    }

    private async Task<WorksheetInstanceQuestion> RowAsync(int rowId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstanceQuestions.AsNoTracking().SingleAsync(x => x.Id == rowId);
    }

    [Fact]
    public void SelectPilot_HyphenatedWordIsOneTokenWithTwoBoxes()
    {
        using var doc = JsonDocument.Parse(SelectPlan);
        var dolasmis = doc.RootElement.GetProperty("tokens").EnumerateArray().Single(t => t.GetProperty("text").GetString() == "dolaşmış");
        dolasmis.GetProperty("boxes").GetArrayLength().ShouldBe(2);
        dolasmis.GetProperty("line").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task CheckActivity_ThreeModesFullyCorrect_AreCorrectAndStored()
    {
        var s = await SeedAsync();
        var select = Payload(["t2", "t5", "t10"]);
        var punctuation = Payload([], new() { ["g1"] = "!", ["g2"] = "?", ["g3"] = " . " });
        var correct = Payload(["t1"], new() { ["t1"] = "HERKES" });

        var result = await CheckAsync(s.InstanceId, Request((s.SelectRow, select), (s.PunctuationRow, punctuation), (s.CorrectRow, correct)));

        result.Success.ShouldBeTrue(result.Message);
        result.Questions.Select(q => (q.InstanceQuestionId, q.IsCorrect, q.CorrectCount, q.TotalCount))
            .ShouldBe(new[] { (s.SelectRow, true, 3, 3), (s.PunctuationRow, true, 3, 3), (s.CorrectRow, true, 1, 1) }, ignoreOrder: true);
        (await RowAsync(s.SelectRow)).AnswerPayload.ShouldBe(select);
    }

    [Fact]
    public async Task CheckActivity_SelectPartial_ReportsFoundMissingAndExtraWithoutRevealingTargets()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request((s.SelectRow, Payload(["t1", "t3"]))));

        result.Success.ShouldBeTrue(result.Message);
        var q = result.Questions.Single();
        (q.IsCorrect, q.CorrectCount, q.TotalCount).ShouldBe((false, 1, 4));
        using var doc = JsonDocument.Parse(q.Feedback!);
        var root = doc.RootElement;
        root.GetProperty("rows").EnumerateArray().Select(r => (r.GetProperty("id").GetString(), r.GetProperty("correct").GetBoolean()))
            .ShouldBe([("t1", true), ("t3", false)]);
        (root.GetProperty("found").GetInt32(), root.GetProperty("missing").GetInt32(), root.GetProperty("extra").GetInt32())
            .ShouldBe((1, 2, 1));
        var json = JsonSerializer.Serialize(result);
        foreach (var secret in new[] { "t5", "t6", "t9", "t10", "targets", "tokenIds" })
            json.ShouldNotContain(secret);
    }

    [Theory]
    [InlineData("""{"version":1,"type":"textSelect","selected":["t99"]}""")]                // plan dışı token
    [InlineData("""{"version":1,"type":"textSelect","selected":["t1"],"values":{"t1":"x"}}""")] // select'te değer
    [InlineData("""{"version":1,"type":"slotFill","values":{}}""")]                         // başka tipin payload'u
    public async Task CheckActivity_InvalidPayload_IsInvalidInputAndWritesNothing(string payload)
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request((s.SelectRow, payload)));

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        (await RowAsync(s.SelectRow)).AnswerPayload.ShouldBeNull();
    }

    [Fact]
    public async Task CheckActivity_EmptySelection_IsStoredAsBlank()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request((s.PunctuationRow, Payload([], new() { ["g1"] = " " }))));

        result.Success.ShouldBeTrue();
        (await RowAsync(s.PunctuationRow)).AnswerPayload.ShouldBeNull();
        (result.Questions.Single().CorrectCount, result.Questions.Single().TotalCount).ShouldBe((0, 3));
    }

    [Fact]
    public async Task CheckActivity_QuestionWithoutSolution_IsUngradable()
    {
        var s = await SeedAsync(withSolutions: false);

        var result = await CheckAsync(s.InstanceId, Request((s.SelectRow, Payload(["t1"]))));

        result.Success.ShouldBeTrue();
        var q = result.Questions.Single();
        (q.IsCorrect, q.TotalCount).ShouldBe((false, 0));
        q.Feedback.ShouldBeNull();
    }

    [Fact]
    public async Task GetActivitySolutions_AfterCheck_ReturnsTheSolution()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var early = await new TestSessionService(ctx).GetActivitySolutionsAsync(s.InstanceId, OwnerUserId);
            early.ErrorCode.ShouldBe(TestSessionErrorCodes.ActivityCheckRequired);
        }

        (await CheckAsync(s.InstanceId, Request((s.CorrectRow, Payload(["t1"], new() { ["t1"] = "Herkez" }))))).Success.ShouldBeTrue();

        await using var ctx2 = _db.NewContext();
        var result = await new TestSessionService(ctx2).GetActivitySolutionsAsync(s.InstanceId, OwnerUserId);
        result.Success.ShouldBeTrue();
        result.Questions.Single(q => q.InstanceQuestionId == s.CorrectRow).InteractionSolution.ShouldBe(CorrectSolution);
    }
}
