using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #16 — "Sınavı kopyala": WorksheetAuthoringService.CopyWorksheetAsync.
/// Kopya yeni bir worksheet olur; kopyalayan onun sahibidir. Atama/instance taşınmaz.
/// </summary>
public class WorksheetAuthoringCopyTests : IDisposable
{
    private const int Owner = 10;
    private const int Stranger = 20;
    private const int Admin = 999;

    private readonly TestDb _db = TestDb.Create();

    private WorksheetAuthoringService NewService(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    private async Task<int> SeedGradeAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = await ctx.Grades.FirstOrDefaultAsync();
        if (grade != null) return grade.Id;
        grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        return grade.Id;
    }

    private async Task<int> SeedSourceWorksheetAsync(
        int? ownerUserId,
        WorksheetTeacherSharing sharing = WorksheetTeacherSharing.Private,
        WorksheetStudentVisibility studentVisibility = WorksheetStudentVisibility.Normal,
        int questionCount = 0,
        bool isDeleted = false)
    {
        var gradeId = await SeedGradeAsync();

        await using var ctx = _db.NewContext();
        var ws = new Worksheet
        {
            Name = "Kaynak Sınav",
            Description = "kaynak açıklama",
            GradeId = gradeId,
            MaxDurationSeconds = 1234,
            IsPracticeTest = true,
            Subtitle = "alt başlık",
            BadgeText = "rozet",
            ImageUrl = "http://minio/kaynak.png",
        };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();

        for (var i = 1; i <= questionCount; i++)
        {
            var q = new Question { Text = $"Soru {i}" };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = ws.Id, QuestionId = q.Id, Order = i });
        }

        ws.CreateUserId = ownerUserId;
        ws.TeacherSharing = sharing;
        ws.StudentVisibility = studentVisibility;
        ws.IsDeleted = isDeleted;
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    private async Task<CopyWorksheetResultDto> CopyAsync(int sourceId, int userId, bool isAdmin)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CopyWorksheetAsync(sourceId, userId, isAdmin);
    }

    // ---- Kabul kriteri 1: PublicView kaynağı yabancı öğretmen kopyalar -> başarı ----

    [Theory]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public async Task CopyWorksheetAsync_PublicSource_StrangerTeacher_SucceedsAndReturnsNewWorksheet(
        WorksheetTeacherSharing sharing)
    {
        var sourceId = await SeedSourceWorksheetAsync(Owner, sharing);

        var result = await CopyAsync(sourceId, Stranger, isAdmin: false);

        result.Success.ShouldBeTrue();
        result.NotFound.ShouldBeFalse();
        result.WorksheetId.ShouldBeGreaterThan(0);
        result.WorksheetId.ShouldNotBe(sourceId);

        await using var ctx = _db.NewContext();
        (await ctx.Worksheets.AnyAsync(w => w.Id == result.WorksheetId)).ShouldBeTrue();
    }

    // ---- Kabul kriteri 2: Private kaynağı yabancı öğretmen kopyalayamaz -> NotFound ----

    [Fact]
    public async Task CopyWorksheetAsync_PrivateSource_StrangerTeacher_ReturnsNotFound()
    {
        var sourceId = await SeedSourceWorksheetAsync(Owner, WorksheetTeacherSharing.Private);

        var result = await CopyAsync(sourceId, Stranger, isAdmin: false);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
        result.WorksheetId.ShouldBe(0);

        await using var ctx = _db.NewContext();
        (await ctx.Worksheets.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CopyWorksheetAsync_MissingSource_ReturnsNotFound()
    {
        var result = await CopyAsync(123456, Owner, isAdmin: false);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task CopyWorksheetAsync_SoftDeletedSource_ReturnsNotFound()
    {
        var sourceId = await SeedSourceWorksheetAsync(Owner, WorksheetTeacherSharing.PublicView, isDeleted: true);

        var result = await CopyAsync(sourceId, Owner, isAdmin: false);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
    }

    // ---- Kabul kriteri 3: Sahip kendi Private sınavını kopyalar -> başarı ----

    [Fact]
    public async Task CopyWorksheetAsync_OwnerCopiesOwnPrivateSource_Succeeds()
    {
        var sourceId = await SeedSourceWorksheetAsync(Owner, WorksheetTeacherSharing.Private);

        var result = await CopyAsync(sourceId, Owner, isAdmin: false);

        result.Success.ShouldBeTrue();
        result.WorksheetId.ShouldNotBe(sourceId);
    }

    [Fact]
    public async Task CopyWorksheetAsync_AdminCopiesPrivateSource_Succeeds()
    {
        var sourceId = await SeedSourceWorksheetAsync(Owner, WorksheetTeacherSharing.Private);

        var result = await CopyAsync(sourceId, Admin, isAdmin: true);

        result.Success.ShouldBeTrue();
    }

    // ---- Kabul kriteri 4: Kopya alanları ----

    [Fact]
    public async Task CopyWorksheetAsync_Success_CopiesScalarFieldsAndResetsVisibilityAndOwnership()
    {
        var sourceId = await SeedSourceWorksheetAsync(Owner, WorksheetTeacherSharing.PublicAssignable,
            WorksheetStudentVisibility.Restricted);

        var result = await CopyAsync(sourceId, Stranger, isAdmin: false);
        result.Success.ShouldBeTrue();

        await using var ctx = _db.NewContext();
        var source = await ctx.Worksheets.SingleAsync(w => w.Id == sourceId);
        var copy = await ctx.Worksheets.SingleAsync(w => w.Id == result.WorksheetId);

        copy.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        copy.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Normal);
        copy.CreateUserId.ShouldBe(Stranger);
        copy.SourceWorksheetId.ShouldBe(sourceId);

        copy.Name.ShouldBe(source.Name);
        copy.Description.ShouldBe(source.Description);
        copy.GradeId.ShouldBe(source.GradeId);
        copy.MaxDurationSeconds.ShouldBe(source.MaxDurationSeconds);
        copy.IsPracticeTest.ShouldBe(source.IsPracticeTest);
        copy.Subtitle.ShouldBe(source.Subtitle);
        copy.BadgeText.ShouldBe(source.BadgeText);
        copy.ImageUrl.ShouldBe(source.ImageUrl);
        copy.BookTestId.ShouldBe(source.BookTestId);

        // kaynak görünürlüğü/sahipliği değişmemeli
        source.TeacherSharing.ShouldBe(WorksheetTeacherSharing.PublicAssignable);
        source.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
        source.CreateUserId.ShouldBe(Owner);
    }

    // ---- Kabul kriteri 5: Sorular taşınır, kaynağınkiler bozulmaz ----

    [Fact]
    public async Task CopyWorksheetAsync_Success_CopiesWorksheetQuestionsWithSameQuestionIdAndOrder()
    {
        var sourceId = await SeedSourceWorksheetAsync(Owner, WorksheetTeacherSharing.PublicView, questionCount: 3);

        var result = await CopyAsync(sourceId, Stranger, isAdmin: false);
        result.Success.ShouldBeTrue();

        await using var ctx = _db.NewContext();
        var sourceQuestions = await ctx.TestQuestions
            .Where(wq => wq.TestId == sourceId)
            .OrderBy(wq => wq.Order)
            .ToListAsync();
        var copyQuestions = await ctx.TestQuestions
            .Where(wq => wq.TestId == result.WorksheetId)
            .OrderBy(wq => wq.Order)
            .ToListAsync();

        sourceQuestions.Count.ShouldBe(3); // kaynak dokunulmamış
        copyQuestions.Count.ShouldBe(3);

        copyQuestions.Select(q => (q.Order, q.QuestionId))
            .ShouldBe(sourceQuestions.Select(q => (q.Order, q.QuestionId)));

        // yeni satırlar, kaynağınkiler değil
        copyQuestions.Select(q => q.Id).ShouldNotBe(sourceQuestions.Select(q => q.Id));
    }

    // ---- Kabul kriteri 6: Atamalar taşınmaz ----

    [Fact]
    public async Task CopyWorksheetAsync_SourceHasAssignment_CopyHasNoAssignments()
    {
        var gradeId = await SeedGradeAsync();
        var sourceId = await SeedSourceWorksheetAsync(Owner, WorksheetTeacherSharing.PublicAssignable);

        await using (var seed = _db.NewContext())
        {
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = sourceId,
                GradeId = gradeId,
                StartAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var result = await CopyAsync(sourceId, Stranger, isAdmin: false);
        result.Success.ShouldBeTrue();

        await using var ctx = _db.NewContext();
        (await ctx.WorksheetAssignments.CountAsync(a => a.WorksheetId == result.WorksheetId)).ShouldBe(0);
        (await ctx.WorksheetAssignments.CountAsync(a => a.WorksheetId == sourceId)).ShouldBe(1);
    }

    // ---- Kabul kriteri 7: WorksheetInstance taşınmaz ----

    [Fact]
    public async Task CopyWorksheetAsync_SourceHasInstance_CopyHasNoInstances()
    {
        var sourceId = await SeedSourceWorksheetAsync(Owner, WorksheetTeacherSharing.PublicView);

        await using (var seed = _db.NewContext())
        {
            var student = new Student { UserId = 1, StudentNumber = "1" };
            seed.Students.Add(student);
            await seed.SaveChangesAsync();

            seed.TestInstances.Add(new WorksheetInstance
            {
                StudentId = student.Id,
                WorksheetId = sourceId,
                StartTime = DateTime.UtcNow,
                Status = WorksheetInstanceStatus.Started,
            });
            await seed.SaveChangesAsync();
        }

        var result = await CopyAsync(sourceId, Stranger, isAdmin: false);
        result.Success.ShouldBeTrue();

        await using var ctx = _db.NewContext();
        (await ctx.TestInstances.CountAsync(i => i.WorksheetId == result.WorksheetId)).ShouldBe(0);
        (await ctx.TestInstances.CountAsync(i => i.WorksheetId == sourceId)).ShouldBe(1);
    }

    public void Dispose() => _db.Dispose();
}
