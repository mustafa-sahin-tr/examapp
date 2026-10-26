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
/// issue #573: soru aktarımı — kapsam tür (<c>kind</c>) ve yönerge (<c>instruction</c>) export manifestine yazılır ve import'ta
/// geri okunur. Eski paketlerde alan yok → Reading / null; geçersiz tür → Reading; yönerge uzunluğu 2000 ile sınırlanır.
/// </summary>
public class QuestionTransferActivityPassageTests : IDisposable
{
    private const string SourceKey = "src";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly JobStorage? _previousStorage;

    public QuestionTransferActivityPassageTests()
    {
        // Export, dağıtık kilit için JobStorage.Current ister; testte bellek-içi sahte depo.
        var storage = Substitute.For<JobStorage>();
        var connection = Substitute.For<IStorageConnection>();
        storage.GetConnection().Returns(connection);
        _previousStorage = TryGetCurrentStorage();
        JobStorage.Current = storage;
    }

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

    private async Task<Guid> SeedJobAsync(QuestionTransferJobKind kind, string? requestJson = null)
    {
        await using var ctx = _db.NewContext();
        var job = new QuestionTransferJob
        {
            Id = Guid.NewGuid(), Kind = kind, SourceKey = SourceKey, FileUrl = "http://minio/in.zip",
            Message = "Queued", RequestJson = requestJson,
        };
        ctx.Add(job);
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>Etkinlik kapsamlı bir soru (tür + yönerge) ve okuma kapsamlı bir soru.</summary>
    private async Task<(int ActivityQuestionId, int ReadingQuestionId)> SeedQuestionsAsync()
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();

        var activityPassage = new Passage { Title = "Görsel", Text = "t", IsCanvasQuestion = true, Kind = PassageKind.Activity, Instruction = "Eşleştir." };
        var readingPassage = new Passage { Title = "Metin", Text = "okuma", IsCanvasQuestion = true };
        ctx.Passage.AddRange(activityPassage, readingPassage);
        await ctx.SaveChangesAsync();

        var activity = new Question { Text = "Etkinlik sorusu", Point = 5, SubjectId = subject.Id, DifficultyLevel = 1, PassageId = activityPassage.Id };
        var reading = new Question { Text = "Okuma sorusu", Point = 5, SubjectId = subject.Id, DifficultyLevel = 1, PassageId = readingPassage.Id };
        ctx.Questions.AddRange(activity, reading);
        await ctx.SaveChangesAsync();
        return (activity.Id, reading.Id);
    }

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

    // ---------------------------------------------------------------- export

    [Fact]
    public async Task Export_ActivityPassage_WritesKindAndInstruction()
    {
        var (activityId, _) = await SeedQuestionsAsync();

        var manifest = await RunExportAndReadManifestAsync(activityId);

        var passage = QuestionEntry(manifest, activityId).GetProperty("passage");
        passage.GetProperty("kind").GetInt32().ShouldBe((int)PassageKind.Activity);
        passage.GetProperty("instruction").GetString().ShouldBe("Eşleştir.");
    }

    [Fact]
    public async Task Export_ReadingPassage_WritesKindZeroAndNullInstruction()
    {
        var (_, readingId) = await SeedQuestionsAsync();

        var manifest = await RunExportAndReadManifestAsync(readingId);

        var passage = QuestionEntry(manifest, readingId).GetProperty("passage");
        passage.GetProperty("kind").GetInt32().ShouldBe((int)PassageKind.Reading);
        passage.GetProperty("instruction").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ---------------------------------------------------------------- import

    private static JsonElement Entry(string passageJsonFragment)
    {
        // Ham JSON parçası: yalnız test edilen passage alanlarının varlığı/yokluğu/tipi belirleyici olsun.
        var manifest = $$"""
            {
              "version": 2,
              "sourceKey": "{{SourceKey}}",
              "questions": [
                {
                  "externalKey": "question:1",
                  "id": 1,
                  "text": "İçe aktarılan soru",
                  "interactionType": "mcq",
                  "answers": [],
                  "passage": {{passageJsonFragment}}
                }
              ]
            }
            """;
        return JsonDocument.Parse(manifest).RootElement.Clone();
    }

    private async Task<Passage> ImportAndReadPassageAsync(JsonElement manifest)
    {
        var zipBytes = BuildZip(manifest.GetRawText());
        _minio.GetFileStreamAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(zipBytes)));

        var jobId = await SeedJobAsync(QuestionTransferJobKind.Import);
        await using (var ctx = _db.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunImportAsync(jobId, ownerIsAdmin: true);

        await using var check = _db.NewContext();
        var job = await check.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == jobId);
        job.Status.ShouldBe(QuestionTransferJobStatus.Completed);
        job.Message.ShouldContain("Imported 1", customMessage: job.Message);

        var question = await check.Questions.AsNoTracking().SingleAsync();
        question.PassageId.ShouldNotBeNull();
        return await check.Passage.AsNoTracking().SingleAsync(p => p.Id == question.PassageId);
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

    [Fact]
    public async Task Import_ActivityPassageWithKindAndInstruction_RestoresBoth()
    {
        var passage = await ImportAndReadPassageAsync(Entry(
            """{"externalKey":"passage:7","id":7,"title":"Görsel","text":"t","kind":1,"instruction":"Eşleştir."}"""));

        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe("Eşleştir.");
    }

    [Fact]
    public async Task Import_LegacyBundleWithoutKindOrInstruction_DefaultsToReadingAndNullInstruction()
    {
        var passage = await ImportAndReadPassageAsync(Entry(
            """{"externalKey":"passage:7","id":7,"title":"Metin","text":"okuma"}"""));

        passage.Kind.ShouldBe(PassageKind.Reading);
        passage.Instruction.ShouldBeNull();
    }

    [Fact]
    public async Task Import_UndefinedKindValue_FallsBackToReading()
    {
        var passage = await ImportAndReadPassageAsync(Entry(
            """{"externalKey":"passage:7","id":7,"title":"P","text":"t","kind":9,"instruction":"Yönerge"}"""));

        passage.Kind.ShouldBe(PassageKind.Reading);
        passage.Instruction.ShouldBe("Yönerge");
    }

    [Fact]
    public async Task Import_NonStringInstruction_IsIgnored()
    {
        var passage = await ImportAndReadPassageAsync(Entry(
            """{"externalKey":"passage:7","id":7,"title":"P","text":"t","kind":1,"instruction":42}"""));

        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBeNull();
    }

    [Fact]
    public async Task Import_InstructionLongerThanTheLimit_IsTruncatedToTheMaximum()
    {
        var longInstruction = new string('a', Passage.MaxInstructionLength + 50);

        var passage = await ImportAndReadPassageAsync(Entry(
            $$"""{"externalKey":"passage:7","id":7,"title":"P","text":"t","kind":1,"instruction":"{{longInstruction}}"}"""));

        passage.Instruction.ShouldNotBeNull();
        passage.Instruction!.Length.ShouldBe(Passage.MaxInstructionLength);
    }
}
