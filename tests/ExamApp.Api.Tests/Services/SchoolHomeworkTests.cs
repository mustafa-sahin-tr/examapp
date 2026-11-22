using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #1282 (#1071 S7): düz ödev — kapsam (sınıf / şube / seçili öğrenci, başka okul 404), durum akışı (öğrenci "yaptım",
/// öğretmen Yapıldı / Kısmen / Yapılmadı, kontrol sonrası kilit), gecikme ve okul değişimi matrisinin "Sınıf atamaları" satırı.
/// </summary>
public class SchoolHomeworkTests : IDisposable
{
    private const int TeacherUser = 901;
    private const int OtherTeacherUser = 902;
    private const int IndependentUser = 903;
    private const int SchoolBTeacherUser = 904;

    // 2026-11-22 12:00 İstanbul.
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero));
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private static readonly DateOnly Today = new(2026, 11, 22);

    public SchoolHomeworkTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>()
                .Select(id => new UserLookupResultDto { Id = id, FullName = $"Ad{id} Soyad", KeycloakId = $"kc-{id}" })
                .ToList());
    }

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int SchoolA, int SchoolB, int Grade7, int Grade8, int Subject, int S7a, int S7b, int S8, int Pending,
        int OtherSchool, int Section7A, int SectionB);

    // Kullanıcı kimlikleri: öğrenci userId = 100 + sıra (S7a 101, S7b 102, S8 103, Pending 104, OtherSchool 105).
    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A Okulu" };
        var b = new School { Name = "B Okulu" };
        var g7 = new Grade { Name = "7. Sınıf" };
        var g8 = new Grade { Name = "8. Sınıf" };
        var subject = new Subject { Name = "Türkçe" };
        ctx.AddRange(a, b, g7, g8, subject);
        await ctx.SaveChangesAsync();
        var now = _time.GetUtcNow().UtcDateTime;
        Teacher T(int userId, int? school, bool independent = false) => new()
        {
            UserId = userId, SchoolId = school, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = now.AddDays(-10),
            IsIndependentTutor = independent
        };
        ctx.Teachers.AddRange(T(TeacherUser, a.Id), T(OtherTeacherUser, a.Id), T(IndependentUser, null, independent: true),
            T(SchoolBTeacherUser, b.Id));
        Student S(int userId, int school, int grade, bool verified = true) => new()
        {
            UserId = userId, StudentNumber = $"2024{userId}", SchoolId = school, GradeId = grade, SchoolVerifiedAt = verified ? now : null
        };
        var s7a = S(101, a.Id, g7.Id);
        var s7b = S(102, a.Id, g7.Id);
        var s8 = S(103, a.Id, g8.Id);
        var pending = S(104, a.Id, g7.Id, verified: false);
        var other = S(105, b.Id, g7.Id);
        ctx.Students.AddRange(s7a, s7b, s8, pending, other);
        var section = new SchoolSection { SchoolId = a.Id, GradeId = g7.Id, Name = "A", NormalizedName = "A" };
        var sectionB = new SchoolSection { SchoolId = b.Id, GradeId = g7.Id, Name = "A", NormalizedName = "A" };
        ctx.SchoolSections.AddRange(section, sectionB);
        await ctx.SaveChangesAsync();
        ctx.SchoolSectionMemberships.Add(new SchoolSectionMembership { SectionId = section.Id, StudentId = s7a.Id, StartedAt = now });
        await ctx.SaveChangesAsync();
        return new Seed(a.Id, b.Id, g7.Id, g8.Id, subject.Id, s7a.Id, s7b.Id, s8.Id, pending.Id, other.Id, section.Id, sectionB.Id);
    }

    private SchoolHomeworkService Service(AppDbContext ctx) =>
        new(ctx, new SchoolSectionService(ctx, _authApi), new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, _time), _authApi,
            time: _time);

    private static SchoolHomeworkCreateRequestDto Req(int subject, string target, int? gradeId = null, int? sectionId = null,
        List<int>? studentIds = null, DateOnly? due = null, string? pages = "8-10", string? note = null) =>
        new()
        {
            Target = target, GradeId = gradeId, SectionId = sectionId, StudentIds = studentIds, SubjectId = subject,
            BookName = "Okuma Kitabı", Pages = pages, Note = note, DueDate = due ?? Today.AddDays(2)
        };

    private async Task<T> Run<T>(Func<SchoolHomeworkService, Task<T>> action)
    {
        await using var ctx = _db.NewContext();
        return await action(Service(ctx));
    }

    private async Task<int> CreateAsync(int teacher, SchoolHomeworkCreateRequestDto req)
    {
        var result = await Run(s => s.CreateAsync(teacher, req));
        Assert.Equal(SchoolHomeworkStatus.Success, result.Status);
        return result.Value!.Id;
    }

    private Task<IReadOnlyList<StudentHomeworkDto>?> StudentListAsync(int studentUserId) =>
        Run(s => s.ListForStudentAsync(studentUserId));

    private async Task<List<int>> VisibleIdsAsync(int studentUserId) => (await StudentListAsync(studentUserId))!.Select(h => h.Id).ToList();

    [Fact]
    public async Task Section_target_reaches_only_active_members()
    {
        var seed = await SeedAsync();
        var id = await CreateAsync(TeacherUser, Req(seed.Subject, "Section", sectionId: seed.Section7A));

        Assert.Equal([id], await VisibleIdsAsync(101));
        Assert.Empty(await VisibleIdsAsync(102));
        var detail = await Run(s => s.GetAsync(TeacherUser, id));
        Assert.Equal("7. Sınıf / A", detail.Value!.Homework.TargetLabel);
        Assert.Equal([seed.S7a], detail.Value.Students.Select(x => x.StudentId));
        Assert.Equal("Ad101 Soyad", detail.Value.Students[0].FullName);
    }

    [Fact]
    public async Task Grade_target_reaches_verified_students_of_the_school_and_grade()
    {
        var seed = await SeedAsync();
        var id = await CreateAsync(TeacherUser, Req(seed.Subject, "Grade", gradeId: seed.Grade7));

        Assert.Equal([id], await VisibleIdsAsync(101));
        Assert.Equal([id], await VisibleIdsAsync(102));
        Assert.Empty(await VisibleIdsAsync(103)); // 8. sınıf
        Assert.Empty(await VisibleIdsAsync(104)); // doğrulanmamış
        Assert.Empty(await VisibleIdsAsync(105)); // başka okul, aynı sınıf
        var list = await Run(s => s.ListOwnAsync(TeacherUser, 1, 20));
        Assert.Equal(2, Assert.Single(list.Items).StudentCount);
    }

    [Fact]
    public async Task Students_target_requires_verified_students_of_the_school()
    {
        var seed = await SeedAsync();
        var bad = await Run(s => s.CreateAsync(TeacherUser, Req(seed.Subject, "Students", studentIds: [seed.S7b, seed.OtherSchool])));
        Assert.Equal(SchoolHomeworkStatus.NotFound, bad.Status);
        var pending = await Run(s => s.CreateAsync(TeacherUser, Req(seed.Subject, "Students", studentIds: [seed.Pending])));
        Assert.Equal(SchoolHomeworkStatus.NotFound, pending.Status);

        var id = await CreateAsync(TeacherUser, Req(seed.Subject, "Students", studentIds: [seed.S7b, seed.S8]));
        Assert.Equal([id], await VisibleIdsAsync(102));
        Assert.Equal([id], await VisibleIdsAsync(103));
        Assert.Empty(await VisibleIdsAsync(101));
        await using var ctx = _db.NewContext();
        Assert.Equal(1, await ctx.SchoolHomeworks.CountAsync()); // reddedilenler yazılmadı
    }

    [Fact]
    public async Task Create_rejects_non_school_teacher_other_school_section_and_invalid_input()
    {
        var seed = await SeedAsync();
        Assert.Equal(SchoolHomeworkStatus.Forbidden,
            (await Run(s => s.CreateAsync(IndependentUser, Req(seed.Subject, "Grade", gradeId: seed.Grade7)))).Status);
        Assert.Equal(SchoolHomeworkStatus.NotFound,
            (await Run(s => s.CreateAsync(TeacherUser, Req(seed.Subject, "Section", sectionId: seed.SectionB)))).Status);

        SchoolHomeworkCreateRequestDto[] invalid =
        [
            Req(seed.Subject, "Grade", gradeId: seed.Grade7, due: Today.AddDays(-1)),
            Req(seed.Subject, "Grade", gradeId: seed.Grade7, due: Today.AddDays(SchoolHomeworkService.MaxDueDays + 1)),
            Req(seed.Subject, "Grade", gradeId: seed.Grade7, pages: "  ", note: null),
            Req(seed.Subject, "Grade", gradeId: seed.Grade7, pages: new string('1', SchoolHomework.PagesMaxLength + 1)),
            Req(seed.Subject + 99, "Grade", gradeId: seed.Grade7),
            Req(seed.Subject, "Grade"),
            Req(seed.Subject, "School"),
            Req(seed.Subject, "Students", studentIds: [])
        ];
        foreach (var req in invalid)
            Assert.Equal(SchoolHomeworkStatus.Invalid, (await Run(s => s.CreateAsync(TeacherUser, req))).Status);

        // Bugün teslim ve yalnız not geçerli.
        await CreateAsync(TeacherUser, Req(seed.Subject, "Grade", gradeId: seed.Grade7, due: Today, pages: null, note: "Kompozisyon yaz"));
        await using var ctx = _db.NewContext();
        Assert.Equal(1, await ctx.SchoolHomeworks.CountAsync());
    }

    [Fact]
    public async Task Status_flow_student_marks_teacher_confirms_or_overrides_and_locks()
    {
        var seed = await SeedAsync();
        var id = await CreateAsync(TeacherUser, Req(seed.Subject, "Grade", gradeId: seed.Grade7));

        var marked = await Run(s => s.MarkDoneAsync(101, id, true));
        Assert.Equal(SchoolHomeworkStatus.Success, marked.Status);
        Assert.NotNull(marked.Value!.StudentDoneAt);
        var summary = Assert.Single((await Run(s => s.ListOwnAsync(TeacherUser, 1, 20))).Items);
        Assert.Equal(1, summary.StudentDoneCount);

        // Öğretmen 102'yi işaretsiz de kontrol edebilir; 101'i "Kısmen" ile geçersiz kılar.
        var partial = await Run(s => s.CheckAsync(TeacherUser, id, seed.S7a,
            new SchoolHomeworkCheckRequestDto { Status = "Partial", Note = " s. 10 eksik " }));
        Assert.Equal(SchoolHomeworkStatus.Success, partial.Status);
        Assert.Equal("Partial", partial.Value!.TeacherStatus);
        Assert.Equal("s. 10 eksik", partial.Value.TeacherNote);
        Assert.Equal(SchoolHomeworkStatus.Success,
            (await Run(s => s.CheckAsync(TeacherUser, id, seed.S7b, new SchoolHomeworkCheckRequestDto { Status = "NotDone" }))).Status);

        summary = Assert.Single((await Run(s => s.ListOwnAsync(TeacherUser, 1, 20))).Items);
        Assert.Equal((0, 0, 1, 1), (summary.StudentDoneCount, summary.DoneCount, summary.PartialCount, summary.NotDoneCount));

        var studentView = Assert.Single((await StudentListAsync(101))!);
        Assert.Equal(("Partial", "s. 10 eksik", "Ad901 S."), (studentView.TeacherStatus, studentView.TeacherNote, studentView.TeacherName));

        // Kontrol sonrası öğrenci işaretini değiştiremez; öğretmen kontrolü kaldırınca yine değiştirebilir.
        Assert.Equal(SchoolHomeworkStatus.Locked, (await Run(s => s.MarkDoneAsync(101, id, false))).Status);
        Assert.Equal(SchoolHomeworkStatus.Locked, (await Run(s => s.MarkDoneAsync(102, id, true))).Status);
        Assert.Equal(SchoolHomeworkStatus.Success,
            (await Run(s => s.CheckAsync(TeacherUser, id, seed.S7a, new SchoolHomeworkCheckRequestDto { Status = null }))).Status);
        var undone = await Run(s => s.MarkDoneAsync(101, id, false));
        Assert.Equal(SchoolHomeworkStatus.Success, undone.Status);
        Assert.Null(undone.Value!.StudentDoneAt);
        Assert.Null(undone.Value.TeacherStatus);

        await using var ctx = _db.NewContext();
        Assert.Equal(2, await ctx.SchoolHomeworkProgress.CountAsync());
    }

    [Fact]
    public async Task Check_rejects_bad_status_note_without_status_and_students_outside_target()
    {
        var seed = await SeedAsync();
        var id = await CreateAsync(TeacherUser, Req(seed.Subject, "Section", sectionId: seed.Section7A));

        Assert.Equal(SchoolHomeworkStatus.Invalid,
            (await Run(s => s.CheckAsync(TeacherUser, id, seed.S7a, new SchoolHomeworkCheckRequestDto { Status = "Great" }))).Status);
        Assert.Equal(SchoolHomeworkStatus.Invalid,
            (await Run(s => s.CheckAsync(TeacherUser, id, seed.S7a, new SchoolHomeworkCheckRequestDto { Note = "not" }))).Status);
        Assert.Equal(SchoolHomeworkStatus.NotFound,
            (await Run(s => s.CheckAsync(TeacherUser, id, seed.S7b, new SchoolHomeworkCheckRequestDto { Status = "Done" }))).Status);
        Assert.Equal(SchoolHomeworkStatus.NotFound, (await Run(s => s.MarkDoneAsync(102, id, true))).Status);
    }

    [Fact]
    public async Task Other_teachers_cannot_see_check_or_delete_and_delete_hides_from_students()
    {
        var seed = await SeedAsync();
        var id = await CreateAsync(TeacherUser, Req(seed.Subject, "Grade", gradeId: seed.Grade7));

        Assert.Equal(SchoolHomeworkStatus.NotFound, (await Run(s => s.GetAsync(OtherTeacherUser, id))).Status);
        Assert.Equal(SchoolHomeworkStatus.NotFound, (await Run(s => s.CheckAsync(OtherTeacherUser, id, seed.S7a,
            new SchoolHomeworkCheckRequestDto { Status = "Done" }))).Status);
        Assert.Equal(SchoolHomeworkStatus.NotFound, await Run(s => s.DeleteAsync(OtherTeacherUser, id)));
        Assert.Empty((await Run(s => s.ListOwnAsync(OtherTeacherUser, 1, 20))).Items);

        Assert.Equal(SchoolHomeworkStatus.Success, await Run(s => s.DeleteAsync(TeacherUser, id)));
        Assert.Empty(await VisibleIdsAsync(101));
        Assert.Equal(SchoolHomeworkStatus.NotFound, (await Run(s => s.MarkDoneAsync(101, id, true))).Status);
        Assert.Empty((await Run(s => s.ListOwnAsync(TeacherUser, 1, 20))).Items);
    }

    [Fact]
    public async Task Overdue_after_due_day_unless_marked_or_checked()
    {
        var seed = await SeedAsync();
        var id = await CreateAsync(TeacherUser, Req(seed.Subject, "Students", studentIds: [seed.S7a, seed.S7b, seed.S8], due: Today));
        await Run(s => s.MarkDoneAsync(101, id, true));
        await Run(s => s.CheckAsync(TeacherUser, id, seed.S8, new SchoolHomeworkCheckRequestDto { Status = "NotDone" }));

        // Teslim günü bitmeden gecikme yok (İstanbul 23:30).
        _time.Now = new DateTimeOffset(2026, 11, 22, 20, 30, 0, TimeSpan.Zero);
        Assert.False(Assert.Single((await StudentListAsync(102))!).IsOverdue);
        Assert.Equal(0, Assert.Single((await Run(s => s.ListOwnAsync(TeacherUser, 1, 20))).Items).OverdueCount);

        // Ertesi gün (İstanbul 00:30): yalnız işaretsiz ve kontrolsüz öğrenci gecikmiş.
        _time.Now = new DateTimeOffset(2026, 11, 22, 21, 30, 0, TimeSpan.Zero);
        Assert.True(Assert.Single((await StudentListAsync(102))!).IsOverdue);
        Assert.False(Assert.Single((await StudentListAsync(101))!).IsOverdue);
        Assert.False(Assert.Single((await StudentListAsync(103))!).IsOverdue);
        var summary = Assert.Single((await Run(s => s.ListOwnAsync(TeacherUser, 1, 20))).Items);
        Assert.True(summary.IsPastDue);
        Assert.Equal(1, summary.OverdueCount);
        var detail = await Run(s => s.GetAsync(TeacherUser, id));
        Assert.Equal([seed.S7b], detail.Value!.Students.Where(s => s.IsOverdue).Select(s => s.StudentId));

        // Gecikmiş ödev yine işaretlenebilir; 30 günden eski teslimler öğrenci listesinden düşer.
        Assert.Equal(SchoolHomeworkStatus.Success, (await Run(s => s.MarkDoneAsync(102, id, true))).Status);
        _time.Now = _time.Now.AddDays(SchoolHomeworkService.StudentHistoryDays + 1);
        Assert.Empty(await VisibleIdsAsync(102));
    }

    [Fact]
    public async Task Student_school_change_drops_grade_and_section_homework_but_keeps_direct_homework()
    {
        var seed = await SeedAsync();
        var grade = await CreateAsync(TeacherUser, Req(seed.Subject, "Grade", gradeId: seed.Grade7));
        var section = await CreateAsync(TeacherUser, Req(seed.Subject, "Section", sectionId: seed.Section7A));
        var direct = await CreateAsync(TeacherUser, Req(seed.Subject, "Students", studentIds: [seed.S7a]));
        var schoolB = await CreateAsync(SchoolBTeacherUser, Req(seed.Subject, "Grade", gradeId: seed.Grade7));
        Assert.Equal([grade, section, direct], (await VisibleIdsAsync(101)).Order());

        await using (var ctx = _db.NewContext())
        {
            var student = await ctx.Students.SingleAsync(s => s.Id == seed.S7a);
            student.SchoolId = seed.SchoolB;
            await SchoolSectionService.EndForSchoolChangeAsync(ctx, student.Id, null, _time.GetUtcNow().UtcDateTime, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }

        Assert.Equal([direct, schoolB], (await VisibleIdsAsync(101)).Order());
        // Eski okulun öğretmeni taşınan öğrenciyi hiçbir ödevinde görmez, kontrol edemez.
        foreach (var id in new[] { grade, section, direct })
            Assert.DoesNotContain(seed.S7a, (await Run(s => s.GetAsync(TeacherUser, id))).Value!.Students.Select(x => x.StudentId));
        Assert.Equal(SchoolHomeworkStatus.NotFound, (await Run(s => s.CheckAsync(TeacherUser, direct, seed.S7a,
            new SchoolHomeworkCheckRequestDto { Status = "Done" }))).Status);
    }

    [Fact]
    public async Task Teacher_school_change_keeps_homework_for_old_school_students_but_hides_their_status()
    {
        var seed = await SeedAsync();
        var id = await CreateAsync(TeacherUser, Req(seed.Subject, "Grade", gradeId: seed.Grade7));

        await using (var ctx = _db.NewContext())
        {
            var teacher = await ctx.Teachers.SingleAsync(t => t.UserId == TeacherUser);
            teacher.SchoolId = seed.SchoolB;
            await ctx.SaveChangesAsync();
        }

        Assert.Equal([id], await VisibleIdsAsync(101));
        Assert.Equal(SchoolHomeworkStatus.Success, (await Run(s => s.MarkDoneAsync(101, id, true))).Status);
        Assert.DoesNotContain(id, await VisibleIdsAsync(105)); // yeni okulun 7. sınıfına geçmez

        var own = Assert.Single((await Run(s => s.ListOwnAsync(TeacherUser, 1, 20))).Items);
        Assert.Equal((id, 0), (own.Id, own.StudentCount));
        Assert.Empty((await Run(s => s.GetAsync(TeacherUser, id))).Value!.Students);
        Assert.Equal(SchoolHomeworkStatus.Success, await Run(s => s.DeleteAsync(TeacherUser, id)));
    }
}
