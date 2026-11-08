using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1022: soru aktarımı aşaması — sayfa bazlı numaralama, anahtardan cevap, inceleme işaretleri, bir sayfanın / testin
/// hatasının diğerlerini durdurmaması, kırpma tekrarlarının sayılması, kaldığı testten devam ve maliyet.
/// </summary>
public class BookQuestionImportTests : IDisposable
{
    private const int OwnerId = 41;
    private const int PageWidth = 2363;
    private const int PageHeight = 3300;
    private const string Model = BookImportJobRunner.DefaultDetectModel;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly ILabelingService _labeling = Substitute.For<ILabelingService>();
    private readonly ILabelingBankService _bank = Substitute.For<ILabelingBankService>();
    private readonly ServiceProvider _services;

    /// <summary>Dosya sayfası → tespit edilen sorular (her biri şık sayısıyla); listede olmayan sayfa tespitte hata verir.</summary>
    private readonly Dictionary<int, List<PredictionDto>> _detections = new();

    private readonly List<LabelingImportPageRequest> _saved = new();
    private readonly HashSet<string> _duplicateItems = new();
    private int _bookId;
    private int _gradeId;

    public BookQuestionImportTests()
    {
        _minio.GetFileStreamAsync(Arg.Any<string>())
            .Returns(ci => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(ci.Arg<string>()))));
        _labeling.GetAiModels().Returns([new LabelingAiModelDto(Model, "Haiku", 1, 5, true)]);
        _labeling.DetectForImportAsync(Arg.Any<string>(), Model, Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var url = Encoding.UTF8.GetString(Convert.FromBase64String(ci.ArgAt<string>(0).Split(',')[1]));
            var page = int.Parse(url[(url.LastIndexOf('/') + 1)..]);
            if (!_detections.TryGetValue(page, out var predictions))
                throw new LabelingAiException("Claude response could not be used.");
            var response = new LabelingAiDetectResponseDto("live", new string('a', 64), new LabelingResultDto("Ai", Model,
                new PredictionListDto { Predictions = predictions }, null, null, 10, 10, 0.01m, 0, null, DateTime.UtcNow,
                DateTime.UtcNow, null));
            return Task.FromResult(new LabelingImportDetection(response, 0.01m));
        });
        _bank.SaveImportedPageAsync(Arg.Any<LabelingImportPageRequest>(), OwnerId, Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var request = ci.Arg<LabelingImportPageRequest>();
            _saved.Add(request);
            var duplicates = request.Questions.Select((q, i) => (q, i)).Where(x => _duplicateItems.Contains(x.q.ItemLabel!))
                .Select(x => x.i).ToList();
            return Task.FromResult(new LabelingImportPageResult(request.Questions.Count - duplicates.Count, duplicates));
        });
        _services = new ServiceCollection().AddScoped(_ => _bank).BuildServiceProvider();
    }

    [Fact]
    public async Task Tests_are_imported_in_order_with_page_anchored_numbers_and_answers_from_the_key()
    {
        var id = await SeedAsync(
            [new(1, 1, 2, "BCAD", [2, 4]), new(2, 3, 3, "DA", [2])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        Detect(3, 4, 4);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.ReadyForReview);
        job.Stage.ShouldBe(BookImportStage.Review);
        job.StageTotal.ShouldBe(2);
        job.StageProcessed.ShouldBe(2);
        job.AiCostUsd.ShouldBe(0.03m);

        var tests = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        tests.ShouldAllBe(t => t.Status == BookImportTestStatus.Done && t.ReviewFlagsJson == null);
        tests[0].QuestionsFound.ShouldBe(4);
        tests[0].QuestionsSaved.ShouldBe(4);

        _saved.Select(s => s.FilePage).ShouldBe([1, 2, 3]);
        _saved.ShouldAllBe(s => s.BookId == _bookId && s.GradeId == _gradeId && s.Sharing == QuestionBankSharing.Private);
        var first = _saved.Where(s => s.FilePage <= 2).SelectMany(s => s.Questions).ToList();
        first.Select(q => q.ItemLabel).ShouldBe(["1", "2", "3", "4"]);
        first.Select(q => q.CorrectOptionIndex).ShouldBe([1, 2, 0, 3]);
        first.ShouldAllBe(q => q.ActivityLabel == "1-01 · Test 1" && q.Options.Count == 4);
        _saved[2].Questions.Select(q => q.CorrectOptionIndex).ShouldBe([3, 0]);
    }

    [Fact]
    public async Task A_missed_question_is_flagged_on_its_page_and_does_not_shift_the_next_page()
    {
        var id = await SeedAsync([new(1, 1, 2, "BCAD", [2, 4])]);
        Detect(1, 4);
        Detect(2, 4, 4);

        await RunAsync(id);

        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.NeedsReview);
        test.QuestionsFound.ShouldBe(3);
        Flags(test).ShouldBe([
            F(BookImportReviewFlag.MissingNumbers, [1], [2]),
            F(BookImportReviewFlag.CountMismatch, [1, 2], null, 3, 4),
        ], ignoreOrder: true);
        _saved[1].Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("3", 0), ("4", 3)]);
    }

    [Fact]
    public async Task Option_count_and_missing_answers_are_flagged_and_one_option_questions_are_not_saved()
    {
        var id = await SeedAsync([new(1, 1, 1, null, [3])]);
        Detect(1, 4, 3, 1);

        await RunAsync(id);

        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.NeedsReview);
        Flags(test).ShouldBe([
            F(BookImportReviewFlag.NoAnswer, [1], [1, 2]),
            F(BookImportReviewFlag.OptionCount, [1], [2, 3]),
            F(BookImportReviewFlag.NotSaved, [1], [3]),
        ], ignoreOrder: true);
        _saved.ShouldHaveSingleItem().Questions.Select(q => q.CorrectOptionIndex).ShouldBe([-1, -1]);
        test.QuestionsSaved.ShouldBe(2);
    }

    [Fact]
    public async Task A_failing_page_and_a_failing_test_do_not_stop_the_other_tests()
    {
        var id = await SeedAsync(
            [new(1, 1, 2, "AB", [1, 2]), new(2, 3, 3, "C", [1]), new(3, 4, 4, "D", [1])]);
        Detect(2, 4); // sayfa 1 ve 3 tespitte hata verir
        Detect(4, 4);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var tests = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        tests.Select(t => t.Status).ShouldBe([BookImportTestStatus.NeedsReview, BookImportTestStatus.Failed, BookImportTestStatus.Done]);
        Flags(tests[0]).ShouldContain(F(BookImportReviewFlag.DetectionFailed, [1]));
        Flags(tests[0]).ShouldContain(F(BookImportReviewFlag.MissingNumbers, [1], [1]));
        _saved.First().Questions.ShouldHaveSingleItem().ItemLabel.ShouldBe("2"); // sayfa 2 hâlâ 2'den başlar
        tests[1].Error.ShouldNotBeNull();
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
    }

    [Fact]
    public async Task Crop_duplicates_are_counted_not_failed()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        Detect(1, 4, 4);
        _duplicateItems.Add("2");

        await RunAsync(id);

        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.Done);
        test.QuestionsSaved.ShouldBe(1);
        test.DuplicatesSkipped.ShouldBe(1);
    }

    [Fact]
    public async Task Finished_tests_are_skipped_when_the_job_resumes()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 2, "B", [1])]);
        await using (var ctx = _db.NewContext())
        {
            var done = await ctx.BookImportJobTests.AsTracking().SingleAsync(t => t.Order == 1);
            done.Status = BookImportTestStatus.Done;
            await ctx.SaveChangesAsync();
        }
        Detect(2, 4);

        await RunAsync(id);

        _saved.ShouldHaveSingleItem().FilePage.ShouldBe(2);
        await _labeling.Received(1).DetectForImportAsync(Arg.Any<string>(), Model, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_multiple_choice_blocks_are_imported_and_a_stem_split_from_its_options_is_merged()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        _detections[1] =
        [
            new PredictionDto { X = 100, Y = 100, Width = 900, Height = 300 }, // kök, şıksız
            Question(110, 410, 4),                                              // hemen altında şıklar
            new PredictionDto { Kind = LabelingBlocks.InfoBox, X = 100, Y = 900, Width = 900, Height = 200 },
            Question(1300, 100, 4),
        ];

        await RunAsync(id);

        var test = await SingleTestAsync();
        test.QuestionsFound.ShouldBe(2);
        test.Status.ShouldBe(BookImportTestStatus.Done);
        var merged = _saved.Single().Questions[0];
        merged.Y.ShouldBe(100);
    }

    [Fact]
    public async Task Missing_detection_model_fails_the_job_with_a_readable_error()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1])]);
        _labeling.GetAiModels().Returns([]);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Failed);
        job.Error.ShouldBe("Claude is not configured (Anthropic:ApiKey is empty).");
    }

    [Fact]
    public void Draft_questions_are_hidden_unless_explicitly_included()
    {
        using var ctx = _db.NewContext();
        ctx.Questions.AddRange(new Question { Text = "aktif" }, new Question { Text = "taslak", IsDraft = true });
        ctx.SaveChanges();

        using var check = _db.NewContext();
        check.Questions.Select(q => q.Text).ToList().ShouldBe(["aktif"]);
        check.Questions.WithDrafts().Select(q => q.Text).OrderBy(t => t).ToList().ShouldBe(["aktif", "taslak"]);
        // Taslağı açmak soft-delete filtresini kapatmaz.
        var deleted = check.Questions.WithDrafts().AsTracking().First(q => q.Text == "aktif");
        deleted.IsDeleted = true;
        check.SaveChanges();
        check.Questions.WithDrafts().Select(q => q.Text).ToList().ShouldBe(["taslak"]);
    }

    // ---------- yardımcılar ----------

    private sealed record TestSeed(int Order, int FileFrom, int FileTo, string? Key, int[] PageMaxNumbers);

    private static PredictionDto Question(int x, int y, int options) => new()
    {
        X = x, Y = y, Width = 900, Height = 400, Kind = LabelingBlocks.McqQuestion,
        Subpredictions = Enumerable.Range(0, options)
            .Select(i => new PredictionDto { X = x + 20 + i % 2 * 440, Y = y + 200 + i / 2 * 90, Width = 400, Height = 60 })
            .ToList(),
    };

    /// <summary>Sayfada yukarıdan aşağı, verilen şık sayılarıyla sorular (sol sütun).</summary>
    private void Detect(int filePage, params int[] optionCounts) =>
        _detections[filePage] = optionCounts.Select((n, i) => Question(100, 100 + i * 700, n)).ToList();

    private async Task<Guid> SeedAsync(IReadOnlyList<TestSeed> tests)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Grades.Add(grade);
        var book = new Book { Name = "Puan BTS 5 Matematik" };
        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();
        _gradeId = grade.Id;
        _bookId = book.Id;

        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            BookName = book.Name,
            BookId = book.Id,
            GradeId = grade.Id,
            Status = BookImportJobStatus.Queued,
            Stage = BookImportStage.Questions,
            FileUrl = "http://minio/source.pdf",
        };
        ctx.SetCurrentUser(OwnerId);
        ctx.BookImportJobs.Add(job);
        foreach (var t in tests)
        {
            ctx.BookImportJobTests.Add(new BookImportJobTest
            {
                JobId = job.Id, Order = t.Order, Unit = 1, TestNo = t.Order, Title = $"Test {t.Order}",
                FileFrom = t.FileFrom, FileTo = t.FileTo, AnswerKey = t.Key, KeyAnswers = t.Key?.Length ?? 0,
            });
            for (var page = t.FileFrom; page <= t.FileTo; page++)
            {
                ctx.BookImportPages.Add(new BookImportPage
                {
                    JobId = job.Id, FilePage = page, ImageUrl = $"http://minio/pages/{page}", Width = PageWidth, Height = PageHeight,
                    MaxQuestionNumber = t.PageMaxNumbers[page - t.FileFrom],
                });
            }
        }
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    private async Task RunAsync(Guid id)
    {
        var guard = Substitute.For<IApprovedTeacherGuard>();
        guard.CheckAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(TeacherApprovalCheck.NoTeacherProfile);
        await using var ctx = _db.NewContext();
        await new BookImportJobRunner(ctx, guard, _minio, Substitute.For<IPdfRasterizer>(), new FakeBookSetupAi(), _labeling,
            _services.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build()).RunLockedAsync(id);
    }

    private async Task<BookImportJobTest> SingleTestAsync()
    {
        await using var check = _db.NewContext();
        return await check.BookImportJobTests.SingleAsync();
    }

    private static List<string> Flags(BookImportJobTest test) =>
        BookImportReviewFlag.Parse(test.ReviewFlagsJson).Select(f => F(f.Code, f.Pages, f.Numbers, f.Found, f.Expected)).ToList();

    private static string F(string code, IEnumerable<int> pages, IEnumerable<int>? numbers = null, int? found = null, int? expected = null) =>
        $"{code} p{string.Join(",", pages)} n{string.Join(",", numbers ?? [])} {found}/{expected}";

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
    }
}
