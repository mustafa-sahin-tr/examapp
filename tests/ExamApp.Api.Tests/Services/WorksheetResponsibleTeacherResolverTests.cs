using ExamApp.Api.Data;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #105: ilgili öğretmen önceliği — aktif atama (her zaman) &gt; kopya sahibi &gt; worksheet sahibi.
/// Kaynak worksheet'in sahibi hiçbir zaman seçilmez. issue #326 (O2): atama yoksa sahip/kopyalayan YALNIZ öğrenciyle aynı
/// okuldaysa; okulsuz taraf (null == null) aynı okul sayılmaz. issue #334: atayan da aynı okul koşuluna tabi (tek istisna:
/// atayan VE öğrenci okulsuz). Varsayılan seed: sahip, kopyalayan, atayanlar ve öğrenciler Okul A'da.
/// </summary>
public class WorksheetResponsibleTeacherResolverTests : IDisposable
{
    private const int SourceOwner = 5000;
    private const int CopyOwner = 5100;
    private const int Assigner = 6000;
    private const int OtherAssigner = 6100;
    private const int StudentUserId = 101;
    private const int OtherStudentUserId = 102;

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static WorksheetResponsibleTeacherResolver NewResolver(AppDbContext ctx) => new(ctx);

    private sealed record World(int GradeId, int SchoolId, int OtherSchoolId, int SourceWorksheetId, int CopyWorksheetId, int StudentId, int OtherStudentId);

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var school = new School { Name = "Okul A" };
        var otherSchool = new School { Name = "Okul B" };
        ctx.AddRange(grade, school, otherSchool);
        await ctx.SaveChangesAsync();

        ctx.Teachers.AddRange(
            new Teacher { UserId = SourceOwner, SchoolId = school.Id },
            new Teacher { UserId = CopyOwner, SchoolId = school.Id },
            new Teacher { UserId = Assigner, SchoolId = school.Id },       // #334: atayanın okulu da karar girdisi
            new Teacher { UserId = OtherAssigner, SchoolId = school.Id });
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(SourceOwner);
        var source = new Worksheet { Name = "Kaynak", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        ctx.Worksheets.Add(source);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(CopyOwner);
        var copy = new Worksheet
        {
            Name = "Kopya", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, SourceWorksheetId = source.Id
        };
        ctx.Worksheets.Add(copy);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(0);
        var student = new Student { UserId = StudentUserId, StudentNumber = "s1", GradeId = grade.Id, SchoolId = school.Id };
        var other = new Student { UserId = OtherStudentUserId, StudentNumber = "s2", GradeId = grade.Id, SchoolId = school.Id };
        ctx.Students.AddRange(student, other);
        await ctx.SaveChangesAsync();

        return new World(grade.Id, school.Id, otherSchool.Id, source.Id, copy.Id, student.Id, other.Id);
    }

    private async Task<int> AddAssignmentAsync(int creatorUserId, int worksheetId, int? studentId = null, int? gradeId = null,
        int? schoolId = null, bool platformWide = false, DateTime? startAt = null, DateTime? endAt = null,
        bool? commentsOverride = null, bool deleted = false)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(creatorUserId);
        var a = new WorksheetAssignment
        {
            WorksheetId = worksheetId,
            StudentId = studentId,
            GradeId = gradeId,
            SchoolId = schoolId,
            IsPlatformWide = platformWide,
            StartAt = startAt ?? DateTime.UtcNow.AddDays(-1),
            EndAt = endAt,
            CommentsEnabledOverride = commentsOverride,
            IsDeleted = deleted
        };
        ctx.WorksheetAssignments.Add(a);
        await ctx.SaveChangesAsync();
        return a.Id;
    }

    private async Task<ResponsibleTeacher?> ResolveAsync(int worksheetId, int studentUserId = StudentUserId)
    {
        await using var ctx = _db.NewContext();
        return await NewResolver(ctx).ResolveResponsibleTeacherAsync(worksheetId, studentUserId);
    }

    [Fact]
    public async Task Active_student_assignment_wins_over_worksheet_owner()
    {
        var w = await SeedAsync();
        var assignmentId = await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId);

        var teacher = await ResolveAsync(w.SourceWorksheetId);

        teacher.ShouldBe(new ResponsibleTeacher(Assigner, ResponsibleTeacherSource.Assignment, assignmentId));
    }

    [Fact]
    public async Task Active_assignment_wins_even_on_a_copy_and_even_if_the_student_started_it_on_their_own()
    {
        var w = await SeedAsync();
        var assignmentId = await AddAssignmentAsync(Assigner, w.CopyWorksheetId, studentId: w.StudentId);
        await using (var ctx = _db.NewContext())
        {
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.CopyWorksheetId, StartTime = DateTime.UtcNow.AddDays(-3),
                Status = WorksheetInstanceStatus.Completed
            });
            await ctx.SaveChangesAsync();
        }

        var teacher = await ResolveAsync(w.CopyWorksheetId);

        teacher.ShouldBe(new ResponsibleTeacher(Assigner, ResponsibleTeacherSource.Assignment, assignmentId));
    }

    [Fact]
    public async Task Active_grade_assignment_in_the_students_school_counts()
    {
        var w = await SeedAsync();
        var assignmentId = await AddAssignmentAsync(Assigner, w.SourceWorksheetId, gradeId: w.GradeId, schoolId: w.SchoolId);

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(Assigner, ResponsibleTeacherSource.Assignment, assignmentId));
    }

    [Fact]
    public async Task Grade_assignment_of_another_school_is_not_the_students_assignment()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, gradeId: w.GradeId, schoolId: w.OtherSchoolId);

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(SourceOwner, ResponsibleTeacherSource.Owner, null));
    }

    [Fact]
    public async Task Student_targeted_assignment_beats_a_newer_grade_assignment()
    {
        var w = await SeedAsync();
        var studentAssignment = await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId,
            startAt: DateTime.UtcNow.AddDays(-5));
        await AddAssignmentAsync(OtherAssigner, w.SourceWorksheetId, gradeId: w.GradeId, schoolId: w.SchoolId,
            startAt: DateTime.UtcNow.AddDays(-1));

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(Assigner, ResponsibleTeacherSource.Assignment, studentAssignment));
    }

    [Fact]
    public async Task Among_same_kind_assignments_the_newest_start_wins()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, gradeId: w.GradeId, schoolId: w.SchoolId,
            startAt: DateTime.UtcNow.AddDays(-5), endAt: DateTime.UtcNow.AddDays(1));
        var newer = await AddAssignmentAsync(OtherAssigner, w.SourceWorksheetId, gradeId: w.GradeId, platformWide: true,
            startAt: DateTime.UtcNow.AddHours(-1));

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(OtherAssigner, ResponsibleTeacherSource.Assignment, newer));
    }

    [Fact]
    public async Task Expired_future_and_deleted_assignments_are_ignored()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId,
            startAt: DateTime.UtcNow.AddDays(-10), endAt: DateTime.UtcNow.AddDays(-1));
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId, startAt: DateTime.UtcNow.AddDays(2));
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId, deleted: true);

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(SourceOwner, ResponsibleTeacherSource.Owner, null));
    }

    [Fact]
    public async Task Without_assignment_a_copy_goes_to_the_copier_not_the_source_owner()
    {
        var w = await SeedAsync();
        // Kaynak worksheet'teki atama kopyayı etkilemez.
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId);

        (await ResolveAsync(w.CopyWorksheetId))
            .ShouldBe(new ResponsibleTeacher(CopyOwner, ResponsibleTeacherSource.CopyOwner, null));
    }

    [Fact]
    public async Task Without_assignment_an_original_goes_to_its_creator()
    {
        var w = await SeedAsync();

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(SourceOwner, ResponsibleTeacherSource.Owner, null));
    }

    [Fact]
    public async Task Legacy_assignment_without_creator_falls_back_to_the_owner()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(0, w.SourceWorksheetId, studentId: w.StudentId);

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(SourceOwner, ResponsibleTeacherSource.Owner, null));
    }

    [Fact]
    public async Task Ownerless_legacy_worksheet_without_assignment_has_no_responsible_teacher()
    {
        var w = await SeedAsync();
        int legacyId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(0);
            var legacy = new Worksheet { Name = "Legacy", Description = "", GradeId = w.GradeId, MaxDurationSeconds = 60 };
            ctx.Worksheets.Add(legacy);
            await ctx.SaveChangesAsync();
            legacyId = legacy.Id;
        }

        (await ResolveAsync(legacyId)).ShouldBeNull();
        (await ResolveAsync(999_999)).ShouldBeNull();
    }

    [Fact]
    public async Task Retired_worksheet_still_resolves_its_owner()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var ws = await ctx.Worksheets.FindAsync(w.SourceWorksheetId);
            ws!.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(SourceOwner, ResponsibleTeacherSource.Owner, null));
    }

    [Fact]
    public async Task Unknown_student_user_has_no_school_so_there_is_no_owner_fallback()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, gradeId: w.GradeId, platformWide: true);

        // issue #326: Students satırı olmayan kullanıcının okulu yok → sahip fallback'i yok (eskiden sahibe düşerdi).
        (await ResolveAsync(w.SourceWorksheetId, studentUserId: 424242)).ShouldBeNull();
    }

    [Fact]
    public async Task Batch_resolution_matches_single_resolution_per_student()
    {
        var w = await SeedAsync();
        var assignmentId = await AddAssignmentAsync(Assigner, w.CopyWorksheetId, studentId: w.StudentId);

        await using var ctx = _db.NewContext();
        var map = await NewResolver(ctx).ResolveResponsibleTeachersAsync(
            w.CopyWorksheetId, new[] { StudentUserId, OtherStudentUserId, StudentUserId });

        map.Count.ShouldBe(2);
        map[StudentUserId].ShouldBe(new ResponsibleTeacher(Assigner, ResponsibleTeacherSource.Assignment, assignmentId));
        map[OtherStudentUserId].ShouldBe(new ResponsibleTeacher(CopyOwner, ResponsibleTeacherSource.CopyOwner, null));
    }

    [Fact]
    public async Task Relevant_active_assignment_carries_the_comments_override()
    {
        var w = await SeedAsync();
        var id = await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId, commentsOverride: false);

        await using var ctx = _db.NewContext();
        var resolver = NewResolver(ctx);
        var relevant = await resolver.FindRelevantActiveAssignmentAsync(w.SourceWorksheetId, w.StudentId, w.GradeId, w.SchoolId);
        var none = await resolver.FindRelevantActiveAssignmentAsync(w.CopyWorksheetId, w.StudentId, w.GradeId, w.SchoolId);

        relevant.ShouldBe(new RelevantAssignment(id, Assigner, false));
        none.ShouldBeNull();
    }

    [Fact]
    public async Task Overload_with_caller_loaded_worksheet_matches_the_id_based_resolution()
    {
        var w = await SeedAsync();
        var assignmentId = await AddAssignmentAsync(Assigner, w.CopyWorksheetId, studentId: w.StudentId);

        await using var ctx = _db.NewContext();
        var resolver = NewResolver(ctx);
        var info = new ResponsibleTeacherWorksheet(w.CopyWorksheetId, CopyOwner, w.SourceWorksheetId);

        (await resolver.ResolveResponsibleTeacherAsync(info, StudentUserId))
            .ShouldBe(new ResponsibleTeacher(Assigner, ResponsibleTeacherSource.Assignment, assignmentId));
        (await resolver.ResolveResponsibleTeacherAsync(info, OtherStudentUserId))
            .ShouldBe(await resolver.ResolveResponsibleTeacherAsync(w.CopyWorksheetId, OtherStudentUserId));
    }

    /// <summary>
    /// review D6: resolver bellek içi <c>IsAssignmentVisibleTo</c> kullanır; SQL tarafı <c>AssignmentVisibleTo</c>
    /// expression'ını. İkisi tüm kombinasyonlarda aynı sonucu vermeli.
    /// </summary>
    [Fact]
    public void In_memory_assignment_visibility_matches_the_sql_expression_for_every_combination()
    {
        int?[] ids = { null, 1, 2 };
        bool[] flags = { false, true };
        var checkedCases = 0;
        foreach (var aStudent in ids)
        foreach (var aGrade in ids)
        foreach (var aSchool in ids)
        foreach (var wide in flags)
        foreach (var grade in ids)
        foreach (var school in ids)
        {
            const int studentId = 1;
            var expected = WorksheetStudentAccess.AssignmentVisibleTo(studentId, grade, school).Compile()(new WorksheetAssignment
            {
                StudentId = aStudent, GradeId = aGrade, SchoolId = aSchool, IsPlatformWide = wide
            });
            WorksheetStudentAccess.IsAssignmentVisibleTo(aStudent, aGrade, aSchool, wide, studentId, grade, school)
                .ShouldBe(expected, $"a=({aStudent},{aGrade},{aSchool},{wide}) s=({grade},{school})");
            checkedCases++;
        }

        checkedCases.ShouldBe(3 * 3 * 3 * 2 * 3 * 3);
    }

    // ---- issue #326 (O2): sahip fallback'i yalnız aynı okulda -------------------------------------------------------

    private async Task SetTeacherSchoolAsync(int userId, int? schoolId)
    {
        await using var ctx = _db.NewContext();
        await ctx.Teachers.Where(t => t.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(t => t.SchoolId, schoolId));
    }

    private async Task SetStudentSchoolAsync(int userId, int? schoolId)
    {
        await using var ctx = _db.NewContext();
        await ctx.Students.Where(s => s.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SchoolId, schoolId));
    }

    [Fact]
    public async Task Owner_in_another_school_is_not_responsible_without_an_assignment()
    {
        var w = await SeedAsync();
        await SetTeacherSchoolAsync(SourceOwner, w.OtherSchoolId);

        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull();
    }

    [Fact]
    public async Task Schoolless_independent_owner_is_not_responsible_without_an_assignment()
    {
        var w = await SeedAsync();
        await SetTeacherSchoolAsync(SourceOwner, null);

        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull();
    }

    [Fact]
    public async Task Schoolless_student_gets_no_owner_fallback_even_from_a_schoolless_owner()
    {
        var w = await SeedAsync();
        await SetTeacherSchoolAsync(SourceOwner, null);
        await SetStudentSchoolAsync(StudentUserId, null);

        // null == null aynı okul SAYILMAZ (kullanıcı onayı 2026-09-30).
        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull();

        await SetTeacherSchoolAsync(SourceOwner, w.SchoolId);
        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull();
    }

    [Fact]
    public async Task Owner_without_any_teacher_profile_is_not_responsible_without_an_assignment()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.UserId == SourceOwner).ExecuteDeleteAsync();

        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull();
    }

    [Fact]
    public async Task Same_school_assignment_wins_even_when_the_owner_is_out_of_school()
    {
        var w = await SeedAsync();
        await SetTeacherSchoolAsync(SourceOwner, w.OtherSchoolId);
        var assignmentId = await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId);

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(Assigner, ResponsibleTeacherSource.Assignment, assignmentId));
    }

    // ---- issue #334: atayan da güncel okul koşuluna tabi --------------------------------------------------------------

    [Theory]
    [InlineData(true)]  // atayan başka okula taşındı
    [InlineData(false)] // atayan bağımsıza geçti (okulsuz), öğrenci okullu
    public async Task Assigner_outside_the_students_school_is_not_responsible_and_the_same_school_owner_takes_over(bool moved)
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId);
        await SetTeacherSchoolAsync(Assigner, moved ? w.OtherSchoolId : null);

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(SourceOwner, ResponsibleTeacherSource.Owner, null), "atama yokmuş gibi sahip fallback'i");

        await SetTeacherSchoolAsync(SourceOwner, w.OtherSchoolId);
        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull("sahip de okul dışı → sorumlu yok");
    }

    [Fact]
    public async Task Student_who_moves_to_another_school_is_no_longer_pinned_to_the_old_schools_assigner()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId); // öğrenci hedefli: okul değişse de aktif
        await SetStudentSchoolAsync(StudentUserId, w.OtherSchoolId);

        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull("ne atayan ne sahip öğrencinin yeni okulunda");
    }

    [Fact]
    public async Task Independent_assigner_keeps_a_schoolless_student()
    {
        var w = await SeedAsync();
        var assignmentId = await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId);
        await SetTeacherSchoolAsync(Assigner, null);
        await SetStudentSchoolAsync(StudentUserId, null);

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(Assigner, ResponsibleTeacherSource.Assignment, assignmentId), "bağımsız istisnası");
    }

    [Fact]
    public async Task School_assigner_is_not_responsible_for_a_schoolless_student()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId);
        await SetStudentSchoolAsync(StudentUserId, null);

        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull("okullu atayan + okulsuz öğrenci: eşleşme yok");
    }

    [Fact]
    public async Task Assigner_without_a_teacher_profile_is_not_responsible()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId);
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.UserId == Assigner).ExecuteDeleteAsync();

        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(SourceOwner, ResponsibleTeacherSource.Owner, null), "okulu çözülemeyen atayan sorumlu olmaz");
    }

    [Fact]
    public async Task Batch_resolution_applies_the_assigner_school_rule_per_student()
    {
        var w = await SeedAsync();
        var grade = await AddAssignmentAsync(Assigner, w.SourceWorksheetId, gradeId: w.GradeId, platformWide: true);
        await SetStudentSchoolAsync(OtherStudentUserId, w.OtherSchoolId);

        await using var ctx = _db.NewContext();
        var map = await NewResolver(ctx).ResolveResponsibleTeachersAsync(w.SourceWorksheetId, new[] { StudentUserId, OtherStudentUserId });

        map[StudentUserId].ShouldBe(new ResponsibleTeacher(Assigner, ResponsibleTeacherSource.Assignment, grade));
        map[OtherStudentUserId].ShouldBeNull("platform geneli atama aktif ama atayan öğrencinin okulunda değil");
    }

    [Theory]
    //          atayan  sahip   öğrenci  beklenen
    [InlineData(1,      1,      1,       "assignment")]
    [InlineData(2,      1,      1,       "owner")]
    [InlineData(null,   1,      1,       "owner")]
    [InlineData(2,      2,      1,       "none")]
    [InlineData(1,      null,   null,    "none")]
    [InlineData(null,   null,   null,    "assignment")] // bağımsız istisnası yalnız atamada
    [InlineData(null,   1,      null,    "assignment")]
    [InlineData(1,      1,      null,    "none")]
    public void Decide_matrix_follows_the_single_school_rule(int? assignerSchool, int? ownerSchool, int? studentSchool, string expected)
    {
        var worksheet = new ResponsibleTeacherWorksheet(1, SourceOwner, null);
        var assignment = new RelevantAssignment(7, Assigner, null);

        var result = ResponsibleTeacherRule.Decide(worksheet, assignment, ownerSchool, studentSchool, assignerSchool);

        var actual = result switch
        {
            null => "none",
            { Source: ResponsibleTeacherSource.Assignment } => "assignment",
            _ => "owner"
        };
        actual.ShouldBe(expected);
    }

    [Fact]
    public void Owner_fallback_has_no_schoolless_exception()
    {
        ResponsibleTeacherRule.Decide(new ResponsibleTeacherWorksheet(1, SourceOwner, null), null, null, null, null)
            .ShouldBeNull("sahip fallback'inde okulsuz istisna yok");
    }

    /// <summary>
    /// CR-3 (kabul edilen davranış): birden çok aktif atamada önce seçim (öğrenci hedefli &gt; sınıf hedefli &gt; yeni StartAt),
    /// SONRA okul koşulu; seçilen atayan koşulu sağlamazsa diğer atamaya geçilmez, sahip fallback'ine düşülür.
    /// </summary>
    [Fact]
    public async Task With_several_active_assignments_the_school_rule_applies_after_selection_then_falls_back_to_the_owner()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(Assigner, w.SourceWorksheetId, studentId: w.StudentId);       // öğrenci hedefli, okul A'lı atayan
        await SetStudentSchoolAsync(StudentUserId, w.OtherSchoolId);                            // öğrenci B'ye taşındı
        await SetTeacherSchoolAsync(OtherAssigner, w.OtherSchoolId);
        await AddAssignmentAsync(OtherAssigner, w.SourceWorksheetId, gradeId: w.GradeId, schoolId: w.OtherSchoolId); // B'de sınıf hedefli

        // Seçilen = öğrenci hedefli (Assigner, okul A) → okul koşulu yok; B'deki sınıf ataması (OtherAssigner) DEVREYE GİRMEZ;
        // sahip de A'da → sorumlu yok.
        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull();

        // Sahip öğrencinin yeni okuluna geçerse fallback sahibi verir (yine OtherAssigner değil).
        await SetTeacherSchoolAsync(SourceOwner, w.OtherSchoolId);
        (await ResolveAsync(w.SourceWorksheetId))
            .ShouldBe(new ResponsibleTeacher(SourceOwner, ResponsibleTeacherSource.Owner, null));
    }

    [Fact]
    public async Task Copier_in_another_school_is_not_responsible_and_the_source_owner_is_never_chosen()
    {
        var w = await SeedAsync();
        await SetTeacherSchoolAsync(CopyOwner, w.OtherSchoolId); // kaynak sahibi hâlâ öğrencinin okulunda

        (await ResolveAsync(w.CopyWorksheetId)).ShouldBeNull();
    }

    [Fact]
    public async Task Legacy_creatorless_assignment_falls_back_only_to_a_same_school_owner()
    {
        var w = await SeedAsync();
        await AddAssignmentAsync(0, w.SourceWorksheetId, studentId: w.StudentId);
        await SetTeacherSchoolAsync(SourceOwner, w.OtherSchoolId);

        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull();
    }

    [Fact]
    public async Task Batch_resolution_applies_the_school_rule_per_student()
    {
        var w = await SeedAsync();
        await SetStudentSchoolAsync(OtherStudentUserId, w.OtherSchoolId);

        await using var ctx = _db.NewContext();
        var map = await NewResolver(ctx).ResolveResponsibleTeachersAsync(w.SourceWorksheetId, new[] { StudentUserId, OtherStudentUserId });

        map[StudentUserId].ShouldBe(new ResponsibleTeacher(SourceOwner, ResponsibleTeacherSource.Owner, null));
        map[OtherStudentUserId].ShouldBeNull();
    }

    // ---- issue #326 (D3): okul tek kaynaktan, çoklu satırda deterministik ------------------------------------------

    [Fact]
    public async Task Owner_school_comes_from_the_live_teacher_row_not_a_soft_deleted_one()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // Eski (silinmiş) satır öğrencinin okulunda; canlı satır başka okulda → aynı okul DEĞİL.
            await ctx.Teachers.Where(t => t.UserId == SourceOwner).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
            ctx.Teachers.Add(new Teacher { UserId = SourceOwner, SchoolId = w.OtherSchoolId });
            await ctx.SaveChangesAsync();
        }

        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull();
    }

    [Fact]
    public async Task Student_school_comes_from_the_live_student_row_not_a_soft_deleted_one()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.Students.Where(s => s.UserId == StudentUserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            ctx.Students.Add(new Student { UserId = StudentUserId, StudentNumber = "s1b", GradeId = w.GradeId, SchoolId = w.OtherSchoolId });
            await ctx.SaveChangesAsync();
        }

        (await ResolveAsync(w.SourceWorksheetId)).ShouldBeNull();
    }
}
