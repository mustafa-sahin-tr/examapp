using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using System.Data.Common;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #56: öğretmen dashboard aktivite kartları (GetOwnActivitySummaryAsync) ve
/// "Öğrenci Aktivitesi" + "En Aktif Öğrenciler" (GetStudentsActivitySummaryAsync).
/// </summary>
public class TeacherServiceActivitySummaryTests : IDisposable
{
    private const int TeacherId = 1;
    private const int OtherTeacherId = 2;
    private const int TutorUserId = 3;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public TeacherServiceActivitySummaryTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto>());
    }

    public void Dispose() => _db.Dispose();

    private TeacherService NewService(AppDbContext ctx) => new(ctx, _authApi, schoolAccessPolicy: new SchoolAccessPolicy(ctx));

    private static DateTime Now => DateTime.UtcNow;

    // ---- Seed helpers ----

    private int _questionSeq;

    /// <summary>
    /// <paramref name="worksheetId"/> üzerinde öğrenci için bir instance açar ve <paramref name="count"/> soru satırı ekler;
    /// ilk <paramref name="correct"/> tanesi doğru. <paramref name="answered"/>=false ise satırlar cevapsız (test başlarken
    /// açılan boş satır) kalır. UpdateTime (cevap anı) Added durumunda audit tarafından ezilmez.
    /// </summary>
    private async Task AddAnswersAsync(int worksheetId, int studentId, int count, int correct, int timeEach,
        DateTime answeredAt, bool answered = true)
    {
        await using var ctx = _db.NewContext();

        var instance = new WorksheetInstance
        {
            StudentId = studentId,
            WorksheetId = worksheetId,
            StartTime = answeredAt,
            Status = WorksheetInstanceStatus.Started,
        };
        ctx.Add(instance);
        await ctx.SaveChangesAsync();

        for (var i = 0; i < count; i++)
        {
            var question = new Question { Text = $"q{++_questionSeq}", Point = 1 };
            ctx.Questions.Add(question);
            await ctx.SaveChangesAsync();

            var answer = new Answer { QuestionId = question.Id, Text = "A", Tag = "A" };
            var wq = new WorksheetQuestion { TestId = worksheetId, QuestionId = question.Id, Order = i + 1 };
            ctx.Answers.Add(answer);
            ctx.TestQuestions.Add(wq);
            await ctx.SaveChangesAsync();

            ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
            {
                WorksheetInstanceId = instance.Id,
                WorksheetQuestionId = wq.Id,
                SelectedAnswerId = answered ? answer.Id : null,
                IsCorrect = answered && i < correct,
                TimeTaken = answered ? timeEach : 0,
                UpdateTime = answered ? answeredAt : null,
            });
        }

        await ctx.SaveChangesAsync();
    }

    private sealed record SchoolSeed(
        int SchoolId, int GradeId, int W1, int W2, int W3, int OtherWs,
        int StudentA, int StudentB, int StudentC, int StudentD, int StudentX);

    /// <summary>
    /// Okullu öğretmen (okul S1) senaryosu:
    ///  - W1: sınıf ataması (SchoolId=null legacy) → S1'deki A, B, C + (kapsam dışı, #235) S2'deki X.
    ///  - W2: yalnız C'ye direkt atama.  W3: atamasız (public) worksheet.  W4: 10 gün önce oluşturulmuş.
    ///  - OtherWs: diğer öğretmenin worksheet'i, A'ya diğer öğretmen atadı.
    ///  - D: S1'de ama öğretmenin hiçbir atamasının hedefi değil (başka sınıf).
    /// </summary>
    private async Task<SchoolSeed> SeedSchoolScenarioAsync()
    {
        await using var ctx = _db.NewContext();

        var grade = new Grade { Name = "8" };
        var otherGrade = new Grade { Name = "9" };
        var s1 = new School { Name = "S1" };
        var s2 = new School { Name = "S2" };
        ctx.AddRange(grade, otherGrade, s1, s2);
        await ctx.SaveChangesAsync();

        ctx.Teachers.AddRange(
            new Teacher { UserId = TeacherId, SchoolId = s1.Id },
            new Teacher { UserId = OtherTeacherId, SchoolId = s1.Id });

        var a = new Student { UserId = 101, StudentNumber = "a", SchoolName = "S1", SchoolId = s1.Id, GradeId = grade.Id };
        var b = new Student { UserId = 102, StudentNumber = "b", SchoolName = "S1", SchoolId = s1.Id, GradeId = grade.Id };
        var c = new Student { UserId = 103, StudentNumber = "c", SchoolName = "S1", SchoolId = s1.Id, GradeId = grade.Id };
        var d = new Student { UserId = 104, StudentNumber = "d", SchoolName = "S1", SchoolId = s1.Id, GradeId = otherGrade.Id };
        var x = new Student { UserId = 105, StudentNumber = "x", SchoolName = "S2", SchoolId = s2.Id, GradeId = grade.Id };
        ctx.Students.AddRange(a, b, c, d, x);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(TeacherId);
        var w1 = new Worksheet { Name = "W1", Description = "", GradeId = grade.Id };
        var w2 = new Worksheet { Name = "W2", Description = "", GradeId = grade.Id };
        var w3 = new Worksheet { Name = "W3", Description = "", GradeId = grade.Id };
        var w4 = new Worksheet { Name = "W4-old", Description = "", GradeId = grade.Id };
        ctx.Worksheets.AddRange(w1, w2, w3, w4);
        await ctx.SaveChangesAsync();

        var startAt = Now.AddDays(-20);
        ctx.WorksheetAssignments.AddRange(
            new WorksheetAssignment { WorksheetId = w1.Id, GradeId = grade.Id, SchoolId = null, StartAt = startAt },
            new WorksheetAssignment { WorksheetId = w2.Id, StudentId = c.Id, StartAt = startAt });
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(OtherTeacherId);
        var otherWs = new Worksheet { Name = "Other", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(otherWs);
        await ctx.SaveChangesAsync();
        ctx.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = otherWs.Id, StudentId = a.Id, StartAt = startAt });
        await ctx.SaveChangesAsync();

        // W4 pencere dışında oluşturulmuş sayılsın (audit CreateTime'ı Add'de ezer → ExecuteUpdate ile geri al).
        await ctx.Worksheets.Where(w => w.Id == w4.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.CreateTime, Now.AddDays(-10)));

        return new SchoolSeed(s1.Id, grade.Id, w1.Id, w2.Id, w3.Id, otherWs.Id, a.Id, b.Id, c.Id, d.Id, x.Id);
    }

    private static SchoolScope SchoolTeacher(SchoolSeed seed) => SchoolScope.For(TeacherId, seed.SchoolId);

    /// <summary>Kapsam içi + kapsam dışı cevaplar. Beklenen kapsam içi: B=8 (2 doğru, 5 sn), A=5 (3 doğru, 10 sn, 3 gün önce), C=3 (3 doğru, 20 sn).</summary>
    private async Task SeedAnswersAsync(SchoolSeed seed)
    {
        // Kapsam içi
        await AddAnswersAsync(seed.W1, seed.StudentA, count: 5, correct: 3, timeEach: 10, answeredAt: Now.AddDays(-3));
        await AddAnswersAsync(seed.W1, seed.StudentB, count: 8, correct: 2, timeEach: 5, answeredAt: Now);
        await AddAnswersAsync(seed.W2, seed.StudentC, count: 3, correct: 3, timeEach: 20, answeredAt: Now);

        // Kapsam dışı
        await AddAnswersAsync(seed.W1, seed.StudentB, count: 4, correct: 4, timeEach: 5, answeredAt: Now, answered: false); // cevapsız satırlar
        await AddAnswersAsync(seed.W2, seed.StudentC, count: 20, correct: 20, timeEach: 20, answeredAt: Now.AddDays(-10)); // pencere dışı
        await AddAnswersAsync(seed.OtherWs, seed.StudentA, count: 50, correct: 50, timeEach: 1, answeredAt: Now); // başka öğretmenin sınavı
        await AddAnswersAsync(seed.W2, seed.StudentA, count: 7, correct: 7, timeEach: 1, answeredAt: Now); // W2 A'ya atanmadı
        await AddAnswersAsync(seed.W3, seed.StudentD, count: 9, correct: 9, timeEach: 1, answeredAt: Now); // atamasız public worksheet
        await AddAnswersAsync(seed.W1, seed.StudentX, count: 30, correct: 30, timeEach: 1, answeredAt: Now); // başka okul (#235)
    }

    // ---- Benim Aktivitem ----

    [Fact]
    public async Task Own_activity_counts_worksheets_and_assignments_the_teacher_created_in_the_window()
    {
        var seed = await SeedSchoolScenarioAsync();
        await SeedAnswersAsync(seed);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetOwnActivitySummaryAsync(SchoolTeacher(seed), 7);

        result.WorksheetsCreated.ShouldBe(3);   // W1, W2, W3 — W4 10 gün önce, Other başka öğretmenin
        result.AssignmentsCreated.ShouldBe(2);  // W1 sınıf + W2 direkt — diğer öğretmenin ataması hariç
        result.ActiveStudents.ShouldBe(3);      // A, B, C — D/X ve başka sınavdaki aktivite hariç
    }

    [Fact]
    public async Task Own_activity_is_all_zero_for_a_teacher_without_data()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetOwnActivitySummaryAsync(SchoolScope.For(TeacherId, null), 7);

        result.WorksheetsCreated.ShouldBe(0);
        result.AssignmentsCreated.ShouldBe(0);
        result.ActiveStudents.ShouldBe(0);
    }

    [Fact]
    public async Task Own_activity_window_follows_days_parameter()
    {
        var seed = await SeedSchoolScenarioAsync();
        await SeedAnswersAsync(seed);
        await using var ctx = _db.NewContext();

        var wide = await NewService(ctx).GetOwnActivitySummaryAsync(SchoolTeacher(seed), 30);
        var narrow = await NewService(ctx).GetOwnActivitySummaryAsync(SchoolTeacher(seed), 2);

        wide.WorksheetsCreated.ShouldBe(4);  // W4 (10 gün önce) 30 günlük pencerede
        wide.ActiveStudents.ShouldBe(3);
        narrow.ActiveStudents.ShouldBe(2);   // A'nın cevapları 3 gün önce → 2 günlük pencere dışında
    }

    // ---- Öğrenci Aktivitesi + En Aktif Öğrenciler ----

    [Fact]
    public async Task Students_activity_totals_only_include_students_assigned_to_the_teachers_own_worksheets()
    {
        var seed = await SeedSchoolScenarioAsync();
        await SeedAnswersAsync(seed);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 7);

        result.TotalQuestionsSolved.ShouldBe(16);          // 8 + 5 + 3
        result.TotalCorrectCount.ShouldBe(8);              // 2 + 3 + 3
        result.TotalTimeSeconds.ShouldBe(150);             // 8*5 + 5*10 + 3*20
        result.TopStudents.Select(s => s.StudentId)
            .ShouldNotContain(id => id == seed.StudentD || id == seed.StudentX);
    }

    [Fact]
    public async Task Top_students_are_ordered_by_questions_solved_descending_with_per_student_numbers()
    {
        var seed = await SeedSchoolScenarioAsync();
        await SeedAnswersAsync(seed);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 7);

        result.TopStudents.Select(s => s.StudentId).ShouldBe(new[] { seed.StudentB, seed.StudentA, seed.StudentC });

        var a = result.TopStudents.Single(s => s.StudentId == seed.StudentA);
        a.QuestionsSolved.ShouldBe(5);  // OtherWs (50) ve atanmadığı W2 (7) hariç
        a.CorrectCount.ShouldBe(3);
        a.TimeSeconds.ShouldBe(50);

        var c = result.TopStudents.Single(s => s.StudentId == seed.StudentC);
        c.QuestionsSolved.ShouldBe(3);  // 10 gün önceki 20 cevap pencere dışı
    }

    [Fact]
    public async Task Students_activity_window_follows_days_parameter()
    {
        var seed = await SeedSchoolScenarioAsync();
        await SeedAnswersAsync(seed);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 2);

        result.TotalQuestionsSolved.ShouldBe(11);  // A'nın 3 gün önceki 5 cevabı düşer
        result.TopStudents.Select(s => s.StudentId).ShouldBe(new[] { seed.StudentB, seed.StudentC });
    }

    [Fact]
    public async Task Top_students_are_limited_to_ten_and_ties_break_on_correct_count()
    {
        int worksheetId;
        var studentIds = new List<int>();
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            var school = new School { Name = "S" };
            ctx.AddRange(grade, school);
            await ctx.SaveChangesAsync();
            ctx.Teachers.Add(new Teacher { UserId = TeacherId, SchoolId = school.Id });
            for (var i = 0; i < 12; i++)
                ctx.Students.Add(new Student { UserId = 200 + i, StudentNumber = $"n{i}", SchoolName = "S", SchoolId = school.Id, GradeId = grade.Id });
            await ctx.SaveChangesAsync();
            studentIds.AddRange(await ctx.Students.OrderBy(s => s.Id).Select(s => s.Id).ToListAsync());

            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            ctx.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws.Id, GradeId = grade.Id, SchoolId = school.Id, StartAt = Now.AddDays(-1) });
            await ctx.SaveChangesAsync();
            worksheetId = ws.Id;
        }

        // i. öğrenci i+1 soru çözer; son iki öğrenci 12'şer soruyla berabere, doğru sayısı ayırır.
        for (var i = 0; i < 10; i++)
            await AddAnswersAsync(worksheetId, studentIds[i], count: i + 1, correct: 0, timeEach: 1, answeredAt: Now);
        await AddAnswersAsync(worksheetId, studentIds[10], count: 12, correct: 1, timeEach: 1, answeredAt: Now);
        await AddAnswersAsync(worksheetId, studentIds[11], count: 12, correct: 5, timeEach: 1, answeredAt: Now);

        await using var check = _db.NewContext();
        var schoolId = await check.Teachers.Select(t => t.SchoolId).SingleAsync();
        var result = await NewService(check).GetStudentsActivitySummaryAsync(SchoolScope.For(TeacherId, schoolId), 7);

        result.TopStudents.Count.ShouldBe(TeacherService.ActivityTopStudentsLimit);
        result.TopStudents[0].StudentId.ShouldBe(studentIds[11]);
        result.TopStudents[1].StudentId.ShouldBe(studentIds[10]);
        result.TopStudents.Select(s => s.QuestionsSolved).ShouldBeInOrder(SortDirection.Descending);
        result.TotalQuestionsSolved.ShouldBe(55 + 24); // toplamlar listelenmeyen öğrencileri de kapsar
    }

    [Fact]
    public async Task Student_names_are_resolved_in_one_batch_with_student_number_fallback()
    {
        var seed = await SeedSchoolScenarioAsync();
        await SeedAnswersAsync(seed);
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto> { new() { Id = 102, FullName = "Berk B." } });
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 7);

        result.TopStudents.Single(s => s.StudentId == seed.StudentB).StudentName.ShouldBe("Berk B.");
        result.TopStudents.Single(s => s.StudentId == seed.StudentA).StudentName.ShouldContain("a");
        await _authApi.Received(1).GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Students_activity_is_empty_for_a_teacher_without_worksheets()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetStudentsActivitySummaryAsync(SchoolScope.For(TeacherId, null), 7);

        result.TotalQuestionsSolved.ShouldBe(0);
        result.TotalCorrectCount.ShouldBe(0);
        result.TotalTimeSeconds.ShouldBe(0);
        result.TopStudents.ShouldBeEmpty();
        await _authApi.DidNotReceive().GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Other_teacher_does_not_see_activity_on_worksheets_they_do_not_own()
    {
        var seed = await SeedSchoolScenarioAsync();
        await SeedAnswersAsync(seed);
        await using var ctx = _db.NewContext();

        // Diğer öğretmen yalnızca kendi OtherWs'inde A'nın 50 cevabını görür.
        var result = await NewService(ctx).GetStudentsActivitySummaryAsync(SchoolScope.For(OtherTeacherId, seed.SchoolId), 7);

        result.TotalQuestionsSolved.ShouldBe(50);
        result.TopStudents.ShouldHaveSingleItem().StudentId.ShouldBe(seed.StudentA);
    }

    // ---- Okul kapsamı (#235) ----

    [Fact]
    public async Task School_teacher_does_not_see_other_school_students_via_legacy_or_cross_school_public_assignments()
    {
        // #235 lagging testinin eşleniği: okullu öğretmenin (S1) worksheet'ine
        //  (a) SchoolId=null legacy sınıf ataması → S1 ve S2 öğrencileri hedefte görünür,
        //  (b) S2 öğretmeninin PublicAssignable akışıyla yaptığı SchoolId=S2 sınıf ataması,
        //  (c) S2 öğretmeninin S2 öğrencisine direkt ataması.
        // S2 öğrencilerinin cevapları hiçbir yoldan S1 öğretmeninin aktivite uçlarına girmemeli.
        const int s2TeacherId = 9;
        int wsId, s1Student, s2Student, s2Direct, s1SchoolId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            var s1 = new School { Name = "S1" };
            var s2 = new School { Name = "S2" };
            ctx.AddRange(grade, s1, s2);
            await ctx.SaveChangesAsync();

            ctx.Teachers.AddRange(
                new Teacher { UserId = TeacherId, SchoolId = s1.Id },
                new Teacher { UserId = s2TeacherId, SchoolId = s2.Id });
            var a = new Student { UserId = 401, StudentNumber = "s1", SchoolName = "S1", SchoolId = s1.Id, GradeId = grade.Id };
            var x = new Student { UserId = 402, StudentNumber = "s2", SchoolName = "S2", SchoolId = s2.Id, GradeId = grade.Id };
            var y = new Student { UserId = 403, StudentNumber = "s2d", SchoolName = "S2", SchoolId = s2.Id, GradeId = null };
            ctx.AddRange(a, x, y);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "Public WS", Description = "", GradeId = grade.Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            ctx.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws.Id, GradeId = grade.Id, SchoolId = null, StartAt = Now.AddDays(-1) });
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(s2TeacherId);
            ctx.WorksheetAssignments.AddRange(
                new WorksheetAssignment { WorksheetId = ws.Id, GradeId = grade.Id, SchoolId = s2.Id, StartAt = Now.AddDays(-1) },
                new WorksheetAssignment { WorksheetId = ws.Id, StudentId = y.Id, StartAt = Now.AddDays(-1) });
            await ctx.SaveChangesAsync();

            (wsId, s1Student, s2Student, s2Direct, s1SchoolId) = (ws.Id, a.Id, x.Id, y.Id, s1.Id);
        }

        await AddAnswersAsync(wsId, s1Student, count: 2, correct: 1, timeEach: 10, answeredAt: Now);
        await AddAnswersAsync(wsId, s2Student, count: 9, correct: 9, timeEach: 10, answeredAt: Now);
        await AddAnswersAsync(wsId, s2Direct, count: 7, correct: 7, timeEach: 10, answeredAt: Now);

        await using var check = _db.NewContext();
        var scope = SchoolScope.For(TeacherId, s1SchoolId);
        var students = await NewService(check).GetStudentsActivitySummaryAsync(scope, 7);
        var own = await NewService(check).GetOwnActivitySummaryAsync(scope, 7);

        students.TopStudents.ShouldHaveSingleItem().StudentId.ShouldBe(s1Student);
        students.TotalQuestionsSolved.ShouldBe(2);
        students.TotalCorrectCount.ShouldBe(1);
        students.TotalTimeSeconds.ShouldBe(20);
        own.ActiveStudents.ShouldBe(1);
        own.AssignmentsCreated.ShouldBe(1); // S2 öğretmeninin atamaları sahibin sayacına girmez
    }

    // ---- Süre ----

    [Fact]
    public async Task Zero_or_negative_time_taken_contributes_zero_seconds_but_question_still_counts()
    {
        var seed = await SeedSchoolScenarioAsync();
        await AddAnswersAsync(seed.W1, seed.StudentA, count: 2, correct: 1, timeEach: -30, answeredAt: Now);
        await AddAnswersAsync(seed.W1, seed.StudentA, count: 1, correct: 0, timeEach: 0, answeredAt: Now);
        await AddAnswersAsync(seed.W1, seed.StudentA, count: 1, correct: 1, timeEach: 12, answeredAt: Now);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 7);

        var a = result.TopStudents.ShouldHaveSingleItem();
        a.QuestionsSolved.ShouldBe(4);
        a.TimeSeconds.ShouldBe(12);
        result.TotalTimeSeconds.ShouldBe(12);
    }

    // ---- Bağımsız öğretmen (#222) ----

    [Fact]
    public async Task Independent_teacher_activity_counts_only_approved_booking_students()
    {
        int wsId, studentA, studentBooked;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            var school = new School { Name = "A" };
            ctx.AddRange(grade, school);
            await ctx.SaveChangesAsync();

            var tutor = new Teacher { UserId = TutorUserId, SchoolId = null, IsIndependentTutor = true };
            var a = new Student { UserId = 301, StudentNumber = "a", SchoolName = "A", SchoolId = school.Id, GradeId = grade.Id };
            var k = new Student { UserId = 302, StudentNumber = "k", SchoolName = "A", SchoolId = school.Id, GradeId = grade.Id };
            ctx.AddRange(tutor, a, k);
            await ctx.SaveChangesAsync();
            BookingSeed.Add(ctx, tutor.Id, k.Id, BookingStatus.Approved, 8);

            ctx.SetCurrentUser(TutorUserId);
            var ws = new Worksheet { Name = "Tutor WS", Description = "", GradeId = grade.Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            ctx.WorksheetAssignments.AddRange(
                new WorksheetAssignment { WorksheetId = ws.Id, GradeId = grade.Id, SchoolId = null, StartAt = Now.AddDays(-1) },
                new WorksheetAssignment { WorksheetId = ws.Id, StudentId = a.Id, StartAt = Now.AddDays(-1) },
                new WorksheetAssignment { WorksheetId = ws.Id, StudentId = k.Id, StartAt = Now.AddDays(-1) });
            await ctx.SaveChangesAsync();
            (wsId, studentA, studentBooked) = (ws.Id, a.Id, k.Id);
        }

        await AddAnswersAsync(wsId, studentA, count: 6, correct: 6, timeEach: 1, answeredAt: Now);
        await AddAnswersAsync(wsId, studentBooked, count: 2, correct: 1, timeEach: 1, answeredAt: Now);

        await using var check = _db.NewContext();
        var tutorScope = SchoolScope.For(TutorUserId, null);
        var students = await NewService(check).GetStudentsActivitySummaryAsync(tutorScope, 7);
        var own = await NewService(check).GetOwnActivitySummaryAsync(tutorScope, 7);

        students.TopStudents.ShouldHaveSingleItem().StudentId.ShouldBe(studentBooked);
        students.TotalQuestionsSolved.ShouldBe(2);
        own.ActiveStudents.ShouldBe(1);
    }

    // ---- issue #265: yerel (Europe/Istanbul) gün sınırı ----

    private TeacherService NewService(AppDbContext ctx, ILocalDayCalendar calendar, ITeacherActivityCache? cache = null)
        => new(ctx, _authApi, schoolAccessPolicy: new SchoolAccessPolicy(ctx), dayCalendar: calendar, activityCache: cache);

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Activity_window_uses_istanbul_day_boundary_not_utc()
    {
        var seed = await SeedSchoolScenarioAsync();
        // 2026-09-24 22:30 UTC = 25 Eylül 01:30 TR → "bugün" (25 Eylül) sayılır.
        await AddAnswersAsync(seed.W1, seed.StudentA, count: 2, correct: 1, timeEach: 10, answeredAt: Utc(2026, 9, 24, 22, 30));
        // 2026-09-24 20:59 UTC = 24 Eylül 23:59 TR → dün; days=1 penceresinin dışında. (Eski UTC hesabında "şimdi"
        // 25 Eylül 10:00 UTC iken days=1 = 25 Eylül 00:00 UTC'den sonrası → 22:30'luk cevap da dışarıda kalırdı.)
        await AddAnswersAsync(seed.W1, seed.StudentB, count: 3, correct: 3, timeEach: 10, answeredAt: Utc(2026, 9, 24, 20, 59));
        var calendar = new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, new FixedTimeProvider(new DateTimeOffset(Utc(2026, 9, 25, 10, 0))));
        await using var ctx = _db.NewContext();

        var students = await NewService(ctx, calendar).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 1);
        var own = await NewService(ctx, calendar).GetOwnActivitySummaryAsync(SchoolTeacher(seed), 1);
        var twoDays = await NewService(ctx, calendar).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 2);

        students.TopStudents.ShouldHaveSingleItem().StudentId.ShouldBe(seed.StudentA);
        students.TotalQuestionsSolved.ShouldBe(2);
        own.ActiveStudents.ShouldBe(1);
        twoDays.TotalQuestionsSolved.ShouldBe(5); // 24 Eylül TR de pencerede
    }

    // ---- issue #265: iki uç tek toplama ----

    /// <summary><c>TestInstanceQuestions</c>'a giden sorguları sayar (ağır toplamanın kaç kez çalıştığı).</summary>
    private sealed class AggregationCounter : DbCommandInterceptor
    {
        public int Count;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"TestInstanceQuestions\"", StringComparison.Ordinal))
                Interlocked.Increment(ref Count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Own_and_students_summaries_share_one_aggregation_through_the_cache()
    {
        var seed = await SeedSchoolScenarioAsync();
        await SeedAnswersAsync(seed);
        var counter = new AggregationCounter();
        using var cache = new TeacherActivityCache(TimeSpan.FromSeconds(60));

        // İki ayrı istek kapsamı (ayrı context + servis) — tek singleton önbellek.
        await using var ctx1 = _db.NewContext(counter);
        var own = await NewService(ctx1, LocalDayCalendar.Default, cache).GetOwnActivitySummaryAsync(SchoolTeacher(seed), 7);
        await using var ctx2 = _db.NewContext(counter);
        var students = await NewService(ctx2, LocalDayCalendar.Default, cache).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 7);

        counter.Count.ShouldBe(1);
        own.ActiveStudents.ShouldBe(3);                 // yanıt sözleşmeleri değişmedi
        students.TotalQuestionsSolved.ShouldBe(16);
        students.TopStudents.Select(s => s.StudentId).ShouldBe(new[] { seed.StudentB, seed.StudentA, seed.StudentC });
    }

    [Fact]
    public async Task Cache_key_separates_teachers_and_windows()
    {
        var seed = await SeedSchoolScenarioAsync();
        await SeedAnswersAsync(seed);
        var counter = new AggregationCounter();
        using var cache = new TeacherActivityCache(TimeSpan.FromSeconds(60));
        await using var ctx = _db.NewContext(counter);

        var seven = await NewService(ctx, LocalDayCalendar.Default, cache).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 7);
        var two = await NewService(ctx, LocalDayCalendar.Default, cache).GetStudentsActivitySummaryAsync(SchoolTeacher(seed), 2);
        var other = await NewService(ctx, LocalDayCalendar.Default, cache)
            .GetStudentsActivitySummaryAsync(SchoolScope.For(OtherTeacherId, seed.SchoolId), 7);

        counter.Count.ShouldBe(3);
        seven.TotalQuestionsSolved.ShouldBe(16);
        two.TotalQuestionsSolved.ShouldBe(11);
        other.TotalQuestionsSolved.ShouldBe(50);
    }
}
