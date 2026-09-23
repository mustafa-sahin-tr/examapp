using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;

namespace ExamApp.Api.Tests.Services;

/// <summary>Small CRUD-ish services: Teacher, Book, Student (grade/theme).</summary>
public class SimpleServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    // ---------------- TeacherService ----------------

    private TeacherService NewTeacherService(AppDbContext ctx)
        => new(ctx, Substitute.For<IAuthApiClient>());

    private async Task<int> SeedSchoolAsync(string name = "A Okulu")
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = name };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    [Fact]
    public async Task Teacher_Save_creates_then_rejects_a_school_change_on_the_same_row()
    {
        // issue #234: mevcut öğretmen kaydının okulu register ucuyla değiştirilemez (409), satır aynı kalır.
        var schoolAId = await SeedSchoolAsync("A Okulu");
        var schoolBId = await SeedSchoolAsync("B Okulu");

        await using (var ctx = _db.NewContext())
        {
            var created = await NewTeacherService(ctx).Save(userId: 10, new RegisterTeacherDto { SchoolId = schoolAId });
            created.Success.ShouldBeTrue();
            created.Message.ShouldContain("kaydedildi");
        }

        await using (var ctx = _db.NewContext())
        {
            var updated = await NewTeacherService(ctx).Save(userId: 10, new RegisterTeacherDto { SchoolId = schoolBId });
            updated.Success.ShouldBeFalse();
            updated.Conflict.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var rows = check.Teachers.Where(t => t.UserId == 10).ToList();
        rows.Count.ShouldBe(1);
        rows[0].SchoolId.ShouldBeNull();
        rows[0].RequestedSchoolId.ShouldBe(schoolAId);
    }

    [Fact]
    public async Task Teacher_Save_fails_when_the_given_school_id_does_not_exist()
    {
        await using var ctx = _db.NewContext();
        var response = await NewTeacherService(ctx).Save(userId: 11, new RegisterTeacherDto { SchoolId = 99999 });

        response.Success.ShouldBeFalse();
        response.Message.ShouldBe("Seçilen okul bulunamadı.");

        await using var check = _db.NewContext();
        check.Teachers.Any(t => t.UserId == 11).ShouldBeFalse();
    }

    [Fact]
    public async Task Teacher_Save_succeeds_and_stores_null_when_school_id_is_not_provided()
    {
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(userId: 12, new RegisterTeacherDto { SchoolId = null });
            response.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        check.Teachers.Single(t => t.UserId == 12).SchoolId.ShouldBeNull();
    }

    [Fact]
    public async Task Teacher_Save_sets_ApprovalStatus_Pending_when_creating_an_independent_tutor()
    {
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 40,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            response.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.UserId == 40);
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    [Fact]
    public async Task Teacher_Save_sets_ApprovalStatus_Pending_and_holds_the_school_as_a_request_when_creating_a_school_bound_teacher()
    {
        var schoolId = await SeedSchoolAsync("C Okulu");

        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 41,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            response.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.UserId == 41);
        teacher.IsIndependentTutor.ShouldBeFalse();
        // issue #234: okul bağı onaya kadar kurulmaz.
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        teacher.SchoolId.ShouldBeNull();
        teacher.RequestedSchoolId.ShouldBe(schoolId);
    }

    [Fact]
    public async Task Teacher_Save_updates_ApprovalStatus_when_an_existing_teacher_switches_to_independent_tutor()
    {
        var schoolId = await SeedSchoolAsync("D Okulu");

        await using (var ctx = _db.NewContext())
        {
            var created = await NewTeacherService(ctx).Save(
                userId: 42,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            created.Success.ShouldBeTrue();
        }

        await using (var check1 = _db.NewContext())
        {
            // issue #234: okul talebi admin onayı bekler.
            check1.Teachers.Single(t => t.UserId == 42).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        }

        await using (var ctx = _db.NewContext())
        {
            var updated = await NewTeacherService(ctx).Save(
                userId: 42,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            updated.Success.ShouldBeTrue();
        }

        await using var check2 = _db.NewContext();
        var teacher = check2.Teachers.Single(t => t.UserId == 42);
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        teacher.SchoolId.ShouldBeNull();
        teacher.RequestedSchoolId.ShouldBeNull(); // bağımsız başvuruya geçişte okul talebi geri çekilir
    }

    [Fact]
    public async Task Teacher_Save_preserves_admin_Approved_status_when_IsIndependentTutor_is_unchanged()
    {
        // Bağımsız öğretmen Pending oluşturulur, admin onaylar (doğrudan DB), sonra öğretmen
        // IsIndependentTutor=true kalarak tekrar register çağırır → Approved korunmalı (issue #92 review).
        await using (var ctx = _db.NewContext())
        {
            var created = await NewTeacherService(ctx).Save(
                userId: 44,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            created.Success.ShouldBeTrue();
        }

        await using (var admin = _db.NewContext())
        {
            var t = admin.Teachers.Single(t => t.UserId == 44);
            t.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
            t.ApprovalStatus = TeacherApprovalStatus.Approved;
            await admin.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            var updated = await NewTeacherService(ctx).Save(
                userId: 44,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            updated.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.UserId == 44);
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
    }

    [Fact]
    public async Task Teacher_Save_does_not_let_a_pending_tutor_self_approve_by_resending_the_same_flag()
    {
        // Pending bağımsız öğretmen, aynı IsIndependentTutor=true ile tekrar register → hâlâ Pending.
        await using (var ctx = _db.NewContext())
        {
            (await NewTeacherService(ctx).Save(
                userId: 45,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true })).Success.ShouldBeTrue();
        }

        await using (var ctx = _db.NewContext())
        {
            (await NewTeacherService(ctx).Save(
                userId: 45,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true })).Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        check.Teachers.Single(t => t.UserId == 45).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    [Fact]
    public async Task Teacher_default_ApprovalStatus_is_Approved_when_inserted_directly_without_the_service()
    {
        await using (var ctx = _db.NewContext())
        {
            // ApprovalStatus deliberately not set — exercises the EF default value/sentinel
            // configured in AppDbContext (regression guard for issue #92).
            ctx.Teachers.Add(new Teacher { UserId = 43, SchoolId = null });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.UserId == 43);
        teacher.SchoolId.ShouldBeNull();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
        teacher.IsIndependentTutor.ShouldBeFalse();
    }

    [Fact]
    public async Task Teacher_GetTeacher_returns_null_when_absent()
    {
        await using var ctx = _db.NewContext();
        (await NewTeacherService(ctx).GetTeacher(999)).ShouldBeNull();
    }

    // ---------------- TeacherService: IndependentTeacherRegisteredEvent outbox ----------------

    private static readonly string IndependentTeacherEventType =
        OutboxEventRegistry.NameFor<IndependentTeacherRegisteredEvent>();

    private static List<OutboxMessage> ReadIndependentTeacherEvents(AppDbContext ctx)
        => ctx.OutboxMessages.Where(m => m.Type == IndependentTeacherEventType).ToList();

    [Fact]
    public async Task Teacher_Save_writes_outbox_event_when_new_registration_is_independent()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 60,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            response.Success.ShouldBeTrue();
            teacherId = response.ObjectId;
        }

        await using var check = _db.NewContext();
        var events = ReadIndependentTeacherEvents(check);
        events.Count.ShouldBe(1);

        var payload = JsonSerializer.Deserialize<IndependentTeacherRegisteredEvent>(events[0].Content)!;
        payload.TeacherId.ShouldBe(teacherId);
        payload.IsNewRegistration.ShouldBeTrue();
    }

    [Fact]
    public async Task Teacher_Save_does_not_write_outbox_event_when_new_registration_is_school_bound()
    {
        var schoolId = await SeedSchoolAsync("E Okulu");

        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 61,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            response.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        ReadIndependentTeacherEvents(check).ShouldBeEmpty();
    }

    [Fact]
    public async Task Teacher_Save_writes_outbox_event_when_school_bound_teacher_switches_to_independent()
    {
        var schoolId = await SeedSchoolAsync("F Okulu");
        int teacherId;

        await using (var ctx = _db.NewContext())
        {
            var created = await NewTeacherService(ctx).Save(
                userId: 62,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            created.Success.ShouldBeTrue();
            teacherId = created.ObjectId;
        }

        await using (var check1 = _db.NewContext())
        {
            // İlk (okula bağlı) kayıt event üretmemeli.
            ReadIndependentTeacherEvents(check1).ShouldBeEmpty();
        }

        await using (var ctx = _db.NewContext())
        {
            var updated = await NewTeacherService(ctx).Save(
                userId: 62,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            updated.Success.ShouldBeTrue();
        }

        await using var check2 = _db.NewContext();
        var events = ReadIndependentTeacherEvents(check2);
        events.Count.ShouldBe(1);

        var payload = JsonSerializer.Deserialize<IndependentTeacherRegisteredEvent>(events[0].Content)!;
        payload.TeacherId.ShouldBe(teacherId);
        payload.IsNewRegistration.ShouldBeFalse();
    }

    [Fact]
    public async Task Teacher_Save_rejects_independent_teacher_switching_to_school_bound_without_writing_an_outbox_event()
    {
        var schoolId = await SeedSchoolAsync("G Okulu");

        await using (var ctx = _db.NewContext())
        {
            // İlk kayıt bağımsız olarak yapılır: bu adım kendi event'ini üretir (yeni-kayıt kuralı).
            var created = await NewTeacherService(ctx).Save(
                userId: 63,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            created.Success.ShouldBeTrue();
        }

        int eventsAfterCreation;
        await using (var check1 = _db.NewContext())
        {
            eventsAfterCreation = ReadIndependentTeacherEvents(check1).Count;
        }

        await using (var ctx = _db.NewContext())
        {
            var updated = await NewTeacherService(ctx).Save(
                userId: 63,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            // issue #234: bağımsız → okula bağlı geçiş register ucuyla yapılamaz (okul bağı + kendini onaylama yolu).
            updated.Success.ShouldBeFalse();
            updated.Conflict.ShouldBeTrue();
        }

        await using var check2 = _db.NewContext();
        // Reddedilen geçiş yeni event eklememeli; sayı creation sonrasıyla aynı kalmalı.
        ReadIndependentTeacherEvents(check2).Count.ShouldBe(eventsAfterCreation);
        var teacher = check2.Teachers.Single(t => t.UserId == 63);
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.SchoolId.ShouldBeNull();
    }

    [Fact]
    public async Task Teacher_Save_does_not_write_outbox_event_when_resubmitting_the_same_independent_flag()
    {
        await using (var ctx = _db.NewContext())
        {
            var created = await NewTeacherService(ctx).Save(
                userId: 64,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            created.Success.ShouldBeTrue();
        }

        await using (var ctx = _db.NewContext())
        {
            var resubmitted = await NewTeacherService(ctx).Save(
                userId: 64,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            resubmitted.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        // İlk kayıttan bir event zaten yazılmıştı; tekrar submit yeni event eklememeli.
        ReadIndependentTeacherEvents(check).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Teacher_UpdateTheme_fails_for_an_unknown_teacher_and_succeeds_otherwise()
    {
        await using (var ctx = _db.NewContext())
            (await NewTeacherService(ctx).UpdateTeacherTheme(5, "enhanced", null)).Success.ShouldBeFalse();

        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 5, SchoolName = "S" });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            var r = await NewTeacherService(ctx).UpdateTeacherTheme(5, "full", "{\"x\":1}");
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
        => new(ctx, Substitute.For<IAuthApiClient>(), new ExamApp.Api.Services.Tenancy.SchoolAccessPolicy(ctx));

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
        var schoolId = await SeedSchoolAsync("Okul");

        await using (var ctx = _db.NewContext())
        {
            var r = await NewStudentService(ctx).Save(userId: 20,
                new RegisterStudentDto { StudentNumber = "123", SchoolId = schoolId, GradeId = gradeId });
            r.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var student = check.Students.Single(s => s.UserId == 20);
        student.SchoolId.ShouldBe(schoolId);
    }

    [Fact]
    public async Task Student_Save_fails_when_the_given_school_id_does_not_exist()
    {
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "6" };
            ctx.Grades.Add(g);
            await ctx.SaveChangesAsync();
            gradeId = g.Id;
        }

        await using var ctx2 = _db.NewContext();
        var response = await NewStudentService(ctx2).Save(userId: 21,
            new RegisterStudentDto { StudentNumber = "n21", SchoolId = 99999, GradeId = gradeId });

        response.Success.ShouldBeFalse();
        response.Message.ShouldBe("Seçilen okul bulunamadı.");

        await using var check = _db.NewContext();
        check.Students.Any(s => s.UserId == 21).ShouldBeFalse();
    }

    [Fact]
    public async Task Student_Save_succeeds_and_stores_null_when_school_id_is_not_provided()
    {
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "7" };
            ctx.Grades.Add(g);
            await ctx.SaveChangesAsync();
            gradeId = g.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var response = await NewStudentService(ctx).Save(userId: 22,
                new RegisterStudentDto { StudentNumber = "n22", SchoolId = null, GradeId = gradeId });
            response.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        check.Students.Single(s => s.UserId == 22).SchoolId.ShouldBeNull();
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

    // --------- Acceptance Criteria: issue #234 teacher registration school boundary ---------

    /// <summary>
    /// Acceptance Criterion 1: User cannot bind themselves to a school without approval.
    /// When registering with a SchoolId, the request is stored as RequestedSchoolId (not SchoolId),
    /// ApprovalStatus is Pending, and SchoolId remains null until admin approval.
    /// </summary>
    [Fact]
    public async Task Teacher_Register_WithSchoolId_StoresAsRequestedSchoolId_ApprovalPending()
    {
        var schoolId = await SeedSchoolAsync("Test School");

        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 100,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            response.Success.ShouldBeTrue();
            response.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
            response.SchoolId.ShouldBeNull(); // NOT immediately set
            response.RequestedSchoolId.ShouldBe(schoolId); // stored as request
        }

        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.UserId == 100);
        teacher.SchoolId.ShouldBeNull();
        teacher.RequestedSchoolId.ShouldBe(schoolId);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    /// <summary>
    /// Acceptance Criterion 1: Independent registration ignores SchoolId sent in request.
    /// Even if SchoolId is provided, it's discarded for independent tutors.
    /// </summary>
    [Fact]
    public async Task Teacher_Register_IndependentTutor_IgnoresSchoolIdInRequest()
    {
        var schoolId = await SeedSchoolAsync("Ignored School");

        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 101,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = true });
            response.Success.ShouldBeTrue();
            response.SchoolId.ShouldBeNull();
            response.RequestedSchoolId.ShouldBeNull(); // school request ignored
            response.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        }

        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.UserId == 101);
        teacher.SchoolId.ShouldBeNull();
        teacher.RequestedSchoolId.ShouldBeNull();
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    /// <summary>
    /// Acceptance Criterion 2: Existing teacher cannot change their requested school.
    /// Attempting to request a different school returns 409 Conflict.
    /// </summary>
    [Fact]
    public async Task Teacher_Register_ExistingTeacher_CannotChangeRequestedSchool_Returns409()
    {
        var schoolA = await SeedSchoolAsync("School A");
        var schoolB = await SeedSchoolAsync("School B");

        // Create initial request for School A
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 102,
                new RegisterTeacherDto { SchoolId = schoolA, IsIndependentTutor = false });
            response.Success.ShouldBeTrue();
            response.RequestedSchoolId.ShouldBe(schoolA);
        }

        // Attempt to change request to School B
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 102,
                new RegisterTeacherDto { SchoolId = schoolB, IsIndependentTutor = false });
            response.Success.ShouldBeFalse();
            response.Conflict.ShouldBeTrue();
        }

        // Verify no change occurred
        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.UserId == 102);
        teacher.RequestedSchoolId.ShouldBe(schoolA);
        teacher.SchoolId.ShouldBeNull();
    }

    /// <summary>
    /// Acceptance Criterion 2: Independent teacher cannot be converted to school-bound.
    /// Attempting to change IsIndependentTutor=true→false returns 409 Conflict.
    /// </summary>
    [Fact]
    public async Task Teacher_Register_IndependentToSchoolBound_Returns409()
    {
        var schoolId = await SeedSchoolAsync("School for Conversion");

        // Create as independent tutor
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 103,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            response.Success.ShouldBeTrue();
        }

        // Attempt to convert to school-bound
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 103,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            response.Success.ShouldBeFalse();
            response.Conflict.ShouldBeTrue();
        }

        // Verify still independent
        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.UserId == 103);
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.SchoolId.ShouldBeNull();
    }

    /// <summary>
    /// Acceptance Criterion 3: Admin approve flow links school (RequestedSchoolId → SchoolId).
    /// After approval, SchoolId is set and RequestedSchoolId is cleared.
    /// </summary>
    [Fact]
    public async Task TeacherApproval_Approve_LinksSchoolAndClearsRequest()
    {
        var schoolId = await SeedSchoolAsync("Approval Test School");
        int teacherId;

        // Create pending school request
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 104,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            response.Success.ShouldBeTrue();
            response.RequestedSchoolId.ShouldBe(schoolId);
            response.SchoolId.ShouldBeNull();
            teacherId = response.ObjectId;
        }

        // Admin approves
        await using (var ctx = _db.NewContext())
        {
            var approvalService = NewTeacherApprovalService(ctx);
            var result = await approvalService.ApproveAsync(teacherId, adminUserId: 999, actorAdminKeycloakId: "kc-admin-test");
            result.Success.ShouldBeTrue();
        }

        // Verify school link is established
        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.Id == teacherId);
        teacher.SchoolId.ShouldBe(schoolId);
        teacher.RequestedSchoolId.ShouldBeNull();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
    }

    /// <summary>
    /// Acceptance Criterion 3: Admin reject flow does NOT link school.
    /// After rejection, SchoolId stays null and RequestedSchoolId is retained (for audit).
    /// </summary>
    [Fact]
    public async Task TeacherApproval_Reject_DoesNotLinkSchool_PreservesRequest()
    {
        var schoolId = await SeedSchoolAsync("Rejected School");
        int teacherId;

        // Create pending school request
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 105,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            teacherId = response.ObjectId;
        }

        // Admin rejects
        await using (var ctx = _db.NewContext())
        {
            var approvalService = NewTeacherApprovalService(ctx);
            var result = await approvalService.RejectAsync(teacherId, "Insufficient qualifications", adminUserId: 999, actorAdminKeycloakId: "kc-admin-test");
            result.Success.ShouldBeTrue();
        }

        // Verify school link is NOT established
        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.Id == teacherId);
        teacher.SchoolId.ShouldBeNull();
        teacher.RequestedSchoolId.ShouldBe(schoolId); // preserved for audit
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        teacher.RejectionReason.ShouldBe("Insufficient qualifications");
    }

    /// <summary>
    /// Acceptance Criterion 3: Approval fails if requested school no longer exists.
    /// </summary>
    [Fact]
    public async Task TeacherApproval_Approve_FailsIfSchoolDeleted()
    {
        var schoolId = await SeedSchoolAsync("Deleted School");
        int teacherId;

        // Create pending request
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 106,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            teacherId = response.ObjectId;
        }

        // Delete the school
        await using (var ctx = _db.NewContext())
        {
            var school = ctx.Schools.Single(s => s.Id == schoolId);
            ctx.Schools.Remove(school);
            await ctx.SaveChangesAsync();
        }

        // Attempt approval
        await using (var ctx = _db.NewContext())
        {
            var approvalService = NewTeacherApprovalService(ctx);
            var result = await approvalService.ApproveAsync(teacherId, adminUserId: 999, actorAdminKeycloakId: "kc-admin-test");
            result.Success.ShouldBeFalse();
            result.Message.ShouldNotBeNull();
        }

        // Verify teacher still pending
        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.Id == teacherId);
        teacher.SchoolId.ShouldBeNull();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    /// <summary>
    /// Acceptance Criterion 3: Approved independent tutor cannot be approved again (idempotency guard).
    /// </summary>
    [Fact]
    public async Task TeacherApproval_Approve_IndependentAlreadyApproved_ReturnConflict()
    {
        int teacherId;

        // Create independent and approve
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 107,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = true });
            teacherId = response.ObjectId;
        }

        await using (var ctx = _db.NewContext())
        {
            var approvalService = NewTeacherApprovalService(ctx);
            var result = await approvalService.ApproveAsync(teacherId, adminUserId: 999, actorAdminKeycloakId: "kc-admin-test");
            result.Success.ShouldBeTrue();
        }

        // Attempt second approval - should fail with Conflict
        await using (var ctx = _db.NewContext())
        {
            var approvalService = NewTeacherApprovalService(ctx);
            var result = await approvalService.ApproveAsync(teacherId, adminUserId: 999, actorAdminKeycloakId: "kc-admin-test");
            result.Success.ShouldBeFalse();
            result.Conflict.ShouldBeTrue();
        }
    }

    // Helper method for TeacherApprovalService
    private TeacherApprovalService NewTeacherApprovalService(AppDbContext ctx)
        => new(ctx, Substitute.For<IAuthApiClient>());

    // --------- New Rules: Mutual Exclusion, Conditional Approve/Reject ---------

    /// <summary>
    /// Rule: Student record blocks teacher registration (mutual exclusion).
    /// </summary>
    [Fact]
    public async Task Teacher_Register_FailsIfUserHasStudentRecord_Returns409Conflict()
    {
        int gradeId;
        var schoolId = await SeedSchoolAsync("School for Student");

        // Create grade and student record
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "5" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            gradeId = grade.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var studentService = NewStudentService(ctx);
            await studentService.Save(userId: 200,
                new RegisterStudentDto { StudentNumber = "n200", SchoolId = schoolId, GradeId = gradeId });
        }

        // Attempt teacher registration on same user
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 200,
                new RegisterTeacherDto { SchoolId = null, IsIndependentTutor = false });
            response.Success.ShouldBeFalse();
            response.Conflict.ShouldBeTrue();
        }

        // Verify no teacher record created
        await using var check = _db.NewContext();
        check.Teachers.Any(t => t.UserId == 200).ShouldBeFalse();
    }

    /// <summary>
    /// Rule: Teacher record blocks student registration (mutual exclusion).
    /// </summary>
    [Fact]
    public async Task Student_Register_FailsIfUserHasTeacherRecord_Returns409Conflict()
    {
        var schoolId = await SeedSchoolAsync("School for Teacher");
        int gradeId;

        // Create teacher record
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 201,
                new RegisterTeacherDto { SchoolId = schoolId, IsIndependentTutor = false });
            response.Success.ShouldBeTrue();
        }

        // Create grade
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "6" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            gradeId = grade.Id;
        }

        // Attempt student registration on same user
        await using (var ctx = _db.NewContext())
        {
            var studentService = NewStudentService(ctx);
            var response = await studentService.Save(userId: 201,
                new RegisterStudentDto { StudentNumber = "n201", SchoolId = schoolId, GradeId = gradeId });
            response.Success.ShouldBeFalse();
            response.Conflict.ShouldBeTrue();
        }

        // Verify no student record created
        await using var check = _db.NewContext();
        check.Students.Any(s => s.UserId == 201).ShouldBeFalse();
    }

    /// <summary>
    /// Rule: Rejected school request + new school request → Pending + new RequestedSchoolId + RejectionReason = null
    /// </summary>
    [Fact]
    public async Task Teacher_Register_AfterRejection_CanRequestNewSchool()
    {
        var schoolA = await SeedSchoolAsync("School A for Rejection");
        var schoolB = await SeedSchoolAsync("School B for Rejection");
        int teacherId;

        // Create initial request for School A
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 202,
                new RegisterTeacherDto { SchoolId = schoolA, IsIndependentTutor = false });
            teacherId = response.ObjectId;
        }

        // Admin rejects
        await using (var ctx = _db.NewContext())
        {
            var approvalService = NewTeacherApprovalService(ctx);
            await approvalService.RejectAsync(teacherId, "Insufficient credentials", adminUserId: 999, actorAdminKeycloakId: "kc-admin-test");
        }

        // Verify state after rejection
        await using (var check1 = _db.NewContext())
        {
            var teacher = check1.Teachers.Single(t => t.Id == teacherId);
            teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
            teacher.RequestedSchoolId.ShouldBe(schoolA);
            teacher.RejectionReason.ShouldBe("Insufficient credentials");
        }

        // Now request new school (School B)
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 202,
                new RegisterTeacherDto { SchoolId = schoolB, IsIndependentTutor = false });
            response.Success.ShouldBeTrue();
            response.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
            response.RequestedSchoolId.ShouldBe(schoolB);
        }

        // Verify state after new request
        await using var check2 = _db.NewContext();
        var final = check2.Teachers.Single(t => t.Id == teacherId);
        final.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        final.RequestedSchoolId.ShouldBe(schoolB);
        final.RejectionReason.ShouldBeNull();
        final.SchoolId.ShouldBeNull();
    }

    /// <summary>
    /// Rule: Approved Okulsuz (non-independent, no pending request) teacher can request school
    /// </summary>
    [Fact]
    public async Task Teacher_Register_ApprovedOkulsuzCanRequestSchool()
    {
        var school = await SeedSchoolAsync("School for Okulsuz Request");
        int teacherId;

        // Create teacher with no school request (transition-era approved okulsuz)
        await using (var ctx = _db.NewContext())
        {
            var newTeacher = new Teacher
            {
                UserId = 203,
                SchoolId = null,
                RequestedSchoolId = null,
                IsIndependentTutor = false,
                ApprovalStatus = TeacherApprovalStatus.Approved
            };
            ctx.Teachers.Add(newTeacher);
            await ctx.SaveChangesAsync();
            teacherId = newTeacher.Id;
        }

        // Request school
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 203,
                new RegisterTeacherDto { SchoolId = school, IsIndependentTutor = false });
            response.Success.ShouldBeTrue();
            response.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
            response.RequestedSchoolId.ShouldBe(school);
        }

        // Verify state
        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.Id == teacherId);
        teacher.RequestedSchoolId.ShouldBe(school);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        teacher.SchoolId.ShouldBeNull();
    }

    /// <summary>
    /// Rule: Approved SchoolId + same school request → idempotent success
    /// </summary>
    [Fact]
    public async Task Teacher_Register_ApprovedSchoolIdSameSchoolIdempotent()
    {
        var school = await SeedSchoolAsync("School for Idempotent");
        int teacherId;

        // Create approved school-linked teacher
        await using (var ctx = _db.NewContext())
        {
            var newTeacher = new Teacher
            {
                UserId = 204,
                SchoolId = school,
                RequestedSchoolId = null,
                IsIndependentTutor = false,
                ApprovalStatus = TeacherApprovalStatus.Approved
            };
            ctx.Teachers.Add(newTeacher);
            await ctx.SaveChangesAsync();
            teacherId = newTeacher.Id;
        }

        // Re-register with same school
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 204,
                new RegisterTeacherDto { SchoolId = school, IsIndependentTutor = false });
            response.Success.ShouldBeTrue();
            response.SchoolId.ShouldBe(school);
            response.RequestedSchoolId.ShouldBeNull();
        }

        // Verify unchanged
        await using var check = _db.NewContext();
        var teacher = check.Teachers.Single(t => t.Id == teacherId);
        teacher.SchoolId.ShouldBe(school);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
    }

    /// <summary>
    /// Rule: Conditional approve via ExecuteUpdate — if record changed, return alreadyDecided
    /// </summary>
    [Fact]
    public async Task TeacherApproval_Approve_AlreadyDecidedIfRecordChanged()
    {
        var school = await SeedSchoolAsync("School for Conditional Approve");
        int teacherId;

        // Create pending school request
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 205,
                new RegisterTeacherDto { SchoolId = school, IsIndependentTutor = false });
            teacherId = response.ObjectId;
        }

        // Simulate race: approve reading sees Pending, but between read and ExecuteUpdate
        // another admin (or the teacher) already approved it.
        await using (var ctx = _db.NewContext())
        {
            var teacher = ctx.Teachers.Single(t => t.Id == teacherId);
            teacher.ApprovalStatus = TeacherApprovalStatus.Approved;
            await ctx.SaveChangesAsync();
        }

        // Now attempt approval
        await using (var ctx = _db.NewContext())
        {
            var approvalService = NewTeacherApprovalService(ctx);
            var result = await approvalService.ApproveAsync(teacherId, adminUserId: 999, actorAdminKeycloakId: "kc-admin-test");
            result.Success.ShouldBeFalse();
            result.Conflict.ShouldBeTrue();
        }
    }

    /// <summary>
    /// Rule: Conditional reject via ExecuteUpdate — if another admin already rejected, return alreadyDecided
    /// </summary>
    [Fact]
    public async Task TeacherApproval_Reject_AlreadyDecidedIfAlreadyRejected()
    {
        var school = await SeedSchoolAsync("School for Double Reject");
        int teacherId;

        // Create pending request
        await using (var ctx = _db.NewContext())
        {
            var response = await NewTeacherService(ctx).Save(
                userId: 206,
                new RegisterTeacherDto { SchoolId = school, IsIndependentTutor = false });
            teacherId = response.ObjectId;
        }

        // First admin rejects
        await using (var ctx = _db.NewContext())
        {
            var approvalService = NewTeacherApprovalService(ctx);
            var result = await approvalService.RejectAsync(teacherId, "Rejected", adminUserId: 999, actorAdminKeycloakId: "kc-admin-test");
            result.Success.ShouldBeTrue();
        }

        // Second admin attempts to reject (should fail - already decided)
        await using (var ctx = _db.NewContext())
        {
            var approvalService = NewTeacherApprovalService(ctx);
            var result = await approvalService.RejectAsync(teacherId, "Try again", adminUserId: 998, actorAdminKeycloakId: "kc-admin-test");
            result.Success.ShouldBeFalse();
            result.Conflict.ShouldBeTrue();
        }
    }
}
