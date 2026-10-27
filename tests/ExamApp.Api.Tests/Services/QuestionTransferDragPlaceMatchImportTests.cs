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
/// issue #576: içe aktarımda <c>dragPlace</c> / <c>match</c> tanımı kayıt yoluyla aynı kontrolcüyle doğrulanır — çözüm planla
/// tutarsızsa çözüm, plan şemaya uymuyorsa plan + çözüm düşürülür; soru yine içe alınır ve iş mesajına uyarı yazılır.
/// </summary>
public class QuestionTransferDragPlaceMatchImportTests : IDisposable
{
    private const string SourceKey = "src";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    public void Dispose() => _db.Dispose();

    private async Task<(Question Question, string Message)> ImportSingleAsync(string externalKey, string type, string? plan, string? solution)
    {
        var entry = new
        {
            externalKey, id = 1, text = "Etkinlik", interactionType = type,
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

    [Theory]
    [InlineData("DragPlace", "dragPlace", QuestionServiceDragPlaceMatchTests.DragPlan, QuestionServiceDragPlaceMatchTests.DragSolution)]
    [InlineData("Match", "match", QuestionServiceDragPlaceMatchTests.MatchPlan, QuestionServiceDragPlaceMatchTests.MatchSolution)]
    public async Task Import_ValidDefinition_IsStoredWithCanonicalTypeAndNoWarning(string type, string canonical, string plan, string solution)
    {
        var (q, message) = await ImportSingleAsync("question:1", type, plan, solution);

        q.InteractionType.ShouldBe(canonical);
        InteractionScrambleAssert.Scrambled(canonical, plan, solution, q.InteractionPlan, q.InteractionSolution);
        message.ShouldNotContain("Warnings");
    }

    [Theory]
    [InlineData("dragPlace", QuestionServiceDragPlaceMatchTests.DragPlan, """{"t1":["c9"],"t2":[]}""")]
    [InlineData("match", QuestionServiceDragPlaceMatchTests.MatchPlan, """{"l2":"r1"}""")]
    public async Task Import_SolutionInconsistentWithPlan_DropsSolutionAndRecordsWarning(string type, string plan, string solution)
    {
        var (q, message) = await ImportSingleAsync("question:2", type, plan, solution);

        q.InteractionSolution.ShouldBeNull();
        InteractionScrambleAssert.Scrambled(type, plan, null, q.InteractionPlan, q.InteractionSolution);
        message.ShouldContain("Warnings 1");
        message.ShouldContain("question:2");
        message.ShouldContain("solution dropped");
    }

    [Theory]
    [InlineData("dragPlace", QuestionServiceDragPlaceMatchTests.DragPlan, QuestionServiceDragPlaceMatchTests.DragSolution, "\"label\":\"Buz\"")]
    [InlineData("match", QuestionServiceDragPlaceMatchTests.MatchPlan, QuestionServiceDragPlaceMatchTests.MatchSolution, "\"label\":\"Top\"")]
    public async Task Import_PlanWithUnknownField_DropsPlanAndSolutionAndRecordsWarning(string type, string plan, string solution, string anchor)
    {
        var smuggled = plan.Replace(anchor, anchor + ",\"correct\":true");

        var (q, message) = await ImportSingleAsync("question:3", type, smuggled, solution);

        q.InteractionPlan.ShouldBeNull();
        q.InteractionSolution.ShouldBeNull();
        message.ShouldContain("plan and solution dropped");
    }
}
