using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #10 — PUT api/worksheet/{id}/visibility, backed by
/// WorksheetAuthoringService.UpdateVisibilityAsync.
/// </summary>
public class WorksheetAuthoringServiceVisibilityTests : IDisposable
{
    private const int Owner = 10;
    private const int OtherTeacher = 20;
    private const int Admin = 999;

    private readonly TestDb _db = TestDb.Create();

    private WorksheetAuthoringService NewService(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    private static UpdateWorksheetVisibilityDto Dto(
        WorksheetTeacherSharing sharing = WorksheetTeacherSharing.PublicAssignable,
        WorksheetStudentVisibility studentVisibility = WorksheetStudentVisibility.Restricted) => new()
    {
        TeacherSharing = sharing,
        StudentVisibility = studentVisibility,
    };

    private async Task<int> SeedWorksheetAsync(
        int? ownerUserId,
        WorksheetTeacherSharing sharing = WorksheetTeacherSharing.Private,
        WorksheetStudentVisibility studentVisibility = WorksheetStudentVisibility.Normal,
        bool isDeleted = false)
    {
        await using var ctx = _db.NewContext();
        var grade = await ctx.Grades.FirstOrDefaultAsync() ?? new Grade { Name = "5" };
        if (grade.Id == 0) { ctx.Grades.Add(grade); await ctx.SaveChangesAsync(); }

        var ws = new Worksheet { Name = "Alfa", Description = "d", GradeId = grade.Id };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = ownerUserId;
        ws.TeacherSharing = sharing;
        ws.StudentVisibility = studentVisibility;
        ws.IsDeleted = isDeleted;
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    private async Task<int> GradeIdAsync()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.Grades.FirstAsync()).Id;
    }

    private async Task<int> SeedAssignmentAsync(int worksheetId, int gradeId)
    {
        await using var ctx = _db.NewContext();
        var assignment = new WorksheetAssignment
        {
            WorksheetId = worksheetId,
            GradeId = gradeId,
            StartAt = DateTime.UtcNow,
        };
        ctx.WorksheetAssignments.Add(assignment);
        await ctx.SaveChangesAsync();
        return assignment.Id;
    }

    [Fact]
    public async Task UpdateVisibilityAsync_Owner_PersistsBothAxesAndReturnsSuccess()
    {
        var id = await SeedWorksheetAsync(Owner);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateVisibilityAsync(
            id, Dto(WorksheetTeacherSharing.PublicView, WorksheetStudentVisibility.Restricted), Owner, isAdmin: false);

        result.Success.ShouldBeTrue();
        result.NotFound.ShouldBeFalse();
        result.Forbidden.ShouldBeFalse();

        await using var verifyCtx = _db.NewContext();
        var ws = await verifyCtx.Worksheets.SingleAsync(w => w.Id == id);
        ws.TeacherSharing.ShouldBe(WorksheetTeacherSharing.PublicView);
        ws.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
    }

    [Fact]
    public async Task UpdateVisibilityAsync_Admin_CanUpdateAnotherTeachersWorksheet()
    {
        var id = await SeedWorksheetAsync(Owner);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateVisibilityAsync(
            id, Dto(WorksheetTeacherSharing.PublicAssignable, WorksheetStudentVisibility.Restricted), Admin, isAdmin: true);

        result.Success.ShouldBeTrue();

        await using var verifyCtx = _db.NewContext();
        var ws = await verifyCtx.Worksheets.SingleAsync(w => w.Id == id);
        ws.TeacherSharing.ShouldBe(WorksheetTeacherSharing.PublicAssignable);
        ws.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
    }

    [Fact]
    public async Task UpdateVisibilityAsync_NonOwnerNonAdmin_ReturnsForbiddenNotNotFound()
    {
        var id = await SeedWorksheetAsync(Owner);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateVisibilityAsync(id, Dto(), OtherTeacher, isAdmin: false);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
        result.NotFound.ShouldBeFalse();

        await using var verifyCtx = _db.NewContext();
        var ws = await verifyCtx.Worksheets.SingleAsync(w => w.Id == id);
        ws.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        ws.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Normal);
    }

    [Fact]
    public async Task UpdateVisibilityAsync_MissingWorksheet_ReturnsNotFound()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateVisibilityAsync(123456, Dto(), Owner, isAdmin: false);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
        result.Forbidden.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateVisibilityAsync_SoftDeletedWorksheet_ReturnsNotFound()
    {
        var id = await SeedWorksheetAsync(Owner, isDeleted: true);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateVisibilityAsync(id, Dto(), Owner, isAdmin: false);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateVisibilityAsync_SoftDeletedWorksheet_AdminAlsoGetsNotFound()
    {
        var id = await SeedWorksheetAsync(Owner, isDeleted: true);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateVisibilityAsync(id, Dto(), Admin, isAdmin: true);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateVisibilityAsync_PublicToPrivateTransition_DoesNotRemoveExistingAssignments()
    {
        var id = await SeedWorksheetAsync(Owner, WorksheetTeacherSharing.PublicAssignable, WorksheetStudentVisibility.Normal);
        var gradeId = await GradeIdAsync();
        var assignmentId = await SeedAssignmentAsync(id, gradeId);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateVisibilityAsync(
            id, Dto(WorksheetTeacherSharing.Private, WorksheetStudentVisibility.Normal), Owner, isAdmin: false);

        result.Success.ShouldBeTrue();

        await using var verifyCtx = _db.NewContext();
        var ws = await verifyCtx.Worksheets.SingleAsync(w => w.Id == id);
        ws.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);

        var assignment = await verifyCtx.WorksheetAssignments.SingleOrDefaultAsync(a => a.Id == assignmentId);
        assignment.ShouldNotBeNull();
        assignment!.WorksheetId.ShouldBe(id);
        assignment.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateVisibilityAsync_LegacyOwnerlessWorksheet_NonAdminCallerForbidden_OnlyAdminCanChange()
    {
        var id = await SeedWorksheetAsync(ownerUserId: null);

        await using var teacherCtx = _db.NewContext();
        var teacherResult = await NewService(teacherCtx).UpdateVisibilityAsync(id, Dto(), Owner, isAdmin: false);
        teacherResult.Success.ShouldBeFalse();
        teacherResult.Forbidden.ShouldBeTrue();
        teacherResult.NotFound.ShouldBeFalse();

        await using var adminCtx = _db.NewContext();
        var adminResult = await NewService(adminCtx).UpdateVisibilityAsync(
            id, Dto(WorksheetTeacherSharing.PublicView, WorksheetStudentVisibility.Restricted), Admin, isAdmin: true);
        adminResult.Success.ShouldBeTrue();

        await using var verifyCtx = _db.NewContext();
        var ws = await verifyCtx.Worksheets.SingleAsync(w => w.Id == id);
        ws.TeacherSharing.ShouldBe(WorksheetTeacherSharing.PublicView);
        ws.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
    }

    public void Dispose() => _db.Dispose();
}
