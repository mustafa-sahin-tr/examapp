using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1032: örnek kitapla demo: AI ve hak yok, gerçek aşama sırası, kopyalar öğretmenin, sayı sınırı.</summary>
public class BookImportDemoTests : IDisposable
{
    private const int PilotId = 11;
    private const int TeacherId = 42;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();
    private readonly FakeBookSetupAi _ai = new();
    private readonly int _gradeId;
    private readonly Guid _sourceId;

    public BookImportDemoTests()
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "4. Sınıf" };
        ctx.Grades.Add(grade);
        ctx.SaveChanges();
        _gradeId = grade.Id;
        _sourceId = SeedSourceAsync(ctx, questions: 7).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Start_without_a_source_is_unavailable()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.StartDemoAsync(Teacher, default)).Error.ShouldBe(StartBookImportDemoError.Unavailable);
        (await service.GetDemoAsync(Teacher, default)).Available.ShouldBeFalse();
    }

    [Fact]
    public async Task Replay_follows_the_real_stages_without_ai_quota_or_storage()
    {
        await MarkSourceAsync();

        var id = await StartAsync();
        await using (var ctx = _db.NewContext())
        {
            var job = await ctx.BookImportJobs.AsNoTracking().FirstAsync(j => j.Id == id);
            job.DemoSourceJobId.ShouldBe(_sourceId);
            job.CreateUserId.ShouldBe(TeacherId);
            job.BookId.ShouldBeNull();
            job.Status.ShouldBe(BookImportJobStatus.Queued);
        }

        await RunAsync(id);
        await using (var ctx = _db.NewContext())
        {
            var job = await ctx.BookImportJobs.AsNoTracking().FirstAsync(j => j.Id == id);
            job.Status.ShouldBe(BookImportJobStatus.AwaitingConfirmation);
            job.Stage.ShouldBe(BookImportStage.SetupConfirmation);
            (await ctx.BookImportJobTests.AsNoTracking().SingleAsync(t => t.JobId == id)).WorksheetId.ShouldBeNull();
            var setup = (await NewService(ctx).GetSetupAsync(Teacher, id, default))!;
            setup.IsDemo.ShouldBeTrue();
            setup.ExistingBook.ShouldBeNull();
            setup.Tests.Count.ShouldBe(1);
            (await NewService(ctx).ConfirmSetupAsync(Teacher, id, new ConfirmBookImportSetupDto { BookName = "x" }, default)).Error
                .ShouldBe(ConfirmSetupError.None);
        }

        await RunAsync(id);
        await using (var ctx = _db.NewContext())
        {
            var job = await ctx.BookImportJobs.AsNoTracking().FirstAsync(j => j.Id == id);
            job.Status.ShouldBe(BookImportJobStatus.ReadyForReview);
            job.Stage.ShouldBe(BookImportStage.Review);
            job.BookId.ShouldBeNull();
            (await ctx.BookImportJobTests.AsNoTracking().SingleAsync(t => t.JobId == id)).QuestionsSaved.ShouldBe(BookImportDemoReplay.MaxQuestions);
        }

        _ai.SurveyBatches.ShouldBeEmpty();
        _ai.KeyReads.ShouldBeEmpty();
        await _entitlements.DidNotReceiveWithAnyArgs().CheckAsync(default!, default!, default);
        await _entitlements.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default!, default!, default);
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Copies_are_private_drafts_of_the_teacher_with_their_answers_and_passage()
    {
        await MarkSourceAsync();
        var id = await PlayToReviewAsync();

        await using var ctx = _db.NewContext();
        var test = await ctx.BookImportJobTests.AsNoTracking().SingleAsync(t => t.JobId == id);
        var worksheet = await ctx.Worksheets.IgnoreQueryFilters([DraftQueryFilter.Key]).AsNoTracking().FirstAsync(w => w.Id == test.WorksheetId);
        worksheet.CreateUserId.ShouldBe(TeacherId);
        worksheet.IsDraft.ShouldBeTrue();
        worksheet.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);

        var ids = await BookImportDemoReplay.SourceQuestionIds(ctx, worksheet.Id).ToListAsync();
        ids.Count.ShouldBe(BookImportDemoReplay.MaxQuestions);
        var copies = await ctx.Questions.IgnoreQueryFilters([DraftQueryFilter.Key]).AsNoTracking().Include(q => q.Answers)
            .Where(q => ids.Contains(q.Id)).ToListAsync();
        copies.ShouldAllBe(q => q.CreateUserId == TeacherId && q.IsDraft && q.BankSharing == QuestionBankSharing.Private);
        copies.ShouldAllBe(q => q.PassageId != null && q.Answers.Count == 2);
        // Doğru şık kopyanın kendi şıkkına bakar ve kaynaktakiyle aynı sıradadır (ikinci şık).
        copies.ShouldAllBe(q => q.CorrectAnswerId == q.Answers.OrderBy(a => a.Order).ElementAt(1).Id);

        var sources = await ctx.QuestionSources.AsNoTracking().Where(s => ids.Contains(s.QuestionId)).ToListAsync();
        sources.Count.ShouldBe(BookImportDemoReplay.MaxQuestions);
        sources.ShouldAllBe(s => s.BookId == null);
        sources.Select(s => s.CropSha256).Distinct().Count().ShouldBe(BookImportDemoReplay.MaxQuestions);

        // Kaynak dokunulmaz kalır.
        (await ctx.Questions.IgnoreQueryFilters([DraftQueryFilter.Key]).CountAsync(q => q.CreateUserId == PilotId)).ShouldBe(7);
    }

    [Fact]
    public async Task Approving_keeps_the_copies_with_the_teacher()
    {
        await MarkSourceAsync();
        var id = await PlayToReviewAsync();

        await using var ctx = _db.NewContext();
        var test = await ctx.BookImportJobTests.AsNoTracking().SingleAsync(t => t.JobId == id);
        (await NewService(ctx).ApproveTestAsync(Teacher, id, test.Id, default)).ShouldBe(BookImportReviewResult.Ok);

        var worksheet = await ctx.Worksheets.AsNoTracking().FirstAsync(w => w.Id == test.WorksheetId);
        worksheet.IsDraft.ShouldBeFalse();
        worksheet.CreateUserId.ShouldBe(TeacherId);
        worksheet.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
    }

    [Fact]
    public async Task Active_demo_is_returned_and_the_limit_holds_after_deleting()
    {
        await MarkSourceAsync();
        var first = await StartAsync();

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).StartDemoAsync(Teacher, default)).Job!.Id.ShouldBe(first);
            var demo = await NewService(ctx).GetDemoAsync(Teacher, default);
            demo.ActiveJobId.ShouldBe(first);
            demo.Used.ShouldBe(1);
            demo.Limit.ShouldBe(2);
            (await NewService(ctx).CancelAsync(Teacher, first, false, default)).ShouldBe(CancelBookImportResult.Cancelled);
        }

        var second = await StartAsync();
        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).CancelAsync(Teacher, second, false, default)).ShouldBe(CancelBookImportResult.Cancelled);
            await ctx.BookImportJobs.Where(j => j.Id == second)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.IsDeleted, true));

            (await NewService(ctx).StartDemoAsync(Teacher, default)).Error.ShouldBe(StartBookImportDemoError.LimitReached);
            var demo = await NewService(ctx).GetDemoAsync(Teacher, default);
            demo.Available.ShouldBeFalse();
            demo.Used.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Failed_demo_does_not_use_a_run()
    {
        await MarkSourceAsync();
        var id = await StartAsync();

        await using var ctx = _db.NewContext();
        await ctx.BookImportJobs.Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BookImportJobStatus.Failed));

        (await NewService(ctx).GetDemoAsync(Teacher, default)).Used.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelled_demo_stops_at_the_next_step()
    {
        await MarkSourceAsync();
        var id = await StartAsync();
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CancelAsync(Teacher, id, false, default)).ShouldBe(CancelBookImportResult.Cancelled);

        await RunAsync(id);

        await using var read = _db.NewContext();
        (await read.BookImportJobs.AsNoTracking().FirstAsync(j => j.Id == id)).Status.ShouldBe(BookImportJobStatus.Cancelled);
    }

    [Fact]
    public async Task Source_selection_keeps_one_source_and_rejects_jobs_without_questions()
    {
        await MarkSourceAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.SetDemoSourceAsync(Guid.NewGuid(), true, default)).ShouldBe(SetBookImportDemoSourceResult.NotFound);

        ctx.SetCurrentUser(PilotId);
        var empty = new BookImportJob { Id = Guid.NewGuid(), BookName = "Boş", GradeId = _gradeId, FileName = "b.pdf" };
        ctx.BookImportJobs.Add(empty);
        await ctx.SaveChangesAsync();
        (await service.SetDemoSourceAsync(empty.Id, true, default)).ShouldBe(SetBookImportDemoSourceResult.NotEligible);

        var second = await SeedSourceAsync(ctx, questions: 1);
        (await service.SetDemoSourceAsync(second, true, default)).ShouldBe(SetBookImportDemoSourceResult.Ok);
        var sources = await ctx.BookImportJobs.AsNoTracking().Where(j => j.IsDemoSource).Select(j => j.Id).ToListAsync();
        sources.ShouldBe([second]);

        (await service.SetDemoSourceAsync(second, false, default)).ShouldBe(SetBookImportDemoSourceResult.Ok);
        (await ctx.BookImportJobs.AsNoTracking().AnyAsync(j => j.IsDemoSource)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(nameof(BookImportController.Start))]
    [InlineData(nameof(BookImportController.Retry))]
    [InlineData(nameof(BookImportController.RetryTest))]
    [InlineData(nameof(BookImportController.RetryFailedTests))]
    [InlineData(nameof(BookImportController.ReplacePage))]
    public void Teacher_cannot_reach_real_import_endpoints(string method)
    {
        var roles = typeof(BookImportController).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>().Single().Roles!.Split(',');

        roles.ShouldBe(["Admin", "QuestionTrainer"], ignoreOrder: true);
    }

    [Fact]
    public void Only_admin_selects_the_demo_source()
    {
        typeof(BookImportController).GetMethod(nameof(BookImportController.SetDemoSource))!.GetCustomAttributes<AuthorizeAttribute>()
            .Single().Roles.ShouldBe("Admin");
    }

    private static BookImportOwner Teacher => BookImportOwner.Create(TeacherId);

    private async Task MarkSourceAsync()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).SetDemoSourceAsync(_sourceId, true, default)).ShouldBe(SetBookImportDemoSourceResult.Ok);
    }

    private async Task<Guid> StartAsync()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).StartDemoAsync(Teacher, default);
        result.Error.ShouldBe(StartBookImportDemoError.None);
        return result.Job!.Id;
    }

    private async Task<Guid> PlayToReviewAsync()
    {
        var id = await StartAsync();
        await RunAsync(id);
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ConfirmSetupAsync(Teacher, id, new ConfirmBookImportSetupDto { BookName = "x" }, default);
        await RunAsync(id);
        return id;
    }

    private async Task<Guid> SeedSourceAsync(AppDbContext ctx, int questions)
    {
        ctx.SetCurrentUser(PilotId);
        var passage = new Passage { ImageUrl = "/img/passage.jpg" };
        ctx.Add(passage);
        var worksheet = new Worksheet { Name = "Test 1", GradeId = _gradeId, IsDraft = true };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();
        for (var i = 1; i <= questions; i++)
        {
            var question = new Question
            {
                ImageUrl = $"/img/q{i}.jpg",
                PassageId = passage.Id,
                IsDraft = true,
                Answers = [new Answer { Text = "A", Order = 0 }, new Answer { Text = "B", Order = 1 }],
            };
            ctx.Questions.Add(question);
            await ctx.SaveChangesAsync();
            question.CorrectAnswerId = question.Answers.Single(a => a.Order == 1).Id;
            ctx.QuestionSources.Add(new QuestionSource
            {
                QuestionId = question.Id, FilePage = 2, ItemLabel = i.ToString(), CropSha256 = new string('a', 63) + (i % 10),
            });
            worksheet.WorksheetQuestions.Add(new WorksheetQuestion { QuestionId = question.Id, Order = i });
            await ctx.SaveChangesAsync();
        }

        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            BookName = "Örnek Kitap",
            GradeId = _gradeId,
            FileName = "ornek.pdf",
            PageCount = 3,
            SetupJson = "{}",
            Status = BookImportJobStatus.ReadyForReview,
            Stage = BookImportStage.Review,
        };
        job.Tests.Add(new BookImportJobTest
        {
            Order = 1, TestNo = 1, Title = "Test 1", FileFrom = 2, FileTo = 2, Minutes = 10, WorksheetId = worksheet.Id,
            Status = BookImportTestStatus.Done,
        });
        foreach (var page in new[] { 1, 2, 3 })
            job.Pages.Add(new BookImportPage { FilePage = page, ImageUrl = $"http://minio/p{page}.webp", Width = 100, Height = 100 });
        ctx.BookImportJobs.Add(job);
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    private BookImportService NewService(AppDbContext ctx) =>
        new(ctx, _minio, _jobs, _entitlements, new BookRegistryService(ctx, TimeProvider.System), Config());

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["BookImport:DemoMaxRuns"] = "2", ["BookImport:DemoStepMilliseconds"] = "0" })
        .Build();

    private async Task RunAsync(Guid id)
    {
        var guard = Substitute.For<IApprovedTeacherGuard>();
        guard.CheckAsync(TeacherId, Arg.Any<CancellationToken>()).Returns(TeacherApprovalCheck.NoTeacherProfile);
        await using var ctx = _db.NewContext();
        await new BookImportJobRunner(ctx, guard, _minio, new PdfiumRasterizer(pageWidth: 200), _ai,
            Substitute.For<ExamApp.Api.Services.Labeling.ILabelingService>(), Substitute.For<IServiceScopeFactory>(), Config()).RunLockedAsync(id);
    }

    public void Dispose() => _db.Dispose();
}
