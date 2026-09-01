using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class ExamServiceCreateAndStatsTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private ExamService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    private async Task<int> AddGradeAsync()
    {
        await using var ctx = _db.NewContext();
        var g = new Grade { Name = "5" };
        ctx.Grades.Add(g);
        await ctx.SaveChangesAsync();
        return g.Id;
    }

    private static ExamDto Dto(int gradeId, string name = "Deneme 1") => new()
    {
        Name = name, GradeId = gradeId, MaxDurationSeconds = 600, Description = "d",
    };

    // ---- CreateOrUpdateAsync ----

    [Fact]
    public async Task Null_dto_is_rejected()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).CreateOrUpdateAsync(null!, 1)).Message.ShouldContain("eksik");
    }

    [Fact]
    public async Task A_book_and_book_test_must_be_identified()
    {
        var gradeId = await AddGradeAsync();
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        (await svc.CreateOrUpdateAsync(Dto(gradeId), 1)).Message.ShouldContain("Kitap seçilmedi");

        var d = Dto(gradeId);
        d.NewBookName = "Yeni Kitap";
        (await svc.CreateOrUpdateAsync(d, 1)).Message.ShouldContain("Test seçilmedi");
    }

    [Fact]
    public async Task An_unknown_book_id_is_rejected()
    {
        var gradeId = await AddGradeAsync();
        await using var ctx = _db.NewContext();
        var d = Dto(gradeId);
        d.BookId = 9999;
        d.BookTestId = 8888;
        (await NewService(ctx).CreateOrUpdateAsync(d, 1)).Message.ShouldContain("Kitap bulunamadı");
    }

    [Fact]
    public async Task Creates_a_new_book_test_and_worksheet()
    {
        var gradeId = await AddGradeAsync();
        ExamSavedDto saved;
        await using (var ctx = _db.NewContext())
        {
            var d = Dto(gradeId, "Ünite 1 Testi");
            d.NewBookName = "Fen 5";
            d.NewBookTestName = "Ünite 1";
            saved = await NewService(ctx).CreateOrUpdateAsync(d, userId: 9);
        }

        saved.Message.ShouldContain("kaydedildi");
        saved.ExamId.ShouldNotBeNull();

        await using var check = _db.NewContext();
        (await check.Books.AnyAsync(b => b.Name == "Fen 5")).ShouldBeTrue();
        (await check.BookTests.AnyAsync(bt => bt.Name == "Ünite 1")).ShouldBeTrue();
        var ws = await check.Worksheets.FirstAsync(w => w.Id == saved.ExamId);
        ws.Name.ShouldBe("Ünite 1 Testi");
    }

    [Fact]
    public async Task Uses_an_existing_book_and_book_test()
    {
        var gradeId = await AddGradeAsync();
        int bookId, bookTestId;
        await using (var ctx = _db.NewContext())
        {
            var book = new Book { Name = "Mat 5", BookTests = { new BookTest { Name = "Deneme A" } } };
            ctx.Books.Add(book);
            await ctx.SaveChangesAsync();
            bookId = book.Id;
            bookTestId = book.BookTests.First().Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var d = Dto(gradeId, "WS");
            d.BookId = bookId;
            d.BookTestId = bookTestId;
            var saved = await NewService(ctx).CreateOrUpdateAsync(d, 1);
            saved.ExamId.ShouldNotBeNull();
            saved.BookTestId.ShouldBe(bookTestId);
        }

        await using var check = _db.NewContext();
        (await check.Books.CountAsync()).ShouldBe(1); // no new book created
    }

    [Fact]
    public async Task Updating_a_missing_worksheet_is_rejected()
    {
        var gradeId = await AddGradeAsync();
        int bookId, bookTestId;
        await using (var ctx = _db.NewContext())
        {
            var book = new Book { Name = "B", BookTests = { new BookTest { Name = "T" } } };
            ctx.Books.Add(book);
            await ctx.SaveChangesAsync();
            bookId = book.Id; bookTestId = book.BookTests.First().Id;
        }

        await using var ctx2 = _db.NewContext();
        var d = Dto(gradeId);
        d.Id = 4242;
        d.BookId = bookId;
        d.BookTestId = bookTestId;
        (await NewService(ctx2).CreateOrUpdateAsync(d, 1)).Message.ShouldContain("Test bulunamadı");
    }

    // ---- CreateBulkExamsAsync ----

    [Fact]
    public async Task Bulk_create_reports_per_row_success_and_failure()
    {
        var gradeId = await AddGradeAsync();
        await using var ctx = _db.NewContext();

        var bulk = new BulkExamCreateDto
        {
            Exams =
            {
                new BulkExamItemDto { Name = "OK", Description = "d", GradeId = gradeId, MaxDurationSeconds = 300, NewBookName = "K", NewBookTestName = "T1" },
                new BulkExamItemDto { Name = "BAD", Description = "d", GradeId = gradeId, MaxDurationSeconds = 300 }, // no book -> fails
            },
        };

        var result = await NewService(ctx).CreateBulkExamsAsync(bulk, 1);

        result.TotalProcessed.ShouldBe(2);
        result.SuccessCount.ShouldBe(1, customMessage: string.Join(" | ", result.FailedExams.Select(f => $"{f.ExamName}: {f.ErrorMessage}")));
        result.FailureCount.ShouldBe(1);
        result.Success.ShouldBeFalse();
        result.FailedExams.ShouldContain(f => f.ExamName == "BAD" && f.RowNumber == 2);
    }

    // ---- GetGroupedStudentStatistics ----

    [Fact]
    public async Task Statistics_aggregate_totals_and_group_by_worksheet()
    {
        var gradeId = await AddGradeAsync();
        int studentId;
        await using (var ctx = _db.NewContext())
        {
            var student = new Student { UserId = 1, StudentNumber = "n", SchoolName = "s", GradeId = gradeId };
            var ws = new Worksheet { Name = "Test A", Description = "", GradeId = gradeId };
            var q = new Question { Text = "q", Point = 5 };
            ctx.AddRange(student, ws, q);
            await ctx.SaveChangesAsync();
            studentId = student.Id;

            var correct = new Answer { QuestionId = q.Id, Text = "c" };
            var wrong = new Answer { QuestionId = q.Id, Text = "w" };
            ctx.Answers.AddRange(correct, wrong);
            await ctx.SaveChangesAsync();
            q.CorrectAnswerId = correct.Id;

            var wq = new WorksheetQuestion { TestId = ws.Id, QuestionId = q.Id, Order = 1 };
            ctx.TestQuestions.Add(wq);
            await ctx.SaveChangesAsync();

            var completed = new WorksheetInstance
            {
                StudentId = studentId, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Completed,
                StartTime = DateTime.UtcNow.AddMinutes(-30),
                WorksheetInstanceQuestions = new List<WorksheetInstanceQuestion>
                {
                    new() { WorksheetQuestionId = wq.Id, SelectedAnswerId = correct.Id, TimeTaken = 120 },
                },
            };
            var started = new WorksheetInstance
            {
                StudentId = studentId, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Started,
                StartTime = DateTime.UtcNow,
            };
            ctx.TestInstances.AddRange(completed, started);
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var stats = await NewService(read).GetGroupedStudentStatistics(studentId);

        stats.Total.TotalSolvedTests.ShouldBe(2);
        stats.Total.CompletedTests.ShouldBe(1);
        stats.Total.TotalCorrectAnswers.ShouldBe(1);
        stats.Total.TotalWrongAnswers.ShouldBe(0);
        stats.Total.TotalTimeSpentMinutes.ShouldBe(2); // 120s / 60
        stats.Grouped.ShouldHaveSingleItem().CompletedTests.ShouldBe(1);
    }

    public void Dispose() => _db.Dispose();
}
