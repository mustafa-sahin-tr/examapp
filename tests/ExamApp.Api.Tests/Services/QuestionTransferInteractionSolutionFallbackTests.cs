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
/// issue #570 review: içe aktarım — katı kurala uymayan eski manifest planı (yinelenen anahtar, nesne olmayan gömülü çözüm,
/// bozuk JSON) aktarımı düşürmez: plan çözümsüz yazılır, geçersiz çözüm atılır, aktarım tamamlanır.
/// </summary>
public class QuestionTransferInteractionSolutionFallbackTests : IDisposable
{
    private const string SourceKey = "src";
    private const string Solution = """{"placements":[{"zone":"z1","label":"l1"}]}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    public void Dispose() => _db.Dispose();

    private async Task<(string? Plan, string? Solution, string Message)> ImportSingleAsync(object questionEntry)
    {
        var manifest = JsonSerializer.Serialize(new { version = 2, sourceKey = SourceKey, questions = new[] { questionEntry } });
        var zipBytes = BuildZip(manifest);
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
        finished.Message.ShouldContain("Imported 1", customMessage: finished.Message);

        var q = await check.Questions.AsNoTracking().SingleAsync();
        return (q.InteractionPlan, q.InteractionSolution, finished.Message);
    }

    private static byte[] BuildZip(string manifestJson)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("manifest.json").Open();
            s.Write(System.Text.Encoding.UTF8.GetBytes(manifestJson));
        }
        return ms.ToArray();
    }

    private static object Entry(string externalKey, string? plan, string? interactionSolution = null) => new
    {
        externalKey,
        id = 1,
        text = "İçe aktarılan soru",
        interactionType = "dragDropLabeling",
        interactionPlan = plan,
        interactionSolution,
        answers = Array.Empty<object>(),
    };

    private static void ShouldHaveNoSolutionKey(string? plan)
    {
        plan.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(plan!);
        doc.RootElement.TryGetProperty("solution", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Import_LegacyPlanWithDuplicateSolutionKeys_FallsBackToTolerantSplitAndCompletes()
    {
        var plan = """{"labels":["l1"],"solution":{"placements":[]},"solution":{"placements":[{"zone":"z1","label":"l1"}]}}""";

        var (stored, solution, _) = await ImportSingleAsync(Entry("question:1", plan));

        ShouldHaveNoSolutionKey(stored);
        stored.ShouldBe("""{"labels":["l1"]}""");
        solution.ShouldBe("""{"placements":[{"zone":"z1","label":"l1"}]}""");
    }

    [Fact]
    public async Task Import_LegacyPlanWithNonObjectEmbeddedSolution_StripsPlanAndDropsSolution()
    {
        var (stored, solution, _) = await ImportSingleAsync(Entry("question:2", """{"labels":["l1"],"solution":"abc"}"""));

        stored.ShouldBe("""{"labels":["l1"]}""");
        solution.ShouldBeNull();
    }

    [Fact]
    public async Task Import_InvalidExplicitSolutionWithDuplicateKeys_IsDroppedAndCleanPlanStored()
    {
        var (stored, solution, _) = await ImportSingleAsync(Entry("question:3", """{"labels":["l1"]}""",
            interactionSolution: """{"placements":[{"z":1,"z":2}]}"""));

        stored.ShouldBe("""{"labels":["l1"]}""");
        solution.ShouldBeNull();
    }

    [Fact]
    public async Task Import_ExplicitValidSolutionWithUnparseableLegacyPlan_StoresExplicitSolution()
    {
        var (stored, solution, _) = await ImportSingleAsync(Entry("question:4", """{"labels":["l1"]""",
            interactionSolution: Solution));

        stored.ShouldNotBeNull();
        stored!.ShouldNotContain("solution", Case.Insensitive);
        solution.ShouldBe(Solution);
    }

    [Fact]
    public async Task Import_BrokenPlanWithoutSolutionText_StoresPlanAsIsAndNoSolution()
    {
        var (stored, solution, _) = await ImportSingleAsync(Entry("question:5", "{broken"));

        stored.ShouldBe("{broken");
        solution.ShouldBeNull();
    }

    [Fact]
    public async Task Import_BrokenPlanMentioningSolution_StoresNoPlanTextWithSolution()
    {
        // Kapalı başarısızlık (yanıt tarafıyla aynı): ayrıştırılamayan ve "solution" geçen plan metni kalıcı olarak
        // çözüm taşımamalı; plan null ya da "solution" içermeyen bir metin olmalı.
        var (stored, _, _) = await ImportSingleAsync(Entry("question:6", """{"labels":["l1"],"solution":{"placements":[]}"""));

        if (stored != null)
            stored.ShouldNotContain("solution", Case.Insensitive);
    }
}
