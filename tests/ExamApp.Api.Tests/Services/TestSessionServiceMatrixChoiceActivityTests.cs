using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #574: <c>matrixChoice</c> etkinliği uçtan uca — "Cevapları kontrol et" satır bazlı sonuç (plan sırası, cevapsız satır
/// yanlış), cevabın saklanması/normalize edilmesi, geçersiz seçimin 400 olup hiçbir şey yazmaması, çözümün öğrenciye kontrol ve
/// oynatıcı yanıtlarında gitmemesi (#570) ve yalnız "Doğru cevapları göster"den sonra dönmesi.
/// </summary>
public class TestSessionServiceMatrixChoiceActivityTests : IDisposable
{
    private const int OwnerUserId = 7401;

    // Kolon id'leri bilinçli olarak ayırt edilebilir: kontrol yanıtında hiçbiri görünmemeli.
    private const string Plan =
        """{"version":1,"type":"matrixChoice","multiPerRow":false,"columns":[{"id":"COL-TRUE-7","label":"D"},{"id":"COL-FALSE-8","label":"Y"}],"rows":[{"id":"r1","label":"Güneş bir yıldızdır.","box":{"x":0,"y":0,"width":10,"height":3}},{"id":"r2","box":{"x":0,"y":4,"width":10,"height":3}},{"id":"r3","box":{"x":0,"y":8,"width":10,"height":3}}]}""";

    private const string Solution = """{"r1":["COL-TRUE-7"],"r2":["COL-FALSE-8"],"r3":["COL-TRUE-7"]}""";

    private const string FullPayload =
        """{"version":1,"type":"matrixChoice","selections":{"r1":["COL-TRUE-7"],"r2":["COL-FALSE-8"],"r3":["COL-TRUE-7"]}}""";

    private const string PartialPayload =
        """{"version":1,"type":"matrixChoice","selections":{"r1":["COL-TRUE-7"],"r2":["COL-TRUE-7"]}}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record Seeded(int InstanceId, int RowId);

    private async Task<Seeded> SeedAsync(string? solution = Solution)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = OwnerUserId, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "D/Y", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, Kind = WorksheetKind.Activity };
        var question = new Question
        {
            Text = "Doğru mu yanlış mı?", Point = 10, InteractionType = "matrixChoice", InteractionPlan = Plan, InteractionSolution = solution,
        };
        ctx.AddRange(student, worksheet, question);
        await ctx.SaveChangesAsync();

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        ctx.TestQuestions.Add(wq);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Started,
            StartTime = DateTime.UtcNow.AddMinutes(-5), MaxDurationSeconds = 600,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var row = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id };
        ctx.TestInstanceQuestions.Add(row);
        await ctx.SaveChangesAsync();
        return new Seeded(instance.Id, row.Id);
    }

    private static ActivityCheckRequestDto Request(int rowId, string? payload) => new()
    {
        Answers = new List<ActivityCheckAnswerDto> { new() { InstanceQuestionId = rowId, AnswerPayload = payload } },
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

    private static List<(string Id, bool Correct)> FeedbackRows(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        return doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean()))
            .ToList();
    }

    [Fact]
    public async Task CheckActivity_FullyCorrect_IsCorrectAndStoresTheGrade()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(s.RowId, FullPayload));

        result.Success.ShouldBeTrue();
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
        result.Questions.Single().IsCorrect.ShouldBeTrue();
        var row = await RowAsync(s.RowId);
        row.AnswerPayload.ShouldBe(FullPayload);
        row.IsCorrect.ShouldBeTrue();
        row.CorrectCount.ShouldBe(3);
        row.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task CheckActivity_Partial_ReturnsRowResultsInPlanOrderWithUnansweredRowWrong()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(s.RowId, PartialPayload));

        result.Success.ShouldBeTrue();
        var q = result.Questions.Single();
        q.IsCorrect.ShouldBeFalse();
        q.CorrectCount.ShouldBe(1);
        q.TotalCount.ShouldBe(3);
        FeedbackRows(q.Feedback).ShouldBe(new[] { ("r1", true), ("r2", false), ("r3", false) });
    }

    [Fact]
    public async Task CheckActivity_ResponseSerialized_NeverContainsTheSolution()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(s.RowId, PartialPayload));

        var json = JsonSerializer.Serialize(result);
        json.ShouldNotContain("COL-TRUE-7");
        json.ShouldNotContain("COL-FALSE-8");
        json.ShouldNotContain("interactionSolution", Case.Insensitive);
    }

    [Theory]
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":["COL-TRUE-7","COL-FALSE-8"]}}""")] // tek seçimli satıra iki kolon
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r9":["COL-TRUE-7"]}}""")]               // plan dışı satır
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":["NOPE"]}}""")]                     // plan dışı kolon
    [InlineData("""{"version":1,"type":"dragDropLabeling","placements":[]}""")]                              // başka tipin payload'u
    public async Task CheckActivity_InvalidSelections_IsInvalidInputAndWritesNothing(string payload)
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(s.RowId, payload));

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        var row = await RowAsync(s.RowId);
        row.AnswerRevision.ShouldBe(0);
        row.AnswerPayload.ShouldBeNull();
    }

    [Fact]
    public async Task CheckActivity_EmptySelections_IsStoredAsBlank()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(s.RowId, """{"version":1,"type":"matrixChoice","selections":{}}"""));

        result.Success.ShouldBeTrue();
        (await RowAsync(s.RowId)).AnswerPayload.ShouldBeNull();
    }

    [Fact]
    public async Task CheckActivity_QuestionWithoutSolution_IsUngradable()
    {
        var s = await SeedAsync(solution: null);

        var result = await CheckAsync(s.InstanceId, Request(s.RowId, FullPayload));

        result.Success.ShouldBeTrue();
        var q = result.Questions.Single();
        q.IsCorrect.ShouldBeFalse();
        q.TotalCount.ShouldBe(0);
        q.Feedback.ShouldBeNull();
    }

    [Fact]
    public async Task PlayerView_BeforeAndAfterCheck_NeverCarriesTheSolution()
    {
        var s = await SeedAsync();

        async Task<WorksheetInstanceResultDto> LoadAsync()
        {
            await using var ctx = _db.NewContext();
            return (await new TestSessionService(ctx).GetCanvasTestResultAsync(s.InstanceId, OwnerUserId))!;
        }

        var before = (await LoadAsync()).TestInstanceQuestions.Single();
        before.Question.InteractionType.ShouldBe("matrixChoice");
        before.Question.InteractionPlan.ShouldBe(Plan);
        before.Question.InteractionSolution.ShouldBeNull();

        (await CheckAsync(s.InstanceId, Request(s.RowId, PartialPayload))).Success.ShouldBeTrue();

        var after = (await LoadAsync()).TestInstanceQuestions.Single();
        after.Question.InteractionSolution.ShouldBeNull();
        after.Question.CorrectAnswerId.ShouldBeNull();
        // Son kontrol sonucu (satır bazlı) döner; çözüm değil.
        FeedbackRows(after.Feedback).ShouldBe(new[] { ("r1", true), ("r2", false), ("r3", false) });
        after.Feedback!.ShouldNotContain("COL-");
    }

    [Fact]
    public async Task GetActivitySolutions_AfterCheck_ReturnsTheMatrixSolution()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var early = await new TestSessionService(ctx).GetActivitySolutionsAsync(s.InstanceId, OwnerUserId);
            early.Success.ShouldBeFalse();
            early.ErrorCode.ShouldBe(TestSessionErrorCodes.ActivityCheckRequired);
            early.Questions.ShouldBeEmpty();
        }

        (await CheckAsync(s.InstanceId, Request(s.RowId, PartialPayload))).Success.ShouldBeTrue();

        await using var ctx2 = _db.NewContext();
        var result = await new TestSessionService(ctx2).GetActivitySolutionsAsync(s.InstanceId, OwnerUserId);
        result.Success.ShouldBeTrue();
        var q = result.Questions.Single();
        q.CorrectAnswerId.ShouldBeNull();
        q.InteractionSolution.ShouldBe(Solution);
    }
}
