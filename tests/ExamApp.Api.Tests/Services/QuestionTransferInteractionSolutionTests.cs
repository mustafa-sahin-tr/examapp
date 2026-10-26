using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #570: soru aktarımı — export manifestine <c>interactionSolution</c> ayrı alan olarak yazılır, <c>interactionPlan</c>
/// çözümsüz yazılır (eski satırda plan içindeki çözüm ayrılır). Import ayrı alanı okur; yoksa eski manifestin plan içindeki
/// <c>solution</c>'ını ayırır; geçersiz ayrı çözüm yok sayılır.
/// </summary>
public class QuestionTransferInteractionSolutionTests : IDisposable
{
    private const string SourceKey = "src";
    private const string Solution = """{"placements":[{"zone":"z1","label":"l1"}]}""";
    private const string PlanWithoutSolution = """{"labels":["l1","l2"],"zones":[{"id":"z1"}]}""";
    private const string PlanWithEmbeddedSolution =
        """{"labels":["l1","l2"],"zones":[{"id":"z1"}],"solution":{"placements":[{"zone":"z1","label":"l1"}]}}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly JobStorage? _previousStorage;

    public QuestionTransferInteractionSolutionTests()
    {
        // Export, dağıtık kilit için JobStorage.Current ister; testte bellek-içi sahte depo.
        var storage = Substitute.For<JobStorage>();
        var connection = Substitute.For<IStorageConnection>();
        storage.GetConnection().Returns(connection);
        _previousStorage = TryGetCurrentStorage();
        JobStorage.Current = storage;
    }

    // JobStorage.Current okunurken ayarlanmamışsa istisna fırlatır.
    private static JobStorage? TryGetCurrentStorage()
    {
        try
        {
            return JobStorage.Current;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        JobStorage.Current = _previousStorage;
        _db.Dispose();
    }

    private static void ShouldHaveNoSolutionKey(string? plan)
    {
        plan.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(plan!);
        doc.RootElement.TryGetProperty("solution", out _).ShouldBeFalse();
    }

    private async Task<Guid> SeedJobAsync(QuestionTransferJobKind kind, string? requestJson = null, string? fileUrl = null)
    {
        await using var ctx = _db.NewContext();
        var job = new QuestionTransferJob
        {
            Id = Guid.NewGuid(), Kind = kind, SourceKey = SourceKey, FileUrl = fileUrl ?? "http://minio/in.zip",
            Message = "Queued", RequestJson = requestJson,
        };
        ctx.Add(job);
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    private async Task<(int Legacy, int Column)> SeedQuestionsAsync()
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();

        var legacy = new Question
        {
            Text = "Eski soru", Point = 5, SubjectId = subject.Id, DifficultyLevel = 1,
            InteractionType = "dragDropLabeling", InteractionPlan = PlanWithEmbeddedSolution, InteractionSolution = null,
        };
        var column = new Question
        {
            Text = "Yeni soru", Point = 5, SubjectId = subject.Id, DifficultyLevel = 1,
            InteractionType = "dragDropLabeling", InteractionPlan = PlanWithoutSolution, InteractionSolution = Solution,
        };
        ctx.Questions.AddRange(legacy, column);
        await ctx.SaveChangesAsync();
        return (legacy.Id, column.Id);
    }

    /// <summary>Export çalıştırır ve üretilen bundle zip'inin <c>manifest.json</c>'ını JSON olarak döndürür.</summary>
    private async Task<JsonElement> RunExportAndReadManifestAsync(params int[] questionIds)
    {
        byte[]? zipBytes = null;
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(call =>
            {
                if (call.ArgAt<string>(1).EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var source = call.ArgAt<Stream>(0);
                    if (source.CanSeek) source.Position = 0;
                    using var copy = new MemoryStream();
                    source.CopyTo(copy);
                    zipBytes = copy.ToArray();
                }
                return Task.FromResult("/img/exam-questions/question-transfer/exports/src/bundle-0001.zip");
            });

        var request = JsonSerializer.Serialize(new { QuestionIds = questionIds, SourceKey = SourceKey });
        var jobId = await SeedJobAsync(QuestionTransferJobKind.Export, requestJson: request);

        await using (var ctx = _db.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunExportAsync(jobId, ownerIsAdmin: true);

        zipBytes.ShouldNotBeNull("export bundle zip yüklenmedi");
        using var zip = new ZipArchive(new MemoryStream(zipBytes!), ZipArchiveMode.Read);
        using var manifestStream = zip.GetEntry("manifest.json")!.Open();
        using var doc = await JsonDocument.ParseAsync(manifestStream);
        return doc.RootElement.Clone();
    }

    private static JsonElement QuestionEntry(JsonElement manifest, int questionId)
    {
        foreach (var q in manifest.GetProperty("questions").EnumerateArray())
            if (q.GetProperty("id").GetInt32() == questionId)
                return q;
        throw new InvalidOperationException($"manifest içinde soru {questionId} yok");
    }

    // ---- export ----

    [Fact]
    public async Task Export_LegacyPlanWithEmbeddedSolution_WritesSolutionToInteractionSolutionAndPlanWithoutSolution()
    {
        var (legacyId, _) = await SeedQuestionsAsync();

        var manifest = await RunExportAndReadManifestAsync(legacyId);

        var entry = QuestionEntry(manifest, legacyId);
        var plan = entry.GetProperty("interactionPlan").GetString();
        ShouldHaveNoSolutionKey(plan);
        entry.GetProperty("interactionSolution").GetString().ShouldNotBeNull().ShouldContain("placements");
    }

    [Fact]
    public async Task Export_QuestionWithSolutionColumn_WritesColumnSolutionAndPlanWithoutSolution()
    {
        var (legacyId, columnId) = await SeedQuestionsAsync();

        var manifest = await RunExportAndReadManifestAsync(legacyId, columnId);

        var entry = QuestionEntry(manifest, columnId);
        ShouldHaveNoSolutionKey(entry.GetProperty("interactionPlan").GetString());
        entry.GetProperty("interactionSolution").GetString().ShouldBe(Solution);
    }

    // ---- import ----

    /// <summary>Import çalıştırır; ilgili sorunun (externalKey ile) saklanan plan/çözümünü döndürür.</summary>
    private async Task<(string? Plan, string? Solution)> ImportSingleAsync(object questionEntry)
    {
        var manifest = JsonSerializer.Serialize(new { version = 2, sourceKey = SourceKey, questions = new[] { questionEntry } });
        var zipBytes = BuildZip(manifest);
        _minio.GetFileStreamAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(zipBytes)));

        var jobId = await SeedJobAsync(QuestionTransferJobKind.Import);
        await using (var ctx = _db.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunImportAsync(jobId, ownerIsAdmin: true);

        await using var check = _db.NewContext();
        var job = await check.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == jobId);
        job.Status.ShouldBe(QuestionTransferJobStatus.Completed);
        job.Message.ShouldContain("Imported 1", customMessage: job.Message);

        var q = await check.Questions.AsNoTracking().SingleAsync();
        return (q.InteractionPlan, q.InteractionSolution);
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

    private static object Entry(string externalKey, string plan, string? interactionSolution = null) => new
    {
        externalKey,
        id = 1, // manifest satır kimliği zorunlu (sourceQuestionId); her testte temiz DB
        text = "İçe aktarılan soru",
        interactionType = "dragDropLabeling",
        interactionPlan = plan,
        interactionSolution,
        answers = Array.Empty<object>(),
    };

    [Fact]
    public async Task Import_V2ManifestWithInteractionSolution_StoresSolutionAndPlanWithoutSolution()
    {
        var (plan, solution) = await ImportSingleAsync(Entry("question:1", PlanWithoutSolution, Solution));

        ShouldHaveNoSolutionKey(plan);
        solution.ShouldBe(Solution);
    }

    [Fact]
    public async Task Import_LegacyManifestPlanEmbeddingSolution_SplitsSolutionIntoColumn()
    {
        var (plan, solution) = await ImportSingleAsync(Entry("question:2", PlanWithEmbeddedSolution));

        ShouldHaveNoSolutionKey(plan);
        solution.ShouldNotBeNull();
        solution!.ShouldContain("placements");
    }

    [Fact]
    public async Task Import_InvalidInteractionSolution_IsIgnoredAndFallsBackToEmbeddedPlanSolution()
    {
        var (plan, solution) = await ImportSingleAsync(Entry("question:3", PlanWithEmbeddedSolution, interactionSolution: "[1,2]"));

        ShouldHaveNoSolutionKey(plan);
        solution.ShouldNotBeNull();
        solution!.ShouldContain("placements");
    }

    [Fact]
    public async Task Import_InvalidInteractionSolutionAndCleanPlan_StoresNoSolution()
    {
        var (plan, solution) = await ImportSingleAsync(Entry("question:4", PlanWithoutSolution, interactionSolution: "\"metin\""));

        ShouldHaveNoSolutionKey(plan);
        solution.ShouldBeNull();
    }
}
