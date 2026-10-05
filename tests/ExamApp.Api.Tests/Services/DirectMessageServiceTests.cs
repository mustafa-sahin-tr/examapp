using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.DirectMessages;
using ExamApp.Api.Services.DirectMessages;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #106 (dilim a): öğrenci ↔ öğretmen doğrudan mesajlaşma — CanMessage (A ∪ B), liste, tek konuşma, öğretmen cevabı,
/// ilişki bitişi, IDOR, engel + audit, şikayet, uzunluk/temizlik. SQLite (gerçek ilişkisel davranış); Postgres SQL çevirisi ve
/// unique yarışı <c>DirectMessagePostgresTests</c>'te.
/// </summary>
public class DirectMessageServiceTests : IDisposable
{
    // Öğretmenler (UserId)
    private const int TSame = 9001;          // okul S1, atamasız → B
    private const int TAssignOther = 9002;   // okul S2, öğrenciye atama → A
    private const int TBoth = 9003;          // okul S1 + atama → both
    private const int TIndepAssign = 9004;   // okulsuz bağımsız + atama → A
    private const int TIndepBooking = 9005;  // okulsuz bağımsız, yalnız onaylı booking → kapsam dışı
    private const int TUnapproved = 9006;    // okul S1, onaysız
    private const int TSuspended = 9007;     // okul S1, askıda
    private const int TOtherSchool = 9008;   // okul S2, ilişkisiz
    private const int TGrade = 9009;         // okul S2, sınıf hedefli atama (SchoolId=S1) → A
    private const int TExpired = 9010;       // okul S2, süresi dolmuş atama → kapsam dışı

    // Öğrenciler (UserId)
    private const int SA = 8001;     // okul S1, sınıf G
    private const int SNull = 8002;  // okulsuz, sınıf G
    private const int SB = 8003;     // okul S1, sınıf G

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private World _w = null!;

    private sealed record World(int S1, int S2, int GradeId, int WorksheetId, Dictionary<int, int> TeacherIds,
        Dictionary<int, int> StudentIds, Dictionary<int, int> AssignmentIds);

    public DirectMessageServiceTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto
            {
                Id = id,
                FullName = id switch
                {
                    TSame => "Selin Aydın",
                    TAssignOther => "Ata Hoca",
                    TBoth => "Işıl Çınar",
                    TIndepAssign => "Bora Bağımsız",
                    TGrade => "Gül Sınıf",
                    SA => "Ayşe Kaya",
                    _ => $"Kullanıcı {id}"
                },
                KeycloakId = $"kc-{id}"
            }).ToList());
    }

    public void Dispose() => _db.Dispose();

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var s1 = new School { Name = "S1" };
        var s2 = new School { Name = "S2" };
        var grade = new Grade { Name = "7" };
        ctx.AddRange(s1, s2, grade);
        await ctx.SaveChangesAsync();

        var now = DateTime.UtcNow;
        Teacher T(int userId, int? schoolId, bool approved = true, bool suspended = false) => new()
        {
            UserId = userId, SchoolId = schoolId,
            AccountApprovedAt = approved && !suspended ? now.AddDays(-10) : null,
            AccountSuspendedAt = suspended ? now.AddDays(-1) : null,
            IsIndependentTutor = schoolId == null
        };
        var teachers = new[]
        {
            T(TSame, s1.Id), T(TAssignOther, s2.Id), T(TBoth, s1.Id), T(TIndepAssign, null), T(TIndepBooking, null),
            T(TUnapproved, s1.Id, approved: false), T(TSuspended, s1.Id, suspended: true), T(TOtherSchool, s2.Id),
            T(TGrade, s2.Id), T(TExpired, s2.Id)
        };
        ctx.Teachers.AddRange(teachers);
        var students = new[]
        {
            new Student { UserId = SA, StudentNumber = "a", SchoolId = s1.Id, GradeId = grade.Id },
            new Student { UserId = SNull, StudentNumber = "n", SchoolId = null, GradeId = grade.Id },
            new Student { UserId = SB, StudentNumber = "b", SchoolId = s1.Id, GradeId = grade.Id }
        };
        ctx.Students.AddRange(students);
        var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();

        var teacherIds = teachers.ToDictionary(t => t.UserId, t => t.Id);
        var studentIds = students.ToDictionary(s => s.UserId, s => s.Id);
        var assignmentIds = new Dictionary<int, int>();

        async Task Assign(int teacherUserId, WorksheetAssignment a)
        {
            ctx.SetCurrentUser(teacherUserId); // CreateUserId = atayan öğretmen
            a.WorksheetId = ws.Id;
            ctx.WorksheetAssignments.Add(a);
            await ctx.SaveChangesAsync();
            assignmentIds[teacherUserId] = a.Id;
        }

        await Assign(TAssignOther, new WorksheetAssignment { StudentId = studentIds[SA], StartAt = now.AddDays(-1), EndAt = now.AddDays(7) });
        await Assign(TBoth, new WorksheetAssignment { StudentId = studentIds[SA], StartAt = now.AddDays(-1), EndAt = null });
        await Assign(TIndepAssign, new WorksheetAssignment { StudentId = studentIds[SA], StartAt = now.AddDays(-1), EndAt = now.AddDays(7) });
        ctx.SetCurrentUser(TIndepAssign);
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = ws.Id, StudentId = studentIds[SNull], StartAt = now.AddDays(-1), EndAt = now.AddDays(7)
        });
        await ctx.SaveChangesAsync();
        // Sınıf hedefli, okulu S1 olan atama: S1'deki SA'yı kapsar, okulsuz SNull'ı kapsamaz (platform geneli değil).
        await Assign(TGrade, new WorksheetAssignment { GradeId = grade.Id, SchoolId = s1.Id, StartAt = now.AddDays(-1), EndAt = now.AddDays(7) });
        await Assign(TExpired, new WorksheetAssignment { StudentId = studentIds[SA], StartAt = now.AddDays(-10), EndAt = now.AddHours(-1) });

        // Bağımsız öğretmenle onaylı booking — tek başına kapsam DEĞİL.
        BookingSeed.Add(ctx, teacherIds[TIndepBooking], studentIds[SNull], BookingStatus.Approved, 10);
        BookingSeed.Add(ctx, teacherIds[TIndepBooking], studentIds[SA], BookingStatus.Approved, 12);
        ctx.SetCurrentUser(0);
        await ctx.SaveChangesAsync();

        return _w = new World(s1.Id, s2.Id, grade.Id, ws.Id, teacherIds, studentIds, assignmentIds);
    }

    private DirectMessageQuotaOptions _quotas = new();

    private DirectMessageService NewService(AppDbContext ctx) =>
        new(ctx, NewPolicy(ctx), _authApi, quotas: Options.Create(_quotas));

    // Mevcut A ∪ B testleri bayrak AÇIK koşar; kapalı (varsayılan, #361) davranış ayrı testte.
    private bool _allowSameSchool = true;

    private DirectMessagePolicy NewPolicy(AppDbContext ctx) =>
        new(ctx, options: Options.Create(new DirectMessagingOptions { AllowSameSchoolMessaging = _allowSameSchool }));

    private static DirectMessageActor Student(int userId) => new(userId, $"kc-{userId}", DirectMessageActorKind.Student);
    private static DirectMessageActor Teacher(int userId) => new(userId, $"kc-{userId}", DirectMessageActorKind.Teacher);

    private async Task<T> Run<T>(Func<DirectMessageService, Task<T>> action)
    {
        await using var ctx = _db.NewContext();
        return await action(NewService(ctx));
    }

    private Task<MessageableTeacherPageResultDto> ListAsync(int studentUserId, string? search = null, int page = 1, int pageSize = 20)
        => Run(s => s.GetMessageableTeachersAsync(Student(studentUserId),
            new MessageableTeacherQueryDto { Search = search, Page = page, PageSize = pageSize }));

    private Task<SendDirectMessageResultDto> SendToTeacherAsync(int studentUserId, int teacherUserId, string body = "Merhaba hocam")
        => Run(s => s.SendToTeacherAsync(Student(studentUserId), _w.TeacherIds[teacherUserId], new SendDirectMessageDto { Body = body }));

    private Task<SendDirectMessageResultDto> SendAsync(DirectMessageActor actor, int conversationId, string body = "mesaj")
        => Run(s => s.SendToConversationAsync(actor, conversationId, new SendDirectMessageDto { Body = body }));

    private Task<ConversationMessagesResultDto> MessagesAsync(DirectMessageActor actor, int conversationId, int? beforeId = null, int take = 30)
        => Run(s => s.GetMessagesAsync(actor, conversationId, new DirectMessageHistoryQueryDto { BeforeId = beforeId, Take = take }));

    private Task<DirectMessageBlockResultDto> BlockAsync(DirectMessageActor actor, int conversationId, bool blocked = true)
        => Run(s => s.SetBlockedAsync(actor, conversationId, blocked));

    private Task<DirectMessageReportResultDto> ReportAsync(DirectMessageActor actor, int conversationId, int? messageId,
        string? reason = "abuse", string? note = null)
        => Run(s => s.ReportAsync(actor, conversationId, new ReportDirectMessageDto { MessageId = messageId, Reason = reason, Note = note }));

    private static int Created(SendDirectMessageResultDto r)
    {
        r.Success.ShouldBeTrue(r.ErrorCode);
        return r.ConversationId;
    }

    private static void ShouldBeForbidden(DirectMessageResponseDto r, string code)
    {
        r.Success.ShouldBeFalse();
        r.Forbidden.ShouldBeTrue();
        r.ErrorCode.ShouldBe(code);
    }

    private static void ShouldBeNotFound(DirectMessageResponseDto r)
    {
        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeTrue();
        r.Forbidden.ShouldBeFalse();
        r.ErrorCode.ShouldBe(DirectMessageErrorCodes.ConversationNotFound);
    }

    private Task<MarkDirectMessagesReadResultDto> MarkReadAsync(DirectMessageActor actor, int conversationId, int? upTo)
        => Run(s => s.MarkReadAsync(actor, conversationId, new MarkDirectMessagesReadDto { UpToMessageId = upTo }));

    private async Task<int> CanMessageCountAsync(int studentUserId, int teacherUserId)
    {
        await using var ctx = _db.NewContext();
        var policy = NewPolicy(ctx);
        var student = await policy.ResolveStudentAsync(studentUserId);
        return student != null && await policy.CanMessageAsync(student, teacherUserId) ? 1 : 0;
    }

    // ---- Liste: A ∪ B ---------------------------------------------------------------------------------------

    [Fact]
    public async Task List_is_the_union_of_school_and_assignment_teachers_without_duplicates_and_with_relation()
    {
        await SeedAsync();
        var page = (await ListAsync(SA)).Page!;

        var byId = page.Items.ToDictionary(i => i.TeacherId, i => i.Relation);
        byId.ShouldBe(new Dictionary<int, string>
        {
            [_w.TeacherIds[TSame]] = DirectMessageRelations.School,
            [_w.TeacherIds[TAssignOther]] = DirectMessageRelations.Assignment,
            [_w.TeacherIds[TBoth]] = DirectMessageRelations.Both,
            [_w.TeacherIds[TIndepAssign]] = DirectMessageRelations.Assignment,
            [_w.TeacherIds[TGrade]] = DirectMessageRelations.Assignment,
        }, ignoreOrder: true);
        page.TotalCount.ShouldBe(5);
        page.Items.Select(i => i.TeacherId).Distinct().Count().ShouldBe(page.Items.Count); // tekrarsız
        page.Items.Single(i => i.TeacherId == _w.TeacherIds[TBoth]).FullName.ShouldBe("Işıl Çınar");
        page.Items.ShouldAllBe(i => i.ConversationId == null);
    }

    [Fact]
    public async Task Same_school_teacher_without_assignment_is_listed_and_can_be_messaged()
    {
        await SeedAsync();
        (await ListAsync(SA)).Page!.Items.ShouldContain(i => i.TeacherId == _w.TeacherIds[TSame]);
        Created(await SendToTeacherAsync(SA, TSame));
    }

    [Fact]
    public async Task Other_school_teacher_who_assigned_the_student_is_listed_and_can_be_messaged()
    {
        await SeedAsync();
        (await ListAsync(SA)).Page!.Items.ShouldContain(i => i.TeacherId == _w.TeacherIds[TAssignOther]);
        Created(await SendToTeacherAsync(SA, TAssignOther));
    }

    [Fact]
    public async Task Schoolless_student_sees_only_assignment_teachers_null_school_never_matches()
    {
        await SeedAsync();
        var page = (await ListAsync(SNull)).Page!;

        // Okulsuz bağımsız öğretmenler (TIndepBooking) okulsuz öğrenciyle "aynı okul" SAYILMAZ; S1'e yapılmış sınıf ataması
        // (TGrade) okulsuz öğrenciyi kapsamaz.
        page.Items.Select(i => (i.TeacherId, i.Relation))
            .ShouldBe(new[] { (_w.TeacherIds[TIndepAssign], DirectMessageRelations.Assignment) });
        ShouldBeForbidden(await SendToTeacherAsync(SNull, TIndepBooking), DirectMessageErrorCodes.CannotMessageTeacher);
    }

    [Fact]
    public async Task Independent_teacher_is_in_scope_only_through_an_assignment_never_through_booking_alone()
    {
        await SeedAsync();
        (await ListAsync(SA)).Page!.Items.ShouldNotContain(i => i.TeacherId == _w.TeacherIds[TIndepBooking]);
        ShouldBeForbidden(await SendToTeacherAsync(SA, TIndepBooking), DirectMessageErrorCodes.CannotMessageTeacher);
        Created(await SendToTeacherAsync(SA, TIndepAssign));
    }

    [Fact]
    public async Task Unrelated_teacher_gets_neutral_403_and_unknown_teacher_id_too()
    {
        await SeedAsync();
        ShouldBeForbidden(await SendToTeacherAsync(SA, TOtherSchool), DirectMessageErrorCodes.CannotMessageTeacher);
        ShouldBeForbidden(await SendToTeacherAsync(SA, TExpired), DirectMessageErrorCodes.CannotMessageTeacher);
        ShouldBeForbidden(await Run(s => s.SendToTeacherAsync(Student(SA), 999_999, new SendDirectMessageDto { Body = "x" })),
            DirectMessageErrorCodes.CannotMessageTeacher);

        await using var ctx = _db.NewContext();
        (await ctx.Conversations.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Unapproved_and_suspended_teachers_are_not_listed_and_cannot_be_messaged()
    {
        await SeedAsync();
        var ids = (await ListAsync(SA)).Page!.Items.Select(i => i.TeacherId).ToList();
        ids.ShouldNotContain(_w.TeacherIds[TUnapproved]);
        ids.ShouldNotContain(_w.TeacherIds[TSuspended]);
        ShouldBeForbidden(await SendToTeacherAsync(SA, TUnapproved), DirectMessageErrorCodes.CannotMessageTeacher);
        ShouldBeForbidden(await SendToTeacherAsync(SA, TSuspended), DirectMessageErrorCodes.CannotMessageTeacher);
    }

    [Fact]
    public async Task Teacher_suspended_after_conversation_stops_student_messages_and_leaves_the_list()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame));
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.UserId == TSame).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.AccountSuspendedAt, DateTime.UtcNow).SetProperty(t => t.AccountApprovedAt, (DateTime?)null));

        ShouldBeForbidden(await SendAsync(Student(SA), conv), DirectMessageErrorCodes.CannotMessageTeacher);
        (await ListAsync(SA)).Page!.Items.ShouldNotContain(i => i.TeacherId == _w.TeacherIds[TSame]);
        (await MessagesAsync(Student(SA), conv)).Success.ShouldBeTrue(); // geçmiş okunur
    }

    [Fact]
    public async Task Search_filters_by_name_case_insensitively_in_turkish_and_pages()
    {
        await SeedAsync();
        var r = (await ListAsync(SA, search: "IŞIL")).Page!;
        r.Items.Select(i => i.TeacherId).ShouldBe(new[] { _w.TeacherIds[TBoth] });
        r.TotalCount.ShouldBe(1);

        var p1 = (await ListAsync(SA, pageSize: 2)).Page!;
        var p2 = (await ListAsync(SA, page: 2, pageSize: 2)).Page!;
        var p3 = (await ListAsync(SA, page: 3, pageSize: 2)).Page!;
        p1.TotalCount.ShouldBe(5);
        p1.Items.Concat(p2.Items).Concat(p3.Items).Select(i => i.TeacherId).Distinct().Count().ShouldBe(5);
        // Atamalı öğretmenler önce.
        p1.Items.ShouldAllBe(i => i.Relation != DirectMessageRelations.School);
    }

    [Fact]
    public async Task List_and_send_use_the_same_rule()
    {
        await SeedAsync();
        var listed = (await ListAsync(SA)).Page!.Items.Select(i => i.TeacherId).ToHashSet();
        foreach (var (userId, teacherId) in _w.TeacherIds)
            (await CanMessageCountAsync(SA, userId) == 1).ShouldBe(listed.Contains(teacherId), $"teacher {userId}");
    }

    // ---- Konuşma ------------------------------------------------------------------------------------------

    [Fact]
    public async Task First_message_creates_the_conversation_and_later_messages_reuse_it()
    {
        await SeedAsync();
        var first = await SendToTeacherAsync(SA, TSame, "ilk");
        first.ConversationCreated.ShouldBeTrue();
        first.DirectMessage!.IsMine.ShouldBeTrue();
        first.DirectMessage.SenderRole.ShouldBe("Student");

        var second = await SendToTeacherAsync(SA, TSame, "ikinci");
        second.ConversationCreated.ShouldBeFalse();
        second.ConversationId.ShouldBe(first.ConversationId);
        Created(await SendAsync(Student(SA), first.ConversationId, "üçüncü")).ShouldBe(first.ConversationId);

        await using var ctx = _db.NewContext();
        (await ctx.Conversations.CountAsync()).ShouldBe(1);
        (await ctx.DirectMessages.CountAsync()).ShouldBe(3);
        (await ListAsync(SA)).Page!.Items.Single(i => i.TeacherId == _w.TeacherIds[TSame]).ConversationId.ShouldBe(first.ConversationId);
    }

    [Fact]
    public async Task Teacher_cannot_start_a_conversation_but_can_reply()
    {
        await SeedAsync();
        ShouldBeForbidden(await Run(s => s.SendToTeacherAsync(Teacher(TBoth), _w.TeacherIds[TSame], new SendDirectMessageDto { Body = "x" })),
            DirectMessageErrorCodes.CannotMessageTeacher);

        var conv = Created(await SendToTeacherAsync(SA, TSame));
        var reply = await SendAsync(Teacher(TSame), conv, "cevap");
        reply.Success.ShouldBeTrue(reply.ErrorCode);
        reply.DirectMessage!.SenderRole.ShouldBe("Teacher");

        var thread = (await MessagesAsync(Student(SA), conv)).Conversation!;
        thread.Items.Select(m => (m.Body, m.IsMine)).ShouldBe(new[] { ("Merhaba hocam", true), ("cevap", false) });
        thread.CounterpartName.ShouldBe("Selin Aydın");
        thread.TeacherId.ShouldBe(_w.TeacherIds[TSame]);
        thread.CanSend.ShouldBeTrue();
        thread.IsBlocked.ShouldBeNull();
    }

    [Fact]
    public async Task Assignment_ending_keeps_history_readable_but_blocks_new_messages_from_both_sides()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TAssignOther));
        Created(await SendAsync(Teacher(TAssignOther), conv, "cevap"));

        await using (var ctx = _db.NewContext())
            await ctx.WorksheetAssignments.Where(a => a.Id == _w.AssignmentIds[TAssignOther])
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.EndAt, DateTime.UtcNow.AddMinutes(-1)));

        var studentView = (await MessagesAsync(Student(SA), conv)).Conversation!;
        studentView.Items.Count.ShouldBe(2);
        studentView.CanSend.ShouldBeFalse();
        var teacherView = (await MessagesAsync(Teacher(TAssignOther), conv)).Conversation!;
        teacherView.Items.Count.ShouldBe(2);
        teacherView.CanSend.ShouldBeFalse();

        ShouldBeForbidden(await SendAsync(Student(SA), conv), DirectMessageErrorCodes.CannotMessageTeacher);
        ShouldBeForbidden(await SendToTeacherAsync(SA, TAssignOther), DirectMessageErrorCodes.CannotMessageTeacher);
        ShouldBeForbidden(await SendAsync(Teacher(TAssignOther), conv), DirectMessageErrorCodes.RelationshipEnded);
    }

    [Fact]
    public async Task Student_changing_school_ends_a_school_only_relation()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame));
        await using (var ctx = _db.NewContext())
            await ctx.Students.Where(s => s.UserId == SA).ExecuteUpdateAsync(s => s.SetProperty(x => x.SchoolId, (int?)_w.S2));

        ShouldBeForbidden(await SendAsync(Student(SA), conv), DirectMessageErrorCodes.CannotMessageTeacher);
        ShouldBeForbidden(await SendAsync(Teacher(TSame), conv), DirectMessageErrorCodes.RelationshipEnded);
        (await MessagesAsync(Teacher(TSame), conv)).Conversation!.Items.Count.ShouldBe(1);
        // Yeni okulda (S2) okul ilişkisi doğar: S2 öğretmeni artık listede.
        (await ListAsync(SA)).Page!.Items.ShouldContain(i => i.TeacherId == _w.TeacherIds[TOtherSchool] && i.Relation == DirectMessageRelations.School);
    }

    // ---- IDOR ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Student_cannot_read_write_or_report_another_students_conversation()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame));

        // security D2: taraf olmayana 404 ConversationNotFound (var/yok oracle'ı yok).
        ShouldBeNotFound(await MessagesAsync(Student(SB), conv));
        ShouldBeNotFound(await SendAsync(Student(SB), conv));
        ShouldBeNotFound(await ReportAsync(Student(SB), conv, null));
        ShouldBeNotFound(await MarkReadAsync(Student(SB), conv, int.MaxValue));
        ShouldBeNotFound(await BlockAsync(Student(SB), conv));
        (await Run(s => s.GetStudentConversationsAsync(Student(SB), new DirectMessagePageQueryDto()))).Page!.Items.ShouldBeEmpty();
        (await MessagesAsync(Student(SA), 999_999)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Teacher_cannot_access_another_teachers_conversation_even_from_the_same_or_another_school()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame));

        foreach (var other in new[] { TBoth, TOtherSchool })
        {
            ShouldBeNotFound(await MessagesAsync(Teacher(other), conv));
            ShouldBeNotFound(await SendAsync(Teacher(other), conv));
            ShouldBeNotFound(await BlockAsync(Teacher(other), conv));
            ShouldBeNotFound(await BlockAsync(Teacher(other), conv, blocked: false));
            ShouldBeNotFound(await ReportAsync(Teacher(other), conv, null));
            ShouldBeNotFound(await MarkReadAsync(Teacher(other), conv, int.MaxValue));
            (await Run(s => s.GetTeacherInboxAsync(Teacher(other), new TeacherInboxQueryDto()))).Page!.Items.ShouldBeEmpty();
        }
    }

    // ---- Gelen kutusu / okundu ----------------------------------------------------------------------------

    [Fact]
    public async Task Inbox_unread_filter_and_reading_marks_messages_read()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame, "bir"));
        Created(await SendAsync(Student(SA), conv, "iki"));
        Created(await SendToTeacherAsync(SB, TSame, "başka öğrenci"));

        var unread = (await Run(s => s.GetTeacherInboxAsync(Teacher(TSame), new TeacherInboxQueryDto { Filter = "unread" }))).Page!;
        unread.TotalCount.ShouldBe(2);
        var row = unread.Items.Single(i => i.ConversationId == conv);
        row.UnreadCount.ShouldBe(2);
        row.CounterpartName.ShouldBe("Ayşe Kaya");
        row.StudentId.ShouldBe(_w.StudentIds[SA]);
        row.LastMessagePreview.ShouldBe("iki");
        row.LastMessageIsMine.ShouldBeFalse();
        row.IsBlocked.ShouldBe(false);

        // GET yan etkisiz (code review W2): okundu işaretlemez.
        var thread = (await MessagesAsync(Teacher(TSame), conv)).Conversation!;
        (await Run(s => s.GetTeacherInboxAsync(Teacher(TSame), new TeacherInboxQueryDto { Filter = "unread" }))).Page!
            .Items.Select(i => i.ConversationId).ShouldContain(conv);

        var marked = await MarkReadAsync(Teacher(TSame), conv, thread.Items.Max(m => m.Id));
        marked.Success.ShouldBeTrue(marked.ErrorCode);
        marked.MarkedCount.ShouldBe(2);
        (await MarkReadAsync(Teacher(TSame), conv, thread.Items.Max(m => m.Id))).MarkedCount.ShouldBe(0); // idempotent
        var after = (await Run(s => s.GetTeacherInboxAsync(Teacher(TSame), new TeacherInboxQueryDto { Filter = "unread" }))).Page!;
        after.Items.Select(i => i.ConversationId).ShouldNotContain(conv);

        // Öğrencinin kendi mesajları onun için okunmamış sayılmaz; öğretmen cevabı öğrencide okunmamış.
        Created(await SendAsync(Teacher(TSame), conv, "cevap"));
        var studentList = (await Run(s => s.GetStudentConversationsAsync(Student(SA), new DirectMessagePageQueryDto()))).Page!;
        studentList.Items.Single().UnreadCount.ShouldBe(1);
        studentList.Items.Single().IsBlocked.ShouldBeNull();
        studentList.Items.Single().TeacherId.ShouldBe(_w.TeacherIds[TSame]);

        (await Run(s => s.GetTeacherInboxAsync(Teacher(TSame), new TeacherInboxQueryDto { Filter = "nope" }))).ErrorCode
            .ShouldBe(DirectMessageErrorCodes.InvalidFilter);
    }

    [Fact]
    public async Task Message_history_pages_backwards_with_before_id()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame, "m1"));
        for (var i = 2; i <= 5; i++)
            Created(await SendAsync(Student(SA), conv, $"m{i}"));

        var p1 = (await MessagesAsync(Teacher(TSame), conv, take: 2)).Conversation!;
        p1.Items.Select(m => m.Body).ShouldBe(new[] { "m4", "m5" });
        p1.HasMore.ShouldBeTrue();
        var p2 = (await MessagesAsync(Teacher(TSame), conv, beforeId: p1.NextBeforeId, take: 2)).Conversation!;
        p2.Items.Select(m => m.Body).ShouldBe(new[] { "m2", "m3" });
        var p3 = (await MessagesAsync(Teacher(TSame), conv, beforeId: p2.NextBeforeId, take: 2)).Conversation!;
        p3.Items.Select(m => m.Body).ShouldBe(new[] { "m1" });
        p3.HasMore.ShouldBeFalse();
        p3.NextBeforeId.ShouldBeNull();
    }

    // ---- Engel ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Block_hides_teacher_and_rejects_student_neutrally_while_teacher_can_still_reply_and_unblock()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame));

        var blocked = await BlockAsync(Teacher(TSame), conv);
        blocked.Success.ShouldBeTrue(blocked.ErrorCode);
        blocked.Changed.ShouldBeTrue();
        blocked.IsBlocked.ShouldBeTrue();

        // Öğrenci: nötr 403 (ilişkisiz öğretmenle AYNI kod) + öğretmen listede yok.
        ShouldBeForbidden(await SendAsync(Student(SA), conv), DirectMessageErrorCodes.CannotMessageTeacher);
        ShouldBeForbidden(await SendToTeacherAsync(SA, TSame), DirectMessageErrorCodes.CannotMessageTeacher);
        (await ListAsync(SA)).Page!.Items.ShouldNotContain(i => i.TeacherId == _w.TeacherIds[TSame]);
        var studentView = (await MessagesAsync(Student(SA), conv)).Conversation!;
        studentView.CanSend.ShouldBeFalse();
        studentView.IsBlocked.ShouldBeNull(); // engel sızdırılmaz

        // Öğretmen: cevap serbest, konuşma "blocked" filtresinde.
        Created(await SendAsync(Teacher(TSame), conv, "cevap"));
        var teacherView = (await MessagesAsync(Teacher(TSame), conv)).Conversation!;
        teacherView.IsBlocked.ShouldBe(true);
        teacherView.CanSend.ShouldBeTrue();
        var blockedInbox = (await Run(s => s.GetTeacherInboxAsync(Teacher(TSame), new TeacherInboxQueryDto { Filter = "blocked" }))).Page!;
        blockedInbox.Items.Single().ConversationId.ShouldBe(conv);
        blockedInbox.Items.Single().IsBlocked.ShouldBe(true);

        // İdempotent: ikinci engel değişiklik/audit üretmez.
        (await BlockAsync(Teacher(TSame), conv)).Changed.ShouldBeFalse();

        var unblocked = await BlockAsync(Teacher(TSame), conv, blocked: false);
        unblocked.Changed.ShouldBeTrue();
        unblocked.IsBlocked.ShouldBeFalse();
        (await BlockAsync(Teacher(TSame), conv, blocked: false)).Changed.ShouldBeFalse();
        Created(await SendAsync(Student(SA), conv, "tekrar"));

        await using var ctx = _db.NewContext();
        var audit = await ctx.AdminUserActionLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        audit.Select(a => (a.Action, a.TargetType, a.TargetId, a.ActorKeycloakId)).ShouldBe(new[]
        {
            (AdminUserAction.DirectMessageStudentBlocked, AdminUserTargetType.Conversation, conv, $"kc-{TSame}"),
            (AdminUserAction.DirectMessageStudentUnblocked, AdminUserTargetType.Conversation, conv, $"kc-{TSame}")
        });
        var row = await ctx.DirectMessageBlocks.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        (row.TeacherUserId, row.StudentUserId, row.CreateUserId, row.IsDeleted, row.DeleteUserId).ShouldBe((TSame, SA, (int?)TSame, true, (int?)TSame));
    }

    [Fact]
    public async Task Student_cannot_block()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame));
        ShouldBeForbidden(await BlockAsync(Student(SA), conv), DirectMessageErrorCodes.BlockTeacherOnly);
        (await BlockAsync(Teacher(TSame), 999_999)).NotFound.ShouldBeTrue();
    }

    // ---- Şikayet ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Report_is_idempotent_per_message_and_per_conversation_and_listed_for_admin()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame, "soru"));
        var reply = await SendAsync(Teacher(TSame), conv, "uygunsuz cevap");
        var replyId = reply.DirectMessage!.Id;

        var first = await ReportAsync(Student(SA), conv, replyId, "abuse", "  rahatsız edici  ");
        first.Success.ShouldBeTrue(first.ErrorCode);
        first.AlreadyReported.ShouldBeFalse();
        var again = await ReportAsync(Student(SA), conv, replyId, "spam");
        again.AlreadyReported.ShouldBeTrue();
        again.ReportId.ShouldBe(first.ReportId);

        var convReport = await ReportAsync(Teacher(TSame), conv, null, "spam");
        convReport.AlreadyReported.ShouldBeFalse();
        (await ReportAsync(Teacher(TSame), conv, null, "other")).ReportId.ShouldBe(convReport.ReportId);

        await using (var ctx = _db.NewContext())
        {
            var stored = await ctx.DirectMessageReports.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
            stored.Count.ShouldBe(2);
            stored[0].Status.ShouldBe(DirectMessageReportStatus.Open);
            stored[0].Note.ShouldBe("rahatsız edici");
            stored[0].ReporterRole.ShouldBe(DirectMessageSenderRole.Student);
        }

        var admin = (await Run(s => s.GetOpenReportsAsync(new DirectMessagePageQueryDto()))).Page!;
        admin.TotalCount.ShouldBe(2);
        var messageReport = admin.Items.Single(i => i.MessageId == replyId);
        (messageReport.Reason, messageReport.MessageBody, messageReport.MessageSenderRole, messageReport.Status)
            .ShouldBe(("abuse", "uygunsuz cevap", "Teacher", "Open"));
        (messageReport.StudentName, messageReport.TeacherName).ShouldBe(("Ayşe Kaya", "Selin Aydın"));
        var conversationReport = admin.Items.Single(i => i.MessageId == null);
        conversationReport.MessageBody.ShouldBeNull();
        conversationReport.ReporterRole.ShouldBe("Teacher");

        // Reviewed şikayet listede yok.
        await using (var ctx = _db.NewContext())
            await ctx.DirectMessageReports.Where(r => r.Id == convReport.ReportId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, DirectMessageReportStatus.Reviewed));
        (await Run(s => s.GetOpenReportsAsync(new DirectMessagePageQueryDto()))).Page!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Report_validation_own_message_unknown_message_reason_and_note()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame));
        var own = (await SendAsync(Student(SA), conv, "kendi")).DirectMessage!.Id;
        var otherConv = Created(await SendToTeacherAsync(SB, TSame));
        var otherMessage = (await SendAsync(Teacher(TSame), otherConv, "başka")).DirectMessage!.Id;

        ShouldBeForbidden(await ReportAsync(Student(SA), conv, own), DirectMessageErrorCodes.CannotReportOwnMessage);
        var foreign = await ReportAsync(Student(SA), conv, otherMessage); // başka konuşmanın mesajı
        foreign.NotFound.ShouldBeTrue();
        foreign.ErrorCode.ShouldBe(DirectMessageErrorCodes.MessageNotFound);
        (await ReportAsync(Teacher(TSame), conv, null, reason: null)).ErrorCode.ShouldBe(DirectMessageErrorCodes.InvalidReportReason);
        (await ReportAsync(Teacher(TSame), conv, null, reason: "kötü")).ErrorCode.ShouldBe(DirectMessageErrorCodes.InvalidReportReason);
        (await ReportAsync(Teacher(TSame), conv, null, note: new string('x', 501))).ErrorCode.ShouldBe(DirectMessageErrorCodes.ReportNoteTooLong);
        (await ReportAsync(Teacher(TSame), conv, null, note: "a\u0007b")).ErrorCode.ShouldBe(DirectMessageErrorCodes.ReportNoteInvalidCharacters);
        (await ReportAsync(Teacher(TSame), conv, null, note: new string('x', 500))).Success.ShouldBeTrue();
    }

    // ---- Gövde ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Body_is_plain_text_trimmed_length_limited_and_control_characters_rejected()
    {
        await SeedAsync();
        (await SendToTeacherAsync(SA, TSame, new string('a', 2001))).ErrorCode.ShouldBe(DirectMessageErrorCodes.BodyTooLong);
        (await SendToTeacherAsync(SA, TSame, "   ")).ErrorCode.ShouldBe(DirectMessageErrorCodes.BodyRequired);
        (await SendToTeacherAsync(SA, TSame, "a\u0000b")).ErrorCode.ShouldBe(DirectMessageErrorCodes.BodyInvalidCharacters);
        await using (var ctx = _db.NewContext())
            (await ctx.Conversations.CountAsync()).ShouldBe(0); // geçersiz ilk mesaj konuşma açmaz

        var ok = await SendToTeacherAsync(SA, TSame, "  <b>merhaba</b>‮  ");
        ok.DirectMessage!.Body.ShouldBe("<b>merhaba</b>"); // HTML olduğu gibi saklanır (render edilmez), bidi ayıklanır
        Created(await SendToTeacherAsync(SA, TSame, new string('a', 2000)));
    }

    // ---- Code/security review düzeltmeleri ---------------------------------------------------------------------

    [Fact]
    public async Task Platform_wide_grade_assignment_does_not_create_an_assignment_relation()
    {
        await SeedAsync();
        // TOtherSchool (S2) platform geneli bir sınıf ataması yapar (#277 migration'ı eski okulsuz sınıf atamalarını böyle
        // işaretledi): öğrenci testi görür ama atayan öğretmen tüm okulların öğrencilerine açılmaz.
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TOtherSchool);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = _w.WorksheetId, GradeId = _w.GradeId, IsPlatformWide = true,
                StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddDays(3)
            });
            await ctx.SaveChangesAsync();
        }

        (await ListAsync(SA)).Page!.Items.ShouldNotContain(i => i.TeacherId == _w.TeacherIds[TOtherSchool]);
        (await ListAsync(SNull)).Page!.Items.ShouldNotContain(i => i.TeacherId == _w.TeacherIds[TOtherSchool]);
        ShouldBeForbidden(await SendToTeacherAsync(SA, TOtherSchool), DirectMessageErrorCodes.CannotMessageTeacher);
    }

    [Fact]
    public async Task Mark_read_only_touches_counterpart_messages_up_to_the_given_id_and_loading_older_pages_marks_nothing()
    {
        await SeedAsync();
        var conv = Created(await SendToTeacherAsync(SA, TSame, "m1"));
        for (var i = 2; i <= 4; i++)
            Created(await SendAsync(Student(SA), conv, $"m{i}"));
        var reply = (await SendAsync(Teacher(TSame), conv, "cevap")).DirectMessage!.Id;

        // Eski sayfa yüklemek (beforeId) hiçbir şeyi okundu yapmaz.
        var older = (await MessagesAsync(Teacher(TSame), conv, beforeId: reply, take: 2)).Conversation!;
        older.Items.Select(m => m.Body).ShouldBe(new[] { "m3", "m4" });
        await using (var ctx = _db.NewContext())
            (await ctx.DirectMessages.CountAsync(m => m.ReadAt != null)).ShouldBe(0);

        // upTo = m2 → yalnız m1, m2; öğretmenin kendi cevabı (karşı tarafın değil) hiç dokunulmaz.
        var m2 = older.Items[0].Id - 1;
        (await MarkReadAsync(Teacher(TSame), conv, m2)).MarkedCount.ShouldBe(2);
        (await MarkReadAsync(Teacher(TSame), conv, int.MaxValue)).MarkedCount.ShouldBe(2); // m3, m4

        await using (var ctx = _db.NewContext())
        {
            var rows = await ctx.DirectMessages.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
            rows.Where(m => m.SenderUserId == SA).ShouldAllBe(m => m.ReadAt != null && m.UpdateUserId == TSame && m.UpdateTime != null);
            rows.Single(m => m.Id == reply).ReadAt.ShouldBeNull();
        }

        // Öğrenci tarafı: öğretmen cevabını okur.
        (await MarkReadAsync(Student(SA), conv, reply)).MarkedCount.ShouldBe(1);
        (await MarkReadAsync(Student(SA), conv, 0)).ErrorCode.ShouldBe(DirectMessageErrorCodes.InvalidUpToMessageId);
        (await MarkReadAsync(Student(SA), conv, null)).ErrorCode.ShouldBe(DirectMessageErrorCodes.InvalidUpToMessageId);
    }

    [Fact]
    public async Task New_conversation_quota_per_day_returns_rate_limited_with_retry_after()
    {
        await SeedAsync();
        _quotas = new DirectMessageQuotaOptions { NewConversationsPerDay = 2 };
        Created(await SendToTeacherAsync(SA, TSame));
        Created(await SendToTeacherAsync(SA, TBoth));

        var third = await SendToTeacherAsync(SA, TAssignOther);
        third.Success.ShouldBeFalse();
        third.RateLimited.ShouldBeTrue();
        third.ErrorCode.ShouldBe(DirectMessageErrorCodes.RateLimited);
        third.RetryAfterSeconds!.Value.ShouldBeInRange(1, 86_400);

        // Mevcut konuşmalara yazmak kotayı etkilemez; başka öğrenci etkilenmez.
        Created(await SendToTeacherAsync(SA, TSame, "devam"));
        Created(await SendToTeacherAsync(SB, TSame));
        await using var ctx = _db.NewContext();
        (await ctx.Conversations.CountAsync(c => c.StudentUserId == SA)).ShouldBe(2);
    }

    [Fact]
    public async Task Messages_per_conversation_per_hour_quota_applies_per_sender()
    {
        await SeedAsync();
        _quotas = new DirectMessageQuotaOptions { MessagesPerConversationPerHour = 3 };
        var conv = Created(await SendToTeacherAsync(SA, TSame, "1"));
        Created(await SendAsync(Student(SA), conv, "2"));
        Created(await SendAsync(Student(SA), conv, "3"));

        var limited = await SendAsync(Student(SA), conv, "4");
        limited.RateLimited.ShouldBeTrue();
        limited.ErrorCode.ShouldBe(DirectMessageErrorCodes.RateLimited);
        (await SendToTeacherAsync(SA, TSame, "4")).RateLimited.ShouldBeTrue();
        Created(await SendAsync(Teacher(TSame), conv, "öğretmen ayrı sayılır"));

        // Pencere dışına düşen mesajlar sayılmaz.
        await using (var ctx = _db.NewContext())
            await ctx.DirectMessages.Where(m => m.SenderUserId == SA)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.CreateTime, DateTime.UtcNow.AddHours(-2)));
        Created(await SendAsync(Student(SA), conv, "5"));
    }

    [Fact]
    public async Task Search_requires_two_characters_and_whitespace_means_no_search()
    {
        await SeedAsync();
        var shortTerm = await ListAsync(SA, search: " a ");
        shortTerm.Success.ShouldBeFalse();
        shortTerm.ErrorCode.ShouldBe(DirectMessageErrorCodes.SearchTooShort);
        (await ListAsync(SA, search: "   ")).Page!.TotalCount.ShouldBe(5);
        (await ListAsync(SA, search: "at")).Page!.Items.Select(i => i.TeacherId).ShouldBe(new[] { _w.TeacherIds[TAssignOther] });
    }

    [Fact]
    public async Task Search_returns_503_when_names_cannot_be_resolved_but_plain_list_falls_back()
    {
        await SeedAsync();
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IReadOnlyList<UserLookupResultDto>>(new HttpRequestException("down")));

        var search = await ListAsync(SA, search: "hoca");
        search.ServiceUnavailable.ShouldBeTrue();
        search.ErrorCode.ShouldBe(DirectMessageErrorCodes.NameLookupUnavailable);

        var plain = (await ListAsync(SA)).Page!;
        plain.TotalCount.ShouldBe(5);
        plain.Items.ShouldAllBe(i => i.FullName.Length > 0); // yedek ad

        // Fail-soft istemci (boş liste) de arama için "kullanılamıyor" sayılır.
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserLookupResultDto>());
        (await ListAsync(SA, search: "hoca")).ErrorCode.ShouldBe(DirectMessageErrorCodes.NameLookupUnavailable);
    }

    [Fact]
    public async Task Search_candidates_are_capped_and_truncated_is_reported()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            for (var i = 0; i < DirectMessageLimits.MaxSearchCandidates; i++)
                ctx.Teachers.Add(new Teacher { UserId = 20_000 + i, SchoolId = _w.S1, AccountApprovedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        var plain = (await ListAsync(SA)).Page!;
        plain.TotalCount.ShouldBe(DirectMessageLimits.MaxSearchCandidates + 5);
        plain.Truncated.ShouldBeFalse();

        var search = (await ListAsync(SA, search: "kullanıcı")).Page!;
        search.Truncated.ShouldBeTrue();
        search.TotalCount.ShouldBeLessThanOrEqualTo(DirectMessageLimits.MaxSearchCandidates);
        (await ListAsync(SA, search: "ışıl")).Page!.Truncated.ShouldBeTrue(); // atamalılar önce → yine bulunur
    }

    [Fact]
    public async Task Page_is_clamped_to_ten_thousand()
    {
        await SeedAsync();
        var page = (await ListAsync(SA, page: int.MaxValue)).Page!;
        page.Page.ShouldBe(DirectMessageLimits.MaxPage);
        page.Items.ShouldBeEmpty();
        (await ListAsync(SA, page: -5)).Page!.Page.ShouldBe(1);
        (await Run(s => s.GetOpenReportsAsync(new DirectMessagePageQueryDto { Page = 99_999_999 }))).Page!.Page.ShouldBe(DirectMessageLimits.MaxPage);
    }

    [Fact]
    public async Task Body_strips_every_format_character_and_maps_line_separators_to_newline()
    {
        await SeedAsync();
        var sep = (char)0x2028;
        var para = (char)0x2029;
        var softHyphen = (char)0x00AD;
        var tag = char.ConvertFromUtf32(0xE0041); // ek düzlem Cf (tag "A")
        var ok = await SendToTeacherAsync(SA, TSame, "a" + softHyphen + "b" + sep + "c" + para + "d" + tag + (char)0x2060 + "e");
        ok.DirectMessage!.Body.ShouldBe("ab\nc\nde");
    }

    // ---- #361 bayrağı + ZWJ -------------------------------------------------------------------------------------

    [Fact]
    public async Task Same_school_path_is_off_by_default_until_school_membership_is_verified()
    {
        await SeedAsync();
        _allowSameSchool = false; // appsettings varsayılanı (#361)

        var page = (await ListAsync(SA)).Page!;
        page.Items.Select(i => (i.TeacherId, i.Relation)).ShouldBe(new[]
        {
            (_w.TeacherIds[TAssignOther], DirectMessageRelations.Assignment),
            (_w.TeacherIds[TBoth], DirectMessageRelations.Assignment), // okul + atama → yalnız "assignment"
            (_w.TeacherIds[TIndepAssign], DirectMessageRelations.Assignment),
            (_w.TeacherIds[TGrade], DirectMessageRelations.Assignment), // sınıf+okul hedefli atama hâlâ A
        }, ignoreOrder: true);
        page.Items.ShouldAllBe(i => i.Relation == DirectMessageRelations.Assignment);

        ShouldBeForbidden(await SendToTeacherAsync(SA, TSame), DirectMessageErrorCodes.CannotMessageTeacher);
        Created(await SendToTeacherAsync(SA, TBoth));
        (await CanMessageCountAsync(SA, TSame)).ShouldBe(0);

        // Bayrak açılınca aynı okul yolu geri gelir.
        _allowSameSchool = true;
        Created(await SendToTeacherAsync(SA, TSame));
    }

    [Fact]
    public async Task Zero_width_joiner_is_kept_only_between_emoji()
    {
        await SeedAsync();
        var zwj = char.ConvertFromUtf32(0x200D);
        var man = char.ConvertFromUtf32(0x1F468);
        var woman = char.ConvertFromUtf32(0x1F469);
        var girl = char.ConvertFromUtf32(0x1F467);
        var family = man + zwj + woman + zwj + girl;
        var tone = char.ConvertFromUtf32(0x1F3FD);
        var laptop = char.ConvertFromUtf32(0x1F4BB);
        var technologist = woman + tone + zwj + laptop;          // ten rengi + ZWJ
        var heartOnFire = "\u2764\uFE0F" + zwj + char.ConvertFromUtf32(0x1F525); // VS16 + ZWJ

        var ok = await SendToTeacherAsync(SA, TSame, family + " " + technologist + " " + heartOnFire);
        ok.DirectMessage!.Body.ShouldBe(family + " " + technologist + " " + heartOnFire);

        // Metin arasında / tek başına / emoji ile harf arasında ZWJ ayıklanır.
        (await SendToTeacherAsync(SA, TSame, "a" + zwj + "b")).DirectMessage!.Body.ShouldBe("ab");
        (await SendToTeacherAsync(SA, TSame, man + zwj + "x" + zwj)).DirectMessage!.Body.ShouldBe(man + "x");
        (await SendToTeacherAsync(SA, TSame, zwj + "  ")).ErrorCode.ShouldBe(DirectMessageErrorCodes.BodyRequired);
    }
}
