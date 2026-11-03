using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary><see cref="JobStorage.Current"/> statiktir: onu değiştiren test sınıfları birbirinin kilidini bozmasın diye paralel koşmaz.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HangfireJobStorageCollection
{
    public const string Name = "Hangfire JobStorage";
}

/// <summary>
/// Gerçek dışa aktarımın ürettiği paket başka bir veritabanına alınır. Elle kurulan manifest, dışa aktarımla içe aktarımın
/// aynı hash'i ürettiğini kanıtlamaz; özelliğin dayandığı varsayım budur.
/// </summary>
[Collection(HangfireJobStorageCollection.Name)]
public class QuestionTransferRoundTripTests : IDisposable
{
    private const string ImageUrl = "/img/exam-questions/a.jpg";
    private const string PackageUrl = "/img/exam-questions/in.zip";
    private const string MatchPlan = """{"version":1,"type":"match","left":[{"id":"l1","label":"Bir"},{"id":"l2","label":"İki"}],"right":[{"id":"r1","label":"1"},{"id":"r2","label":"2"}]}""";
    private const string MatchSolution = """{"l1":"r1","l2":"r2"}""";
    private static readonly byte[] Image = [1, 2, 3, 4];

    private readonly TestDb _source = TestDb.Create();
    private readonly TestDb _target = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly JobStorage? _previousStorage;
    private byte[]? _package;

    public QuestionTransferRoundTripTests()
    {
        var storage = Substitute.For<JobStorage>();
        storage.GetConnection().Returns(Substitute.For<IStorageConnection>());
        try
        {
            _previousStorage = JobStorage.Current;
        }
        catch (InvalidOperationException)
        {
            _previousStorage = null;
        }
        JobStorage.Current = storage;

        _minio.GetFileStreamAsync(ImageUrl).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(Image)));
        _minio.GetFileStreamAsync(PackageUrl).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(_package!)));
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(call =>
            {
                var name = call.ArgAt<string>(1);
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var stream = call.ArgAt<Stream>(0);
                    if (stream.CanSeek) stream.Position = 0;
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    _package = copy.ToArray();
                }
                return Task.FromResult(name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? ImageUrl : $"/img/exam-questions/{name}");
            });
    }

    public void Dispose()
    {
        JobStorage.Current = _previousStorage;
        _source.Dispose();
        _target.Dispose();
    }

    private async Task SeedSourceAsync()
    {
        await using var ctx = _source.NewContext();
        var passage = new Passage { Title = "Parça", Text = "Bir varmış bir yokmuş.", IsCanvasQuestion = true };
        ctx.Passage.Add(passage);
        await ctx.SaveChangesAsync();

        var (plan, solution) = new MatchGrader().PrepareForStorage(MatchPlan, MatchSolution);
        var imageQuestion = new Question { Text = "Görselli", ImageUrl = ImageUrl, Point = 5, DifficultyLevel = 1 };
        var passageQuestion = new Question { Text = "Buna göre hangisi doğrudur?", PassageId = passage.Id, Point = 5, DifficultyLevel = 1 };
        var matchQuestion = new Question { Text = "Eşleştir", InteractionType = MatchGrader.Type, InteractionPlan = plan, InteractionSolution = solution, Point = 5, DifficultyLevel = 1 };
        ctx.Questions.AddRange(imageQuestion, passageQuestion, matchQuestion);
        await ctx.SaveChangesAsync();

        var answers = new[]
        {
            new Answer { QuestionId = imageQuestion.Id, Text = "A", Order = 1 },
            new Answer { QuestionId = imageQuestion.Id, Text = "B", Order = 2 },
            new Answer { QuestionId = passageQuestion.Id, Text = "Evet", Order = 1 },
        };
        ctx.Answers.AddRange(answers);
        await ctx.SaveChangesAsync();
        imageQuestion.CorrectAnswerId = answers[1].Id;
        passageQuestion.CorrectAnswerId = answers[2].Id;
        await ctx.SaveChangesAsync();
    }

    private async Task ExportFromSourceAsync(TestDb from, string sourceKey = "src")
    {
        var job = new QuestionTransferJob { Id = Guid.NewGuid(), Kind = QuestionTransferJobKind.Export, SourceKey = sourceKey, Message = "Queued", RequestJson = JsonSerializer.Serialize(new { SourceKey = sourceKey }) };
        await using (var seed = from.NewContext())
        {
            seed.Add(job);
            await seed.SaveChangesAsync();
        }
        await using var ctx = from.NewContext();
        await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunExportAsync(job.Id, ownerIsAdmin: true);
        _package.ShouldNotBeNull();
    }

    private async Task<QuestionTransferImportResultDto> ImportToAsync(TestDb to, string sourceKey)
    {
        var job = new QuestionTransferJob { Id = Guid.NewGuid(), Kind = QuestionTransferJobKind.Import, SourceKey = sourceKey, FileUrl = PackageUrl, Message = "Queued" };
        await using (var seed = to.NewContext())
        {
            seed.Add(job);
            await seed.SaveChangesAsync();
        }
        await using (var ctx = to.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunImportAsync(job.Id, ownerIsAdmin: true);

        await using var check = to.NewContext();
        var saved = await check.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
        saved.Status.ShouldBe(QuestionTransferJobStatus.Completed, saved.Message);
        return JsonSerializer.Deserialize<QuestionTransferImportResultDto>(saved.ResultJson!)!;
    }

    private static async Task<List<Question>> QuestionsAsync(TestDb db)
    {
        await using var ctx = db.NewContext();
        return await ctx.Questions.AsNoTracking().Include(q => q.Answers).Include(q => q.Passage).OrderBy(q => q.Text).ToListAsync();
    }

    [Fact]
    public async Task Exported_package_arrives_once_even_when_it_comes_again_under_another_source()
    {
        await SeedSourceAsync();
        await ExportFromSourceAsync(_source);

        var first = await ImportToAsync(_target, "first");
        first.Imported.ShouldBe(3, JsonSerializer.Serialize(first));
        first.Failed.ShouldBe(0);
        first.Warnings.ShouldBeEmpty();

        var second = await ImportToAsync(_target, "second");
        second.Matched.ShouldBe(3);
        second.Imported.ShouldBe(0);
        second.Conflicts.ShouldBe(0);
        (await QuestionsAsync(_target)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Hash_computed_in_the_source_equals_the_one_stored_in_the_target()
    {
        await SeedSourceAsync();
        await ExportFromSourceAsync(_source);
        await ImportToAsync(_target, "first");

        await using (var ctx = _source.NewContext())
            (await new QuestionContentHashService(ctx, _minio, Microsoft.Extensions.Logging.Abstractions.NullLogger<QuestionContentHashService>.Instance).FillMissingAsync()).Unresolved.ShouldBe(0);

        var source = await QuestionsAsync(_source);
        var target = await QuestionsAsync(_target);
        source.Select(q => (q.Text, q.ContentHash, q.ContentFingerprint)).ShouldBe(target.Select(q => (q.Text, q.ContentHash, q.ContentFingerprint)));
        source.ShouldAllBe(q => q.ContentHash != null);
    }

    [Fact]
    public async Task Hash_recomputed_in_the_target_stays_the_same_after_the_plan_was_shuffled_again()
    {
        await SeedSourceAsync();
        await ExportFromSourceAsync(_source);
        await ImportToAsync(_target, "first");

        foreach (var question in await QuestionsAsync(_target))
        {
            var image = string.IsNullOrWhiteSpace(question.ImageUrl) ? null : Image;
            var recomputed = QuestionContentHash.ForQuestion(question, image);
            recomputed.ContentHash.ShouldBe(question.ContentHash, question.Text);
            recomputed.Fingerprint.ShouldBe(question.ContentFingerprint, question.Text);
        }
    }

    [Fact]
    public async Task Package_exported_from_the_target_matches_in_the_source_too()
    {
        await SeedSourceAsync();
        await using (var ctx = _source.NewContext())
            await new QuestionContentHashService(ctx, _minio, Microsoft.Extensions.Logging.Abstractions.NullLogger<QuestionContentHashService>.Instance).FillMissingAsync();
        await ExportFromSourceAsync(_source);
        await ImportToAsync(_target, "first");

        _package = null;
        await ExportFromSourceAsync(_target);
        var back = await ImportToAsync(_source, "from-target");

        back.Matched.ShouldBe(3);
        back.Conflicts.ShouldBe(0);
        (await QuestionsAsync(_source)).Count.ShouldBe(3);
    }

    private string PackageOrigin()
    {
        using var zip = new ZipArchive(new MemoryStream(_package!), ZipArchiveMode.Read);
        using var doc = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        return QuestionContentHash.ReadPackageSourceKey(doc.RootElement)!;
    }

    [Fact]
    public async Task Question_without_identity_is_not_copied_when_its_environment_exports_it_again()
    {
        const string answerImage = "/img/exam-questions/answer.jpg";
        _minio.GetFileStreamAsync(answerImage).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(Image)));
        await using (var seed = _source.NewContext())
        {
            var question = new Question { Text = "Hangisi üçgendir?", Point = 5, DifficultyLevel = 1 };
            seed.Questions.Add(question);
            await seed.SaveChangesAsync();
            seed.Answers.Add(new Answer { QuestionId = question.Id, Text = "", ImageUrl = answerImage, Order = 1 });
            await seed.SaveChangesAsync();
        }

        await ExportFromSourceAsync(_source, "transfer-1");
        var origin = PackageOrigin();
        (await ImportToAsync(_target, origin)).Imported.ShouldBe(1);

        await ExportFromSourceAsync(_source, "transfer-2");
        PackageOrigin().ShouldBe(origin);
        var second = await ImportToAsync(_target, origin);

        second.AlreadyImported.ShouldBe(1);
        second.Imported.ShouldBe(0);
        (await QuestionsAsync(_target)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Export_writes_a_zip_with_the_question_image()
    {
        await SeedSourceAsync();
        await ExportFromSourceAsync(_source);

        using var zip = new ZipArchive(new MemoryStream(_package!), ZipArchiveMode.Read);
        zip.Entries.Count(e => e.FullName.EndsWith("/question.jpg", StringComparison.Ordinal)).ShouldBe(1);
    }
}
