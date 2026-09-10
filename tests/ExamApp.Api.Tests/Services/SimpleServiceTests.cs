using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
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
    public async Task Teacher_Save_creates_then_updates_the_same_row()
    {
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
            updated.Message.ShouldContain("güncellendi");
        }

        await using var check = _db.NewContext();
        var rows = check.Teachers.Where(t => t.UserId == 10).ToList();
        rows.Count.ShouldBe(1);
        rows[0].SchoolId.ShouldBe(schoolBId);
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
    public async Task Teacher_Save_sets_ApprovalStatus_Approved_when_creating_a_school_bound_teacher()
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
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
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
            check1.Teachers.Single(t => t.UserId == 42).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
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
    public async Task Teacher_Save_does_not_write_outbox_event_when_independent_teacher_switches_to_school_bound()
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
            updated.Success.ShouldBeTrue();
        }

        await using var check2 = _db.NewContext();
        // Bağımsız → okula bağlı geçiş yeni event eklememeli; sayı creation sonrasıyla aynı kalmalı.
        ReadIndependentTeacherEvents(check2).Count.ShouldBe(eventsAfterCreation);
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
}
