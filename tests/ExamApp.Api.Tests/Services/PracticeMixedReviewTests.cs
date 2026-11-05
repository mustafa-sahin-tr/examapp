using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #534: karışık mini deneme: paylar, tamamlama, tekrar yok ve engelli sorular.</summary>
public class PracticeMixedReviewTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record World(int StudentId, int GradeId, int SubjectId, int TopicId, int WorksheetId);

    private static StudentProfileDto Profile(World w) => new() { Id = w.StudentId, GradeId = w.GradeId };

    private async Task<World> SeedWorldAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();
        var topic = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
        var student = new Student { UserId = 55, StudentNumber = "S1", SchoolName = "S", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = subject.Id };
        ctx.AddRange(topic, student, worksheet);
        await ctx.SaveChangesAsync();
        return new World(student.Id, grade.Id, subject.Id, topic.Id, worksheet.Id);
    }

    private async Task<List<int>> AddQuestionsAsync(World w, int count)
    {
        await using var ctx = _db.NewContext();
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var q = new Question { Text = "?", SubjectId = w.SubjectId, TopicId = w.TopicId, Point = 1, DifficultyLevel = 1 };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            var correct = new Answer { QuestionId = q.Id, Text = "ok", Tag = "A", Order = 0 };
            ctx.Answers.Add(correct);
            await ctx.SaveChangesAsync();
            q.CorrectAnswerId = correct.Id;
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = w.WorksheetId, QuestionId = q.Id, Order = i + 1 });
            await ctx.SaveChangesAsync();
            ids.Add(q.Id);
        }
        return ids;
    }

    /// <summary>Dün yanlış: tekrar zamanı gelmiş, yanlış defterinde ve konuyu zayıf gösterir.</summary>
    private async Task AnswerWrongYesterdayAsync(World w, IEnumerable<int> questionIds)
    {
        await using var ctx = _db.NewContext();
        var at = DateTime.UtcNow.AddDays(-1);
        var session = new PracticeSession { StudentId = w.StudentId, GradeId = w.GradeId, StartTime = at, Status = PracticeSessionStatus.Ended };
        foreach (var id in questionIds)
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = id, ShownAt = at, AnsweredAt = at, IsCorrect = false, TimeTaken = 5 });
        ctx.PracticeSessions.Add(session);
        await ctx.SaveChangesAsync();
    }

    private async Task<List<int>> StartAsync(World w, PracticeSessionStartDto? request = null)
    {
        await using var ctx = _db.NewContext();
        var dto = await new PracticeSessionService(ctx).StartAsync(Profile(w), request ?? new PracticeSessionStartDto { MixedReview = true });
        dto.IsDueReview.ShouldBeTrue();
        var json = await ctx.PracticeSessions.AsNoTracking().Where(s => s.Id == dto.Id).Select(s => s.FixedQuestionIdsJson).SingleAsync();
        return JsonSerializer.Deserialize<List<int>>(json!)!;
    }

    [Fact]
    public async Task With_no_history_no_session_opens()
    {
        var w = await SeedWorldAsync();
        await AddQuestionsAsync(w, 20);

        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            new PracticeSessionService(ctx).StartAsync(Profile(w), new PracticeSessionStartDto { MixedReview = true }));
        (await ctx.PracticeSessions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Enough_sources_give_exactly_the_count_without_repeats_and_new_questions_from_the_weak_topic()
    {
        var w = await SeedWorldAsync();
        var wrong = await AddQuestionsAsync(w, 10);
        var fresh = await AddQuestionsAsync(w, 20);
        await AnswerWrongYesterdayAsync(w, wrong);

        var ids = await StartAsync(w);

        ids.Count.ShouldBe(15);
        ids.Distinct().Count().ShouldBe(15);
        ids.Count(wrong.Contains).ShouldBe(10);
        ids.Count(fresh.Contains).ShouldBe(5);
    }

    [Fact]
    public async Task A_short_source_is_filled_from_the_others()
    {
        var w = await SeedWorldAsync();
        var wrong = await AddQuestionsAsync(w, 5);
        var fresh = await AddQuestionsAsync(w, 20);
        await AnswerWrongYesterdayAsync(w, wrong);

        var ids = await StartAsync(w);

        ids.Count.ShouldBe(15);
        ids.Count(wrong.Contains).ShouldBe(5);
        ids.Count(fresh.Contains).ShouldBe(10);
    }

    [Fact]
    public async Task When_sources_run_out_the_session_is_shorter()
    {
        var w = await SeedWorldAsync();
        var wrong = await AddQuestionsAsync(w, 5);
        await AddQuestionsAsync(w, 2);
        await AnswerWrongYesterdayAsync(w, wrong);

        (await StartAsync(w)).Count.ShouldBe(7);
    }

    [Fact]
    public async Task A_question_in_an_unfinished_test_never_enters()
    {
        var w = await SeedWorldAsync();
        var wrong = await AddQuestionsAsync(w, 5);
        var fresh = await AddQuestionsAsync(w, 6);
        await AnswerWrongYesterdayAsync(w, wrong);
        var running = fresh[0];
        await using (var ctx = _db.NewContext())
        {
            var exam = new Worksheet { Name = "Süren", Description = "", GradeId = w.GradeId, SubjectId = w.SubjectId };
            ctx.Add(exam);
            await ctx.SaveChangesAsync();
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = exam.Id, QuestionId = running, Order = 1 });
            ctx.TestInstances.Add(new WorksheetInstance { StudentId = w.StudentId, WorksheetId = exam.Id, StartTime = DateTime.UtcNow, Status = WorksheetInstanceStatus.Started });
            await ctx.SaveChangesAsync();
        }

        var ids = await StartAsync(w);

        ids.ShouldNotContain(running);
        ids.Count.ShouldBe(10);
    }

    [Fact]
    public async Task Target_count_overrides_the_default_and_a_sub_topic_scope_is_refused()
    {
        var w = await SeedWorldAsync();
        var wrong = await AddQuestionsAsync(w, 5);
        await AddQuestionsAsync(w, 20);
        await AnswerWrongYesterdayAsync(w, wrong);

        (await StartAsync(w, new PracticeSessionStartDto { MixedReview = true, TargetCount = 8 })).Count.ShouldBe(8);

        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            new PracticeSessionService(ctx).StartAsync(Profile(w), new PracticeSessionStartDto { MixedReview = true, SubTopicIds = [999] }));
    }

    [Fact]
    public void Compose_keeps_the_shares_when_every_source_is_large()
    {
        int[] due = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], mistakes = [21, 22, 23, 24, 25, 26, 27, 28, 29, 30], weak = [41, 42, 43, 44, 45, 46, 47, 48, 49, 50];

        var ids = MixedReviewComposer.Compose(due, mistakes, weak, 15, new MixedReviewOptions(), new Random(1));

        ids.Count(due.Contains).ShouldBe(6);
        ids.Count(mistakes.Contains).ShouldBe(4);
        ids.Count(weak.Contains).ShouldBe(5);
    }

    [Fact]
    public void Compose_does_not_count_an_overlap_twice()
    {
        var ids = MixedReviewComposer.Compose([1, 2, 3], [1, 2, 3, 4], [4, 5], 15, new MixedReviewOptions(), new Random(1));

        ids.OrderBy(id => id).ShouldBe([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void Shares_must_add_up_to_100()
    {
        var results = new MixedReviewOptions { DuePct = 50 }.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(new object()));

        results.ShouldNotBeEmpty();
    }
}
