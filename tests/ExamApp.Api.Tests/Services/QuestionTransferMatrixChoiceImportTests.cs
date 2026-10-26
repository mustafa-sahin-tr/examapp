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
/// review #574 (L1/L3): içe aktarımda etkileşim tanımı kayıt yoluyla aynı kontrolcüyle doğrulanır — çözüm planla tutarsızsa
/// çözüm, plan şemaya uymuyorsa plan + çözüm düşürülür; soru yine içe alınır ve iş mesajına uyarı yazılır. Tip kanonik saklanır.
/// </summary>
public class QuestionTransferMatrixChoiceImportTests : IDisposable
{
    private const string SourceKey = "src";

    private const string Plan =
        """{"version":1,"type":"matrixChoice","columns":[{"id":"cD","label":"D"},{"id":"cY","label":"Y"}],"rows":[{"id":"r1","box":{"x":0,"y":0,"width":10,"height":3}},{"id":"r2","box":{"x":0,"y":4,"width":10,"height":3}}]}""";

    private const string Solution = """{"r1":["cD"],"r2":["cY"]}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    public void Dispose() => _db.Dispose();

    private async Task<(Question Question, string Message)> ImportSingleAsync(string externalKey, string type, string? plan, string? solution)
    {
        var entry = new
        {
            externalKey, id = 1, text = "Matris", interactionType = type,
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
    public async Task Import_ValidMatrixDefinition_IsStoredWithCanonicalTypeAndNoWarning()
    {
        var (q, message) = await ImportSingleAsync("question:1", "MatrixChoice", Plan, Solution);

        q.InteractionType.ShouldBe("matrixChoice");
        q.InteractionPlan.ShouldBe(Plan);
        q.InteractionSolution.ShouldBe(Solution);
        message.ShouldNotContain("Warnings");
    }

    [Fact]
    public async Task Import_SolutionInconsistentWithPlan_DropsSolutionAndRecordsWarning()
    {
        var (q, message) = await ImportSingleAsync("question:2", "matrixChoice", Plan, """{"r1":["cD"]}""");

        q.InteractionPlan.ShouldBe(Plan);
        q.InteractionSolution.ShouldBeNull();
        message.ShouldContain("Warnings 1");
        message.ShouldContain("question:2");
        message.ShouldContain("solution dropped");
    }

    [Fact]
    public async Task Import_PlanWithUnknownField_DropsPlanAndSolutionAndRecordsWarning()
    {
        var smuggled = Plan.Replace("\"label\":\"D\"", "\"label\":\"D\",\"answer\":true");

        var (q, message) = await ImportSingleAsync("question:3", "matrixChoice", smuggled, Solution);

        q.InteractionPlan.ShouldBeNull();
        q.InteractionSolution.ShouldBeNull();
        message.ShouldContain("plan and solution dropped");
    }

    [Fact]
    public async Task Import_DragDropDefinition_IsNotSubjectToMatrixRules()
    {
        var (q, message) = await ImportSingleAsync("question:4", "dragDropLabeling", """{"labels":["l1"]}""", """{"placements":[]}""");

        q.InteractionType.ShouldBe("dragDropLabeling");
        q.InteractionPlan.ShouldBe("""{"labels":["l1"]}""");
        q.InteractionSolution.ShouldBe("""{"placements":[]}""");
        message.ShouldNotContain("Warnings");
    }
}
