using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Taxonomy;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class TaxonomyServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();

    private TaxonomyService NewService(AppDbContext ctx) => new(ctx, _jobs);

    private async Task<(int gradeId, int subjectId, int topicId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "3. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        ctx.Grades.Add(grade);
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        var topic = new Topic { Name = "Toplama", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();
        return (grade.Id, subject.Id, topic.Id);
    }

    private bool ScheduledAJob() =>
        _jobs.ReceivedCalls().Any(c =>
            c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create) &&
            c.GetArguments().OfType<IState>().Any(s => s is ScheduledState));

    // ---- GetTreeAsync ----

    [Fact]
    public async Task GetTree_nests_subjects_topics_and_subtopics_with_grade_names()
    {
        var (gradeId, subjectId, topicId) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.SubTopics.Add(new SubTopic { Name = "İki basamaklı", TopicId = topicId });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var tree = await NewService(read).GetTreeAsync();

        var subject = tree.Subjects.ShouldHaveSingleItem();
        subject.Id.ShouldBe(subjectId);
        var topic = subject.Topics.ShouldHaveSingleItem();
        topic.GradeId.ShouldBe(gradeId);
        topic.GradeName.ShouldBe("3. Sınıf");
        topic.SubTopics.ShouldHaveSingleItem().Name.ShouldBe("İki basamaklı");
        tree.Grades.ShouldContain(g => g.Name == "3. Sınıf");
    }

    [Fact]
    public async Task GetTree_reports_question_counts_per_subtopic()
    {
        var (_, subjectId, topicId) = await SeedAsync();
        int subTopicId;
        await using (var ctx = _db.NewContext())
        {
            var st = new SubTopic { Name = "x", TopicId = topicId };
            ctx.SubTopics.Add(st);
            await ctx.SaveChangesAsync();
            subTopicId = st.Id;

            var q = new Question { SubjectId = subjectId, TopicId = topicId };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            ctx.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = q.Id, SubTopicId = subTopicId });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var tree = await NewService(read).GetTreeAsync();

        tree.Subjects[0].Topics[0].SubTopics.Single(s => s.Id == subTopicId).QuestionCount.ShouldBe(1);
    }

    // ---- Create ----

    [Fact]
    public async Task CreateSubject_persists_and_schedules_a_cache_reconcile()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(new UpsertSubjectDto { Name = "  Fen  " }, userId: 7);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Subjects.FindAsync(result.ObjectId))!.Name.ShouldBe("Fen");
        ScheduledAJob().ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateSubject_rejects_a_blank_name_and_schedules_nothing(string name)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(new UpsertSubjectDto { Name = name }, userId: 1);

        result.Success.ShouldBeFalse();
        ScheduledAJob().ShouldBeFalse();
    }

    [Fact]
    public async Task CreateSubject_rejects_a_duplicate_name_case_insensitively()
    {
        await SeedAsync(); // creates "Matematik"
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSubjectAsync(new UpsertSubjectDto { Name = "matematik" }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("zaten var");
    }

    [Fact]
    public async Task CreateTopic_rejects_an_unknown_subject()
    {
        var (gradeId, _, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateTopicAsync(
            new UpsertTopicDto { Name = "x", SubjectId = 9999, GradeId = gradeId }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("Geçersiz ders");
    }

    [Fact]
    public async Task CreateTopic_rejects_an_unknown_grade()
    {
        var (_, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateTopicAsync(
            new UpsertTopicDto { Name = "x", SubjectId = subjectId, GradeId = 9999 }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("Geçersiz sınıf");
    }

    // ---- Delete guards ----

    [Fact]
    public async Task DeleteSubject_is_blocked_while_topics_reference_it()
    {
        var (_, subjectId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).DeleteSubjectAsync(subjectId, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("konular var");
    }

    [Fact]
    public async Task DeleteSubject_is_blocked_while_questions_reference_it()
    {
        var (_, subjectId, topicId) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // remove the topic so only the question blocks the delete
            var t = await ctx.Topics.FindAsync(topicId);
            ctx.Topics.Remove(t!);
            ctx.Questions.Add(new Question { SubjectId = subjectId });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteSubjectAsync(subjectId, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("sorular var");
    }

    [Fact]
    public async Task DeleteSubTopic_is_blocked_while_questions_are_assigned_to_it()
    {
        var (_, subjectId, topicId) = await SeedAsync();
        int subTopicId;
        await using (var ctx = _db.NewContext())
        {
            var st = new SubTopic { Name = "x", TopicId = topicId };
            ctx.SubTopics.Add(st);
            await ctx.SaveChangesAsync();
            subTopicId = st.Id;
            var q = new Question { SubjectId = subjectId, TopicId = topicId };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            ctx.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = q.Id, SubTopicId = subTopicId });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteSubTopicAsync(subTopicId, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteSubTopic_soft_deletes_when_unreferenced()
    {
        var (_, _, topicId) = await SeedAsync();
        int subTopicId;
        await using (var ctx = _db.NewContext())
        {
            var st = new SubTopic { Name = "x", TopicId = topicId };
            ctx.SubTopics.Add(st);
            await ctx.SaveChangesAsync();
            subTopicId = st.Id;
        }

        await using var del = _db.NewContext();
        (await NewService(del).DeleteSubTopicAsync(subTopicId, userId: 1)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.SubTopics.FindAsync(subTopicId)).ShouldBeNull(); // filtered out by the IsDeleted query filter
        (await check.SubTopics.IgnoreQueryFilters().FirstAsync(s => s.Id == subTopicId)).IsDeleted.ShouldBeTrue();
    }

    public void Dispose() => _db.Dispose();
}
