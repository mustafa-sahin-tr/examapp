using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Flashcards;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #699: bilgi kartı destesi, atama görünürlüğü, Leitner tekrar ve sıfırlama.</summary>
public class FlashcardServicesTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _storage = Substitute.For<IMinIoService>();
    private readonly ILocalDayCalendar _calendar = Substitute.For<ILocalDayCalendar>();

    public FlashcardServicesTests()
    {
        _calendar.Today.Returns(Today);
        _storage.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(c => "/img/exam-questions/" + c.ArgAt<string>(1));
    }

    public void Dispose() => _db.Dispose();

    private sealed record World(
        int GradeId, int SubjectId, int TopicId, int SchoolId, int OtherSchoolId,
        FlashcardLearner Learner, FlashcardLearner OtherSchoolLearner, SchoolScope Teacher);

    private const int TeacherUserId = 500;

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var subject = new Subject { Name = "Türkçe" };
        var school = new School { Name = "A" };
        var otherSchool = new School { Name = "B" };
        ctx.AddRange(grade, subject, school, otherSchool);
        await ctx.SaveChangesAsync();

        var topic = new Topic { Name = "Sözcük", SubjectId = subject.Id, GradeId = grade.Id };
        var student = new Student { UserId = 100, StudentNumber = "100", SchoolName = "A", GradeId = grade.Id, SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow };
        var other = new Student { UserId = 101, StudentNumber = "101", SchoolName = "B", GradeId = grade.Id, SchoolId = otherSchool.Id, SchoolVerifiedAt = DateTime.UtcNow };
        var teacher = new Teacher { UserId = TeacherUserId, SchoolId = school.Id };
        ctx.AddRange(topic, student, other, teacher);
        await ctx.SaveChangesAsync();

        return new World(
            grade.Id, subject.Id, topic.Id, school.Id, otherSchool.Id,
            new FlashcardLearner(student.Id, student.UserId, grade.Id, school.Id),
            new FlashcardLearner(other.Id, other.UserId, grade.Id, otherSchool.Id),
            SchoolScope.For(TeacherUserId, school.Id));
    }

    private FlashcardDeckService Decks(AppDbContext ctx) => new(ctx, _storage, new SchoolAccessPolicy(ctx));

    private FlashcardStudyService Study(AppDbContext ctx) => new(ctx, _calendar);

    private static FlashcardOwner StudentOwner(FlashcardLearner learner) => new(learner.UserId, FlashcardDeckOwnerKind.Student);

    private static FlashcardOwner TeacherOwner => new(TeacherUserId, FlashcardDeckOwnerKind.Teacher);

    private async Task<(int DeckId, int CardId)> TeacherDeckAsync(World w)
    {
        await using var ctx = _db.NewContext();
        var deck = await Decks(ctx).CreateAsync(TeacherOwner, new SaveFlashcardDeckDto { Title = "Sözcükler", GradeId = w.GradeId, SubjectId = w.SubjectId, TopicId = w.TopicId });
        var card = await Decks(ctx).AddCardAsync(TeacherOwner, deck.Id, new SaveFlashcardDto { Front = "ön", Back = "arka" });
        return (deck.Id, card!.Id);
    }

    private async Task<(int DeckId, int CardId)> PrivateDeckAsync(FlashcardLearner learner)
    {
        await using var ctx = _db.NewContext();
        var deck = await Decks(ctx).CreateAsync(StudentOwner(learner), new SaveFlashcardDeckDto { Title = "Benim" });
        var card = await Decks(ctx).AddCardAsync(StudentOwner(learner), deck.Id, new SaveFlashcardDto { Front = "ön", Back = "arka" });
        return (deck.Id, card!.Id);
    }

    [Theory]
    [InlineData(null, FlashcardRecall.Forgot, 0, 1)]
    [InlineData(null, FlashcardRecall.Hard, 0, 1)]
    [InlineData(null, FlashcardRecall.Good, 1, 3)]
    [InlineData(1, FlashcardRecall.Hard, 1, 3)]
    [InlineData(1, FlashcardRecall.Good, 2, 7)]
    [InlineData(2, FlashcardRecall.Good, 3, 21)]
    [InlineData(3, FlashcardRecall.Good, 3, 21)]
    [InlineData(3, FlashcardRecall.Forgot, 0, 1)]
    public void Schedule_moves_between_boxes(int? box, FlashcardRecall recall, int expectedBox, int expectedDays)
    {
        var (next, due) = FlashcardSchedule.Next(box, recall, Today);

        next.ShouldBe(expectedBox);
        due.ShouldBe(Today.AddDays(expectedDays));
    }

    [Fact]
    public void Assignment_rule_matches_the_worksheet_rule_without_platform_wide()
    {
        int?[] values = [null, 1, 2];
        foreach (var assignmentStudent in values)
        foreach (var assignmentGrade in values)
        foreach (var assignmentSchool in values)
        foreach (var grade in values)
        foreach (var school in values)
        {
            var compiled = FlashcardAccess.AssignedTo(1, grade, school).Compile();
            var row = new FlashcardDeckAssignment { StudentId = assignmentStudent, GradeId = assignmentGrade, SchoolId = assignmentSchool };
            compiled(row).ShouldBe(
                WorksheetStudentAccess.IsAssignmentVisibleTo(assignmentStudent, assignmentGrade, assignmentSchool, false, 1, grade, school));
        }
    }

    [Fact]
    public async Task Student_deck_stays_private()
    {
        var w = await SeedAsync();
        var (deckId, cardId) = await PrivateDeckAsync(w.Learner);

        await using var ctx = _db.NewContext();
        (await Study(ctx).ListDecksAsync(w.Learner)).Single().IsOwner.ShouldBeTrue();
        (await Study(ctx).ListDecksAsync(w.OtherSchoolLearner)).ShouldBeEmpty();
        (await Study(ctx).GetDeckAsync(w.OtherSchoolLearner, deckId)).ShouldBeNull();
        (await Study(ctx).ReviewAsync(w.OtherSchoolLearner, cardId, FlashcardRecall.Good)).Outcome.ShouldBe(FlashcardReviewOutcome.NotFound);
        (await Decks(ctx).UpdateCardAsync(StudentOwner(w.OtherSchoolLearner), cardId, new SaveFlashcardDto { Back = "x" })).ShouldBeNull();
        (await Decks(ctx).DeleteAsync(StudentOwner(w.OtherSchoolLearner), deckId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Same_user_id_as_teacher_does_not_reach_a_student_deck()
    {
        var w = await SeedAsync();
        var (deckId, _) = await PrivateDeckAsync(w.Learner);

        await using var ctx = _db.NewContext();
        (await Decks(ctx).GetOwnedAsync(new FlashcardOwner(w.Learner.UserId, FlashcardDeckOwnerKind.Teacher), deckId)).ShouldBeNull();
    }

    [Fact]
    public async Task Teacher_deck_needs_grade_and_subject_and_a_matching_topic()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await Should.ThrowAsync<FlashcardRuleException>(() => Decks(ctx).CreateAsync(TeacherOwner, new SaveFlashcardDeckDto { Title = "x" })))
            .Message.ShouldBe("TaxonomyRequired");

        (await Should.ThrowAsync<FlashcardRuleException>(() => Decks(ctx).CreateAsync(TeacherOwner,
            new SaveFlashcardDeckDto { Title = "x", GradeId = w.GradeId, SubjectId = w.SubjectId, TopicId = w.TopicId + 99 })))
            .Message.ShouldBe("InvalidTopic");
    }

    [Fact]
    public async Task Grade_assignment_reaches_only_the_teachers_school()
    {
        var w = await SeedAsync();
        var (deckId, _) = await TeacherDeckAsync(w);

        await using var ctx = _db.NewContext();
        (await Study(ctx).ListDecksAsync(w.Learner)).ShouldBeEmpty();

        var assignment = await Decks(ctx).AssignAsync(w.Teacher, deckId, new AssignFlashcardDeckDto { GradeId = w.GradeId });
        assignment.ShouldNotBeNull();

        var deck = (await Study(ctx).ListDecksAsync(w.Learner)).Single();
        deck.IsOwner.ShouldBeFalse();
        (await Study(ctx).GetDeckAsync(w.Learner, deckId))!.IsOwner.ShouldBeFalse();
        (await Study(ctx).ListDecksAsync(w.OtherSchoolLearner)).ShouldBeEmpty();
        (await Decks(ctx).UpdateAsync(StudentOwner(w.Learner), deckId, new SaveFlashcardDeckDto { Title = "x" })).ShouldBeNull();

        (await Decks(ctx).AssignAsync(w.Teacher, deckId, new AssignFlashcardDeckDto { GradeId = w.GradeId }))!.Id.ShouldBe(assignment.Id);
        (await Decks(ctx).UnassignAsync(w.Teacher, deckId, assignment.Id)).ShouldBeTrue();
        (await Study(ctx).ListDecksAsync(w.Learner)).ShouldBeEmpty();

        // Kaldırılan atama yeniden yapılabilir (tekil index NOT IsDeleted).
        (await Decks(ctx).AssignAsync(w.Teacher, deckId, new AssignFlashcardDeckDto { GradeId = w.GradeId })).ShouldNotBeNull();
    }

    [Fact]
    public async Task Student_target_follows_the_school_scope()
    {
        var w = await SeedAsync();
        var (deckId, _) = await TeacherDeckAsync(w);

        await using var ctx = _db.NewContext();
        (await Decks(ctx).AssignAsync(w.Teacher, deckId, new AssignFlashcardDeckDto { StudentId = w.OtherSchoolLearner.StudentId })).ShouldBeNull();
        (await Decks(ctx).AssignAsync(w.Teacher, deckId, new AssignFlashcardDeckDto { StudentId = w.Learner.StudentId })).ShouldNotBeNull();

        (await Study(ctx).ListDecksAsync(w.Learner)).ShouldHaveSingleItem();
        (await Decks(ctx).ListAssignmentsAsync(w.Teacher, deckId))!.Single().StudentId.ShouldBe(w.Learner.StudentId);
    }

    [Fact]
    public async Task Assignment_target_rules_are_enforced()
    {
        var w = await SeedAsync();
        var (deckId, _) = await TeacherDeckAsync(w);
        await using var ctx = _db.NewContext();

        (await Should.ThrowAsync<FlashcardRuleException>(() => Decks(ctx).AssignAsync(w.Teacher, deckId, new AssignFlashcardDeckDto())))
            .Message.ShouldBe("TargetRequired");
        (await Should.ThrowAsync<FlashcardRuleException>(() =>
                Decks(ctx).AssignAsync(w.Teacher, deckId, new AssignFlashcardDeckDto { StudentId = w.Learner.StudentId, GradeId = w.GradeId })))
            .Message.ShouldBe("TargetRequired");

        // Başka öğretmenin destesi yok gibi davranır.
        (await Decks(ctx).AssignAsync(SchoolScope.For(TeacherUserId + 1, w.SchoolId), deckId, new AssignFlashcardDeckDto { GradeId = w.GradeId })).ShouldBeNull();

        // Öğretmen kaydındaki okulla uyuşmayan kapsam reddedilir (#222).
        (await Should.ThrowAsync<FlashcardRuleException>(() =>
                Decks(ctx).AssignAsync(SchoolScope.For(TeacherUserId, w.OtherSchoolId), deckId, new AssignFlashcardDeckDto { GradeId = w.GradeId })))
            .Message.ShouldBe("OnlyOwnStudents");
    }

    [Fact]
    public async Task Independent_teacher_cannot_assign_to_a_grade()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        const int independentUserId = 600;
        ctx.Teachers.Add(new Teacher { UserId = independentUserId });
        await ctx.SaveChangesAsync();

        var owner = new FlashcardOwner(independentUserId, FlashcardDeckOwnerKind.Teacher);
        var deck = await Decks(ctx).CreateAsync(owner, new SaveFlashcardDeckDto { Title = "x", GradeId = w.GradeId, SubjectId = w.SubjectId });

        (await Should.ThrowAsync<FlashcardRuleException>(() =>
                Decks(ctx).AssignAsync(SchoolScope.For(independentUserId, null), deck.Id, new AssignFlashcardDeckDto { GradeId = w.GradeId })))
            .Message.ShouldBe("IndependentGradeForbidden");

        // Approved Booking'i olmayan öğrenci kapsam dışı.
        (await Decks(ctx).AssignAsync(SchoolScope.For(independentUserId, null), deck.Id, new AssignFlashcardDeckDto { StudentId = w.Learner.StudentId })).ShouldBeNull();
    }

    [Fact]
    public async Task Review_stores_the_next_date_and_refuses_a_card_that_is_not_due()
    {
        var w = await SeedAsync();
        var (_, cardId) = await PrivateDeckAsync(w.Learner);
        await using var ctx = _db.NewContext();

        (await Study(ctx).GetDueCountAsync(w.Learner)).ShouldBe(1);
        (await Study(ctx).GetDueCardsAsync(w.Learner, null, 20)).Single().Box.ShouldBeNull();

        var result = await Study(ctx).ReviewAsync(w.Learner, cardId, FlashcardRecall.Good);
        result.Outcome.ShouldBe(FlashcardReviewOutcome.Recorded);
        result.Review!.Box.ShouldBe(1);
        result.Review.DueDay.ShouldBe(Today.AddDays(3));

        (await Study(ctx).GetDueCountAsync(w.Learner)).ShouldBe(0);
        (await Study(ctx).GetDueCardsAsync(w.Learner, null, 20)).ShouldBeEmpty();
        (await Study(ctx).ReviewAsync(w.Learner, cardId, FlashcardRecall.Good)).Outcome.ShouldBe(FlashcardReviewOutcome.NotDue);

        _calendar.Today.Returns(Today.AddDays(3));
        (await Study(ctx).GetDueCardsAsync(w.Learner, null, 20)).Single().Box.ShouldBe(1);
        (await Study(ctx).ListDecksAsync(w.Learner)).Single().DueCount.ShouldBe(1);

        var second = await Study(ctx).ReviewAsync(w.Learner, cardId, FlashcardRecall.Forgot);
        second.Review!.Box.ShouldBe(0);
        second.Review.DueDay.ShouldBe(Today.AddDays(4));

        await using var read = _db.NewContext();
        (await read.FlashcardReviews.SingleAsync()).ReviewCount.ShouldBe(2);
    }

    [Fact]
    public async Task Due_list_puts_overdue_cards_before_new_ones_and_honours_the_filters()
    {
        var w = await SeedAsync();
        var (deckId, oldCard) = await PrivateDeckAsync(w.Learner);
        await using var ctx = _db.NewContext();
        await Study(ctx).ReviewAsync(w.Learner, oldCard, FlashcardRecall.Forgot);
        var newCard = await Decks(ctx).AddCardAsync(StudentOwner(w.Learner), deckId, new SaveFlashcardDto { Front = "yeni", Back = "arka" });
        var (otherDeck, _) = await PrivateDeckAsync(w.Learner);

        _calendar.Today.Returns(Today.AddDays(1));
        var due = await Study(ctx).GetDueCardsAsync(w.Learner, deckId, 20);
        due.Select(c => c.Id).ShouldBe([oldCard, newCard!.Id]);

        (await Study(ctx).GetDueCardsAsync(w.Learner, null, 2)).Count.ShouldBe(2);
        (await Study(ctx).GetDueCardsAsync(w.Learner, otherDeck, 20)).ShouldHaveSingleItem();
        (await Study(ctx).GetDueCardsAsync(w.OtherSchoolLearner, deckId, 20)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleted_cards_and_decks_leave_the_due_count()
    {
        var w = await SeedAsync();
        var (deckId, cardId) = await PrivateDeckAsync(w.Learner);
        await using var ctx = _db.NewContext();
        await Decks(ctx).AddCardAsync(StudentOwner(w.Learner), deckId, new SaveFlashcardDto { Front = "b", Back = "b" });
        (await Study(ctx).GetDueCountAsync(w.Learner)).ShouldBe(2);

        (await Decks(ctx).DeleteCardAsync(StudentOwner(w.Learner), cardId)).ShouldBeTrue();
        (await Study(ctx).GetDueCountAsync(w.Learner)).ShouldBe(1);
        (await Study(ctx).ReviewAsync(w.Learner, cardId, FlashcardRecall.Good)).Outcome.ShouldBe(FlashcardReviewOutcome.NotFound);

        (await Decks(ctx).DeleteAsync(StudentOwner(w.Learner), deckId)).ShouldBeTrue();
        (await Study(ctx).GetDueCountAsync(w.Learner)).ShouldBe(0);
    }

    [Fact]
    public async Task Removing_the_assignment_hides_the_card_and_giving_it_back_keeps_the_progress()
    {
        var w = await SeedAsync();
        var (deckId, cardId) = await TeacherDeckAsync(w);
        await using var ctx = _db.NewContext();
        var assignment = await Decks(ctx).AssignAsync(w.Teacher, deckId, new AssignFlashcardDeckDto { StudentId = w.Learner.StudentId });
        await Study(ctx).ReviewAsync(w.Learner, cardId, FlashcardRecall.Good);

        await Decks(ctx).UnassignAsync(w.Teacher, deckId, assignment!.Id);
        (await Study(ctx).ReviewAsync(w.Learner, cardId, FlashcardRecall.Good)).Outcome.ShouldBe(FlashcardReviewOutcome.NotFound);

        await Decks(ctx).AssignAsync(w.Teacher, deckId, new AssignFlashcardDeckDto { StudentId = w.Learner.StudentId });
        (await Study(ctx).GetDueCountAsync(w.Learner)).ShouldBe(0);
    }

    [Fact]
    public async Task Reviews_give_no_points()
    {
        var w = await SeedAsync();
        var (_, cardId) = await PrivateDeckAsync(w.Learner);
        await using var ctx = _db.NewContext();

        await Study(ctx).ReviewAsync(w.Learner, cardId, FlashcardRecall.Good);

        await using var read = _db.NewContext();
        (await read.StudentPoints.CountAsync()).ShouldBe(0);
        (await read.StudentPointHistories.CountAsync()).ShouldBe(0);
        (await read.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Card_text_is_sanitised_when_stored()
    {
        var w = await SeedAsync();
        var (deckId, _) = await PrivateDeckAsync(w.Learner);
        await using var ctx = _db.NewContext();

        var card = await Decks(ctx).AddCardAsync(StudentOwner(w.Learner), deckId,
            new SaveFlashcardDto { Front = "<b>ön</b><script>alert(1)</script>", Back = "<img src=x onerror=alert(1)>arka" });

        card!.Front.ShouldBe("<b>ön</b>");
        card.Back.ShouldNotContain("onerror");

        await using var read = _db.NewContext();
        (await read.Flashcards.SingleAsync(c => c.Id == card.Id)).Front.ShouldBe("<b>ön</b>");
    }

    [Theory]
    [InlineData("ön", "   ")]
    [InlineData("<script>alert(1)</script>", "arka")]
    [InlineData("ön", "<script>alert(1)</script>")]
    public async Task Card_without_text_is_refused(string front, string back)
    {
        var w = await SeedAsync();
        var (deckId, _) = await PrivateDeckAsync(w.Learner);
        await using var ctx = _db.NewContext();

        (await Should.ThrowAsync<FlashcardRuleException>(() =>
                Decks(ctx).AddCardAsync(StudentOwner(w.Learner), deckId, new SaveFlashcardDto { Front = front, Back = back })))
            .Message.ShouldBe("CardTextRequired");
    }

    private static string PngDataUri()
    {
        using var bitmap = new SKBitmap(4, 4);
        return "data:image/png;base64," + Convert.ToBase64String(SkiaImaging.Encode(bitmap, SKEncodedImageFormat.Png));
    }

    [Fact]
    public async Task Image_is_decoded_and_stored_under_the_question_prefix()
    {
        var w = await SeedAsync();
        var (deckId, _) = await PrivateDeckAsync(w.Learner);
        await using var ctx = _db.NewContext();

        var card = await Decks(ctx).AddCardAsync(StudentOwner(w.Learner), deckId, new SaveFlashcardDto { Back = "arka", Image = PngDataUri() });

        card!.ImageUrl.ShouldStartWith("/img/exam-questions/questions/flashcards/");
        await _storage.Received(1).UploadFileAsync(Arg.Any<Stream>(), Arg.Is<string>(k => k.StartsWith("questions/flashcards/")), Arg.Any<string>(), Arg.Any<string>());

        var kept = await Decks(ctx).UpdateCardAsync(StudentOwner(w.Learner), card.Id, new SaveFlashcardDto { Back = "yeni" });
        kept!.ImageUrl.ShouldBe(card.ImageUrl);

        // Görsel kalkınca ön yüzde metin gerekir.
        await Should.ThrowAsync<FlashcardRuleException>(() =>
            Decks(ctx).UpdateCardAsync(StudentOwner(w.Learner), card.Id, new SaveFlashcardDto { Back = "yeni", RemoveImage = true }));
        var removed = await Decks(ctx).UpdateCardAsync(StudentOwner(w.Learner), card.Id, new SaveFlashcardDto { Front = "ön", Back = "yeni", RemoveImage = true });
        removed!.ImageUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://example.com/a.png", "InvalidImage")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=", "InvalidImage")]
    [InlineData("data:image/png;base64,%%%", "InvalidImage")]
    [InlineData("data:image/png;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==", "InvalidImage")]
    public async Task Invalid_image_is_refused_and_not_uploaded(string image, string code)
    {
        var w = await SeedAsync();
        var (deckId, _) = await PrivateDeckAsync(w.Learner);
        await using var ctx = _db.NewContext();

        (await Should.ThrowAsync<FlashcardRuleException>(() =>
                Decks(ctx).AddCardAsync(StudentOwner(w.Learner), deckId, new SaveFlashcardDto { Back = "arka", Image = image })))
            .Message.ShouldBe(code);
        await _storage.DidNotReceive().UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Oversized_image_is_refused()
    {
        var w = await SeedAsync();
        var (deckId, _) = await PrivateDeckAsync(w.Learner);
        await using var ctx = _db.NewContext();
        var image = "data:image/png;base64," + Convert.ToBase64String(new byte[3 * 1024 * 1024]);

        (await Should.ThrowAsync<FlashcardRuleException>(() =>
                Decks(ctx).AddCardAsync(StudentOwner(w.Learner), deckId, new SaveFlashcardDto { Back = "arka", Image = image })))
            .Message.ShouldBe("ImageTooLarge");
    }

    [Fact]
    public async Task Reset_clears_progress_and_direct_assignments_but_keeps_the_students_deck()
    {
        var w = await SeedAsync();
        var (teacherDeckId, teacherCardId) = await TeacherDeckAsync(w);
        var (ownDeckId, ownCardId) = await PrivateDeckAsync(w.Learner);
        await using (var ctx = _db.NewContext())
        {
            await Decks(ctx).AssignAsync(w.Teacher, teacherDeckId, new AssignFlashcardDeckDto { StudentId = w.Learner.StudentId });
            await Study(ctx).ReviewAsync(w.Learner, teacherCardId, FlashcardRecall.Good);
            await Study(ctx).ReviewAsync(w.Learner, ownCardId, FlashcardRecall.Good);
        }

        await using (var ctx = _db.NewContext())
        {
            var job = new StudentResetJob(ctx, Substitute.For<IBadgeResetApiClient>());
            await job.RunAsync(w.Learner.UserId, w.Learner.StudentId, "kc");
        }

        await using var read = _db.NewContext();
        (await read.FlashcardReviews.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        var decks = await Study(read).ListDecksAsync(w.Learner);
        decks.Single().Id.ShouldBe(ownDeckId);
        decks.Single().DueCount.ShouldBe(1);
    }
}
