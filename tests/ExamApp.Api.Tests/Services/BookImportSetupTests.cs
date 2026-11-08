using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static ExamApp.Api.Tests.Support.FakeBookSetupAi;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1021: kurulum aşaması (tarama → planlama → işe yazma, maliyet) ve kurulum onayı (hak, kitap kaydı, kitap testleri,
/// aynı adlı kitap, iptal).
/// </summary>
public class BookImportSetupTests : IDisposable
{
    private const int OwnerId = 41;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();
    private readonly FakeBookSetupAi _ai = new();
    private readonly byte[] _pdf = SamplePdf.Create(6);
    private readonly int _gradeId;
    private readonly int _subjectId;
    private readonly int _topicId;

    public BookImportSetupTests()
    {
        _minio.GetFileStreamAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(_pdf)));
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => Task.FromResult("http://minio/exam-questions/" + ci.ArgAt<string>(1)));
        _entitlements.CheckAsync(Arg.Any<EntitlementSubject>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EntitlementCheck(true, 5, 1, null, "pro"));

        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        ctx.SaveChanges();
        var topic = new Topic { Name = "Doğal Sayılar", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        ctx.SaveChanges();
        (_gradeId, _subjectId, _topicId) = (grade.Id, subject.Id, topic.Id);

        // Kitap: 1 içindekiler, 2–3 Test 1-01, 4 Test 1-02, 5 cevap anahtarı, 6 öz değerlendirme.
        _ai.Pages[1] = new SurveyedPage(1, BookPageKind.Toc);
        _ai.Pages[2] = new SurveyedPage(2, BookPageKind.Test, 2, 1, 1, "Milyonlar - I", 16, 2);
        _ai.Pages[3] = new SurveyedPage(3, BookPageKind.Test, 3, MaxQuestionNumber: 4);
        _ai.Pages[4] = new SurveyedPage(4, BookPageKind.Test, 4, 1, 2, "Milyonlar - II", 12, 3);
        _ai.Pages[5] = new SurveyedPage(5, BookPageKind.AnswerKey, 5);
        _ai.Pages[6] = new SurveyedPage(6, BookPageKind.SelfAssessment, 6);
        _ai.Toc.Add(new SurveyedTocRow(1, "Doğal Sayılar", 2));
        _ai.Keys[5] = ([Row(1, 1, "ABCD"), Row(1, 2, "DDA")], [Row(1, 1, "ABCB"), Row(1, 2, "DDA")]);
        _ai.UnitTopics[1] = _topicId;
    }

    [Fact]
    public async Task Setup_surveys_in_batches_reads_the_key_twice_and_waits_for_confirmation()
    {
        var id = await SeedJobAsync();

        await RunAsync(id, batchSize: 4);

        _ai.SurveyBatches.ShouldBe([[1, 2, 3, 4], [5, 6]]);
        _ai.KeyReads.ShouldBe([(5, false), (5, true)]);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.AwaitingConfirmation);
        job.Stage.ShouldBe(BookImportStage.SetupConfirmation);
        job.Error.ShouldBeNull();
        // 2 tarama (1000/100) + 2 anahtar okuması (2000/500) + ünite eşleme (100/10), 1 USD / 1M token.
        job.AiCostUsd.ShouldBe((2 * 1100m + 2 * 2500m + 110m) / 1_000_000m);

        var summary = BookSetupSummary.Parse(job.SetupJson)!;
        summary.SchemaRevision.ShouldBe(ClaudeBookSetupAi.SchemaRevision);
        summary.Offset.ShouldBe(0);
        summary.AnswerKeyRanges.ShouldBe([new BookSetupRange(5, 5)]);
        summary.SelfAssessmentPages.ShouldBe([6]);
        summary.Units.ShouldBe([new BookSetupUnit(1, "Doğal Sayılar", 2, 4, _topicId)]);
        summary.NoAnswerKey.ShouldBeFalse();

        var tests = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        tests.Select(t => (t.TestNo, t.FileFrom, t.FileTo, t.Minutes, t.AnswerKey, t.KeyConflicts, t.ExpectedQuestions)).ShouldBe(
        [
            (1, (int?)2, (int?)3, (int?)16, "ABCD", "...B", (int?)4),
            (2, 4, 4, 12, "DDA", "...", 3),
        ]);
        tests[0].FlagsJson.ShouldBe("[\"keyConflicts\"]");
        tests[0].KeyAnswers.ShouldBe(4);
    }

    [Fact]
    public async Task A_book_without_an_answer_key_is_set_up_for_answerless_import()
    {
        _ai.Pages[5] = new SurveyedPage(5, BookPageKind.Other, 5);
        var id = await SeedJobAsync();

        await RunAsync(id);

        _ai.KeyReads.ShouldBeEmpty();
        await using var check = _db.NewContext();
        BookSetupSummary.Parse((await check.BookImportJobs.SingleAsync()).SetupJson)!.NoAnswerKey.ShouldBeTrue();
        (await check.BookImportJobTests.ToListAsync()).ShouldAllBe(t => t.AnswerKey == null && t.FlagsJson == null);
    }

    [Theory]
    [InlineData(false, null, "Claude is not configured (Anthropic:ApiKey is empty).")]
    [InlineData(true, "Book setup failed: Claude response was truncated.", "Book setup failed: Claude response was truncated.")]
    public async Task Setup_failures_fail_the_job_with_a_readable_error_and_keep_the_cost(bool configured, string? failure, string expected)
    {
        _ai.IsConfigured = configured;
        _ai.SurveyFailure = failure;
        var id = await SeedJobAsync();

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Failed);
        job.Stage.ShouldBe(BookImportStage.Setup);
        job.Error.ShouldBe(expected);
        job.AiCostUsd.ShouldBe(configured ? 1100m / 1_000_000m : 0m);
    }

    [Fact]
    public async Task A_book_without_test_headers_fails_setup()
    {
        foreach (var page in Enumerable.Range(1, 6))
            _ai.Pages[page] = new SurveyedPage(page, BookPageKind.Other);
        var id = await SeedJobAsync();

        await RunAsync(id);

        await using var check = _db.NewContext();
        (await check.BookImportJobs.SingleAsync()).Error.ShouldBe("No tests were found in the book (no page with a test header).");
    }

    [Fact]
    public async Task Negative_offset_starts_the_registry_range_at_printed_page_one()
    {
        // Canlı deneme (#1022, ikinci yayınevi): basılı = dosya - 1 → dosya 1 (kapak) basılı 0 olurdu, kayıt reddediyordu.
        var id = await SetUpAsync();
        await using (var ctx = _db.NewContext())
        {
            var job = await ctx.BookImportJobs.AsTracking().SingleAsync();
            var summary = BookSetupSummary.Parse(job.SetupJson)!;
            summary.Offset = -1;
            job.SetupJson = summary.Serialize();
            await ctx.SaveChangesAsync();
        }

        (await ConfirmAsync(id, Request(Confirmed(1)))).Error.ShouldBe(ConfirmSetupError.None);

        await using var check = _db.NewContext();
        (await check.BookPageOffsets.SingleAsync()).ShouldSatisfyAllConditions(
            o => o.FileFrom.ShouldBe(2), o => o.FileTo.ShouldBe(6), o => o.Offset.ShouldBe(-1));
    }

    [Fact]
    public async Task Confirm_writes_the_book_registry_and_book_tests_records_usage_and_queues_the_questions()
    {
        var id = await SetUpAsync();

        var result = await ConfirmAsync(id, Request(
            new ConfirmBookImportTestDto { Order = 1, Title = " Milyonlar - I ", Minutes = 16, FileFrom = 2, FileTo = 3, AnswerKey = "ABCB" }));

        result.Error.ShouldBe(ConfirmSetupError.None);
        result.Job!.Stage.ShouldBe("Questions");
        result.Job.Status.ShouldBe("Queued");

        await using var check = _db.NewContext();
        var book = await check.Books.SingleAsync();
        book.Name.ShouldBe("Puan BTS 5 Matematik");
        (await check.BookPageOffsets.SingleAsync()).ShouldSatisfyAllConditions(
            o => o.FileFrom.ShouldBe(1), o => o.FileTo.ShouldBe(6), o => o.Offset.ShouldBe(0));
        (await check.BookAnswerKeyRanges.SingleAsync()).ShouldSatisfyAllConditions(r => r.FileFrom.ShouldBe(5), r => r.FileTo.ShouldBe(5));
        (await check.BookTocEntries.SingleAsync()).ShouldSatisfyAllConditions(
            t => t.FileFrom.ShouldBe(2), t => t.FileTo.ShouldBe(4), t => t.SubjectId.ShouldBe(_subjectId), t => t.TopicId.ShouldBe(_topicId));

        var bookTest = await check.BookTests.SingleAsync();
        bookTest.Name.ShouldBe("1-01 · Milyonlar - I");
        var job = await check.BookImportJobs.SingleAsync();
        job.BookId.ShouldBe(book.Id);
        // Onaylanmayan test (1-02) aktarımdan çıkar; onaylanan anahtar düzeltilmiş haliyle, uyuşmazlık işaretleri temiz.
        var row = await check.BookImportJobTests.SingleAsync();
        (row.Title, row.AnswerKey, row.KeyConflicts, row.KeyAnswers, row.BookTestId, row.FlagsJson)
            .ShouldBe(("Milyonlar - I", "ABCB", (string?)null, 4, (int?)bookTest.Id, (string?)null));

        await _entitlements.Received(1).RecordUsageAsync(OwnerId, EntitlementFeatures.BookImport, id.ToString(), Arg.Any<CancellationToken>());
        _jobs.Received(1).Create(Arg.Is<Hangfire.Common.Job>(j => j.Method.Name == nameof(BookImportJobRunner.RunAsync)),
            Arg.Any<Hangfire.States.IState>());
    }

    [Fact]
    public async Task After_confirmation_the_runner_continues_past_the_setup()
    {
        var id = await SetUpAsync();
        (await ConfirmAsync(id, Request(Confirmed(1)))).Error.ShouldBe(ConfirmSetupError.None);

        await RunAsync(id);

        _ai.SurveyBatches.Count.ShouldBe(1); // kurulum yeniden yapılmadı
        await using var check = _db.NewContext();
        // Soru aktarımına geçildi (#1022; burada Claude tespiti yapılandırılmamış → okunur hata, BookQuestionImportTests).
        var job = await check.BookImportJobs.SingleAsync();
        job.Stage.ShouldBe(BookImportStage.Questions);
        job.Error.ShouldBe("Claude is not configured (Anthropic:ApiKey is empty).");
    }

    [Fact]
    public async Task Same_book_name_asks_to_reuse_the_existing_book_which_is_filled_when_it_has_no_registry()
    {
        var id = await SetUpAsync();
        int existingId;
        await using (var seed = _db.NewContext())
        {
            var existing = new Book { Name = "puan bts 5 matematik " };
            seed.Books.Add(existing);
            await seed.SaveChangesAsync();
            existingId = existing.Id;
        }

        var taken = await ConfirmAsync(id, Request(Confirmed(1)));
        taken.Error.ShouldBe(ConfirmSetupError.NameTaken);
        taken.ExistingBook!.Id.ShouldBe(existingId);

        var request = Request(Confirmed(1));
        request.ExistingBookId = existingId;
        (await ConfirmAsync(id, request)).Error.ShouldBe(ConfirmSetupError.None);

        await using var check = _db.NewContext();
        (await check.Books.CountAsync()).ShouldBe(1);
        (await check.BookImportJobs.SingleAsync()).BookId.ShouldBe(existingId);
        (await check.BookAnswerKeyRanges.SingleAsync()).BookId.ShouldBe(existingId);
    }

    [Theory]
    [InlineData("AB?B", "bookImport.invalidAnswerKey")]
    [InlineData("abcb", "bookImport.invalidAnswerKey")]
    public async Task Confirm_rejects_an_unresolved_answer_key(string key, string code)
    {
        var id = await SetUpAsync();
        var test = Confirmed(1);
        test.AnswerKey = key;

        var result = await ConfirmAsync(id, Request(test));

        result.ShouldBe(new ConfirmSetupResult(ConfirmSetupError.Invalid, Code: code));
        await AssertStillAwaitingAsync();
    }

    [Fact]
    public async Task Confirm_rejects_pages_outside_the_book_and_unknown_tests()
    {
        var id = await SetUpAsync();
        var outside = Confirmed(1);
        outside.FileTo = 7;

        (await ConfirmAsync(id, Request(outside))).Code.ShouldBe("bookImport.invalidPages");
        (await ConfirmAsync(id, Request(Confirmed(9)))).Code.ShouldBe("bookImport.unknownTest");
        (await ConfirmAsync(id, Request())).Code.ShouldBe("bookImport.noTests");
        await AssertStillAwaitingAsync();
    }

    [Fact]
    public async Task Confirm_is_blocked_when_the_plan_limit_is_reached()
    {
        var id = await SetUpAsync();
        var full = new EntitlementCheck(false, 1, 1, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), "basic");
        _entitlements.CheckAsync(Arg.Any<EntitlementSubject>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(full);

        var result = await ConfirmAsync(id, Request(Confirmed(1)));

        result.Error.ShouldBe(ConfirmSetupError.QuotaReached);
        result.Quota.ShouldBe(full);
        await _entitlements.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default!, default!, default);
        await AssertStillAwaitingAsync();
    }

    [Fact]
    public async Task Other_users_jobs_and_jobs_not_awaiting_setup_cannot_be_confirmed_or_cancelled()
    {
        var id = await SetUpAsync();

        (await ConfirmAsync(id, Request(Confirmed(1)), userId: 99)).Error.ShouldBe(ConfirmSetupError.NotFound);
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CancelAsync(BookImportOwner.Create(99), id, default)).ShouldBe(CancelBookImportResult.NotFound);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CancelAsync(BookImportOwner.Create(OwnerId), id, default)).ShouldBe(CancelBookImportResult.Cancelled);

        (await ConfirmAsync(id, Request(Confirmed(1)))).Error.ShouldBe(ConfirmSetupError.InvalidState);
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CancelAsync(BookImportOwner.Create(OwnerId), id, default)).ShouldBe(CancelBookImportResult.InvalidState);
        await using var check = _db.NewContext();
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.Cancelled);
    }

    [Fact]
    public async Task Setup_view_shows_the_plan_with_topic_names_and_a_same_named_book()
    {
        var id = await SetUpAsync();
        await using (var seed = _db.NewContext())
        {
            seed.Books.Add(new Book { Name = "Puan BTS 5 Matematik" });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var setup = (await NewService(ctx).GetSetupAsync(BookImportOwner.Create(OwnerId), id, default))!;

        setup.Units.Single().TopicName.ShouldBe("Doğal Sayılar");
        setup.ExistingBook!.Name.ShouldBe("Puan BTS 5 Matematik");
        setup.Tests.Select(t => t.KeyConflicts).ShouldBe(["...B", "..."]);
        setup.Tests[0].Flags.ShouldBe(["keyConflicts"]);
        (await NewService(ctx).GetSetupAsync(BookImportOwner.Create(99), id, default)).ShouldBeNull();
    }

    private static ConfirmBookImportTestDto Confirmed(int order) =>
        new() { Order = order, Title = "Milyonlar - I", Minutes = 16, FileFrom = 2, FileTo = 3, AnswerKey = "ABCB" };

    private static ConfirmBookImportSetupDto Request(params ConfirmBookImportTestDto[] tests) =>
        new() { BookName = "Puan BTS 5 Matematik", Tests = tests.ToList() };

    private async Task<Guid> SetUpAsync()
    {
        var id = await SeedJobAsync();
        await RunAsync(id);
        await using var check = _db.NewContext();
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.AwaitingConfirmation);
        return id;
    }

    private async Task<ConfirmSetupResult> ConfirmAsync(Guid id, ConfirmBookImportSetupDto request, int userId = OwnerId)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).ConfirmSetupAsync(BookImportOwner.Create(userId), id, request, default);
    }

    private async Task AssertStillAwaitingAsync()
    {
        await using var check = _db.NewContext();
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.AwaitingConfirmation);
        (await check.Books.CountAsync()).ShouldBe(0);
    }

    private BookImportService NewService(AppDbContext ctx) =>
        new(ctx, _minio, _jobs, _entitlements, new BookRegistryService(ctx, TimeProvider.System), new ConfigurationBuilder().Build());

    private async Task<Guid> SeedJobAsync()
    {
        await using var ctx = _db.NewContext();
        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            BookName = "Puan BTS 5 Matematik",
            GradeId = _gradeId,
            SubjectId = _subjectId,
            FileUrl = "http://minio/exam-questions/book-imports/x/source.pdf",
            FileName = "kitap.pdf",
            FileSizeBytes = _pdf.Length,
        };
        ctx.SetCurrentUser(OwnerId);
        ctx.BookImportJobs.Add(job);
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    private async Task RunAsync(Guid id, int batchSize = 8)
    {
        var guard = Substitute.For<IApprovedTeacherGuard>();
        guard.CheckAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(TeacherApprovalCheck.NoTeacherProfile);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BookImport:SurveyBatchSize"] = batchSize.ToString() })
            .Build();
        await using var ctx = _db.NewContext();
        await new BookImportJobRunner(ctx, guard, _minio, new PdfiumRasterizer(pageWidth: 200), _ai,
            Substitute.For<ExamApp.Api.Services.Labeling.ILabelingService>(), Substitute.For<IServiceScopeFactory>(), config)
            .RunLockedAsync(id);
    }

    public void Dispose() => _db.Dispose();
}
