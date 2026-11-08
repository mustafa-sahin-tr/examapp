using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using Hangfire;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            return Task.FromResult(new LabelingImportDetection(response, 0.01m, new LabelingAiUsage(1000, 100)));
        });
        _bank.SaveImportedPageAsync(Arg.Any<LabelingImportPageRequest>(), OwnerId, Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var request = ci.Arg<LabelingImportPageRequest>();
            _saved.Add(request);
            var duplicates = request.Questions.Select((q, i) => (q, i)).Where(x => _duplicateItems.Contains(x.q.ItemLabel!))
                .Select(x => x.i).ToList();
            // Bankaya kayıt gibi: kaydedilen her soru taslak bir satır (şıklar, anahtardaki doğru şık, kitap kaynağı).
            using var ctx = _db.NewContext();
            ctx.SetCurrentUser(OwnerId);
            var created = request.Questions.Where((_, i) => !duplicates.Contains(i)).Select(q =>
            {
                var question = new Question { Text = q.ItemLabel!, IsDraft = true };
                question.Answers = Enumerable.Range(0, q.Options.Count)
                    .Select(o => new Answer { Text = ((char)('A' + o)).ToString(), Order = o }).ToList();
                ctx.Questions.Add(question);
                ctx.QuestionSources.Add(new QuestionSource
                {
                    Question = question, BookId = request.BookId, FilePage = request.FilePage, ItemLabel = q.ItemLabel,
                    CropSha256 = Guid.NewGuid().ToString("N"),
                });
                return (question, q.CorrectOptionIndex);
            }).ToList();
            ctx.SaveChanges();
            foreach (var (question, correct) in created.Where(c => c.CorrectOptionIndex >= 0))
                question.CorrectAnswerId = question.Answers.ElementAt(correct).Id;
            ctx.SaveChanges();
            return Task.FromResult(new LabelingImportPageResult(created.Count, duplicates, created.Select(c => c.question.Id).ToList()));
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
    public async Task Each_paid_call_is_recorded_per_test_and_charged_to_the_import_budget_and_only_admin_sees_the_breakdown()
    {
        var id = await SeedAsync([new(1, 1, 2, "AB", [1, 2]), new(2, 3, 3, "C", [3])]);
        Detect(1, 4);
        Detect(2, 4);
        Detect(3, 4);
        var store = new InMemoryFixedWindowCounterStore();

        await RunAsync(id, store);

        var tests = await TestsAsync();
        await using (var check = _db.NewContext())
        {
            var costs = await check.BookImportCosts.ToListAsync();
            costs.Count.ShouldBe(3);
            costs.ShouldAllBe(c => c.Stage == BookImportStage.Questions && c.ModelId == Model && c.InputTokens == 1000 && c.OutputTokens == 100);
            costs.Count(c => c.TestId == tests[0].Id).ShouldBe(2);
            (await check.BookImportJobs.SingleAsync()).AiCostUsd.ShouldBe(0.03m);
        }
        (await store.PeekAsync(LabelingAiQuota.ImportBudgetKey)).Count.ShouldBe(30_000);
        (await store.PeekAsync(LabelingAiQuota.TotalBudgetKey)).Count.ShouldBe(30_000);

        await using var ctx = _db.NewContext();
        var admin = (await Service(ctx).GetAsync(Admin, id, CancellationToken.None))!;
        admin.AiCostUsd.ShouldBe(0.03m);
        var row = admin.Costs.ShouldHaveSingleItem();
        (row.Stage, row.ModelId, row.Calls, row.InputTokens, row.CostUsd).ShouldBe(("Questions", Model, 3, 3000L, 0.03m));
        admin.Tests.Select(t => t.CostUsd).ShouldBe([0.02m, 0.01m]);

        var owner = (await Service(ctx).GetAsync(Owner, id, CancellationToken.None))!;
        owner.AiCostUsd.ShouldBeNull();
        owner.Costs.ShouldBeEmpty();
        owner.Tests.ShouldAllBe(t => t.CostUsd == null);
    }

    [Fact]
    public async Task A_full_import_budget_pauses_the_job_and_the_unfinished_test_is_redone_when_it_resumes()
    {
        var id = await SeedAsync([new(1, 1, 2, "AB", [1, 2]), new(2, 3, 3, "C", [3])]);
        Detect(1, 4);
        Detect(2, 4);
        Detect(3, 4);
        var jobs = Substitute.For<IBackgroundJobClient>();

        // İki çağrıdan (0.02 $) sonra aktarım bütçesi (0.015 $) dolu: ikinci testten önce iş durur.
        await RunAsync(id, new InMemoryFixedWindowCounterStore(), new LabelingAiOptions { DailyBudgetUsdImport = 0.015m }, jobs);

        await using (var check = _db.NewContext())
        {
            var job = await check.BookImportJobs.SingleAsync();
            job.Status.ShouldBe(BookImportJobStatus.AwaitingBudget);
            job.ResumeAt.ShouldNotBeNull();
            job.AiCostUsd.ShouldBe(0.02m);
            (await check.BookImportCosts.CountAsync()).ShouldBe(2);
        }
        jobs.Received(1).Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<Hangfire.States.ScheduledState>());
        (await TestsAsync()).Select(t => t.Status).ShouldBe([BookImportTestStatus.Done, BookImportTestStatus.Running]);

        // Pencere yenilendi (yeni sayaç): iş kaldığı testten devam eder.
        await RunAsync(id, new InMemoryFixedWindowCounterStore());

        await using var after = _db.NewContext();
        var resumed = await after.BookImportJobs.SingleAsync();
        resumed.Status.ShouldBe(BookImportJobStatus.ReadyForReview);
        resumed.ResumeAt.ShouldBeNull();
        resumed.AiCostUsd.ShouldBe(0.03m);
        _saved.Select(s => s.FilePage).ShouldBe([1, 2, 3]);
        (await TestsAsync()).ShouldAllBe(t => t.Status == BookImportTestStatus.Done);
    }

    [Fact]
    public async Task Exceeding_the_job_cost_ceiling_waits_until_an_admin_continues_with_a_raised_ceiling()
    {
        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BookImport:MaxJobCostUsd"] = "0.015",
        }).Build();
        var id = await SeedAsync([new(1, 1, 2, "AB", [1, 2]), new(2, 3, 3, "C", [3])]);
        Detect(1, 4);
        Detect(2, 4);
        Detect(3, 4);
        var runnerJobs = Substitute.For<IBackgroundJobClient>();

        await RunAsync(id, jobs: runnerJobs);

        await using (var check = _db.NewContext())
        {
            var job = await check.BookImportJobs.SingleAsync();
            job.Status.ShouldBe(BookImportJobStatus.AwaitingCostApproval);
            job.AiCostUsd.ShouldBe(0.02m);
        }
        runnerJobs.ReceivedCalls().ShouldBeEmpty();

        var jobs = Substitute.For<IBackgroundJobClient>();
        await using (var ctx = _db.NewContext())
        {
            (await Service(ctx, jobs).ContinueAsync(Owner, id, CancellationToken.None)).ShouldBe(ContinueBookImportResult.NotFound);
            (await Service(ctx, jobs).ContinueAsync(Admin, id, CancellationToken.None)).ShouldBe(ContinueBookImportResult.Continued);
            (await Service(ctx, jobs).ContinueAsync(Admin, id, CancellationToken.None)).ShouldBe(ContinueBookImportResult.InvalidState);
        }
        jobs.Received(1).Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<Hangfire.States.EnqueuedState>());
        await using (var check = _db.NewContext())
        {
            var job = await check.BookImportJobs.SingleAsync();
            job.Status.ShouldBe(BookImportJobStatus.Queued);
            job.CostCeilingUsd.ShouldBe(0.035m);
        }

        await RunAsync(id);

        await using var after = _db.NewContext();
        (await after.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
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
    public async Task Each_test_gets_a_hidden_draft_worksheet_with_its_questions_in_order()
    {
        var id = await SeedAsync([new(1, 1, 2, "ABC", [2, 3]), new(2, 3, 3, "D", [1])]);
        Detect(1, 4, 4);
        Detect(2, 4);
        Detect(3, 4);

        await RunAsync(id);

        await using var check = _db.NewContext();
        var tests = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        tests.ShouldAllBe(t => t.WorksheetId != null);
        var first = await check.Worksheets.WithDrafts().Include(w => w.WorksheetQuestions).ThenInclude(wq => wq.Question)
            .SingleAsync(w => w.Id == tests[0].WorksheetId);
        first.IsDraft.ShouldBeTrue();
        first.CreateUserId.ShouldBe(OwnerId);
        first.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        first.Name.ShouldBe("Puan BTS 5 Matematik – 1-01 · Test 1");
        first.WorksheetQuestions.OrderBy(wq => wq.Order).Select(wq => wq.Question.Text).ShouldBe(["1", "2", "3"]);
        (await check.Worksheets.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Answerless_test_is_blocked_until_its_answers_are_picked_then_approval_activates_it()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [2])]);
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            var detail = await service.GetAsync(Owner, id, default);
            detail!.Tests.Single().Answerless.ShouldBe(1);
            (await service.ApproveTestAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Answerless);

            var questions = await service.GetReviewQuestionsAsync(Owner, id, test.Id, default);
            questions!.Select(q => (q.Number, q.FilePage, q.OptionCount, q.AnswerIndex)).ShouldBe([("1", 1, 4, 0), ("2", 1, 4, null)]);
            (await service.SetAnswerAsync(Owner, id, test.Id, questions[1].Id, 4, default)).ShouldBe(BookImportReviewResult.InvalidOption);
            (await service.SetAnswerAsync(Owner, id, test.Id, questions[1].Id, 2, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.ApproveTestAsync(Owner, id, test.Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(2);
        var worksheet = await check.Worksheets.SingleAsync();
        worksheet.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        (await check.BookImportJobTests.SingleAsync()).Status.ShouldBe(BookImportTestStatus.Approved);
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Completed);
        job.Stage.ShouldBe(BookImportStage.Approval);
    }

    [Fact]
    public async Task Approve_all_skips_blocked_tests_and_says_why()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2]), new(2, 2, 2, "C", [2]), new(3, 3, 3, "D", [1])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        // Sayfa 3 tespit edilemez → test Failed.
        await RunAsync(id);
        var tests = await TestsAsync();

        await using (var ctx = _db.NewContext())
        {
            var result = await Service(ctx).ApproveAllAsync(Owner, id, default);
            result!.Approved.ShouldBe([tests[0].Id]);
            result.Skipped.ShouldBe([new(tests[1].Id, "answerless"), new(tests[2].Id, "failed")]);
        }

        await using var check = _db.NewContext();
        (await check.Worksheets.Select(w => w.Id).ToListAsync()).ShouldBe([tests[0].WorksheetId!.Value]);
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.ReadyForReview);
    }

    [Fact]
    public async Task Removing_a_test_deletes_only_its_draft_worksheet_and_questions()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2]), new(2, 2, 2, "CD", [2])]);
        Detect(1, 4, 4);
        Detect(2, 4, 4);
        await RunAsync(id);
        var tests = await TestsAsync();

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            (await service.ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
            (await service.RemoveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.InvalidState);
            (await service.RemoveTestAsync(Owner, id, tests[1].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        }

        await using var check = _db.NewContext();
        (await check.Questions.WithDrafts().CountAsync()).ShouldBe(2);
        (await check.Questions.IgnoreQueryFilters().CountAsync(q => q.IsDeleted && q.IsDraft)).ShouldBe(2);
        (await check.Worksheets.WithDrafts().Select(w => w.Id).ToListAsync()).ShouldBe([tests[0].WorksheetId!.Value]);
        (await check.BookImportJobTests.OrderBy(t => t.Order).Select(t => t.Status).ToListAsync())
            .ShouldBe([BookImportTestStatus.Approved, BookImportTestStatus.Removed]);
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.Completed);
    }

    [Fact]
    public async Task Another_user_cannot_review_the_job()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();
        var stranger = BookImportOwner.Create(OwnerId + 1, "QuestionTrainer");

        await using var ctx = _db.NewContext();
        var service = Service(ctx);
        (await service.GetReviewQuestionsAsync(stranger, id, test.Id, default)).ShouldBeNull();
        (await service.ApproveTestAsync(stranger, id, test.Id, default)).ShouldBe(BookImportReviewResult.NotFound);
        (await service.RemoveTestAsync(stranger, id, test.Id, default)).ShouldBe(BookImportReviewResult.NotFound);
        (await service.ApproveAllAsync(stranger, id, default)).ShouldBeNull();
        (await service.ApproveTestAsync(BookImportOwner.Create(OwnerId + 2, "Admin"), id, test.Id, default))
            .ShouldBe(BookImportReviewResult.Ok);
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

    [Fact]
    public async Task Fixing_a_page_in_labeling_replaces_its_drafts_in_place_with_answers_from_the_key()
    {
        var id = await SeedAsync([new(1, 1, 2, "ABCD", [2, 4])]);
        Detect(1, 4);
        Detect(2, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();
        Flags(test).ShouldBe([F("missingNumbers", [1], [2]), F("countMismatch", [1, 2], null, 3, 4)]);

        await using (var ctx = _db.NewContext())
        {
            var result = await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0), Box(1)], default);
            result.Error.ShouldBe(BookImportPageFixError.None);
            result.Page!.Saved.ShouldBe(2);
            result.Page.Removed.ShouldBe(1);
            result.Page.Answerless.ShouldBe(0);
        }

        _saved.Last().ReplacingQuestionIds!.Count.ShouldBe(1);
        _saved.Last().Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("1", 0), ("2", 1)]);
        test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.Done);
        Flags(test).ShouldBeEmpty();
        test.QuestionsSaved.ShouldBe(4);
        await using var check = _db.NewContext();
        var worksheet = await check.Worksheets.WithDrafts().Include(w => w.WorksheetQuestions).ThenInclude(wq => wq.Question)
            .SingleAsync(w => w.Id == test.WorksheetId);
        worksheet.WorksheetQuestions.OrderBy(wq => wq.Order).Select(wq => (wq.Order, wq.Question.Text))
            .ShouldBe([(1, "1"), (2, "2"), (3, "3"), (4, "4")]);
        (await check.Questions.IgnoreQueryFilters().CountAsync(q => q.IsDeleted && q.IsDraft && q.Text == "1")).ShouldBe(1);
    }

    [Fact]
    public async Task A_marked_answer_wins_over_the_key_and_an_unanswered_question_stays_flagged()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [2])]);
        Detect(1, 4, 4);
        await RunAsync(id);
        var test = await SingleTestAsync();

        await using (var ctx = _db.NewContext())
        {
            var questions = new List<LabelingBankQuestionDto> { Box(0, correct: 2), Box(1), Box(2, label: "7") };
            var result = await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, questions, default);
            result.Page!.Answerless.ShouldBe(2);
        }

        _saved.Last().Questions.Select(q => (q.ItemLabel, q.CorrectOptionIndex)).ShouldBe([("1", 2), ("2", -1), ("7", -1)]);
        test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.NeedsReview);
        Flags(test).ShouldBe([F("noAnswer", [1], [2, 7]), F("countMismatch", [1], null, 3, 1)]);
    }

    [Fact]
    public async Task Fixing_a_failed_test_creates_its_draft_worksheet()
    {
        var id = await SeedAsync([new(1, 1, 1, "AB", [2])]);
        // Sayfa 1 tespit edilemez → test Failed, taslak test yok.
        await RunAsync(id);
        var test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.Failed);
        test.WorksheetId.ShouldBeNull();

        await using (var ctx = _db.NewContext())
            (await Fix(ctx).ReplacePageAsync(Owner, id, test.Id, 1, [Box(0), Box(1)], default)).Error.ShouldBe(BookImportPageFixError.None);

        test = await SingleTestAsync();
        test.Status.ShouldBe(BookImportTestStatus.Done);
        test.Error.ShouldBeNull();
        await using var check = _db.NewContext();
        var worksheet = await check.Worksheets.WithDrafts().Include(w => w.WorksheetQuestions).SingleAsync(w => w.Id == test.WorksheetId);
        worksheet.IsDraft.ShouldBeTrue();
        worksheet.CreateUserId.ShouldBe(OwnerId);
        worksheet.WorksheetQuestions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_page_cannot_be_fixed_outside_review_outside_the_test_or_by_another_user()
    {
        var id = await SeedAsync([new(1, 1, 1, "A", [1]), new(2, 2, 2, "B", [1])]);
        Detect(1, 4);
        Detect(2, 4);
        await RunAsync(id);
        var tests = await TestsAsync();

        await using var ctx = _db.NewContext();
        var fix = Fix(ctx);
        (await fix.ReplacePageAsync(Owner, id, tests[0].Id, 2, [Box(0)], default)).Error.ShouldBe(BookImportPageFixError.InvalidState);
        (await fix.ReplacePageAsync(BookImportOwner.Create(OwnerId + 1, "QuestionTrainer"), id, tests[0].Id, 1, [Box(0)], default))
            .Error.ShouldBe(BookImportPageFixError.NotFound);
        (await Service(ctx).ApproveTestAsync(Owner, id, tests[0].Id, default)).ShouldBe(BookImportReviewResult.Ok);
        (await fix.ReplacePageAsync(Owner, id, tests[0].Id, 1, [Box(0)], default)).Error.ShouldBe(BookImportPageFixError.InvalidState);
        _saved.Count.ShouldBe(2);
    }

    [Fact]
    public void Page_flags_drop_the_fixed_page_and_keep_the_others()
    {
        var flags = BookImportPageFixService.ReplacePageFlags(
            [
                new(BookImportReviewFlag.MissingNumbers, [1, 2], [2, 5]),
                new(BookImportReviewFlag.DetectionFailed, [1]),
                new(BookImportReviewFlag.OptionCount, [2], [6]),
                new(BookImportReviewFlag.CountMismatch, [1, 2], Found: 4, Expected: 6),
            ],
            filePage: 1, pageNumbers: new HashSet<int> { 1, 2, 3 }, answerless: [3], optionCount: [], found: 6, keyLength: 6, from: 1, to: 2);

        flags.Select(f => F(f.Code, f.Pages, f.Numbers, f.Found, f.Expected))
            .ShouldBe([F("missingNumbers", [2], [5]), F("optionCount", [2], [6]), F("noAnswer", [1], [3])]);
    }

    private BookImportPageFixService Fix(AppDbContext ctx) => new(ctx, _minio, _bank);

    private static LabelingBankQuestionDto Box(int row, int correct = -1, string? label = null) => new()
    {
        X = 100, Y = 100 + row * 700, Width = 900, Height = 400, CorrectOptionIndex = correct, ItemLabel = label,
        Options = Enumerable.Range(0, 4)
            .Select(i => new LabelingBankBoxDto { X = 120 + i % 2 * 440, Y = 300 + row * 700 + i / 2 * 90, Width = 400, Height = 60 })
            .ToList(),
    };

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

    private async Task RunAsync(Guid id, IFixedWindowCounterStore? budgetStore = null, LabelingAiOptions? aiOptions = null,
        IBackgroundJobClient? jobs = null)
    {
        var guard = Substitute.For<IApprovedTeacherGuard>();
        guard.CheckAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(TeacherApprovalCheck.NoTeacherProfile);
        await using var ctx = _db.NewContext();
        await new BookImportJobRunner(ctx, guard, _minio, Substitute.For<IPdfRasterizer>(), new FakeBookSetupAi(), _labeling,
            _services.GetRequiredService<IServiceScopeFactory>(), _config, budgetStore: budgetStore,
            aiOptions: aiOptions is null ? null : Options.Create(aiOptions), jobs: jobs).RunLockedAsync(id);
    }

    private static readonly BookImportOwner Owner = BookImportOwner.Create(OwnerId, "QuestionTrainer");
    private static readonly BookImportOwner Admin = BookImportOwner.Create(1, "Admin");

    private IConfiguration _config = new ConfigurationBuilder().Build();

    private BookImportService Service(AppDbContext ctx, IBackgroundJobClient? jobs = null) => new(ctx, _minio,
        jobs ?? Substitute.For<IBackgroundJobClient>(), Substitute.For<IEntitlementService>(), new BookRegistryService(ctx, TimeProvider.System),
        _config);

    private async Task<List<BookImportJobTest>> TestsAsync()
    {
        await using var check = _db.NewContext();
        return await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
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
