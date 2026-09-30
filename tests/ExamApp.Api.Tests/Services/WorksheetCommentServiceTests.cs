using System.Text.Json;
using ExamApp.Foundation.Contracts;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.WorksheetComments;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #105 (dilim 1): yorum-soru thread'i — toggle/override, çözme şartı, ilgili öğretmen cevabı, görünürlük,
/// tek seviye reply, gövde doğrulama, sayfalama, retire ve DTO'da kişisel veri olmaması.
/// </summary>
public partial class WorksheetCommentServiceTests : IDisposable
{
    private const int Owner = 5000;          // worksheet sahibi
    private const int Assigner = 6000;       // öğrenci A'ya atama yapan öğretmen
    private const int Unrelated = 7000;      // ilgisiz öğretmen
    private const int StudentAUser = 101;    // atamalı öğrenci
    private const int StudentBUser = 102;    // atamasız, aynı sınıf (keşfet ile erişir)
    private const int OutsiderUser = 103;    // başka sınıf, erişimi yok
    private const int ForeignTeacher = 7100; // #305: başka okuldaki öğretmen

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public WorksheetCommentServiceTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto
            {
                Id = id,
                FullName = id switch
                {
                    StudentAUser => "Ayşe Nur kaya",
                    StudentBUser => "Burak İnce",
                    Owner => "Oya Hoca",
                    Assigner => "Ata Hoca",
                    _ => ""
                },
                Email = $"u{id}@mail.local",
                KeycloakId = $"kc-{id}"
            }).ToList());
    }

    public void Dispose() => _db.Dispose();

    private WorksheetCommentService NewService(AppDbContext ctx) =>
        new(ctx, new WorksheetResponsibleTeacherResolver(ctx), _authApi);

    private static WorksheetCommentActor Student(int userId) =>
        new(userId, $"kc-{userId}", userId == StudentAUser ? "Ayşe Nur kaya" : "Öğrenci X", WorksheetCommentActorKind.Student, false);

    private static WorksheetCommentActor Teacher(int userId, bool isAdmin = false) =>
        new(userId, $"kc-{userId}", $"Hoca {userId}", WorksheetCommentActorKind.Teacher, isAdmin);

    private static readonly WorksheetCommentActor AdminReader = new(9000, "kc-9000", "Admin", WorksheetCommentActorKind.AdminReader, true);

    private sealed record World(int GradeId, int WorksheetId, int OtherWorksheetId, int Q1, int Q2, int QForeign,
        int Wq1, int Wq2, int StudentA, int StudentB, int AssignmentId, int AnswerQ1, int SchoolId = 0, int OtherSchoolId = 0);

    private async Task<World> SeedAsync(bool commentsEnabled = true, bool? assignmentOverride = null,
        WorksheetTeacherSharing sharing = WorksheetTeacherSharing.Private)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var otherGrade = new Grade { Name = "6" };
        var school = new School { Name = "Okul" };
        var otherSchool = new School { Name = "Diğer Okul" };
        ctx.AddRange(grade, otherGrade, school, otherSchool);
        await ctx.SaveChangesAsync();

        // issue #305 (okul kapsamı): sahip/atayan/ilgisiz öğretmen öğrencilerle aynı okulda; ForeignTeacher başka okulda.
        ctx.Teachers.AddRange(
            new Teacher { UserId = Owner, SchoolId = school.Id },
            new Teacher { UserId = Assigner, SchoolId = school.Id },
            new Teacher { UserId = Unrelated, SchoolId = school.Id },
            new Teacher { UserId = ForeignTeacher, SchoolId = otherSchool.Id });
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(Owner);
        var ws = new Worksheet
        {
            Name = "Kesirler", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600,
            CommentsEnabled = commentsEnabled, TeacherSharing = sharing
        };
        var otherWs = new Worksheet { Name = "Diğer", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        var q1 = new Question { Text = "Q1" };
        var q2 = new Question { Text = "Q2" };
        var qForeign = new Question { Text = "Q3" };
        ctx.AddRange(ws, otherWs, q1, q2, qForeign);
        await ctx.SaveChangesAsync();

        var a1 = new Answer { QuestionId = q1.Id, Text = "A", Tag = "A", Order = 0 };
        ctx.Answers.Add(a1);
        var wq1 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q1.Id, Order = 1 };
        var wq2 = new WorksheetQuestion { TestId = ws.Id, QuestionId = q2.Id, Order = 2 };
        ctx.TestQuestions.AddRange(wq1, wq2, new WorksheetQuestion { TestId = otherWs.Id, QuestionId = qForeign.Id, Order = 1 });
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(0);
        var studentA = new Student { UserId = StudentAUser, StudentNumber = "a", GradeId = grade.Id, SchoolId = school.Id };
        var studentB = new Student { UserId = StudentBUser, StudentNumber = "b", GradeId = grade.Id, SchoolId = school.Id };
        var outsider = new Student { UserId = OutsiderUser, StudentNumber = "c", GradeId = otherGrade.Id, SchoolId = school.Id };
        ctx.Students.AddRange(studentA, studentB, outsider);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(Assigner);
        var assignment = new WorksheetAssignment
        {
            WorksheetId = ws.Id, StudentId = studentA.Id, StartAt = DateTime.UtcNow.AddDays(-1),
            CommentsEnabledOverride = assignmentOverride
        };
        ctx.WorksheetAssignments.Add(assignment);
        await ctx.SaveChangesAsync();

        return new World(grade.Id, ws.Id, otherWs.Id, q1.Id, q2.Id, qForeign.Id, wq1.Id, wq2.Id, studentA.Id, studentB.Id,
            assignment.Id, a1.Id, school.Id, otherSchool.Id);
    }

    /// <summary>Öğrenci worksheet'i başlatır; <paramref name="answerQ1"/> ile Q1'i cevaplar (seçmeli veya payload).</summary>
    private async Task StartAsync(World w, int studentId, bool answerQ1 = false, bool viaPayload = false, int? worksheetId = null)
    {
        await using var ctx = _db.NewContext();
        var instance = new WorksheetInstance
        {
            StudentId = studentId, WorksheetId = worksheetId ?? w.WorksheetId, StartTime = DateTime.UtcNow,
            Status = WorksheetInstanceStatus.Started,
            WorksheetInstanceQuestions = new List<WorksheetInstanceQuestion>
            {
                new()
                {
                    WorksheetQuestionId = w.Wq1,
                    SelectedAnswerId = answerQ1 && !viaPayload ? w.AnswerQ1 : null,
                    AnswerPayload = answerQ1 && viaPayload ? "{\"order\":[1,2]}" : null
                },
                new() { WorksheetQuestionId = w.Wq2 }
            }
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();
    }

    private async Task<WorksheetCommentResultDto> PostAsync(int worksheetId, WorksheetCommentActor actor, string body,
        int? questionId = null, int? parentId = null)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateAsync(worksheetId,
            new CreateWorksheetCommentDto { Body = body, QuestionId = questionId, ParentCommentId = parentId }, actor);
    }

    private async Task<WorksheetCommentPageResultDto> GetAsync(int worksheetId, WorksheetCommentActor actor,
        int? questionId = null, string? cursor = null, int take = 20)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).GetThreadAsync(worksheetId,
            new WorksheetCommentQueryDto { QuestionId = questionId, Cursor = cursor, Take = take }, actor);
    }

    private static int Created(WorksheetCommentResultDto r)
    {
        r.Success.ShouldBeTrue(r.ErrorCode);
        return r.Comment!.Id;
    }

    private static void ShouldFail(WorksheetCommentResponseDto r, string code, bool forbidden = false, bool notFound = false)
    {
        r.Success.ShouldBeFalse();
        r.ErrorCode.ShouldBe(code);
        r.Forbidden.ShouldBe(forbidden);
        r.NotFound.ShouldBe(notFound);
        r.Message.ShouldNotBeNullOrWhiteSpace();
    }

    // ---- Açık/kapalı anahtarı -------------------------------------------------------------------------------

    [Fact]
    public async Task New_worksheet_defaults_to_comments_enabled()
    {
        new Worksheet().CommentsEnabled.ShouldBeTrue();
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        (await ctx.Worksheets.SingleAsync(x => x.Id == w.WorksheetId)).CommentsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Disabled_worksheet_blocks_new_student_comments_but_existing_ones_stay_visible()
    {
        var w = await SeedAsync();
        var existing = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "önceden yazıldı"));

        await using (var ctx = _db.NewContext())
        {
            var ws = await ctx.Worksheets.SingleAsync(x => x.Id == w.WorksheetId);
            ws.CommentsEnabled = false; // kapalı olarak kaydedilebiliyor (sentinel) — öğretmen düzenlemesi
            await ctx.SaveChangesAsync();
        }

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentBUser), "yeni"), WorksheetCommentErrorCodes.CommentsDisabled, forbidden: true);

        var page = await GetAsync(w.WorksheetId, Student(StudentBUser));
        page.Success.ShouldBeTrue();
        page.Page!.CanWrite.ShouldBeFalse();
        page.Page.LockReason.ShouldBe(WorksheetCommentLockReasons.CommentsDisabled);
        page.Page.Items.Select(i => i.Id).ShouldBe(new[] { existing });
        page.Page.Items[0].CanReply.ShouldBeFalse();
    }

    [Fact]
    public async Task Worksheet_can_be_stored_with_comments_disabled()
    {
        var w = await SeedAsync(commentsEnabled: false);
        await using var ctx = _db.NewContext();
        (await ctx.Worksheets.SingleAsync(x => x.Id == w.WorksheetId)).CommentsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Assignment_override_true_opens_a_disabled_worksheet_for_that_student_only()
    {
        var w = await SeedAsync(commentsEnabled: false, assignmentOverride: true);

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "atamamda açık"));
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentBUser), "bende kapalı"), WorksheetCommentErrorCodes.CommentsDisabled, forbidden: true);
    }

    [Fact]
    public async Task Assignment_override_false_closes_an_enabled_worksheet_for_that_student_only()
    {
        var w = await SeedAsync(commentsEnabled: true, assignmentOverride: false);

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "kapalı"), WorksheetCommentErrorCodes.CommentsDisabled, forbidden: true);
        (await GetAsync(w.WorksheetId, Student(StudentAUser))).Page!.LockReason.ShouldBe(WorksheetCommentLockReasons.CommentsDisabled);
        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "açık"));
    }

    [Fact]
    public async Task Null_override_uses_the_worksheet_default()
    {
        var w = await SeedAsync(commentsEnabled: false, assignmentOverride: null);
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "x"), WorksheetCommentErrorCodes.CommentsDisabled, forbidden: true);
    }

    [Fact]
    public async Task Teachers_can_still_write_when_comments_are_disabled()
    {
        var w = await SeedAsync(commentsEnabled: true, assignmentOverride: false);
        await using (var ctx = _db.NewContext())
        {
            // A'nın kökü kapanmadan önce yazılmış olsun
            ctx.SetCurrentUser(StudentAUser);
            ctx.WorksheetComments.Add(new WorksheetComment
            {
                WorksheetId = w.WorksheetId, AuthorUserId = StudentAUser, AuthorKeycloakId = "kc-101",
                AuthorRole = WorksheetCommentAuthorRole.Student, Body = "soru", ResponsibleTeacherUserId = Assigner
            });
            var ws = await ctx.Worksheets.SingleAsync(x => x.Id == w.WorksheetId);
            ws.CommentsEnabled = false;
            await ctx.SaveChangesAsync();
        }
        var rootId = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.Items.Single().Id;

        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: rootId));
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
    }

    // ---- Çözme şartı --------------------------------------------------------------------------------------

    [Fact]
    public async Task Question_comment_is_rejected_when_the_student_never_started_the_worksheet()
    {
        var w = await SeedAsync();

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "?", questionId: w.Q1),
            WorksheetCommentErrorCodes.WorksheetNotStarted, forbidden: true);

        var page = (await GetAsync(w.WorksheetId, Student(StudentAUser), questionId: w.Q1)).Page!;
        page.CanWrite.ShouldBeFalse();
        page.LockReason.ShouldBe(WorksheetCommentLockReasons.WorksheetNotStarted);
    }

    [Fact]
    public async Task Question_comment_is_rejected_when_the_question_is_not_answered()
    {
        var w = await SeedAsync();
        await StartAsync(w, w.StudentA, answerQ1: true);

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "?", questionId: w.Q2),
            WorksheetCommentErrorCodes.QuestionNotAnswered, forbidden: true);
        (await GetAsync(w.WorksheetId, Student(StudentAUser), questionId: w.Q2)).Page!.LockReason
            .ShouldBe(WorksheetCommentLockReasons.QuestionNotAnswered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Answered_question_accepts_comments_via_selected_answer_or_payload(bool viaPayload)
    {
        var w = await SeedAsync();
        await StartAsync(w, w.StudentA, answerQ1: true, viaPayload: viaPayload);

        var created = await PostAsync(w.WorksheetId, Student(StudentAUser), "neden B?", questionId: w.Q1);

        Created(created);
        created.Comment!.QuestionId.ShouldBe(w.Q1);
        var page = (await GetAsync(w.WorksheetId, Student(StudentAUser), questionId: w.Q1)).Page!;
        page.CanWrite.ShouldBeTrue();
        page.LockReason.ShouldBeNull();
    }

    [Fact]
    public async Task Worksheet_level_comment_needs_no_start()
    {
        var w = await SeedAsync();

        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "genel soru"));
        var page = (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!;
        page.CanWrite.ShouldBeTrue();
        page.LockReason.ShouldBeNull();
    }

    [Fact]
    public async Task Question_and_worksheet_threads_are_separate()
    {
        var w = await SeedAsync();
        await StartAsync(w, w.StudentA, answerQ1: true);
        var general = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "genel"));
        var onQ1 = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "q1", questionId: w.Q1));

        (await GetAsync(w.WorksheetId, Student(StudentAUser))).Page!.Items.Select(i => i.Id).ShouldBe(new[] { general });
        (await GetAsync(w.WorksheetId, Student(StudentAUser), questionId: w.Q1)).Page!.Items.Select(i => i.Id).ShouldBe(new[] { onQ1 });
        (await GetAsync(w.WorksheetId, Student(StudentAUser), questionId: w.Q2)).Page!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Question_outside_the_worksheet_is_rejected()
    {
        var w = await SeedAsync();

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "x", questionId: w.QForeign), WorksheetCommentErrorCodes.QuestionNotInWorksheet);
        ShouldFail(await GetAsync(w.WorksheetId, Student(StudentAUser), questionId: w.QForeign), WorksheetCommentErrorCodes.QuestionNotInWorksheet);
    }

    // ---- İlgili öğretmen ------------------------------------------------------------------------------------

    [Fact]
    public async Task Assigning_teacher_answers_the_assigned_students_thread_and_the_owner_cannot()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "hocam?"));

        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Owner), "ben sahibim", parentId: rootA),
            WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);
        var reply = await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: rootA);
        Created(reply);
        reply.Comment!.AuthorRole.ShouldBe(WorksheetCommentAuthorRole.Teacher);
        reply.Comment.ParentCommentId.ShouldBe(rootA);
    }

    [Fact]
    public async Task Without_assignment_the_owner_answers_and_the_other_assigner_cannot()
    {
        var w = await SeedAsync();
        var rootB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "hocam?"));

        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Assigner), "x", parentId: rootB),
            WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "cevap", parentId: rootB));
    }

    [Fact]
    public async Task Copy_owner_answers_on_a_copy_not_the_source_owner()
    {
        var w = await SeedAsync(sharing: WorksheetTeacherSharing.PublicView);
        int copyId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(Unrelated);
            var copy = new Worksheet
            {
                Name = "Kopya", Description = "", GradeId = w.GradeId, MaxDurationSeconds = 600, SourceWorksheetId = w.WorksheetId
            };
            ctx.Worksheets.Add(copy);
            await ctx.SaveChangesAsync();
            copyId = copy.Id;
        }
        var root = Created(await PostAsync(copyId, Student(StudentBUser), "kopyada soru"));

        ShouldFail(await PostAsync(copyId, Teacher(Owner), "kaynak sahibi", parentId: root),
            WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true); // kopya Private: kaynak sahibi göremez bile
        Created(await PostAsync(copyId, Teacher(Unrelated), "kopyalayan cevaplar", parentId: root));
    }

    [Fact]
    public async Task Get_marks_can_reply_per_thread_for_teachers()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A soruyor"));
        var rootB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B soruyor"));

        var asAssigner = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!;
        asAssigner.CanWrite.ShouldBeTrue(); // aktif ataması var → kök açabilir
        asAssigner.LockReason.ShouldBeNull();
        asAssigner.Items.Single(i => i.Id == rootA).CanReply.ShouldBeTrue();
        asAssigner.Items.Single(i => i.Id == rootB).CanReply.ShouldBeFalse();

        var asOwner = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!;
        asOwner.CanWrite.ShouldBeTrue();
        asOwner.Items.Single(i => i.Id == rootA).CanReply.ShouldBeFalse();
        asOwner.Items.Single(i => i.Id == rootB).CanReply.ShouldBeTrue();
    }

    [Fact]
    public async Task Unrelated_teacher_cannot_write_even_if_the_worksheet_is_visible()
    {
        var w = await SeedAsync(sharing: WorksheetTeacherSharing.PublicView);
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        var page = (await GetAsync(w.WorksheetId, Teacher(Unrelated))).Page!;
        page.CanWrite.ShouldBeFalse();
        page.Items.Single().CanReply.ShouldBeFalse();
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Unrelated), "kök"), WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Unrelated), "reply", parentId: rootA),
            WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);
    }

    [Fact]
    public async Task Teacher_who_cannot_see_a_private_worksheet_gets_not_found()
    {
        var w = await SeedAsync();

        ShouldFail(await GetAsync(w.WorksheetId, Teacher(Unrelated)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Unrelated), "x"), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
    }

    [Fact]
    public async Task Admin_teacher_reads_everything_but_gets_no_write_exemption()
    {
        var w = await SeedAsync();
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        (await GetAsync(w.WorksheetId, Teacher(Unrelated, isAdmin: true))).Page!.Items.Count.ShouldBe(1);
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Unrelated, isAdmin: true), "x"), WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);

        var adminPage = (await GetAsync(w.WorksheetId, AdminReader)).Page!;
        adminPage.Items.Count.ShouldBe(1);
        adminPage.CanWrite.ShouldBeFalse();
        ShouldFail(await PostAsync(w.WorksheetId, AdminReader, "x"), WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);
    }

    [Fact]
    public async Task Teacher_root_can_be_answered_by_its_author_and_by_students()
    {
        var w = await SeedAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));

        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "ek bilgi", parentId: announcement));
        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "teşekkürler", parentId: announcement));
    }

    // ---- Görünürlük -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Student_without_access_gets_not_found_like_a_missing_worksheet()
    {
        var w = await SeedAsync();
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        ShouldFail(await GetAsync(w.WorksheetId, Student(OutsiderUser)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
        ShouldFail(await PostAsync(w.WorksheetId, Student(OutsiderUser), "x"), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
        ShouldFail(await GetAsync(w.WorksheetId, Student(424242)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
    }

    [Fact]
    public async Task Students_with_access_see_every_comment_and_teacher_reply()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A soruyor"));
        var reply = Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "hoca cevaplıyor", parentId: rootA));

        var page = (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!;

        var thread = page.Items.Single();
        thread.Id.ShouldBe(rootA);
        thread.IsMine.ShouldBeFalse();
        thread.Replies.Select(r => r.Id).ShouldBe(new[] { reply });
        thread.Replies[0].AuthorRole.ShouldBe(WorksheetCommentAuthorRole.Teacher);
    }

    [Fact]
    public async Task Student_who_solved_a_restricted_worksheet_keeps_reading_after_the_assignment_is_gone()
    {
        var w = await SeedAsync();
        await StartAsync(w, w.StudentB);
        await using (var ctx = _db.NewContext())
        {
            var ws = await ctx.Worksheets.SingleAsync(x => x.Id == w.WorksheetId);
            ws.StudentVisibility = WorksheetStudentVisibility.Restricted;
            await ctx.SaveChangesAsync();
        }

        (await GetAsync(w.WorksheetId, Student(StudentBUser))).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Retired_worksheet_threads_remain_readable()
    {
        var w = await SeedAsync();
        await StartAsync(w, w.StudentB);
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "soru"));
        await using (var ctx = _db.NewContext())
        {
            var ws = await ctx.Worksheets.SingleAsync(x => x.Id == w.WorksheetId);
            ws.IsDeleted = true;
            ws.DeleteTime = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }

        (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Single().Id.ShouldBe(root);
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single().Id.ShouldBe(root);
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "hâlâ cevaplanır", parentId: root));
    }

    [Fact]
    public async Task Unknown_worksheet_is_not_found()
    {
        await SeedAsync();
        ShouldFail(await GetAsync(999_999, Student(StudentAUser)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
        ShouldFail(await PostAsync(999_999, Student(StudentAUser), "x"), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
    }

    // ---- Tek seviye reply ------------------------------------------------------------------------------------

    [Fact]
    public async Task Student_can_reply_to_another_students_root()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A soruyor"));

        var reply = await PostAsync(w.WorksheetId, Student(StudentBUser), "bence şöyle", parentId: rootA);

        Created(reply);
        reply.Comment!.ParentCommentId.ShouldBe(rootA);
        reply.Comment.IsMine.ShouldBeTrue();
    }

    [Fact]
    public async Task Reply_to_a_reply_is_rejected()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "kök"));
        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "reply", parentId: root));

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "nested", parentId: reply), WorksheetCommentErrorCodes.InvalidParent);
    }

    [Fact]
    public async Task Parent_from_another_question_or_worksheet_is_rejected()
    {
        var w = await SeedAsync();
        await StartAsync(w, w.StudentA, answerQ1: true);
        await StartAsync(w, w.StudentA, worksheetId: w.OtherWorksheetId);
        var generalRoot = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "genel"));
        var q1Root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "q1", questionId: w.Q1));
        var otherWsRoot = Created(await PostAsync(w.OtherWorksheetId, Student(StudentAUser), "diğer test"));

        // Soru thread'ine ait kök, worksheet seviyesinde reply'da kullanılamaz (ve tersi).
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "x", parentId: q1Root), WorksheetCommentErrorCodes.InvalidParent);
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "x", questionId: w.Q1, parentId: generalRoot), WorksheetCommentErrorCodes.InvalidParent);
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "x", parentId: otherWsRoot), WorksheetCommentErrorCodes.InvalidParent);
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "x", parentId: 999_999), WorksheetCommentErrorCodes.InvalidParent);
    }

    [Fact]
    public async Task Soft_deleted_root_cannot_be_replied_to()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "kök"));
        await using (var ctx = _db.NewContext())
        {
            var c = await ctx.WorksheetComments.SingleAsync(x => x.Id == root);
            ctx.WorksheetComments.Remove(c); // soft delete
            await ctx.SaveChangesAsync();
        }

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentBUser), "x", parentId: root), WorksheetCommentErrorCodes.InvalidParent);
        (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.ShouldBeEmpty();
    }

    // ---- Gövde doğrulama -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public async Task Empty_body_is_rejected(string? body)
    {
        var w = await SeedAsync();
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), body!), WorksheetCommentErrorCodes.BodyRequired);
    }

    [Fact]
    public async Task Body_is_trimmed_and_limited_to_2000_characters()
    {
        var w = await SeedAsync();

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), new string('a', 2001)), WorksheetCommentErrorCodes.BodyTooLong);

        var ok = await PostAsync(w.WorksheetId, Student(StudentAUser), "  " + new string('b', 2000) + "  ");
        Created(ok);
        ok.Comment!.Body.Length.ShouldBe(2000);

        await using var ctx = _db.NewContext();
        var stored = await ctx.WorksheetComments.SingleAsync(c => c.Id == ok.Comment.Id);
        stored.Body.ShouldBe(new string('b', 2000));
        stored.AuthorUserId.ShouldBe(StudentAUser);
        stored.AuthorKeycloakId.ShouldBe("kc-101");
        stored.CreateUserId.ShouldBe(StudentAUser);
        stored.AuthorRole.ShouldBe(WorksheetCommentAuthorRole.Student);
    }

    // ---- Sayfalama -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Roots_are_paged_newest_first_with_replies_oldest_first()
    {
        var w = await SeedAsync();
        var roots = new List<int>();
        for (var i = 0; i < 5; i++)
            roots.Add(Created(await PostAsync(w.WorksheetId, Student(StudentBUser), $"kök {i}")));
        var r1 = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "ilk", parentId: roots[4]));
        var r2 = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "ikinci", parentId: roots[4]));

        var seen = new List<int>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = (await GetAsync(w.WorksheetId, Student(StudentBUser), cursor: cursor, take: 2)).Page!;
            page.Items.Count.ShouldBeLessThanOrEqualTo(2);
            seen.AddRange(page.Items.Select(i => i.Id));
            if (pages == 0)
                page.Items[0].Replies.Select(r => r.Id).ShouldBe(new[] { r1, r2 });
            cursor = page.NextCursor;
            pages++;
        } while (cursor != null && pages < 10);

        pages.ShouldBe(3);
        seen.ShouldBe(Enumerable.Reverse(roots).ToList());
    }

    [Fact]
    public async Task Take_is_clamped_and_invalid_cursor_is_rejected()
    {
        var w = await SeedAsync();
        for (var i = 0; i < 3; i++)
            Created(await PostAsync(w.WorksheetId, Student(StudentBUser), $"kök {i}"));

        (await GetAsync(w.WorksheetId, Student(StudentBUser), take: 500)).Page!.Items.Count.ShouldBe(3);
        (await GetAsync(w.WorksheetId, Student(StudentBUser), take: 0)).Page!.Items.Count.ShouldBe(1);
        ShouldFail(await GetAsync(w.WorksheetId, Student(StudentBUser), cursor: "bozuk!!"), WorksheetCommentErrorCodes.InvalidCursor);
        ShouldFail(await GetAsync(w.WorksheetId, Student(StudentBUser), cursor: WorksheetCommentService.EncodeCursor(DateTime.UtcNow, 0)[..3] + "@@"),
            WorksheetCommentErrorCodes.InvalidCursor);
    }

    [Fact]
    public void Cursor_round_trips()
    {
        var at = new DateTime(2026, 9, 30, 10, 11, 12, DateTimeKind.Utc).AddTicks(1234560);
        WorksheetCommentService.TryDecodeCursor(WorksheetCommentService.EncodeCursor(at, 42), out var decoded).ShouldBeTrue();
        decoded.ShouldBe((at, 42));
        WorksheetCommentService.TryDecodeCursor("", out _).ShouldBeFalse();
    }

    // ---- DTO / kişisel veri ---------------------------------------------------------------------------------

    [Fact]
    public async Task Dto_carries_display_names_only_and_no_personal_identifiers()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: root));

        var page = (await GetAsync(w.WorksheetId, Student(StudentAUser))).Page!;
        var thread = page.Items.Single();
        thread.AuthorDisplayName.ShouldBe("Ayşe Nur K.");
        thread.AuthorRole.ShouldBe(WorksheetCommentAuthorRole.Student);
        thread.IsMine.ShouldBeTrue();
        thread.Replies.Single().AuthorDisplayName.ShouldBe("Ata Hoca");
        thread.Replies.Single().IsMine.ShouldBeFalse();

        var json = JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.ShouldNotContain("authorUserId", Case.Insensitive);
        json.ShouldNotContain("keycloak", Case.Insensitive);
        json.ShouldNotContain("email", Case.Insensitive);
        json.ShouldNotContain("kc-101");
        json.ShouldNotContain("@mail.local");
        json.ShouldNotContain("kaya", Case.Insensitive); // soyadın tamamı sızmaz
        json.ShouldContain("\"authorRole\":\"Student\"");
        json.ShouldContain("\"lockReason\":null");
    }

    [Fact]
    public async Task Names_fall_back_to_role_labels_when_auth_api_is_down()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: root));
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<UserLookupResultDto>>>(_ => throw new HttpRequestException("down"));

        var thread = (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Single();

        thread.AuthorDisplayName.ShouldBe("Öğrenci");
        thread.Replies.Single().AuthorDisplayName.ShouldBe("Öğretmen");
    }

    [Theory]
    [InlineData("Ayşe Kaya", "Ayşe K.")]
    [InlineData("ayşe nur kaya", "ayşe nur K.")]
    [InlineData("İrem ışık", "İrem I.")]
    [InlineData("Ali ince", "Ali İ.")]
    [InlineData("  Tek  ", "Tek")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Student_display_name_is_given_names_plus_surname_initial(string? fullName, string? expected)
    {
        WorksheetCommentService.FormatStudentDisplayName(fullName).ShouldBe(expected);
    }

    // ---- Review düzeltmeleri (#105 dilim 1) ------------------------------------------------------------------

    private async Task<WorksheetComment> StoredCommentAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.WorksheetComments.SingleAsync(c => c.Id == id);
    }

    [Fact]
    public async Task Responsible_teacher_is_pinned_on_student_roots_only()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A"));
        var rootB = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "B"));
        var teacherRoot = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        var reply = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "reply", parentId: rootA));

        (await StoredCommentAsync(rootA)).ResponsibleTeacherUserId.ShouldBe(Assigner);
        (await StoredCommentAsync(rootB)).ResponsibleTeacherUserId.ShouldBe(Owner);
        (await StoredCommentAsync(teacherRoot)).ResponsibleTeacherUserId.ShouldBeNull();
        (await StoredCommentAsync(reply)).ResponsibleTeacherUserId.ShouldBeNull();
    }

    [Fact]
    public async Task Pinned_teacher_can_still_reply_after_the_assignment_ended_and_the_new_resolution_cannot()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "hocam?"));
        await using (var ctx = _db.NewContext())
        {
            var a = await ctx.WorksheetAssignments.SingleAsync(x => x.Id == w.AssignmentId);
            a.EndAt = DateTime.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        // Anlık çözüm artık sahibi gösterir — ama yetki kayda sabitlenmiş değerden gelir.
        await using (var ctx = _db.NewContext())
            (await new WorksheetResponsibleTeacherResolver(ctx).ResolveResponsibleTeacherAsync(w.WorksheetId, StudentAUser))!
                .TeacherUserId.ShouldBe(Owner);

        var asAssigner = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!;
        asAssigner.CanWrite.ShouldBeFalse(); // aktif ataması kalmadı → yeni kök açamaz
        asAssigner.Items.Single().CanReply.ShouldBeTrue();
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single().CanReply.ShouldBeFalse();

        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "yine de cevaplıyorum", parentId: rootA));
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Owner), "x", parentId: rootA),
            WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);
    }

    private async Task RetireAsync(int worksheetId)
    {
        await using var ctx = _db.NewContext();
        var ws = await ctx.Worksheets.SingleAsync(x => x.Id == worksheetId);
        ws.IsDeleted = true;
        ws.DeleteTime = DateTime.UtcNow;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Retired_worksheet_is_closed_to_students_without_an_instance()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "retire öncesi"));
        await RetireAsync(w.WorksheetId);

        // B: grade uyumlu + Normal (CanStudentStartTest true) ama instance yok → retired'da geçersiz.
        ShouldFail(await GetAsync(w.WorksheetId, Student(StudentBUser)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentBUser), "x"), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
        // A: aktif ataması var ama instance yok → yine kapalı.
        ShouldFail(await GetAsync(w.WorksheetId, Student(StudentAUser)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
        ShouldFail(await GetRepliesAsync(w.WorksheetId, root, Student(StudentBUser)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

        // Instance'ı olan öğrenci okur ve yazar; öğretmen kuralları değişmez.
        await StartAsync(w, w.StudentB);
        (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!.Items.Single().Id.ShouldBe(root);
        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "retire sonrası"));
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "cevap", parentId: root));
    }

    // ---- auth-api ad çözümü: timeout / Polly ----------------------------------------------------------------

    private async Task<WorksheetCommentThreadDto> ThreadWithNameFailureAsync(
        Func<NSubstitute.Core.CallInfo, Task<IReadOnlyList<UserLookupResultDto>>> behavior)
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: root));
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>()).Returns(behavior);

        await using var ctx = _db.NewContext();
        var service = new WorksheetCommentService(ctx, new WorksheetResponsibleTeacherResolver(ctx), _authApi)
        {
            AuthorNameLookupTimeout = TimeSpan.FromMilliseconds(200)
        };
        var result = await service.GetThreadAsync(w.WorksheetId, new WorksheetCommentQueryDto(), Student(StudentBUser));
        result.Success.ShouldBeTrue();
        return result.Page!.Items.Single();
    }

    [Fact]
    public async Task Slow_auth_api_is_cut_off_by_the_timeout_and_names_fall_back()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var thread = await ThreadWithNameFailureAsync(async ci =>
        {
            await Task.Delay(Timeout.Infinite, ci.Arg<CancellationToken>());
            return new List<UserLookupResultDto>();
        });

        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        thread.AuthorDisplayName.ShouldBe("Öğrenci");
        thread.Replies.Single().AuthorDisplayName.ShouldBe("Öğretmen");
    }

    [Fact]
    public void Default_name_lookup_timeout_is_two_seconds()
    {
        using var db = TestDb.Create();
        using var ctx = db.NewContext();
        new WorksheetCommentService(ctx, new WorksheetResponsibleTeacherResolver(ctx), _authApi)
            .AuthorNameLookupTimeout.ShouldBe(TimeSpan.FromSeconds(2));
    }

    public static TheoryData<string> NameLookupFailures => new() { "timeout", "circuit", "canceled", "http" };

    private static Exception FailureOf(string kind) => kind switch
    {
        "timeout" => new TimeoutRejectedException(),
        "circuit" => new BrokenCircuitException(),
        "canceled" => new OperationCanceledException(),
        _ => new HttpRequestException("down")
    };

    [Theory]
    [MemberData(nameof(NameLookupFailures))]
    public async Task Resilience_failures_of_the_name_lookup_fall_back_to_role_labels(string kind)
    {
        var thread = await ThreadWithNameFailureAsync(_ => Task.FromException<IReadOnlyList<UserLookupResultDto>>(FailureOf(kind)));
        thread.AuthorDisplayName.ShouldBe("Öğrenci");
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        var w = await SeedAsync();
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));
        using var cts = new CancellationTokenSource();
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Task.FromException<IReadOnlyList<UserLookupResultDto>>(new OperationCanceledException(cts.Token));
            });

        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            NewService(ctx).GetThreadAsync(w.WorksheetId, new WorksheetCommentQueryDto(), Student(StudentBUser), cts.Token));
    }

    // ---- Reply sınırı + replies ucu -------------------------------------------------------------------------

    private async Task<(int Root, List<int> Replies)> SeedRepliesAsync(World w, int count)
    {
        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "kök"));
        var replies = new List<int>();
        for (var i = 0; i < count; i++)
            replies.Add(Created(await PostAsync(w.WorksheetId, Student(StudentBUser), $"reply {i}", parentId: root)));
        return (root, replies);
    }

    [Fact]
    public async Task Thread_list_returns_the_latest_five_replies_oldest_first_with_the_total_count()
    {
        var w = await SeedAsync();
        var (root, replies) = await SeedRepliesAsync(w, 8);
        var (small, smallReplies) = await SeedRepliesAsync(w, 2);

        var page = (await GetAsync(w.WorksheetId, Student(StudentBUser))).Page!;

        var big = page.Items.Single(i => i.Id == root);
        big.ReplyCount.ShouldBe(8);
        big.Replies.Select(r => r.Id).ShouldBe(replies.Skip(3));
        var few = page.Items.Single(i => i.Id == small);
        few.ReplyCount.ShouldBe(2);
        few.Replies.Select(r => r.Id).ShouldBe(smallReplies);
    }

    private async Task<WorksheetCommentRepliesResultDto> GetRepliesAsync(int worksheetId, int rootId, WorksheetCommentActor actor,
        string? cursor = null, int take = 20)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).GetRepliesAsync(worksheetId, rootId,
            new WorksheetCommentRepliesQueryDto { Cursor = cursor, Take = take }, actor);
    }

    [Fact]
    public async Task Replies_endpoint_pages_oldest_first_with_cursor()
    {
        var w = await SeedAsync();
        var (root, replies) = await SeedRepliesAsync(w, 7);

        var seen = new List<int>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var result = await GetRepliesAsync(w.WorksheetId, root, Student(StudentBUser), cursor, take: 3);
            result.Success.ShouldBeTrue(result.ErrorCode);
            result.Page!.ReplyCount.ShouldBe(7);
            result.Page.CanReply.ShouldBeTrue();
            seen.AddRange(result.Page.Items.Select(i => i.Id));
            cursor = result.Page.NextCursor;
            pages++;
        } while (cursor != null && pages < 10);

        pages.ShouldBe(3);
        seen.ShouldBe(replies);
        (await GetRepliesAsync(w.WorksheetId, root, Student(StudentBUser), take: 500)).Page!.Items.Count.ShouldBe(7);
        ShouldFail(await GetRepliesAsync(w.WorksheetId, root, Student(StudentBUser), cursor: "@@"), WorksheetCommentErrorCodes.InvalidCursor);
    }

    [Fact]
    public async Task Replies_endpoint_requires_a_root_of_the_same_worksheet_and_read_access()
    {
        var w = await SeedAsync();
        var (root, replies) = await SeedRepliesAsync(w, 1);
        var otherRoot = Created(await PostAsync(w.OtherWorksheetId, Student(StudentBUser), "diğer test"));

        ShouldFail(await GetRepliesAsync(w.WorksheetId, replies[0], Student(StudentBUser)), WorksheetCommentErrorCodes.RootCommentNotFound, notFound: true);
        ShouldFail(await GetRepliesAsync(w.WorksheetId, otherRoot, Student(StudentBUser)), WorksheetCommentErrorCodes.RootCommentNotFound, notFound: true);
        ShouldFail(await GetRepliesAsync(w.WorksheetId, 999_999, Student(StudentBUser)), WorksheetCommentErrorCodes.RootCommentNotFound, notFound: true);
        ShouldFail(await GetRepliesAsync(w.WorksheetId, root, Student(OutsiderUser)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);
        ShouldFail(await GetRepliesAsync(w.WorksheetId, root, Teacher(Unrelated)), WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

        (await GetRepliesAsync(w.WorksheetId, root, Teacher(Assigner))).Page!.CanReply.ShouldBeTrue();
        (await GetRepliesAsync(w.WorksheetId, root, Teacher(Owner))).Page!.CanReply.ShouldBeFalse();
    }

    // ---- Gövde temizliği ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("a\u0000b")]
    [InlineData("zil\u0007")]
    [InlineData("c1\u0085")]
    [InlineData("del\u007F")]
    [InlineData("vt\u000B")]
    public async Task Control_characters_and_broken_unicode_are_rejected(string body)
    {
        var w = await SeedAsync();
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), body), WorksheetCommentErrorCodes.BodyInvalidCharacters);
    }

    [Fact]
    public async Task Lone_surrogates_are_rejected()
    {
        // InlineData lone surrogate'i U+FFFD'ye çeviriyor (serileştirme) — string kodda kurulur.
        var w = await SeedAsync();
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "yalniz" + (char)0xD800 + "x"), WorksheetCommentErrorCodes.BodyInvalidCharacters);
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "ters" + (char)0xDC00), WorksheetCommentErrorCodes.BodyInvalidCharacters);
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "emoji 😀 ok"));
    }

    [Fact]
    public async Task Body_is_normalized_and_invisible_formatting_is_stripped()
    {
        var w = await SeedAsync();

        var created = await PostAsync(w.WorksheetId, Student(StudentAUser),
            "‮caf" + "é" + "‬\r\nsatır\t2​﻿⁦x⁩");

        Created(created);
        created.Comment!.Body.ShouldBe("café\nsatır\t2x");
        (await StoredCommentAsync(created.Comment.Id)).Body.ShouldBe("café\nsatır\t2x");
    }

    [Fact]
    public async Task Body_of_only_invisible_characters_is_required_and_length_is_checked_after_cleaning()
    {
        var w = await SeedAsync();

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "​‍﻿ ‮"), WorksheetCommentErrorCodes.BodyRequired);
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), new string('a', 2000) + new string('​', 50)));
        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), new string('a', 2001) + "​"), WorksheetCommentErrorCodes.BodyTooLong);
    }

    // ---- D9: eşit CreateTime -------------------------------------------------------------------------------

    [Fact]
    public async Task Roots_with_identical_create_time_are_neither_repeated_nor_skipped_across_pages()
    {
        var w = await SeedAsync();
        var ids = new List<int>();
        for (var i = 0; i < 5; i++)
            ids.Add(Created(await PostAsync(w.WorksheetId, Student(StudentBUser), $"kök {i}")));
        var sameInstant = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        await using (var ctx = _db.NewContext())
        {
            await ctx.WorksheetComments.Where(c => ids.Contains(c.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreateTime, sameInstant));
        }

        var seen = new List<int>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = (await GetAsync(w.WorksheetId, Student(StudentBUser), cursor: cursor, take: 2)).Page!;
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
            pages++;
        } while (cursor != null && pages < 10);

        seen.ShouldBe(Enumerable.Reverse(ids).ToList()); // Id DESC ikincil sıralama — tekrar/atlama yok
        pages.ShouldBe(3);
    }

    // ---- Bildirim outbox'ı (dilim 2) ------------------------------------------------------------------------

    private async Task<List<(string Type, string Json)>> OutboxAsync()
    {
        await using var ctx = _db.NewContext();
        var rows = await ctx.OutboxMessages.AsNoTracking().OrderBy(o => o.CreatedAt).ToListAsync();
        return rows.Select(r => (r.Type, r.Content)).ToList();
    }

    private static string CreatedType => OutboxEventRegistry.NameFor<WorksheetCommentCreatedEvent>();
    private static string RepliedType => OutboxEventRegistry.NameFor<WorksheetCommentRepliedEvent>();

    [Fact]
    public async Task Student_root_writes_one_created_event_for_the_pinned_teacher_in_the_same_transaction()
    {
        var w = await SeedAsync();

        // Yeniden denemeli execution strategy: transaction strategy dışında açılırsa burada patlar.
        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            var r = await NewService(ctx).CreateAsync(w.WorksheetId,
                new CreateWorksheetCommentDto { Body = "hocam bu nasıl?" }, Student(StudentAUser));
            r.Success.ShouldBeTrue(r.ErrorCode);
        }

        var rows = await OutboxAsync();
        rows.Count.ShouldBe(1);
        rows[0].Type.ShouldBe(CreatedType);
        var e = JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(rows[0].Json)!;
        await using var check = _db.NewContext();
        var root = await check.WorksheetComments.SingleAsync();
        e.EventId.ShouldNotBe(Guid.Empty);
        e.CommentId.ShouldBe(root.Id);
        e.RootCommentId.ShouldBe(root.Id);
        e.WorksheetId.ShouldBe(w.WorksheetId);
        e.QuestionId.ShouldBeNull();
        e.WorksheetTitle.ShouldBe("Kesirler");
        e.AuthorRole.ShouldBe("Student");
        e.AuthorDisplayName.ShouldBe("Ayşe Nur K.");
        e.RecipientUserId.ShouldBe(Assigner);
        e.RecipientKeycloakId.ShouldBe($"kc-{Assigner}"); // exam DB bilmiyor → auth-api best-effort
    }

    [Fact]
    public async Task Notification_payload_carries_no_body_and_no_personal_data()
    {
        var w = await SeedAsync();
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "GIZLI-GOVDE-METNI"));

        var json = (await OutboxAsync()).Single().Json;

        json.ShouldNotContain("GIZLI-GOVDE-METNI");
        json.ShouldNotContain("@mail.local");
        json.ShouldNotContain("Body", Case.Insensitive);
        json.ShouldNotContain("Email", Case.Insensitive);
        json.ShouldNotContain("Ayşe Nur kaya"); // tam ad değil, yalnızca "Ad S." biçimi
    }

    [Fact]
    public async Task Student_question_comment_carries_the_question_id()
    {
        var w = await SeedAsync();
        await StartAsync(w, w.StudentA, answerQ1: true);

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "bu soru?", questionId: w.Q1));

        var e = JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>((await OutboxAsync()).Single().Json)!;
        e.QuestionId.ShouldBe(w.Q1);
    }

    [Fact]
    public async Task Student_reply_to_another_students_root_notifies_the_teacher_and_the_root_author()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "A soruyor"));

        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "bence şöyle", parentId: rootA));

        var rows = (await OutboxAsync()).Skip(1).ToList(); // ilk satır A'nın kökü
        rows.Count.ShouldBe(2);
        var created = JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(rows.Single(r => r.Type == CreatedType).Json)!;
        var replied = JsonSerializer.Deserialize<WorksheetCommentRepliedEvent>(rows.Single(r => r.Type == RepliedType).Json)!;
        created.RecipientUserId.ShouldBe(Assigner);
        created.RootCommentId.ShouldBe(rootA);
        replied.RecipientUserId.ShouldBe(StudentAUser);
        replied.RecipientKeycloakId.ShouldBe($"kc-{StudentAUser}");
        replied.AuthorRole.ShouldBe("Student");
        replied.AuthorDisplayName.ShouldBe("Öğrenci X.");
        replied.RootCommentId.ShouldBe(rootA);
        created.EventId.ShouldNotBe(replied.EventId);
    }

    [Fact]
    public async Task Student_replying_to_own_root_notifies_only_the_teacher()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "soru"));

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "ek not", parentId: rootA));

        var rows = (await OutboxAsync()).Skip(1).ToList();
        rows.Count.ShouldBe(1);
        rows[0].Type.ShouldBe(CreatedType);
        JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(rows[0].Json)!.RecipientUserId.ShouldBe(Assigner);
    }

    [Fact]
    public async Task Teacher_reply_notifies_the_root_author_student_with_the_stored_sub()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "hocam?"));

        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap bu", parentId: rootA));

        var rows = (await OutboxAsync()).Skip(1).ToList();
        rows.Count.ShouldBe(1);
        rows[0].Type.ShouldBe(RepliedType);
        var e = JsonSerializer.Deserialize<WorksheetCommentRepliedEvent>(rows[0].Json)!;
        e.RecipientUserId.ShouldBe(StudentAUser);
        e.RecipientKeycloakId.ShouldBe($"kc-{StudentAUser}");
        e.AuthorRole.ShouldBe("Teacher");
        e.AuthorDisplayName.ShouldBeEmpty();
        rows[0].Json.ShouldNotContain("Ata Hoca");
        rows[0].Json.ShouldNotContain("cevap bu");
    }

    [Fact]
    public async Task Teacher_root_and_teacher_reply_on_a_teacher_root_write_no_event()
    {
        var w = await SeedAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "ek bilgi", parentId: announcement));

        (await OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Student_reply_on_a_teacher_root_notifies_the_root_author_teacher()
    {
        var w = await SeedAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));

        Created(await PostAsync(w.WorksheetId, Student(StudentBUser), "teşekkürler", parentId: announcement));

        var rows = await OutboxAsync();
        rows.Count.ShouldBe(1);
        rows[0].Type.ShouldBe(CreatedType);
        var e = JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(rows[0].Json)!;
        e.RecipientUserId.ShouldBe(Owner);
        e.RecipientKeycloakId.ShouldBe($"kc-{Owner}");
        e.RootCommentId.ShouldBe(announcement);
    }
    private void AuthApiDown() =>
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromException<IReadOnlyList<UserLookupResultDto>>(new HttpRequestException("down")));

    [Fact]
    public async Task Auth_api_is_not_called_when_every_recipient_sub_is_in_the_exam_db()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "hocam?"));
        _authApi.ClearReceivedCalls();

        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: rootA)); // öğrenci sub'ı kökte

        _authApi.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Sub_missing_in_the_exam_db_is_resolved_from_auth_api_in_one_batched_call()
    {
        var w = await SeedAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));
        _authApi.ClearReceivedCalls();

        // Alıcılar: Owner (kökte sub var) + Assigner (DB'de sub yok) → yalnız Assigner için TEK çağrı.
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "hocam?", parentId: announcement));

        var calls = _authApi.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "GetUsersByIdsAsync").ToList();
        calls.Count.ShouldBe(1);
        ((IEnumerable<int>)calls[0].GetArguments()[0]!).ShouldBe(new[] { Assigner });
        var created = (await OutboxAsync()).Select(r => JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(r.Json)!).ToList();
        created.Single(e => e.RecipientUserId == Assigner).RecipientKeycloakId.ShouldBe($"kc-{Assigner}");
        created.Single(e => e.RecipientUserId == Owner).RecipientKeycloakId.ShouldBe($"kc-{Owner}");
    }

    [Fact]
    public async Task Auth_api_failure_writes_the_event_with_a_blank_sub_and_the_comment_succeeds()
    {
        var w = await SeedAsync();
        AuthApiDown();

        var result = await PostAsync(w.WorksheetId, Student(StudentAUser), "hocam?");

        result.Success.ShouldBeTrue(result.ErrorCode);
        var e = JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>((await OutboxAsync()).Single().Json)!;
        e.RecipientUserId.ShouldBe(Assigner);
        e.RecipientKeycloakId.ShouldBeEmpty(); // consumer BadgeService verisinden çözer ya da retry/dead-letter
        await using var check = _db.NewContext();
        (await check.WorksheetComments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Teacher_sub_is_taken_from_the_teachers_earlier_comment_in_the_exam_db()
    {
        var w = await SeedAsync();
        AuthApiDown();
        var rootA = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "hocam?"));
        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: rootA));
        _authApi.ClearReceivedCalls();

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "bir de şu"));

        _authApi.ReceivedCalls().ShouldBeEmpty();
        var created = (await OutboxAsync()).Where(r => r.Type == CreatedType)
            .Select(r => JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(r.Json)!).ToList();
        created.Count.ShouldBe(2);
        created.ShouldContain(e => e.RecipientUserId == Assigner && e.RecipientKeycloakId == "");   // ilk kök: sub henüz bilinmiyordu
        created.ShouldContain(e => e.RecipientUserId == Assigner && e.RecipientKeycloakId == $"kc-{Assigner}");
    }

    [Fact]
    public async Task Recipient_without_a_stored_sub_and_a_down_auth_api_gets_an_event_with_the_user_id_only()
    {
        var w = await SeedAsync();
        var rootA = Created(await PostAsync(w.WorksheetId, new WorksheetCommentActor(
            StudentAUser, "", "Ayşe Nur kaya", WorksheetCommentActorKind.Student, false), "hocam?"));
        AuthApiDown();

        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "cevap", parentId: rootA));

        var e = JsonSerializer.Deserialize<WorksheetCommentRepliedEvent>((await OutboxAsync()).Single(r => r.Type == RepliedType).Json)!;
        e.RecipientUserId.ShouldBe(StudentAUser);
        e.RecipientKeycloakId.ShouldBeEmpty();
    }
    [Fact]
    public async Task Student_reply_on_a_teacher_root_also_notifies_the_students_responsible_teacher()
    {
        var w = await SeedAsync();
        var announcement = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "duyuru"));

        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "hocam?", parentId: announcement));

        var recipients = (await OutboxAsync()).Where(r => r.Type == CreatedType)
            .Select(r => JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(r.Json)!.RecipientUserId).ToList();
        recipients.OrderBy(x => x).ShouldBe(new[] { Owner, Assigner });
    }

    [Fact]
    public async Task Missing_responsible_teacher_skips_the_notification_and_logs_a_warning_with_ids()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.Worksheets.IgnoreQueryFilters().Where(x => x.Id == w.WorksheetId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreateUserId, (int?)null));
        }

        var logger = new ListLogger<WorksheetCommentService>();
        int commentId;
        await using (var ctx = _db.NewContext())
        {
            var r = await new WorksheetCommentService(ctx, new WorksheetResponsibleTeacherResolver(ctx), _authApi, logger)
                .CreateAsync(w.WorksheetId, new CreateWorksheetCommentDto { Body = "kimse yok mu?" }, Student(StudentBUser));
            commentId = Created(r);
        }

        (await OutboxAsync()).ShouldBeEmpty();
        var warning = logger.Messages.ShouldHaveSingleItem();
        warning.ShouldContain($"CommentId={commentId}");
        warning.ShouldContain($"WorksheetId={w.WorksheetId}");
    }

    [Fact]
    public async Task Transient_failure_on_the_outbox_insert_is_retried_with_one_comment_and_one_correct_outbox_row()
    {
        var w = await SeedAsync();
        var interceptor = new FailFirstCommandInterceptor("INSERT INTO \"OutboxMessages\"");

        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
        {
            var r = await NewService(ctx).CreateAsync(w.WorksheetId,
                new CreateWorksheetCommentDto { Body = "hocam?" }, Student(StudentAUser));
            r.Success.ShouldBeTrue(r.ErrorCode);
        }

        interceptor.Failures.ShouldBe(1);
        await using var check = _db.NewContext();
        var comment = await check.WorksheetComments.SingleAsync();
        var rows = await OutboxAsync();
        rows.Count.ShouldBe(1);
        var e = JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(rows[0].Json)!;
        e.CommentId.ShouldBe(comment.Id);
        e.RootCommentId.ShouldBe(comment.Id);
        e.RecipientUserId.ShouldBe(Assigner);
    }

    private sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task Rejected_comment_writes_no_outbox_row()
    {
        var w = await SeedAsync(commentsEnabled: false);

        ShouldFail(await PostAsync(w.WorksheetId, Student(StudentAUser), "hocam?"),
            WorksheetCommentErrorCodes.CommentsDisabled, forbidden: true);

        (await OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public void Both_comment_events_are_registered_for_the_publisher()
    {
        OutboxEventRegistry.Resolve(CreatedType).ShouldBe(typeof(WorksheetCommentCreatedEvent));
        OutboxEventRegistry.Resolve(RepliedType).ShouldBe(typeof(WorksheetCommentRepliedEvent));
    }

    // ---- issue #309: soru sırası (questionOrder) ------------------------------------------------------------

    [Fact]
    public async Task Question_thread_items_and_previews_carry_the_1_based_question_order()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "2. soru notu", questionId: w.Q2));
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "ek", questionId: w.Q2, parentId: root));
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "genel"));

        var page = (await GetAsync(w.WorksheetId, Teacher(Owner), questionId: w.Q2)).Page!;
        page.QuestionOrder.ShouldBe(2);
        (await GetAsync(w.WorksheetId, Teacher(Owner), questionId: w.Q1)).Page!.QuestionOrder.ShouldBe(1); // boş sayfada da
        (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.QuestionOrder.ShouldBeNull();
        var item = page.Items.Single();
        item.QuestionOrder.ShouldBe(2);
        item.Replies.Single().QuestionOrder.ShouldBe(2);

        var general = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.Items.Single();
        general.QuestionId.ShouldBeNull();
        general.QuestionOrder.ShouldBeNull();
    }

    [Fact]
    public async Task Question_order_is_the_position_not_the_raw_order_column()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // Boşluklu / ters sıra: Q2 önce gelir; silinmiş bir satır sayılmaz.
            (await ctx.TestQuestions.SingleAsync(x => x.Id == w.Wq1)).Order = 40;
            (await ctx.TestQuestions.SingleAsync(x => x.Id == w.Wq2)).Order = 7;
            var gone = new Question { Text = "Q-silinmiş" };
            ctx.Questions.Add(gone);
            await ctx.SaveChangesAsync();
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = w.WorksheetId, QuestionId = gone.Id, Order = 0, IsDeleted = true });
            await ctx.SaveChangesAsync();
        }

        var r1 = await PostAsync(w.WorksheetId, Teacher(Owner), "q1", questionId: w.Q1);
        var r2 = await PostAsync(w.WorksheetId, Teacher(Owner), "q2", questionId: w.Q2);

        r1.Comment!.QuestionOrder.ShouldBe(2);
        r2.Comment!.QuestionOrder.ShouldBe(1);
        (await PostAsync(w.WorksheetId, Teacher(Owner), "genel")).Comment!.QuestionOrder.ShouldBeNull();
        ShouldFail(await PostAsync(w.WorksheetId, Teacher(Owner), "yabancı", questionId: w.QForeign),
            WorksheetCommentErrorCodes.QuestionNotInWorksheet);
    }

    [Fact]
    public async Task Replies_endpoint_carries_the_question_order_of_the_root()
    {
        var w = await SeedAsync();
        var root = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "q1", questionId: w.Q1));
        Created(await PostAsync(w.WorksheetId, Teacher(Owner), "ek", questionId: w.Q1, parentId: root));

        var page = (await GetRepliesAsync(w.WorksheetId, root, Teacher(Owner))).Page!;
        page.QuestionOrder.ShouldBe(1);
        page.Items.Single().QuestionOrder.ShouldBe(1);

        var general = Created(await PostAsync(w.WorksheetId, Teacher(Owner), "genel"));
        (await GetRepliesAsync(w.WorksheetId, general, Teacher(Owner))).Page!.QuestionOrder.ShouldBeNull();
    }

    [Fact]
    public async Task Notification_events_carry_the_question_order()
    {
        var w = await SeedAsync();
        await StartAsync(w, w.StudentA, answerQ1: true);

        var root = Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "bu soru?", questionId: w.Q1));
        Created(await PostAsync(w.WorksheetId, Teacher(Assigner), "şöyle", questionId: w.Q1, parentId: root));
        Created(await PostAsync(w.WorksheetId, Student(StudentAUser), "genel"));

        var rows = await OutboxAsync();
        rows.Count.ShouldBe(3);
        JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(rows[0].Json)!.QuestionOrder.ShouldBe(1);
        var replied = JsonSerializer.Deserialize<WorksheetCommentRepliedEvent>(rows.Single(r => r.Type == RepliedType).Json)!;
        replied.QuestionOrder.ShouldBe(1);
        rows[1].Json.ShouldContain("\"QuestionOrder\":1");
        JsonSerializer.Deserialize<WorksheetCommentCreatedEvent>(rows[2].Json)!.QuestionOrder.ShouldBeNull();
    }

    // ---- issue #309: öğretmen için efektif öğrenci yorum durumu ---------------------------------------------

    /// <summary>
    /// Seed'in Assigner ataması (override=true) + Owner: aktif false, aktif null, süresi dolmuş true, silinmiş false;
    /// Assigner: aktif null.
    /// </summary>
    private async Task SeedSummaryAssignmentsAsync(World w)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(Owner);
        var now = DateTime.UtcNow;
        ctx.WorksheetAssignments.AddRange(
            new WorksheetAssignment { WorksheetId = w.WorksheetId, GradeId = w.GradeId, StartAt = now.AddDays(-1), CommentsEnabledOverride = false },
            new WorksheetAssignment { WorksheetId = w.WorksheetId, StudentId = w.StudentB, StartAt = now.AddDays(-1) },
            new WorksheetAssignment
            {
                WorksheetId = w.WorksheetId, StudentId = w.StudentB, StartAt = now.AddDays(-5), EndAt = now.AddDays(-1),
                CommentsEnabledOverride = true
            },
            new WorksheetAssignment
            {
                WorksheetId = w.WorksheetId, StudentId = w.StudentB, StartAt = now.AddDays(-1), CommentsEnabledOverride = false,
                IsDeleted = true
            },
            new WorksheetAssignment { WorksheetId = w.OtherWorksheetId, GradeId = w.GradeId, StartAt = now.AddDays(-1), CommentsEnabledOverride = false });
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(Assigner);
        ctx.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = w.WorksheetId, GradeId = w.GradeId, StartAt = now.AddDays(-1) });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Owner_sees_only_own_active_assignments_not_other_teachers()
    {
        var w = await SeedAsync(assignmentOverride: true);
        await SeedSummaryAssignmentsAsync(w);

        var summary = (await GetAsync(w.WorksheetId, Teacher(Owner))).Page!.StudentCommentsSummary!;

        summary.WorksheetDefault.ShouldBeTrue();
        summary.AssignmentOverrides.Enabled.ShouldBe(0);  // Assigner'ın true ataması sahibe sızmaz (security L1)
        summary.AssignmentOverrides.Disabled.ShouldBe(1); // Owner'ın aktif false'u (süresi dolmuş/silinmiş/başka ws sayılmaz)
    }

    [Fact]
    public async Task Admin_teacher_sees_all_active_assignments()
    {
        var w = await SeedAsync(assignmentOverride: true);
        await SeedSummaryAssignmentsAsync(w);

        var summary = (await GetAsync(w.WorksheetId, Teacher(Owner, isAdmin: true))).Page!.StudentCommentsSummary!;

        summary.AssignmentOverrides.Enabled.ShouldBe(1);
        summary.AssignmentOverrides.Disabled.ShouldBe(1);
    }

    [Fact]
    public async Task Assigner_sees_only_own_active_assignments()
    {
        var w = await SeedAsync(commentsEnabled: false, assignmentOverride: true);
        await SeedSummaryAssignmentsAsync(w);

        var summary = (await GetAsync(w.WorksheetId, Teacher(Assigner))).Page!.StudentCommentsSummary!;

        summary.WorksheetDefault.ShouldBeFalse();
        summary.AssignmentOverrides.Enabled.ShouldBe(1);
        summary.AssignmentOverrides.Disabled.ShouldBe(0);
    }

    [Fact]
    public async Task Admin_sees_all_active_assignments_and_student_gets_no_summary()
    {
        var w = await SeedAsync(assignmentOverride: false);
        await SeedSummaryAssignmentsAsync(w);

        var admin = (await GetAsync(w.WorksheetId, AdminReader)).Page!.StudentCommentsSummary!;
        admin.AssignmentOverrides.Enabled.ShouldBe(0);
        admin.AssignmentOverrides.Disabled.ShouldBe(2);

        var student = (await GetAsync(w.WorksheetId, Student(StudentAUser))).Page!;
        student.StudentCommentsSummary.ShouldBeNull();
    }
}
