using System.Reflection;
using System.Text;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1056: aktarım maliyet raporu — toplamlar, süzgeçler, ay sınırını aşan iş, CSV.</summary>
public class BookImportCostReportTests : IDisposable
{
    private const int Ayse = 11;
    private const int Veli = 12;

    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 30);
    private static readonly DateTime InRange = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LastMonth = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly MutableClock _clock = new(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));

    private BookImportCostReportService NewService(AppDbContext ctx) =>
        new(ctx, _authApi, new AdminSubscriptionService(ctx, new EntitlementService(ctx, _clock), _authApi, _clock));

    public BookImportCostReportTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>()).Returns(new List<UserLookupResultDto>
        {
            new() { Id = Ayse, FullName = "Ayşe Öğretmen", Role = "Teacher" },
            new() { Id = Veli, FullName = "=Veli Öğretmen", Role = "Teacher" },
        });
    }

    private async Task<Guid> SeedJobAsync(int owner, BookImportJobKind kind, DateTime createdAt, BookImportJobStatus status, int pages, int questions,
        params (decimal Usd, DateTime At, BookImportStage Stage)[] costs)
    {
        await using var ctx = _db.NewContext();
        var grade = await ctx.Grades.FirstOrDefaultAsync();
        if (grade is null)
        {
            grade = new Grade { Name = "5. Sınıf" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
        }

        ctx.SetCurrentUser(owner);
        var job = new BookImportJob { Id = Guid.NewGuid(), BookName = "Kitap", GradeId = grade.Id, Kind = kind, Status = status, PageCount = pages };
        job.Tests.Add(new BookImportJobTest { Order = 1, Unit = 1, TestNo = 1, QuestionsSaved = questions });
        job.Tests.Add(new BookImportJobTest { Order = 2, Unit = 1, TestNo = 2, QuestionsSaved = 50, Status = BookImportTestStatus.Removed });
        foreach (var cost in costs)
            job.Costs.Add(new BookImportCost { Stage = cost.Stage, ModelId = "claude-test", InputTokens = 10, OutputTokens = 5, CostUsd = cost.Usd, CreatedAt = cost.At });
        ctx.BookImportJobs.Add(job);
        await ctx.SaveChangesAsync();
        // CreateTime kayıtta damgalanır; test tarihi sonradan yazılır.
        await ctx.BookImportJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(u => u.SetProperty(j => j.CreateTime, createdAt));
        return job.Id;
    }

    private async Task<BookImportCostReportDto> ReportAsync(BookImportJobKind? kind = null, int? userId = null, int page = 1, int pageSize = 20)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).GetAsync(new BookImportCostFilter(From, To, kind, userId), page, pageSize);
    }

    private async Task SeedStandardAsync()
    {
        await SeedJobAsync(Ayse, BookImportJobKind.Book, InRange, BookImportJobStatus.ReadyForReview, 100, 40,
            (1.5m, InRange, BookImportStage.Setup), (0.5m, InRange, BookImportStage.Questions));
        await SeedJobAsync(Veli, BookImportJobKind.QuickAdd, InRange, BookImportJobStatus.Failed, 2, 0,
            (0.25m, InRange, BookImportStage.Questions));
        // Geçen ay açılmış, bu ay da çağrı yapmış iş: yalnız bu ayki çağrı sayılır.
        await SeedJobAsync(Ayse, BookImportJobKind.Book, LastMonth, BookImportJobStatus.Cancelled, 50, 10,
            (9m, LastMonth, BookImportStage.Setup), (0.75m, InRange, BookImportStage.Questions));
        // Aralık dışı: görünmez.
        await SeedJobAsync(Veli, BookImportJobKind.Book, LastMonth, BookImportJobStatus.Completed, 30, 5, (4m, LastMonth, BookImportStage.Setup));
    }

    [Fact]
    public async Task Totals_equal_the_sum_of_cost_rows_in_range()
    {
        await SeedStandardAsync();

        var report = await ReportAsync();

        await using var ctx = _db.NewContext();
        var rangeStart = From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeEnd = To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var expected = (await ctx.BookImportCosts.AsNoTracking().Where(c => c.CreatedAt >= rangeStart && c.CreatedAt < rangeEnd).ToListAsync()).Sum(c => c.CostUsd);

        expected.ShouldBe(3m);
        report.Totals.CostUsd.ShouldBe(expected);
        report.Totals.Jobs.ShouldBe(3);
        report.Totals.Pages.ShouldBe(152);
        // İncelemede silinen testin soruları sayılmaz.
        report.Totals.QuestionsSaved.ShouldBe(50);
        report.Totals.CostPerQuestionUsd.ShouldBe(0.06m);
        report.Jobs.Sum(j => j.CostUsd).ShouldBe(expected);
        report.Users.Sum(u => u.CostUsd).ShouldBe(expected);
        report.Stages.Sum(s => s.CostUsd).ShouldBe(expected);
    }

    [Fact]
    public async Task Users_are_sorted_by_cost_with_name_role_and_free_plan()
    {
        await SeedStandardAsync();

        var report = await ReportAsync();

        report.Users.Select(u => u.UserId).ShouldBe([Ayse, Veli]);
        report.Users[0].ShouldSatisfyAllConditions(
            u => u.FullName.ShouldBe("Ayşe Öğretmen"),
            u => u.Role.ShouldBe("Teacher"),
            u => u.PlanKey.ShouldBe(SubscriptionPlan.FreeKey),
            u => u.Jobs.ShouldBe(2),
            u => u.CostUsd.ShouldBe(2.75m));
    }

    [Fact]
    public async Task Failed_and_cancelled_jobs_are_included_with_their_status()
    {
        await SeedStandardAsync();

        var report = await ReportAsync();

        report.Jobs.Select(j => j.Status).ShouldBe(["ReadyForReview", "Failed", "Cancelled"], ignoreOrder: true);
        report.Jobs.Single(j => j.Status == "Cancelled").CostUsd.ShouldBe(0.75m);
    }

    [Fact]
    public async Task Job_without_paid_calls_created_in_range_is_listed_with_zero_cost()
    {
        await SeedJobAsync(Ayse, BookImportJobKind.QuickAdd, InRange, BookImportJobStatus.Completed, 1, 3);

        var report = await ReportAsync();

        report.Jobs.Single().CostUsd.ShouldBe(0m);
        report.Totals.CostPerQuestionUsd.ShouldBe(0m);
        report.Stages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Kind_and_user_filters_narrow_every_section()
    {
        await SeedStandardAsync();

        var quickAdd = await ReportAsync(kind: BookImportJobKind.QuickAdd);
        quickAdd.Totals.ShouldSatisfyAllConditions(t => t.Jobs.ShouldBe(1), t => t.CostUsd.ShouldBe(0.25m), t => t.CostPerQuestionUsd.ShouldBeNull());
        quickAdd.Users.Single().UserId.ShouldBe(Veli);

        var ayse = await ReportAsync(userId: Ayse);
        ayse.Totals.CostUsd.ShouldBe(2.75m);
        ayse.Jobs.ShouldAllBe(j => j.OwnerId == Ayse);
        ayse.Stages.Sum(s => s.CostUsd).ShouldBe(2.75m);
    }

    [Fact]
    public async Task Empty_range_returns_empty_report()
    {
        var report = await ReportAsync();

        report.Totals.ShouldSatisfyAllConditions(t => t.Jobs.ShouldBe(0), t => t.CostUsd.ShouldBe(0m), t => t.CostPerQuestionUsd.ShouldBeNull());
        report.Users.ShouldBeEmpty();
        report.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Jobs_are_paged_but_totals_cover_every_job()
    {
        await SeedStandardAsync();

        var report = await ReportAsync(page: 2, pageSize: 2);

        report.Jobs.Count.ShouldBe(1);
        report.JobCount.ShouldBe(3);
        report.Totals.CostUsd.ShouldBe(3m);
    }

    [Fact]
    public void Csv_has_bom_uses_turkish_format_and_neutralises_formulas()
    {
        var bytes = BookImportCostCsv.Write(
        [
            new BookImportCostJobDto
            {
                Id = Guid.Empty, Kind = "Book", BookName = "=HYPERLINK(\"x\";1)", OwnerId = Veli, OwnerName = "@Veli",
                CreatedAt = InRange, Status = "Failed", Pages = 3, QuestionsSaved = 2, CostUsd = 1.5m,
            },
        ]);

        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        var row = Encoding.UTF8.GetString(bytes).Split(Environment.NewLine)[1];
        row.ShouldContain("\"'=HYPERLINK(\"\"x\"\";1)\"");
        row.ShouldContain(";'@Veli;");
        row.ShouldEndWith(";1,500000");
    }

    [Fact]
    public void Controller_is_admin_only_and_audited()
    {
        var type = typeof(AdminBookImportCostsController);
        type.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Single().Roles!.Split(',')
            .ShouldBe(["Admin", "SuperAdmin"], ignoreOrder: true);
        type.GetMethods().ShouldAllBe(m => m.GetCustomAttribute<AllowAnonymousAttribute>() == null);
        foreach (var name in new[] { nameof(AdminBookImportCostsController.Get), nameof(AdminBookImportCostsController.ExportJobs) })
            type.GetMethod(name)!.GetCustomAttribute<AdminDataAccessAttribute>()!.Resource.ShouldBe(AdminDataAccessResource.BookImportCostReport);
    }

    public void Dispose() => _db.Dispose();

    private sealed class MutableClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
