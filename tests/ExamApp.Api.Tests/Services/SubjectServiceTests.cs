using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

public class SubjectServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private SubjectService NewService(AppDbContext ctx) => new(ctx);

    private async Task<(int gradeId, int subjectId, int topicId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "6" };
        var subject = new Subject { Name = "Sosyal" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();
        ctx.GradeSubjects.Add(new GradeSubject { GradeId = grade.Id, SubjectId = subject.Id });
        var topic = new Topic { Name = "Tarih", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();
        return (grade.Id, subject.Id, topic.Id);
    }

    [Fact]
    public async Task GetAllSubjects_returns_them()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetAllSubjectsAsync()).ShouldContain(s => s.Name == "Sosyal");
    }

    [Fact]
    public async Task GetTopicsBySubjectId_filters_by_subject()
    {
        var (_, subjectId, topicId) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var topics = await NewService(ctx).GetTopicBySubjectIdAsync(subjectId);
        topics.ShouldHaveSingleItem().Id.ShouldBe(topicId);

        (await NewService(ctx).GetTopicBySubjectIdAsync(subjectId + 999)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSubTopicsByTopicId_filters_by_topic()
    {
        var (_, _, topicId) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.SubTopics.Add(new SubTopic { Name = "Osmanlı", TopicId = topicId });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        (await NewService(read).GetSubTopicByTopicIdAsync(topicId)).ShouldHaveSingleItem().Name.ShouldBe("Osmanlı");
    }

    [Fact]
    public async Task GetSubjectsByGradeId_goes_through_the_grade_subject_join()
    {
        var (gradeId, _, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).GetSubjectsByGradeIdAsync(gradeId)).ShouldContain(s => s.Name == "Sosyal");
        (await NewService(ctx).GetSubjectsByGradeIdAsync(gradeId + 999)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetTopicsBySubjectAndGrade_filters_on_both()
    {
        var (gradeId, subjectId, topicId) = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).GetTopicsBySubjectAndGradeAsync(subjectId, gradeId))
            .ShouldHaveSingleItem().Id.ShouldBe(topicId);
        (await NewService(ctx).GetTopicsBySubjectAndGradeAsync(subjectId, gradeId + 1)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteSubject_removes_it_and_returns_false_when_missing()
    {
        var (_, subjectId, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // remove the topic first so the delete isn't blocked by anything
            ctx.Topics.RemoveRange(ctx.Topics);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).DeleteSubjectAsync(subjectId)).ShouldBeTrue();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).DeleteSubjectAsync(subjectId)).ShouldBeFalse();
    }

    public void Dispose() => _db.Dispose();
}
