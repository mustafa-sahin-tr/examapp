using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>Small CRUD-ish services: Teacher, Book, Student (grade/theme).</summary>
public class SimpleServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    // ---------------- TeacherService ----------------

    [Fact]
    public async Task Teacher_Save_creates_then_updates_the_same_row()
    {
        await using (var ctx = _db.NewContext())
        {
            var created = await new TeacherService(ctx).Save(userId: 10, new RegisterTeacherDto { SchoolName = "A Okulu" });
            created.Success.ShouldBeTrue();
            created.Message.ShouldContain("kaydedildi");
        }

        await using (var ctx = _db.NewContext())
        {
            var updated = await new TeacherService(ctx).Save(userId: 10, new RegisterTeacherDto { SchoolName = "B Okulu" });
            updated.Message.ShouldContain("güncellendi");
        }

        await using var check = _db.NewContext();
        var rows = check.Teachers.Where(t => t.UserId == 10).ToList();
        rows.Count.ShouldBe(1);
        rows[0].SchoolName.ShouldBe("B Okulu");
    }

    [Fact]
    public async Task Teacher_GetTeacher_returns_null_when_absent()
    {
        await using var ctx = _db.NewContext();
        (await new TeacherService(ctx).GetTeacher(999)).ShouldBeNull();
    }

    [Fact]
    public async Task Teacher_UpdateTheme_fails_for_an_unknown_teacher_and_succeeds_otherwise()
    {
        await using (var ctx = _db.NewContext())
            (await new TeacherService(ctx).UpdateTeacherTheme(5, "enhanced", null)).Success.ShouldBeFalse();

        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 5, SchoolName = "S" });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            var r = await new TeacherService(ctx).UpdateTeacherTheme(5, "full", "{\"x\":1}");
            r.Success.ShouldBeTrue();
            r.ThemePreset.ShouldBe("full");
        }
    }

    // ---------------- BookService ----------------

    [Fact]
    public async Task Book_lists_all_books_and_tests_for_one_book()
    {
        int bookId;
        await using (var ctx = _db.NewContext())
        {
            var book = new Book { Name = "Matematik 5" };
            ctx.Books.Add(book);
            await ctx.SaveChangesAsync();
            bookId = book.Id;
            ctx.BookTests.AddRange(
                new BookTest { BookId = book.Id, Name = "Ünite 1" },
                new BookTest { BookId = book.Id, Name = "Ünite 2" });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var svc = new BookService(ctx2);
        (await svc.GetAllBooksAsync()).ShouldContain(b => b.Name == "Matematik 5");
        (await svc.GetBookTestsByBookIdAsync(bookId)).Count.ShouldBe(2);
        (await svc.GetBookTestsByBookIdAsync(bookId + 999)).ShouldBeEmpty();
    }

    // ---------------- StudentService ----------------

    private StudentService NewStudentService(AppDbContext ctx)
        => new(ctx, Substitute.For<IAuthApiClient>());

    [Fact]
    public async Task Student_Save_creates_the_student()
    {
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "5" };
            ctx.Grades.Add(g);
            await ctx.SaveChangesAsync();
            gradeId = g.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var r = await NewStudentService(ctx).Save(userId: 20,
                new RegisterStudentDto { StudentNumber = "123", SchoolName = "Okul", GradeId = gradeId });
            r.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        check.Students.Count(s => s.UserId == 20).ShouldBe(1);
    }

    [Fact]
    public async Task Student_UpdateGrade_moves_the_student_to_the_new_grade()
    {
        int g1, g2;
        await using (var ctx = _db.NewContext())
        {
            var a = new Grade { Name = "3" };
            var b = new Grade { Name = "4" };
            ctx.AddRange(a, b);
            await ctx.SaveChangesAsync();
            g1 = a.Id; g2 = b.Id;
            ctx.Students.Add(new Student { UserId = 30, StudentNumber = "n", SchoolName = "s", GradeId = g1 });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewStudentService(ctx).UpdateStudentGrade(30, g2)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        check.Students.Single(s => s.UserId == 30).GradeId.ShouldBe(g2);
    }

    [Fact]
    public async Task Student_UpdateTheme_fails_when_the_student_is_unknown()
    {
        await using var ctx = _db.NewContext();
        (await NewStudentService(ctx).UpdateStudentTheme(404, "minimal", null)).Success.ShouldBeFalse();
    }
}
