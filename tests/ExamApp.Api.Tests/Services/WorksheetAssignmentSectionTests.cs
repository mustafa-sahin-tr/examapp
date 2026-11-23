using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #1296 (#1071 takibi): test / etkinlik atamasında şube hedefi. Atama şubenin sınıfı ve okuluyla yazılır, yalnız şubenin
/// aktif üyelerine görünür (öğrenci listesi, test başlatma kapısı, ödev listesi, öğretmen takibi aynı kural); üyelik bitince
/// (taşındı, okul değişti) düşer. Çakışma şube başınadır: 5-A ve 5-B ayrı alır, bütün sınıf ataması şubeyi kapsar.
/// Veli gecikme bildirimi: <c>ParentNotificationProducersTests.Sweep_notifies_parents_for_a_section_assignment_only_while_the_student_is_a_member</c>;
/// SQL ve bellek içi kuralın eşdeğerliği: <c>WorksheetResponsibleTeacherResolverTests</c>.
/// </summary>
public class WorksheetAssignmentSectionTests : IDisposable
{
    private const int TeacherUser = 1;
    private static readonly DateTime Start = DateTime.UtcNow.AddDays(-1);

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record World(int SchoolId, int OtherSchoolId, int GradeId, int Ws, int SectionA, int SectionB, int OtherSchoolSection,
        int InA, int InB, int NoSection);

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        var school = new School { Name = "Okul" };
        var other = new School { Name = "Öbür" };
        ctx.AddRange(grade, school, other);
        await ctx.SaveChangesAsync();

        SchoolSection Section(int schoolId, string name) => new() { SchoolId = schoolId, GradeId = grade.Id, Name = name, NormalizedName = name };
        var a = Section(school.Id, "A");
        var b = Section(school.Id, "B");
        var o = Section(other.Id, "A");
        Student S(int userId) => new()
        {
            UserId = userId, StudentNumber = $"n{userId}", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id
        };
        var inA = S(10);
        var inB = S(11);
        var none = S(12);
        ctx.AddRange(a, b, o, inA, inB, none, new Teacher { UserId = TeacherUser, SchoolId = school.Id });
        await ctx.SaveChangesAsync();
        ctx.SchoolSectionMemberships.AddRange(
            new SchoolSectionMembership { SectionId = a.Id, StudentId = inA.Id, StartedAt = DateTime.UtcNow.AddDays(-10) },
            new SchoolSectionMembership { SectionId = b.Id, StudentId = inB.Id, StartedAt = DateTime.UtcNow.AddDays(-10) });

        ctx.SetCurrentUser(TeacherUser);
        var ws = new Worksheet { Name = "Kesirler", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        return new World(school.Id, other.Id, grade.Id, ws.Id, a.Id, b.Id, o.Id, inA.Id, inB.Id, none.Id);
    }

    private async Task<ResponseBaseDto> AssignAsync(World w, int? sectionId = null, int? gradeId = null, int? studentId = null,
        bool homework = false)
    {
        await using var ctx = _db.NewContext();
        return await new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx)).AssignWorksheetAsync(
            new WorksheetAssignmentRequestDto
            {
                WorksheetId = w.Ws, SectionId = sectionId, GradeId = gradeId, StudentId = studentId, StartAt = Start,
                EndAt = homework ? DateTime.UtcNow.AddDays(2) : null, IsHomework = homework
            },
            SchoolScope.For(TeacherUser, w.SchoolId));
    }

    /// <summary>Test başlatma kapısı ve öğrenci listesinin kullandığı aktif atama kümesi.</summary>
    private async Task<bool> SeesAsync(int studentId)
    {
        await using var ctx = _db.NewContext();
        var s = await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == studentId);
        return await ctx.ActiveAssignmentsFor(s.Id, s.GradeId, s.VerifiedSchoolId, DateTime.UtcNow).AnyAsync();
    }

    [Fact]
    public async Task A_section_assignment_is_written_with_the_sections_grade_and_school_and_reaches_only_its_members()
    {
        var w = await SeedAsync();

        (await AssignAsync(w, sectionId: w.SectionA)).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            var a = await ctx.WorksheetAssignments.AsNoTracking().SingleAsync();
            (a.SectionId, a.GradeId, a.SchoolId, a.StudentId, a.IsPlatformWide)
                .ShouldBe(((int?)w.SectionA, (int?)w.GradeId, (int?)w.SchoolId, (int?)null, false));
        }

        (await SeesAsync(w.InA)).ShouldBeTrue();
        (await SeesAsync(w.InB)).ShouldBeFalse();
        (await SeesAsync(w.NoSection)).ShouldBeFalse();
    }

    [Fact]
    public async Task Ending_the_membership_drops_the_assignment_for_that_student()
    {
        var w = await SeedAsync();
        (await AssignAsync(w, sectionId: w.SectionA)).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            // Okul değişimi (AdminStudentSchoolService) üyeliği SchoolChanged ile kapatır ve okulu değiştirir.
            var m = await ctx.SchoolSectionMemberships.SingleAsync(x => x.StudentId == w.InA);
            m.EndedAt = DateTime.UtcNow;
            m.EndReason = SchoolSectionEndReason.SchoolChanged;
            var s = await ctx.Students.SingleAsync(x => x.Id == w.InA);
            s.SchoolId = w.OtherSchoolId;
            await ctx.SaveChangesAsync();
        }

        (await SeesAsync(w.InA)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_student_moved_to_another_section_follows_the_new_section()
    {
        var w = await SeedAsync();
        (await AssignAsync(w, sectionId: w.SectionB)).Success.ShouldBeTrue();
        (await SeesAsync(w.InA)).ShouldBeFalse();

        await using (var ctx = _db.NewContext())
        {
            var m = await ctx.SchoolSectionMemberships.SingleAsync(x => x.StudentId == w.InA);
            m.EndedAt = DateTime.UtcNow;
            m.EndReason = SchoolSectionEndReason.Moved;
            await ctx.SaveChangesAsync();
            ctx.SchoolSectionMemberships.Add(new SchoolSectionMembership { SectionId = w.SectionB, StudentId = w.InA, StartedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        (await SeesAsync(w.InA)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_deleted_section_hides_its_assignment()
    {
        var w = await SeedAsync();
        (await AssignAsync(w, sectionId: w.SectionA)).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            (await ctx.SchoolSections.SingleAsync(s => s.Id == w.SectionA)).IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        (await SeesAsync(w.InA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Overlap_is_per_section_and_a_whole_class_assignment_covers_its_sections()
    {
        var w = await SeedAsync();

        (await AssignAsync(w, sectionId: w.SectionA)).Success.ShouldBeTrue();
        (await AssignAsync(w, sectionId: w.SectionB)).Success.ShouldBeTrue(); // 5-A ve 5-B ayrı
        (await AssignAsync(w, sectionId: w.SectionA)).Success.ShouldBeFalse(); // aynı şube aynı aralıkta
        (await AssignAsync(w, gradeId: w.GradeId)).Success.ShouldBeFalse(); // bütün sınıf şubeleri kapsar
    }

    [Fact]
    public async Task A_section_cannot_follow_a_whole_class_assignment_in_the_same_window()
    {
        var w = await SeedAsync();

        (await AssignAsync(w, gradeId: w.GradeId)).Success.ShouldBeTrue();
        (await AssignAsync(w, sectionId: w.SectionA)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Another_schools_section_is_not_found_and_targets_cannot_be_combined()
    {
        var w = await SeedAsync();

        var foreign = await AssignAsync(w, sectionId: w.OtherSchoolSection);
        foreign.Success.ShouldBeFalse();
        foreign.Message.ShouldContain("Şube");
        (await AssignAsync(w, sectionId: w.SectionA, gradeId: w.GradeId)).Success.ShouldBeFalse();
        (await AssignAsync(w, sectionId: w.SectionA, studentId: w.InA)).Success.ShouldBeFalse();

        await using var ctx = _db.NewContext();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Independent_teacher_cannot_target_a_section()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var result = await new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx)).AssignWorksheetAsync(
            new WorksheetAssignmentRequestDto { WorksheetId = w.Ws, SectionId = w.SectionA, StartAt = Start },
            SchoolScope.For(TeacherUser, null));

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Teacher_progress_and_homework_lists_count_only_the_section_members()
    {
        var w = await SeedAsync();
        (await AssignAsync(w, sectionId: w.SectionA, homework: true)).Success.ShouldBeTrue();

        await using var ctx = _db.NewContext();
        var assignments = new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx));
        var progress = (await assignments.GetWorksheetAssignmentsForTeacherAsync(w.Ws, SchoolScope.For(TeacherUser, w.SchoolId)))
            .Assignments.ShouldHaveSingleItem();
        (progress.TargetType, progress.TargetName, progress.StudentCount).ShouldBe(("Section", "5. Sınıf / A", 1));
        progress.Students.ShouldHaveSingleItem().StudentId.ShouldBe(w.InA);

        var homework = new AssignmentHomeworkService(ctx, assignments);
        (await homework.ListForTeacherAsync(SchoolScope.For(TeacherUser, w.SchoolId))).ShouldHaveSingleItem().StudentCount.ShouldBe(1);
        (await homework.ListForStudentAsync(10))!.ShouldHaveSingleItem().Status.ShouldBe("Pending");
        (await homework.ListForStudentAsync(11))!.ShouldBeEmpty();
    }
}
