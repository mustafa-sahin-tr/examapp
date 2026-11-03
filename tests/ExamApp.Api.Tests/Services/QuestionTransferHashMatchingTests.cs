using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

public class QuestionTransferHashMatchingTests : IDisposable
{
    private const string ZipUrl = "http://minio/in.zip";
    private static readonly byte[] ImageA = [1, 2, 3, 4];

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();

    public void Dispose() => _db.Dispose();

    private static object Answer(int id, string text, bool correct = false, int? order = null) =>
        new { id, text, tag = (string?)null, order, isCorrect = correct };

    private static byte[] Zip(IEnumerable<object> questions, Dictionary<int, byte[]>? images = null, string sourceKey = "src")
    {
        var manifest = JsonSerializer.Serialize(new { version = 3, sourceKey, questions });
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = zip.CreateEntry("manifest.json").Open())
                entry.Write(System.Text.Encoding.UTF8.GetBytes(manifest));
            foreach (var (id, bytes) in images ?? new())
            {
                using var entry = zip.CreateEntry($"questions/{id}/question.jpg").Open();
                entry.Write(bytes);
            }
        }
        return ms.ToArray();
    }

    private async Task<QuestionTransferJob> ImportAsync(byte[] zip, string sourceKey = "src")
    {
        _minio.GetFileStreamAsync(ZipUrl).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(zip)));
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>()).Returns("http://minio/uploaded.jpg");

        var job = new QuestionTransferJob { Id = Guid.NewGuid(), Kind = QuestionTransferJobKind.Import, SourceKey = sourceKey, FileUrl = ZipUrl, Message = "Queued" };
        await using (var seed = _db.NewContext())
        {
            seed.Add(job);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunImportAsync(job.Id, ownerIsAdmin: true);

        await using var check = _db.NewContext();
        return await check.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
    }

    private static QuestionTransferImportResultDto Result(QuestionTransferJob job) =>
        JsonSerializer.Deserialize<QuestionTransferImportResultDto>(job.ResultJson!)!;

    private static object ImageQuestion(int id, string correct) => new
    {
        externalKey = $"question:{id}", id, text = "Soru",
        answers = new[] { Answer(id * 10 + 1, "A", correct == "A", 1), Answer(id * 10 + 2, "B", correct == "B", 2) },
    };

    private static object TextQuestion(int id, string text) => new
    {
        externalKey = $"question:{id}", id, text, answers = new[] { Answer(id * 10 + 1, "Evet", true, 1) },
    };

    // ---- hash rules

    private static QuestionContentHashes Hash(byte[]? image = null, string? text = "Soru", string? plan = null, string? solution = null, params QuestionContentAnswer[] answers) =>
        QuestionContentHash.Compute(image, text, null, null, plan, solution, null, answers);

    [Fact]
    public void Image_question_is_identified_by_its_image_and_fingerprint_by_its_content()
    {
        var first = Hash(ImageA, "Bir");
        var second = Hash(ImageA, "İki");

        first.ContentHash.ShouldBe(second.ContentHash);
        first.Fingerprint.ShouldNotBe(second.Fingerprint);
    }

    [Fact]
    public void Text_question_is_identified_by_its_content()
    {
        var hash = Hash(null, "Metin sorusu");

        hash.ContentHash.ShouldBe(hash.Fingerprint);
        hash.ContentHash.ShouldNotBe(Hash(null, "Başka soru").ContentHash);
    }

    [Fact]
    public void Question_without_image_or_content_gets_no_identity()
    {
        Hash(null, "   ").ContentHash.ShouldBeNull();
    }

    [Fact]
    public void Answer_order_in_the_file_and_json_formatting_do_not_change_the_fingerprint()
    {
        var a = new QuestionContentAnswer("A", null, 1, true);
        var b = new QuestionContentAnswer("B", null, 2, false);

        Hash(answers: [a, b]).Fingerprint.ShouldBe(Hash(answers: [b, a]).Fingerprint);
        Hash(plan: """{"a":1,"b":[2]}""").Fingerprint.ShouldBe(Hash(plan: """ { "b": [2], "a": 1 } """).Fingerprint);
    }

    [Fact]
    public void Correct_answer_and_solution_change_the_fingerprint()
    {
        Hash(answers: [new("A", null, 1, true)]).Fingerprint.ShouldNotBe(Hash(answers: [new("A", null, 1, false)]).Fingerprint);
        Hash(solution: """{"x":1}""").Fingerprint.ShouldNotBe(Hash(solution: """{"x":2}""").Fingerprint);
    }

    // ---- import

    [Fact]
    public async Task New_question_is_imported_and_keeps_the_source_hash()
    {
        var job = await ImportAsync(Zip([ImageQuestion(1, "A")], new() { [1] = ImageA }));

        Result(job).Imported.ShouldBe(1);
        await using var check = _db.NewContext();
        var question = await check.Questions.SingleAsync();
        question.ContentHash.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(ImageA)));
        question.ContentFingerprint.ShouldNotBeNull();
    }

    [Fact]
    public async Task Same_content_under_another_source_key_creates_no_copy()
    {
        await ImportAsync(Zip([ImageQuestion(1, "A")], new() { [1] = ImageA }));

        var again = await ImportAsync(Zip([ImageQuestion(77, "A")], new() { [77] = ImageA }, "other"), "other");

        Result(again).Matched.ShouldBe(1);
        Result(again).Imported.ShouldBe(0);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(1);
        var map = await check.Set<QuestionTransferImportMap>().SingleAsync(m => m.SourceKey == "other");
        map.TargetQuestionId.ShouldBe((await check.Questions.SingleAsync()).Id);
    }

    [Fact]
    public async Task Same_image_with_another_answer_is_a_conflict_and_changes_nothing()
    {
        await ImportAsync(Zip([ImageQuestion(1, "A")], new() { [1] = ImageA }));
        int correctBefore;
        await using (var before = _db.NewContext())
            correctBefore = (await before.Questions.SingleAsync()).CorrectAnswerId!.Value;

        var job = await ImportAsync(Zip([ImageQuestion(2, "B")], new() { [2] = ImageA }, "other"), "other");

        var result = Result(job);
        result.Conflicts.ShouldBe(1);
        result.ConflictSamples.Single().ShouldContain("question:2");
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(1);
        (await check.Questions.SingleAsync()).CorrectAnswerId.ShouldBe(correctBefore);
        (await check.Set<QuestionTransferImportMap>().AnyAsync(m => m.SourceKey == "other")).ShouldBeFalse();
    }

    [Fact]
    public async Task Text_only_activity_is_imported_and_matched_on_a_second_import()
    {
        object Activity(int id) => new
        {
            externalKey = $"question:{id}", id, text = "Etkinlik", interactionType = "openText",
            interactionPlan = """{"maxLength":100}""", answers = Array.Empty<object>(),
        };

        (Result(await ImportAsync(Zip([Activity(1)])))).Imported.ShouldBe(1);
        var again = await ImportAsync(Zip([Activity(9)], sourceKey: "other"), "other");

        Result(again).Matched.ShouldBe(1);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Two_empty_questions_are_not_matched_to_each_other()
    {
        object Empty(int id) => new { externalKey = $"question:{id}", id, text = "", answers = Array.Empty<object>() };

        var job = await ImportAsync(Zip([Empty(1), Empty(2)]));

        Result(job).Imported.ShouldBe(2);
    }

    [Fact]
    public async Task Licence_publication_point_and_book_name_travel_with_the_question()
    {
        var question = new
        {
            externalKey = "question:1", id = 1, text = "Soru", point = 5, bookName = "Kitap", sourceType = "Publisher",
            rightsHolder = "Yayınevi", licenseReference = "S-1", licenseEndsAt = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            isUnpublished = true, answers = Array.Empty<object>(),
        };

        await ImportAsync(Zip([question]));

        await using var check = _db.NewContext();
        var saved = await check.Questions.SingleAsync();
        saved.SourceType.ShouldBe(QuestionSourceType.Publisher);
        saved.RightsHolder.ShouldBe("Yayınevi");
        saved.LicenseReference.ShouldBe("S-1");
        saved.LicenseEndsAt.ShouldNotBeNull();
        saved.IsUnpublished.ShouldBeTrue();
        saved.Point.ShouldBe(5);
        saved.BookName.ShouldBe("Kitap");
    }

    [Theory]
    [InlineData("""{"sourceType":"Robot"}""")]
    [InlineData("""{"sourceType":"99"}""")]
    [InlineData("""{}""")]
    public async Task Package_without_a_valid_source_type_stays_unknown(string fields)
    {
        var merged = JsonSerializer.Deserialize<Dictionary<string, object>>(fields)!;
        merged["externalKey"] = "question:1";
        merged["id"] = 1;
        merged["text"] = "Soru";
        merged["answers"] = Array.Empty<object>();

        await ImportAsync(Zip([merged]));

        await using var check = _db.NewContext();
        (await check.Questions.SingleAsync()).SourceType.ShouldBe(QuestionSourceType.Unknown);
    }

    private async Task<(int SubjectId, int TopicId, int SubTopicId)> SeedCatalogAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf", Code = "5-sinif" };
        var subject = new Subject { Name = "Yeni Ad", Code = "matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();
        var topic = new Topic { Name = "Yeni Konu", Code = "5-sinif.matematik.sayilar", GradeId = grade.Id, SubjectId = subject.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();
        var subTopic = new SubTopic { Name = "Yeni Alt Konu", Code = "5-sinif.matematik.sayilar.sayma", TopicId = topic.Id };
        ctx.SubTopics.Add(subTopic);
        await ctx.SaveChangesAsync();
        return (subject.Id, topic.Id, subTopic.Id);
    }

    [Fact]
    public async Task Taxonomy_matches_by_code_even_after_a_rename()
    {
        var ids = await SeedCatalogAsync();
        var question = new
        {
            externalKey = "question:1", id = 1, text = "Soru", answers = Array.Empty<object>(),
            subject = new { code = "matematik", name = "Eski Ad" },
            topic = new { code = "5-sinif.matematik.sayilar", name = "Eski Konu", gradeName = "5. Sınıf" },
            subTopics = new[] { new { code = "5-sinif.matematik.sayilar.sayma", name = "Eski Alt Konu", topicName = "Eski Konu", gradeName = "5. Sınıf" } },
        };

        var job = await ImportAsync(Zip([question]));

        Result(job).MissingTaxonomy.ShouldBe(0);
        await using var check = _db.NewContext();
        var saved = await check.Questions.Include(q => q.QuestionSubTopics).SingleAsync();
        saved.SubjectId.ShouldBe(ids.SubjectId);
        saved.TopicId.ShouldBe(ids.TopicId);
        saved.QuestionSubTopics.Single().SubTopicId.ShouldBe(ids.SubTopicId);
    }

    [Fact]
    public async Task Old_package_without_codes_still_matches_by_name()
    {
        var ids = await SeedCatalogAsync();
        var question = new
        {
            externalKey = "question:1", id = 1, text = "Soru", answers = Array.Empty<object>(),
            subject = new { name = "yeni ad" },
            topic = new { name = "yeni konu", gradeName = "5. Sınıf" },
        };

        await ImportAsync(Zip([question]));

        await using var check = _db.NewContext();
        var saved = await check.Questions.SingleAsync();
        saved.SubjectId.ShouldBe(ids.SubjectId);
        saved.TopicId.ShouldBe(ids.TopicId);
    }

    [Fact]
    public async Task Topic_name_lookup_stays_inside_the_subject()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "6. Sınıf" };
            var turkish = new Subject { Name = "Türkçe" };
            var math = new Subject { Name = "Matematik" };
            ctx.AddRange(grade, turkish, math);
            await ctx.SaveChangesAsync();
            ctx.Topics.Add(new Topic { Name = "Okuma", GradeId = grade.Id, SubjectId = turkish.Id });
            ctx.Topics.Add(new Topic { Name = "Okuma", GradeId = grade.Id, SubjectId = math.Id });
            await ctx.SaveChangesAsync();
        }
        var question = new
        {
            externalKey = "question:1", id = 1, text = "Soru", answers = Array.Empty<object>(),
            subject = new { name = "Matematik" },
            topic = new { name = "Okuma", gradeName = "6. Sınıf" },
        };

        await ImportAsync(Zip([question]));

        await using var check = _db.NewContext();
        var saved = await check.Questions.Include(q => q.Topic).ThenInclude(t => t.Subject).SingleAsync();
        saved.Topic.Subject.Name.ShouldBe("Matematik");
    }

    [Fact]
    public async Task Missing_taxonomy_is_reported()
    {
        var question = new
        {
            externalKey = "question:1", id = 1, text = "Soru", answers = Array.Empty<object>(),
            subject = new { code = "yok", name = "Yok" },
        };

        var job = await ImportAsync(Zip([question]));

        Result(job).MissingTaxonomy.ShouldBe(1);
        job.Message!.ShouldContain("Missing taxonomy 1");
    }

    [Fact]
    public async Task Match_fills_blank_classification_but_keeps_what_the_target_has()
    {
        var ids = await SeedCatalogAsync();
        await ImportAsync(Zip([TextQuestion(1, "Soru metni")]));
        var question = new
        {
            externalKey = "question:2", id = 2, text = "Soru metni",
            answers = new[] { Answer(21, "Evet", true, 1) },
            subject = new { code = "matematik", name = "x" },
            topic = new { code = "5-sinif.matematik.sayilar", name = "x", gradeName = "x" },
        };

        var job = await ImportAsync(Zip([question], sourceKey: "other"), "other");

        Result(job).Matched.ShouldBe(1);
        await using var check = _db.NewContext();
        var saved = await check.Questions.SingleAsync();
        saved.SubjectId.ShouldBe(ids.SubjectId);
        saved.TopicId.ShouldBe(ids.TopicId);
    }

    [Fact]
    public async Task A_failed_question_does_not_break_the_next_one_that_shares_its_passage()
    {
        object WithPassage(int id, object[] answers) => new
        {
            externalKey = $"question:{id}", id, text = $"Soru {id}", answers,
            passage = new { externalKey = "passage:1", id = 1, title = "P", text = "Metin", kind = 0 },
        };
        var broken = WithPassage(1, [new { text = "cevap kimliği yok" }]);
        var good = WithPassage(2, [Answer(21, "A", true, 1)]);

        var job = await ImportAsync(Zip([broken, good]));

        var result = Result(job);
        job.Status.ShouldBe(QuestionTransferJobStatus.Completed, job.Message);
        result.Failed.ShouldBe(1);
        result.Imported.ShouldBe(1);
        await using var check = _db.NewContext();
        var saved = await check.Questions.Include(q => q.Passage).SingleAsync();
        saved.Text.ShouldBe("Soru 2");
        saved.Passage.ShouldNotBeNull();
        (await check.Set<QuestionTransferImportMap>().CountAsync()).ShouldBe(1);
    }

    // ---- preview

    private QuestionTransferService Service(AppDbContext ctx) =>
        new(ctx, _minio, _jobs, new ConfigurationBuilder().Build());

    [Fact]
    public async Task Preview_counts_new_matched_conflicting_and_missing_taxonomy_without_writing()
    {
        await ImportAsync(Zip([ImageQuestion(1, "A")], new() { [1] = ImageA }));
        var missing = new
        {
            externalKey = "question:4", id = 4, text = "Yeni", answers = Array.Empty<object>(),
            subject = new { name = "Yok" },
        };
        var zip = Zip(
            [ImageQuestion(2, "A"), ImageQuestion(3, "B"), missing, ImageQuestion(1, "A")],
            new() { [1] = ImageA, [2] = ImageA, [3] = ImageA },
            "other");

        await using var ctx = _db.NewContext();
        var preview = await Service(ctx).PreviewImportAsync(new FormFile(new MemoryStream(zip), 0, zip.Length, "file", "p.zip"), "other", default);

        preview.QuestionCount.ShouldBe(4);
        preview.NewCount.ShouldBe(1);
        preview.MatchedCount.ShouldBe(2);
        preview.ConflictCount.ShouldBe(1);
        preview.MissingTaxonomyCount.ShouldBe(1);
        preview.ConflictSamples.Single().ShouldBe("question:3");
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Preview_reports_target_questions_that_have_no_hash_yet()
    {
        await using (var seed = _db.NewContext())
        {
            seed.Questions.Add(new Question { Text = "Eski", Point = 1, DifficultyLevel = 1 });
            await seed.SaveChangesAsync();
        }
        var zip = Zip([TextQuestion(1, "Yeni")]);

        await using var ctx = _db.NewContext();
        var preview = await Service(ctx).PreviewImportAsync(new FormFile(new MemoryStream(zip), 0, zip.Length, "file", "p.zip"), null, default);

        preview.TargetUnhashedCount.ShouldBe(1);
        _jobs.ReceivedCalls().ShouldNotBeEmpty();
    }

    // ---- stored hash lifecycle

    private async Task<int> SeedHashedQuestionAsync()
    {
        await using var ctx = _db.NewContext();
        var question = new Question { Text = "Soru", Point = 1, DifficultyLevel = 1 };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        ctx.Answers.Add(new Answer { QuestionId = question.Id, Text = "A" });
        await ctx.SaveChangesAsync();
        // Şık eklemek hash'i sıfırladığı için en son, doğrudan yazılır.
        await ctx.Questions.Where(q => q.Id == question.Id).ExecuteUpdateAsync(s => s
            .SetProperty(q => q.ContentHash, "h")
            .SetProperty(q => q.ContentFingerprint, "f"));
        return question.Id;
    }

    [Fact]
    public async Task Editing_the_question_clears_the_hash()
    {
        var id = await SeedHashedQuestionAsync();

        await using (var ctx = _db.NewContext())
        {
            (await ctx.Questions.SingleAsync(q => q.Id == id)).Text = "Değişti";
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var saved = await check.Questions.SingleAsync(q => q.Id == id);
        saved.ContentHash.ShouldBeNull();
        saved.ContentFingerprint.ShouldBeNull();
    }

    [Fact]
    public async Task Editing_an_answer_clears_the_hash()
    {
        var id = await SeedHashedQuestionAsync();

        await using (var ctx = _db.NewContext())
        {
            var question = await ctx.Questions.Include(q => q.Answers).SingleAsync(q => q.Id == id);
            question.Answers.Single().Text = "Değişti";
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        (await check.Questions.SingleAsync(q => q.Id == id)).ContentFingerprint.ShouldBeNull();
    }

    [Fact]
    public async Task Changing_classification_keeps_the_hash()
    {
        var id = await SeedHashedQuestionAsync();

        await using (var ctx = _db.NewContext())
        {
            (await ctx.Questions.SingleAsync(q => q.Id == id)).DifficultyLevel = 5;
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        (await check.Questions.SingleAsync(q => q.Id == id)).ContentHash.ShouldBe("h");
    }

    // ---- backfill

    private async Task<int> FillAsync()
    {
        await using var ctx = _db.NewContext();
        return await new QuestionContentHashService(ctx, _minio, NullLogger<QuestionContentHashService>.Instance).FillMissingAsync();
    }

    [Fact]
    public async Task Backfill_hashes_image_and_text_questions_without_touching_audit_fields()
    {
        int imageId, textId;
        await using (var seed = _db.NewContext())
        {
            var image = new Question { Text = "G", ImageUrl = "http://minio/a.jpg", Point = 1, DifficultyLevel = 1 };
            var text = new Question { Text = "Metin", Point = 1, DifficultyLevel = 1 };
            seed.AddRange(image, text);
            await seed.SaveChangesAsync();
            imageId = image.Id;
            textId = text.Id;
        }
        _minio.GetFileStreamAsync("http://minio/a.jpg").Returns(_ => Task.FromResult<Stream?>(new MemoryStream(ImageA)));

        (await FillAsync()).ShouldBe(2);

        await using var check = _db.NewContext();
        var savedImage = await check.Questions.SingleAsync(q => q.Id == imageId);
        savedImage.ContentHash.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(ImageA)));
        savedImage.UpdateTime.ShouldBeNull();
        var savedText = await check.Questions.SingleAsync(q => q.Id == textId);
        savedText.ContentHash.ShouldBe(savedText.ContentFingerprint);
    }

    [Fact]
    public async Task Backfilled_question_matches_the_same_question_in_a_package()
    {
        await using (var seed = _db.NewContext())
        {
            var question = new Question { Text = "Soru", ImageUrl = "http://minio/a.jpg", Point = 1, DifficultyLevel = 1 };
            seed.Questions.Add(question);
            await seed.SaveChangesAsync();
            var a = new Answer { QuestionId = question.Id, Text = "A", Order = 1 };
            var b = new Answer { QuestionId = question.Id, Text = "B", Order = 2 };
            seed.Answers.AddRange(a, b);
            await seed.SaveChangesAsync();
            question.CorrectAnswerId = a.Id;
            await seed.SaveChangesAsync();
        }
        _minio.GetFileStreamAsync("http://minio/a.jpg").Returns(_ => Task.FromResult<Stream?>(new MemoryStream(ImageA)));

        var job = await ImportAsync(Zip([ImageQuestion(5, "A")], new() { [5] = ImageA }));

        Result(job).Matched.ShouldBe(1);
        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Question_with_an_unreadable_image_gets_no_identity_and_is_not_retried()
    {
        await using (var seed = _db.NewContext())
        {
            seed.Questions.Add(new Question { Text = "Soru", ImageUrl = "http://minio/gone.jpg", Point = 1, DifficultyLevel = 1 });
            await seed.SaveChangesAsync();
        }
        _minio.GetFileStreamAsync("http://minio/gone.jpg").Returns(Task.FromResult<Stream?>(null));

        (await FillAsync()).ShouldBe(1);
        (await FillAsync()).ShouldBe(0);

        await using var check = _db.NewContext();
        var saved = await check.Questions.SingleAsync();
        saved.ContentHash.ShouldBeNull();
        saved.ContentFingerprint.ShouldNotBeNull();
    }

    [Fact]
    public async Task Backfill_stops_without_writing_when_storage_looks_down()
    {
        await using (var seed = _db.NewContext())
        {
            for (var i = 0; i < 25; i++)
                seed.Questions.Add(new Question { Text = $"S{i}", ImageUrl = $"http://minio/{i}.jpg", Point = 1, DifficultyLevel = 1 });
            await seed.SaveChangesAsync();
        }
        _minio.GetFileStreamAsync(Arg.Any<string>()).Returns(Task.FromResult<Stream?>(null));

        (await FillAsync()).ShouldBe(0);

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync(q => q.ContentFingerprint == null)).ShouldBe(25);
    }

    // ---- export scope

    [Fact]
    public async Task Export_scope_with_no_questions_does_not_become_export_all()
    {
        await using (var seed = _db.NewContext())
        {
            seed.Questions.Add(new Question { Text = "Soru", Point = 1, DifficultyLevel = 1 });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var job = await Service(ctx).StartExportAsync(new StartQuestionExportDto { SubjectId = 999 }, new QuestionTransferOwner(1, true), default);

        job.Status.ShouldBe(nameof(QuestionTransferJobStatus.Completed));
        job.Message!.ShouldContain("No questions");
        _jobs.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_scope_finds_questions_by_subject_and_by_the_grade_of_their_activity()
    {
        int matchId;
        int gradeId;
        await using (var seed = _db.NewContext())
        {
            var grade = new Grade { Name = "5" };
            seed.Grades.Add(grade);
            var subject = new Subject { Name = "M" };
            seed.Subjects.Add(subject);
            await seed.SaveChangesAsync();
            gradeId = grade.Id;
            var worksheet = new Worksheet { Name = "E", Description = "", GradeId = grade.Id };
            seed.Add(worksheet);
            var match = new Question { Text = "İçinde", SubjectId = subject.Id, Point = 1, DifficultyLevel = 1 };
            var other = new Question { Text = "Dışında", Point = 1, DifficultyLevel = 1 };
            seed.Questions.AddRange(match, other);
            await seed.SaveChangesAsync();
            matchId = match.Id;
            seed.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = match.Id, Order = 1 });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var request = new StartQuestionExportDto { GradeId = gradeId };
        await Service(ctx).StartExportAsync(request, new QuestionTransferOwner(1, true), default);

        request.QuestionIds.ShouldBe([matchId]);
    }
}
