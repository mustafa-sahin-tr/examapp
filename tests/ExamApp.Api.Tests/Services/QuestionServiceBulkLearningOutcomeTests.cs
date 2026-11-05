using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #692: toplu kayıtta seçilen kazanımlar soruyla birlikte yazılır.</summary>
public class QuestionServiceBulkLearningOutcomeTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    private async Task<int> SeedOutcomeAsync()
    {
        await using var ctx = _db.NewContext();
        var outcome = new LearningOutcome { Code = "MAT.5.1", Description = "x", GradeId = 1, CurriculumVersion = CurriculumVersion.Maarif };
        ctx.LearningOutcomes.Add(outcome);
        await ctx.SaveChangesAsync();
        return outcome.Id;
    }

    private static BulkQuestionCreateDto Request(params int[] outcomeIds) => new()
    {
        ImageData = "data:image/png;base64," + Convert.ToBase64String(TestImages.Png(100, 100)),
        Passages = new List<BulkPassageDto>(),
        Header = new HeaderInfo(),
        Questions = new List<BulkQuestionDto>
        {
            new()
            {
                Name = "soru", InteractionType = "OpenText",
                InteractionPlan = """{"version":1,"type":"openText","minWords":10,"maxWords":80,"lines":6}""",
                InteractionSolution = """{"sampleAnswer":"a","rubric":"b"}""",
                Answers = new List<BulkAnswerDto>(),
                X = 0, Y = 0, Width = 10, Height = 10,
                LearningOutcomeIds = outcomeIds.ToList(),
            },
        },
    };

    [Fact]
    public async Task SelectedOutcomes_AreLinkedToTheNewQuestion()
    {
        var known = await SeedOutcomeAsync();

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(Request(known, known), actingUserId: 7);

        result.Success.ShouldBeTrue(result.Message);
        await using var read = _db.NewContext();
        var question = await read.Questions.SingleAsync();
        question.SourcePageWidth.ShouldBe(100); // issue #778: kırpılan sayfanın genişliği
        (await read.QuestionLearningOutcomes.SingleAsync()).ShouldSatisfyAllConditions(
            link => link.QuestionId.ShouldBe(question.Id),
            link => link.LearningOutcomeId.ShouldBe(known));
    }

    [Fact]
    public async Task NoOutcomes_SavesTheQuestionWithoutLinks()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(Request(), actingUserId: 7);

        result.Success.ShouldBeTrue(result.Message);
        await using var read = _db.NewContext();
        (await read.Questions.CountAsync()).ShouldBe(1);
        (await read.QuestionLearningOutcomes.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UnknownOutcome_FailsBeforeWritingAnything()
    {
        var known = await SeedOutcomeAsync();

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(Request(known, known + 100), actingUserId: 7);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("kazanım");
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!);
        await using var read = _db.NewContext();
        (await read.Questions.CountAsync()).ShouldBe(0);
        (await read.QuestionLearningOutcomes.CountAsync()).ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
