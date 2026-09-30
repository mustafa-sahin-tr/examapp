using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #105: "yorum/soru açık mı" anahtarı — worksheet oluşturma/düzenleme/kopyalama ve atama override'ı; okuma
/// DTO'larında geri dönmesi (UI dilim 3).
/// </summary>
public class WorksheetCommentsToggleTests : IDisposable
{
    private const int Owner = 10;
    private const int Copier = 30;

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static WorksheetAuthoringService NewAuthoring(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    private static WorksheetAssignmentService NewAssignments(AppDbContext ctx) => new(ctx, new SchoolAccessPolicy(ctx));

    private async Task<(int gradeId, int bookId, int bookTestId)> SeedBookAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var book = new Book { Name = "Kitap", BookTests = { new BookTest { Name = "Test" } } };
        ctx.AddRange(grade, book);
        await ctx.SaveChangesAsync();
        return (grade.Id, book.Id, book.BookTests.First().Id);
    }

    private static ExamDto Dto(int gradeId, int bookId, int bookTestId, bool? commentsEnabled, int? id = null, string name = "Test 1") => new()
    {
        Id = id, Name = name, Description = "d", GradeId = gradeId, MaxDurationSeconds = 600,
        BookId = bookId, BookTestId = bookTestId, CommentsEnabled = commentsEnabled
    };

    private async Task<bool> StoredAsync(int worksheetId)
    {
        await using var ctx = _db.NewContext();
        return (await ctx.Worksheets.SingleAsync(w => w.Id == worksheetId)).CommentsEnabled;
    }

    [Fact]
    public async Task Create_defaults_to_enabled_when_the_field_is_omitted()
    {
        var (g, b, bt) = await SeedBookAsync();
        await using var ctx = _db.NewContext();

        var r = await NewAuthoring(ctx).CreateOrUpdateAsync(Dto(g, b, bt, commentsEnabled: null), Owner, isAdmin: false);

        r.Success.ShouldBeTrue(r.Message);
        (await StoredAsync(r.ExamId!.Value)).ShouldBeTrue();
    }

    [Fact]
    public async Task Teacher_can_create_closed_and_toggle_it_later_while_omitting_keeps_the_value()
    {
        var (g, b, bt) = await SeedBookAsync();
        int id;
        await using (var ctx = _db.NewContext())
        {
            var r = await NewAuthoring(ctx).CreateOrUpdateAsync(Dto(g, b, bt, commentsEnabled: false), Owner, isAdmin: false);
            r.Success.ShouldBeTrue(r.Message);
            id = r.ExamId!.Value;
        }
        (await StoredAsync(id)).ShouldBeFalse();

        await using (var ctx = _db.NewContext())
            (await NewAuthoring(ctx).CreateOrUpdateAsync(Dto(g, b, bt, commentsEnabled: null, id: id), Owner, isAdmin: false))
                .Success.ShouldBeTrue();
        (await StoredAsync(id)).ShouldBeFalse();

        await using (var ctx = _db.NewContext())
            (await NewAuthoring(ctx).CreateOrUpdateAsync(Dto(g, b, bt, commentsEnabled: true, id: id), Owner, isAdmin: false))
                .Success.ShouldBeTrue();
        (await StoredAsync(id)).ShouldBeTrue();

        await using (var ctx = _db.NewContext())
            (await NewAuthoring(ctx).CreateOrUpdateAsync(Dto(g, b, bt, commentsEnabled: false, id: id), Owner, isAdmin: false))
                .Success.ShouldBeTrue();
        (await StoredAsync(id)).ShouldBeFalse();
    }

    [Fact]
    public async Task Copy_starts_enabled_regardless_of_the_source_setting()
    {
        var (g, _, _) = await SeedBookAsync();
        int sourceId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(Owner);
            var ws = new Worksheet
            {
                Name = "Kaynak", Description = "", GradeId = g, MaxDurationSeconds = 60, CommentsEnabled = false,
                TeacherSharing = WorksheetTeacherSharing.PublicView
            };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            sourceId = ws.Id;
        }

        await using var copyCtx = _db.NewContext();
        var copy = await NewAuthoring(copyCtx).CopyWorksheetAsync(sourceId, Copier, isAdmin: false);

        copy.Success.ShouldBeTrue(copy.Message);
        // review D8: kopyalayan yeni sahip — kaynağın kapalı ayarı devralınmaz.
        (await StoredAsync(copy.WorksheetId)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assignment_override_is_stored_and_returned_to_teacher_and_student(bool? commentsOverride)
    {
        int wsId, studentId, gradeId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(Owner);
            var grade = new Grade { Name = "8" };
            var school = new School { Name = "Okul" };
            ctx.AddRange(grade, school);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "Atanacak", Description = "", GradeId = grade.Id, CommentsEnabled = true };
            var student = new Student { UserId = 77, StudentNumber = "n", SchoolId = school.Id, GradeId = grade.Id };
            ctx.AddRange(ws, student, new Teacher { UserId = Owner, SchoolId = school.Id });
            await ctx.SaveChangesAsync();
            (wsId, studentId, gradeId) = (ws.Id, student.Id, grade.Id);
        }

        await using (var ctx = _db.NewContext())
        {
            var r = await NewAssignments(ctx).AssignAsUserAsync(ctx, new WorksheetAssignmentRequestDto
            {
                WorksheetId = wsId, StudentId = studentId, StartAt = DateTime.UtcNow.AddHours(-1),
                CommentsEnabledOverride = commentsOverride
            }, Owner);
            r.Success.ShouldBeTrue(r.Message);
        }

        await using (var ctx = _db.NewContext())
        {
            (await ctx.WorksheetAssignments.SingleAsync()).CommentsEnabledOverride.ShouldBe(commentsOverride);

            var teacherView = await NewAssignments(ctx).GetWorksheetAssignmentsForTeacherAsync(wsId, SchoolScope.Unrestricted(Owner));
            teacherView.Assignments.Single().CommentsEnabledOverride.ShouldBe(commentsOverride);

            var studentView = await NewAssignments(ctx).GetActiveAssignmentsForStudentAsync(
                new StudentProfileDto { Id = studentId, GradeId = gradeId });
            var item = studentView.Single();
            item.CommentsEnabledOverride.ShouldBe(commentsOverride);
            item.CommentsEnabled.ShouldBe(commentsOverride ?? true);
        }
    }
}
