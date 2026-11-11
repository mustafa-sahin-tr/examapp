using System.Collections.Concurrent;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.AskTeacher;
using ExamApp.Api.Services.DirectMessages;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1111: öğretmene soru sor — yalnız ilişkili ya da randevulu öğretmene; hak cevapta düşer, gönderimde bekleyenler de
/// sınırdan sayılır; süre dolan ve geri çekilen soru sayılmaz; başkasının sorusu ve görseli görünmez. SQLite.
/// </summary>
public class AskTeacherServiceTests : IDisposable
{
    private const int StudentUser = 9101;
    private const int OtherStudentUser = 9102;
    private const int RelatedTeacherUser = 9201;
    private const int BookedTeacherUser = 9202;
    private const int StrangerTeacherUser = 9203;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly ConcurrentDictionary<string, byte[]> _objects = new();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly Clock _clock = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly List<string> _recorded = [];
    private int? _limit = 2;
    private int _studentId, _otherStudentId, _relatedTeacherId, _bookedTeacherId, _strangerTeacherId, _mathId;

    public AskTeacherServiceTests()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                using var copy = new MemoryStream();
                ci.ArgAt<Stream>(0).CopyTo(copy);
                var url = "/img/exam-questions/" + ci.ArgAt<string>(1);
                _objects[url] = copy.ToArray();
                return Task.FromResult(url);
            });
        _minio.GetFileStreamAsync(Arg.Any<string>())
            .Returns(ci => Task.FromResult<Stream?>(_objects.TryGetValue(ci.ArgAt<string>(0), out var b) ? new MemoryStream(b) : null));
        _minio.DeleteFileByUrlAsync(Arg.Any<string>()).Returns(ci => Task.FromResult(_objects.TryRemove(ci.ArgAt<string>(0), out _)));
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto { Id = id, KeycloakId = $"kc-{id}", FullName = $"Ad{id} Soyad" }).ToList());
        _entitlements.CheckAsync(Arg.Any<EntitlementSubject>(), EntitlementFeatures.AskTeacher, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var used = _recorded.Count;
                return new EntitlementCheck(_limit is not { } l || used < l, _limit, used, null, "student-pro");
            });
        _entitlements.RecordUsageAsync(StudentUser, EntitlementFeatures.AskTeacher, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _recorded.Add(ci.ArgAt<string>(2));
                return true;
            });

        using var ctx = _db.NewContext();
        var student = new Student { UserId = StudentUser, StudentNumber = "s1" };
        var other = new Student { UserId = OtherStudentUser, StudentNumber = "s2" };
        Teacher Approved(int userId) => new() { UserId = userId, AccountApprovedAt = DateTime.UtcNow.AddDays(-10) };
        var related = Approved(RelatedTeacherUser);
        var booked = Approved(BookedTeacherUser);
        var stranger = Approved(StrangerTeacherUser);
        var math = new Subject { Name = "Matematik" };
        ctx.AddRange(student, other, related, booked, stranger, math);
        ctx.SaveChanges();
        ctx.TeacherSubjects.Add(new TeacherSubject { TeacherId = booked.Id, SubjectId = math.Id });
        BookingSeed.Add(ctx, booked.Id, student.Id, BookingStatus.Approved, 10);
        BookingSeed.Add(ctx, stranger.Id, student.Id, BookingStatus.Rejected, 11);
        ctx.SaveChanges();
        (_studentId, _otherStudentId, _relatedTeacherId, _bookedTeacherId, _strangerTeacherId, _mathId) =
            (student.Id, other.Id, related.Id, booked.Id, stranger.Id, math.Id);
    }

    public void Dispose() => _db.Dispose();

    private async Task<T> With<T>(Func<AskTeacherService, Task<T>> action)
    {
        await using var ctx = _db.NewContext();
        var policy = Substitute.For<IDirectMessagePolicy>();
        policy.ResolveStudentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<int>(0) switch
            {
                StudentUser => new StudentMessagingContext(StudentUser, _studentId, null, null),
                OtherStudentUser => new StudentMessagingContext(OtherStudentUser, _otherStudentId, null, null),
                _ => null,
            });
        // Mesajlaşma ilişkisi: yalnız öğrenci 1 ↔ ilişkili öğretmen.
        policy.RelatedTeachers(Arg.Any<StudentMessagingContext>(), Arg.Any<DateTime>(), Arg.Any<bool>())
            .Returns(ci => ctx.Teachers
                .Where(t => ci.ArgAt<StudentMessagingContext>(0).UserId == StudentUser && t.UserId == RelatedTeacherUser)
                .Select(t => new RelatedTeacherRow { TeacherId = t.Id, TeacherUserId = t.UserId }));
        return await action(new AskTeacherService(ctx, policy, _entitlements, _minio, _authApi,
            Options.Create(new AskTeacherOptions { AnswerHours = 48 }), _clock));
    }

    private static readonly EntitlementSubject Student = EntitlementSubject.Create(StudentUser, "Student");

    private static byte[] Photo(int width = 800, int height = 600)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private Task<AskTeacherResult> Send(int teacherId, string? note = null, byte[]? bytes = null, EntitlementSubject? subject = null)
    {
        bytes ??= Photo();
        return With(s => s.SendAsync(subject ?? Student,
            new AskTeacherSendRequest(teacherId, _mathId, note, new AskTeacherImage(bytes.Length, new MemoryStream(bytes))), default));
    }

    private Task<AskTeacherResult> Answer(int teacherUser, int id, string? text = "Önce paydaları eşitle.") =>
        With(s => s.AnswerAsync(teacherUser, id, text, null, default));

    [Fact]
    public async Task Related_and_booked_teachers_are_offered_with_the_subject_teacher_first()
    {
        var teachers = await With(s => s.GetTeachersAsync(StudentUser, _mathId, default));

        teachers!.Select(t => (t.TeacherId, t.TeachesSubject)).ShouldBe([(_bookedTeacherId, true), (_relatedTeacherId, false)]);
        teachers[0].FullName.ShouldBe($"Ad{BookedTeacherUser} Soyad");
        (await With(s => s.GetTeachersAsync(OtherStudentUser, null, default)))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_question_goes_only_to_an_eligible_teacher_and_lands_in_their_queue()
    {
        (await Send(_strangerTeacherId)).Error.ShouldBe(AskTeacherError.TeacherNotAvailable);

        var sent = await Send(_relatedTeacherId, "  Bunu anlamadım  ");

        sent.Error.ShouldBe(AskTeacherError.None);
        sent.Question!.Status.ShouldBe("Open");
        sent.Question.Note.ShouldBe("Bunu anlamadım");
        sent.Question.DueAt.ShouldBe(_clock.GetUtcNow().UtcDateTime.AddHours(48));
        var queue = await With(s => s.QueueAsync(RelatedTeacherUser, AskTeacherStatus.Open, default));
        queue.Single().StudentName.ShouldBe($"Ad{StudentUser} S.");
        queue.Single().SubjectName.ShouldBe("Matematik");
        (await With(s => s.QueueAsync(BookedTeacherUser, null, default))).ShouldBeEmpty();
        _recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task Usage_is_recorded_on_answer_and_pending_questions_count_against_the_limit()
    {
        var first = (await Send(_relatedTeacherId)).Question!;
        var second = (await Send(_bookedTeacherId)).Question!;
        // Sınır 2: iki soru cevap bekliyorken üçüncüsü gönderilemez.
        (await Send(_relatedTeacherId)).Error.ShouldBe(AskTeacherError.QuotaReached);
        (await With(s => s.GetQuotaAsync(Student, default))).Pending.ShouldBe(2);

        var answered = await Answer(RelatedTeacherUser, first.Id);

        answered.Error.ShouldBe(AskTeacherError.None);
        answered.Question!.Status.ShouldBe("Answered");
        answered.Question.AnswerText.ShouldBe("Önce paydaları eşitle.");
        _recorded.ShouldBe([$"ask-teacher:{first.Id}"]);
        (await Answer(RelatedTeacherUser, first.Id)).Error.ShouldBe(AskTeacherError.InvalidState);
        (await Answer(RelatedTeacherUser, second.Id)).Error.ShouldBe(AskTeacherError.NotFound);
        (await Answer(BookedTeacherUser, second.Id, "  ")).Error.ShouldBe(AskTeacherError.AnswerRequired);
        (await Send(_relatedTeacherId)).Error.ShouldBe(AskTeacherError.QuotaReached);
    }

    [Fact]
    public async Task Expired_and_cancelled_questions_return_the_credit_and_cannot_be_answered()
    {
        var expiring = (await Send(_relatedTeacherId)).Question!;
        var cancelled = (await Send(_bookedTeacherId)).Question!;

        (await With(s => s.CancelAsync(OtherStudentUser, cancelled.Id, default))).ShouldBe(AskTeacherError.NotFound);
        (await With(s => s.CancelAsync(StudentUser, cancelled.Id, default))).ShouldBe(AskTeacherError.None);
        (await With(s => s.CancelAsync(StudentUser, cancelled.Id, default))).ShouldBe(AskTeacherError.InvalidState);

        _clock.Advance(TimeSpan.FromHours(49));
        (await With(s => s.GetForStudentAsync(StudentUser, expiring.Id, default)))!.Status.ShouldBe("Expired");
        (await Answer(RelatedTeacherUser, expiring.Id)).Error.ShouldBe(AskTeacherError.InvalidState);
        (await With(s => s.ExpireAsync(default))).ShouldBe(1);

        _recorded.ShouldBeEmpty();
        (await With(s => s.GetQuotaAsync(Student, default))).Pending.ShouldBe(0);
        (await Send(_relatedTeacherId)).Error.ShouldBe(AskTeacherError.None);
    }

    [Fact]
    public async Task Plan_without_the_feature_and_bad_input_are_refused()
    {
        _limit = 0;
        (await Send(_relatedTeacherId)).Error.ShouldBe(AskTeacherError.NotInPlan);
        _limit = null;
        (await Send(_relatedTeacherId, bytes: "not an image"u8.ToArray())).Error.ShouldBe(AskTeacherError.InvalidImage);
        (await Send(_relatedTeacherId, note: new string('x', AskTeacherQuestion.MaxNoteLength + 1))).Error
            .ShouldBe(AskTeacherError.NoteTooLong);
        (await Send(_relatedTeacherId, subject: EntitlementSubject.Create(9999, "Student"))).Error
            .ShouldBe(AskTeacherError.StudentNotFound);
        _objects.ShouldBeEmpty();
    }

    [Fact]
    public async Task Images_are_readable_only_by_the_student_and_the_teacher_and_ratings_need_an_answer()
    {
        var question = (await Send(_relatedTeacherId)).Question!;
        (await With(s => s.RateAsync(StudentUser, question.Id, 5, default))).ShouldBe(AskTeacherError.InvalidState);

        (await With(s => s.GetImageAsync(StudentUser, question.Id, false, default))).ShouldNotBeNull();
        (await With(s => s.GetImageAsync(RelatedTeacherUser, question.Id, false, default))).ShouldNotBeNull();
        (await With(s => s.GetImageAsync(OtherStudentUser, question.Id, false, default))).ShouldBeNull();
        (await With(s => s.GetImageAsync(BookedTeacherUser, question.Id, false, default))).ShouldBeNull();

        var withImage = await With(s => s.AnswerAsync(RelatedTeacherUser, question.Id, null,
            new AskTeacherImage(Photo().Length, new MemoryStream(Photo())), default));
        withImage.Question!.HasAnswerImage.ShouldBeTrue();
        withImage.Question.AnswerText.ShouldBeNull();
        (await With(s => s.GetImageAsync(StudentUser, question.Id, true, default))).ShouldNotBeNull();

        (await With(s => s.RateAsync(StudentUser, question.Id, 6, default))).ShouldBe(AskTeacherError.InvalidRating);
        (await With(s => s.RateAsync(StudentUser, question.Id, 4, default))).ShouldBe(AskTeacherError.None);
        (await With(s => s.ListForStudentAsync(StudentUser, default))).Single().Rating.ShouldBe(4);
        (await With(s => s.ListForStudentAsync(OtherStudentUser, default))).ShouldBeEmpty();
    }

    [Fact]
    public void Short_name_keeps_given_names_and_the_surname_initial()
    {
        AskTeacherService.ShortName("Ayşe Nur Kaya").ShouldBe("Ayşe Nur K.");
        AskTeacherService.ShortName("Ayşe").ShouldBe("Ayşe");
        AskTeacherService.ShortName("").ShouldBe("");
    }

    [Fact]
    public async Task Teacher_is_notified_of_a_new_question_and_the_student_of_the_answer_or_expiry()
    {
        var answered = (await Send(_relatedTeacherId)).Question!;
        var expiring = (await Send(_bookedTeacherId)).Question!;
        (await Answer(RelatedTeacherUser, answered.Id)).Error.ShouldBe(AskTeacherError.None);
        (await Answer(RelatedTeacherUser, answered.Id)).Error.ShouldBe(AskTeacherError.InvalidState);
        _clock.Advance(TimeSpan.FromHours(49));
        (await With(s => s.ExpireAsync(default))).ShouldBe(1);
        (await With(s => s.ExpireAsync(default))).ShouldBe(0);

        var notices = await NoticesAsync();
        notices.Select(n => (n.Kind, n.QuestionId, n.RecipientUserId)).ShouldBe(
        [
            (AskTeacherNoticeEvent.Asked, answered.Id, RelatedTeacherUser),
            (AskTeacherNoticeEvent.Asked, expiring.Id, BookedTeacherUser),
            (AskTeacherNoticeEvent.Answered, answered.Id, StudentUser),
            (AskTeacherNoticeEvent.Expired, expiring.Id, StudentUser),
        ], ignoreOrder: true);
        var asked = notices.Single(n => n.Kind == AskTeacherNoticeEvent.Asked && n.QuestionId == answered.Id);
        asked.CounterpartName.ShouldBe($"Ad{StudentUser} S.");
        asked.RecipientKeycloakId.ShouldBe($"kc-{RelatedTeacherUser}");
        asked.DueAtUtc.ShouldBe(new DateTime(2027, 3, 3, 9, 0, 0));
        notices.Single(n => n.Kind == AskTeacherNoticeEvent.Answered).CounterpartName.ShouldBe($"Ad{RelatedTeacherUser} S.");
        notices.Select(n => n.EventId).Distinct().Count().ShouldBe(4);
    }

    private async Task<List<AskTeacherNoticeEvent>> NoticesAsync()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<AskTeacherNoticeEvent>();
        return (await ctx.OutboxMessages.Where(o => o.Type == type).ToListAsync())
            .Select(o => JsonSerializer.Deserialize<AskTeacherNoticeEvent>(o.Content)!).ToList();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
