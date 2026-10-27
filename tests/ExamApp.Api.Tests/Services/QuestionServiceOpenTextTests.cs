using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #583: <c>openText</c> tanımı kayıt (<see cref="QuestionService"/> tekli + toplu) ve içe aktarım yollarında aynı kontrolcüyle
/// doğrulanır; tip kanonik saklanır, açık uçlu soru doğru şık istemez ve soru görseli kırpılmaz.
/// </summary>
public class QuestionServiceOpenTextTests : IDisposable
{
    private const string Type = "openText";
    private const string Plan = """{"version":1,"type":"openText","minWords":10,"maxWords":80,"lines":6}""";
    private const string Solution = """{"sampleAnswer":"Su ısınınca buharlaşır.","rubric":"Isı ve buharlaşma geçmeli."}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    public void Dispose() => _db.Dispose();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    private async Task<int> SeedQuestionAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(5);
        var q = new Question { Text = "Açıkla", Point = 5, InteractionType = Type, InteractionPlan = Plan, InteractionSolution = Solution };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private static QuestionDto UpdateDto(int id, string? plan, string? solution, string type = Type) => new()
    {
        Id = id, Text = "Açıkla", Point = 5, InteractionType = type,
        InteractionPlan = plan, InteractionSolution = solution, Answers = new List<AnswerDto>(),
    };

    [Fact]
    public async Task CreateOrUpdate_ValidDefinition_IsStoredWithCanonicalType()
    {
        var id = await SeedQuestionAsync();
        const string newSolution = """{"rubric":"Yalnız rubrik"}""";

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, Plan, newSolution, type: "OpenText"), actingUserId: 5)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync(x => x.Id == id);
        (q.InteractionType, q.InteractionPlan, q.InteractionSolution).ShouldBe((Type, Plan, newSolution));
    }

    [Theory]
    [InlineData("""{"version":1,"type":"openText","maxWords":5000}""", null)]
    [InlineData("""{"version":1,"type":"openText","prompt":"x"}""", null)]
    [InlineData(Plan, """{"sampleAnswer":"x","answerKey":"y"}""")]
    [InlineData(null, Solution)]
    public async Task CreateOrUpdate_InvalidDefinition_ReturnsInvalidInputAndKeepsRow(string? plan, string? solution)
    {
        var id = await SeedQuestionAsync();

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(UpdateDto(id, plan, solution), actingUserId: 5);

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync(x => x.Id == id);
        (q.InteractionPlan, q.InteractionSolution).ShouldBe((Plan, Solution));
    }

    [Fact]
    public async Task SaveBulk_ValidQuestion_NeedsNoCorrectAnswerAndKeepsFullHeight()
    {
        var request = new BulkQuestionCreateDto
        {
            ImageData = "data:image/png;base64," + Convert.ToBase64String(TestImages.Png(100, 100)),
            Passages = new List<BulkPassageDto>(),
            Header = new HeaderInfo(),
            Questions = new List<BulkQuestionDto>
            {
                new()
                {
                    Name = "açık uçlu", InteractionType = "OpenText", InteractionPlan = Plan, InteractionSolution = Solution,
                    Answers = new List<BulkAnswerDto> { new() { Label = "x", X = 0, Y = 5, Width = 5, Height = 3, IsCorrect = false } },
                    X = 0, Y = 0, Width = 10, Height = 10,
                },
            },
        };

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(request, actingUserId: 7);

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var q = await check.Questions.AsNoTracking().SingleAsync();
        (q.InteractionType, q.InteractionPlan, q.InteractionSolution).ShouldBe((Type, Plan, Solution));
        q.SanitizedHeight.ShouldBe(10);
        q.CorrectAnswerId.ShouldBeNull();
    }

    // ---------- içe aktarım ----------

    private async Task<(Question Question, string Message)> ImportSingleAsync(string externalKey, string type, string? plan, string? solution)
    {
        const string sourceKey = "src";
        var entry = new
        {
            externalKey, id = 1, text = "Açıkla", interactionType = type,
            interactionPlan = plan, interactionSolution = solution, answers = Array.Empty<object>(),
        };
        var manifest = JsonSerializer.Serialize(new { version = 2, sourceKey, questions = new[] { entry } });
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("manifest.json").Open();
            s.Write(System.Text.Encoding.UTF8.GetBytes(manifest));
        }
        var zipBytes = ms.ToArray();
        _minio.GetFileStreamAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(zipBytes)));

        Guid jobId;
        await using (var seed = _db.NewContext())
        {
            var job = new QuestionTransferJob
            {
                Id = Guid.NewGuid(), Kind = QuestionTransferJobKind.Import, SourceKey = sourceKey,
                FileUrl = "http://minio/in.zip", Message = "Queued",
            };
            seed.Add(job);
            await seed.SaveChangesAsync();
            jobId = job.Id;
        }

        await using (var ctx = _db.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunImportAsync(jobId, ownerIsAdmin: true);

        await using var check = _db.NewContext();
        var finished = await check.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == jobId);
        finished.Status.ShouldBe(QuestionTransferJobStatus.Completed, finished.Message);
        return (await check.Questions.AsNoTracking().SingleAsync(), finished.Message!);
    }

    [Fact]
    public async Task Import_ValidDefinition_IsStoredWithCanonicalTypeAndNoWarning()
    {
        var (q, message) = await ImportSingleAsync("question:1", "OPENTEXT", Plan, Solution);

        (q.InteractionType, q.InteractionPlan, q.InteractionSolution).ShouldBe((Type, Plan, Solution));
        message.ShouldNotContain("Warnings");
    }

    [Fact]
    public async Task Import_SolutionWithUnknownField_DropsSolutionAndRecordsWarning()
    {
        var (q, message) = await ImportSingleAsync("question:2", Type, Plan, """{"sampleAnswer":"x","points":3}""");

        q.InteractionPlan.ShouldBe(Plan);
        q.InteractionSolution.ShouldBeNull();
        message.ShouldContain("solution dropped");
    }

    [Fact]
    public async Task Import_PlanOutOfRange_DropsPlanAndSolutionAndRecordsWarning()
    {
        var (q, message) = await ImportSingleAsync("question:3", Type, """{"version":1,"type":"openText","lines":99}""", Solution);

        q.InteractionPlan.ShouldBeNull();
        q.InteractionSolution.ShouldBeNull();
        message.ShouldContain("plan and solution dropped");
    }
}
