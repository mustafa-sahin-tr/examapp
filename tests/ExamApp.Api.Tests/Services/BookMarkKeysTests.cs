using ExamApp.Api.Data;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>#1235: kitapta basılı cevap işaretlerinden kurulumda cevap anahtarı.</summary>
public class BookMarkKeysTests : IDisposable
{
    private const int Width = 1400;
    private readonly TestDb _db = TestDb.Create();

    private static string Mark(string letter, int x, int y) => $$"""{"letter":"{{letter}}","x":{{x}},"y":{{y}},"w":30,"h":30}""";

    [Fact]
    public void Marks_are_read_column_by_column_top_to_bottom()
    {
        var marks = BookMarkKeys.Parse($"[{Mark("D", 900, 100)},{Mark("B", 300, 900)},{Mark("A", 200, 100)},{Mark("C", 800, 50)}]");

        string.Concat(BookMarkKeys.ReadingOrder(marks, Width).Select(m => m.Letter)).ShouldBe("ABCD");
        BookMarkKeys.Parse("not json").ShouldBeEmpty();
        BookMarkKeys.AnswerIn(marks, new LayoutBox(150, 50, 400, 200)).ShouldBe(0);
        BookMarkKeys.AnswerIn(marks, new LayoutBox(0, 0, Width, 200)).ShouldBe(-1); // iki farklı harf
        BookMarkKeys.AnswerIn(marks, new LayoutBox(0, 400, 100, 100)).ShouldBe(-1);
    }

    [Fact]
    public async Task Setup_keys_come_from_the_marks_and_a_different_book_key_gets_them_as_conflicts()
    {
        var jobId = Guid.NewGuid();
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(41);
            ctx.BookImportJobs.Add(new BookImportJob
            {
                Id = jobId, BookName = "Kitap", Status = BookImportJobStatus.AwaitingConfirmation, FileUrl = "x",
                GradeId = (await AddGradeAsync(ctx)).Id,
            });
            // Test 1 (sayfa 1-2) anahtarsız; test 2 (sayfa 3) kitap anahtarı "AB" ile farklı; test 3 elle; test 4 işaretsiz.
            ctx.BookImportPages.AddRange(
                Page(jobId, 1, Mark("B", 900, 100), Mark("A", 100, 500)),
                Page(jobId, 2, Mark("C", 100, 100)),
                Page(jobId, 3, Mark("A", 100, 100), Mark("D", 100, 600)),
                Page(jobId, 4, Mark("E", 100, 100)),
                new BookImportPage { JobId = jobId, FilePage = 5, ImageUrl = "p5", Width = Width, Height = 2000 });
            ctx.BookImportJobTests.AddRange(
                Test(jobId, 1, 1, 2, null, null),
                Test(jobId, 2, 3, 3, "AB", BookKeySources.Book),
                Test(jobId, 3, 4, 4, "A", BookKeySources.Manual),
                Test(jobId, 4, 5, 5, null, null));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await BookMarkKeys.ApplyAsync(ctx, jobId, default)).ShouldBe(2);

        await using var check = _db.NewContext();
        var tests = await check.BookImportJobTests.OrderBy(t => t.Order).ToListAsync();
        (tests[0].AnswerKey, tests[0].KeySource, tests[0].KeyAnswers, tests[0].KeyConflicts).ShouldBe(("ABC", BookKeySources.Marks, 3, "..."));
        (tests[1].AnswerKey, tests[1].KeySource, tests[1].KeyConflicts).ShouldBe(("AB", BookKeySources.Book, ".D"));
        (tests[2].AnswerKey, tests[2].KeyConflicts).ShouldBe(("A", null));
        tests[3].AnswerKey.ShouldBeNull();

        // Yeniden temizlik (başka renk) işaretleri değiştirdiyse işaretten gelen anahtar yenilenir; aynıysa dokunulmaz.
        await using (var ctx = _db.NewContext())
        {
            var page = await ctx.BookImportPages.AsTracking().SingleAsync(p => p.JobId == jobId && p.FilePage == 2);
            page.MarksJson = $"[{Mark("D", 100, 100)}]";
            await ctx.SaveChangesAsync();
            (await BookMarkKeys.ApplyAsync(ctx, jobId, default)).ShouldBe(1);
            (await BookMarkKeys.ApplyAsync(ctx, jobId, default)).ShouldBe(0);
        }

        await using var after = _db.NewContext();
        (await after.BookImportJobTests.SingleAsync(t => t.Order == 1)).AnswerKey.ShouldBe("ABD");
    }

    private static async Task<Grade> AddGradeAsync(AppDbContext ctx)
    {
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        return grade;
    }

    private static BookImportPage Page(Guid jobId, int filePage, params string[] marks) => new()
    {
        JobId = jobId, FilePage = filePage, ImageUrl = $"p{filePage}", Width = Width, Height = 2000,
        MarksJson = $"[{string.Join(',', marks)}]",
    };

    private static BookImportJobTest Test(Guid jobId, int order, int from, int to, string? key, string? source) => new()
    {
        JobId = jobId, Order = order, Unit = 1, TestNo = order, Title = $"Test {order}", FileFrom = from, FileTo = to,
        AnswerKey = key, KeySource = source, KeyAnswers = key?.Length ?? 0,
    };

    public void Dispose() => _db.Dispose();
}
