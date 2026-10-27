using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #582: içe aktarımda <c>textSelect</c> tanımı kayıt yoluyla aynı kontrolcüyle doğrulanır — çözüm planla tutarsızsa çözüm,
/// plan şemaya uymuyorsa plan + çözüm düşürülür; soru yine içe alınır ve iş mesajına uyarı yazılır. Tip kanonik saklanır.
/// </summary>
public class QuestionTransferTextSelectImportTests : IDisposable
{
    private const string SourceKey = "src";

    private const string Plan =
        """{"version":1,"type":"textSelect","mode":"punctuation","palette":[".","!","?"],"tokens":[{"id":"t1","text":"Eyvah","line":0,"boxes":[{"x":0,"y":0,"width":50,"height":10}]},{"id":"g1","text":"( )","line":0,"gap":true,"boxes":[{"x":60,"y":0,"width":30,"height":10}]}]}""";

    private const string Solution = """{"gaps":{"g1":{"accept":["!"]}}}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    public void Dispose() => _db.Dispose();

    private async Task<(Question Question, string Message)> ImportSingleAsync(string externalKey, string type, string? plan, string? solution)
    {
        var entry = new
        {
            externalKey, id = 1, text = "Noktalama", interactionType = type,
            interactionPlan = plan, interactionSolution = solution, answers = Array.Empty<object>(),
        };
        var manifest = JsonSerializer.Serialize(new { version = 2, sourceKey = SourceKey, questions = new[] { entry } });
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
                Id = Guid.NewGuid(), Kind = QuestionTransferJobKind.Import, SourceKey = SourceKey,
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
        finished.Message!.ShouldContain("Imported 1");
        return (await check.Questions.AsNoTracking().SingleAsync(), finished.Message!);
    }

    [Fact]
    public async Task Import_ValidDefinition_IsStoredWithCanonicalTypeAndNoWarning()
    {
        var (q, message) = await ImportSingleAsync("question:1", "TextSelect", Plan, Solution);

        q.InteractionType.ShouldBe("textSelect");
        q.InteractionPlan.ShouldBe(Plan);
        q.InteractionSolution.ShouldBe(Solution);
        message.ShouldNotContain("Warnings");
    }

    [Fact]
    public async Task Import_SolutionOutsidePalette_DropsSolutionAndRecordsWarning()
    {
        var (q, message) = await ImportSingleAsync("question:2", "textSelect", Plan, """{"gaps":{"g1":{"accept":[";"]}}}""");

        q.InteractionPlan.ShouldBe(Plan);
        q.InteractionSolution.ShouldBeNull();
        message.ShouldContain("Warnings 1");
        message.ShouldContain("question:2");
        message.ShouldContain("solution dropped");
    }

    [Fact]
    public async Task Import_PlanWithUnknownField_DropsPlanAndSolutionAndRecordsWarning()
    {
        var smuggled = Plan.Replace("\"gap\":true", "\"gap\":true,\"answer\":\"!\"");

        var (q, message) = await ImportSingleAsync("question:3", "textSelect", smuggled, Solution);

        q.InteractionPlan.ShouldBeNull();
        q.InteractionSolution.ShouldBeNull();
        message.ShouldContain("plan and solution dropped");
    }
}
