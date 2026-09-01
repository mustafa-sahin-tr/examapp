using ExamApp.Api.Data;

using ExamApp.Api.Services;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class QuestionClassificationServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private QuestionClassificationService NewService(AppDbContext ctx) => new(ctx);

    private sealed record World(int QuestionId, int SubjectId, int TopicId, int SubTopicA, int SubTopicB);

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        var grade = new Grade { Name = "4" };
        ctx.AddRange(subject, grade);
        await ctx.SaveChangesAsync();

        var topic = new Topic { Name = "Kuvvet", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();

        var stA = new SubTopic { Name = "Sürtünme", TopicId = topic.Id };
        var stB = new SubTopic { Name = "Yerçekimi", TopicId = topic.Id };
        ctx.SubTopics.AddRange(stA, stB);
        var question = new Question { Text = "q", Point = 5, DifficultyLevel = 1 };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();

        return new World(question.Id, subject.Id, topic.Id, stA.Id, stB.Id);
    }

    [Fact]
    public async Task Unknown_question_fails()
    {
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateQuestionClassification(questionId: 404, subTopicIds: new[] { 1 });
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("bulunamadı");
    }

    [Fact]
    public async Task An_unknown_subtopic_id_is_rejected()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).UpdateQuestionClassification(
            w.QuestionId, subTopicIds: new[] { w.SubTopicA, 999_999 }, classificationSourceStr: "AI");

        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Geçersiz alt konu");

        await using var check = _db.NewContext();
        (await check.QuestionSubTopics.AnyAsync(x => x.QuestionId == w.QuestionId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Valid_subtopics_are_mapped_and_topic_subject_are_derived()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).UpdateQuestionClassification(
                w.QuestionId, subTopicIds: new[] { w.SubTopicA, w.SubTopicB },
                classificationSourceStr: "AI", difficulty: 6);
            r.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var q = await check.Questions.FirstAsync(x => x.Id == w.QuestionId);
        q.TopicId.ShouldBe(w.TopicId);
        q.SubjectId.ShouldBe(w.SubjectId);
        q.DifficultyLevel.ShouldBe(6);
        q.ClassificationSource.ShouldBe(ClassificationSource.AI);

        var mapped = await check.QuestionSubTopics.Where(x => x.QuestionId == w.QuestionId)
            .Select(x => x.SubTopicId).ToListAsync();
        mapped.ShouldBe(new[] { w.SubTopicA, w.SubTopicB }, ignoreOrder: true);
    }

    [Fact]
    public async Task Re_classifying_replaces_the_previous_subtopic_mappings()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpdateQuestionClassification(w.QuestionId, subTopicIds: new[] { w.SubTopicA });
        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpdateQuestionClassification(w.QuestionId, subTopicIds: new[] { w.SubTopicB });

        await using var check = _db.NewContext();
        var mapped = await check.QuestionSubTopics.Where(x => x.QuestionId == w.QuestionId)
            .Select(x => x.SubTopicId).ToListAsync();
        mapped.ShouldBe(new[] { w.SubTopicB });
    }

    [Fact]
    public async Task Defaults_the_classification_source_to_Human()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpdateQuestionClassification(w.QuestionId, subTopicIds: new[] { w.SubTopicA });

        await using var check = _db.NewContext();
        (await check.Questions.FirstAsync(x => x.Id == w.QuestionId)).ClassificationSource
            .ShouldBe(ClassificationSource.Human);
    }

    [Fact]
    public async Task An_unrecognised_classification_source_is_rejected()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateQuestionClassification(
            w.QuestionId, subTopicIds: new[] { w.SubTopicA }, classificationSourceStr: "Robot");
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Geçersiz sınıflandırma kaynağı");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Difficulty_out_of_range_is_rejected(int difficulty)
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateQuestionClassification(
            w.QuestionId, subTopicIds: new[] { w.SubTopicA }, difficulty: difficulty);
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("zorluk");
    }

    [Fact]
    public async Task An_explicit_unknown_subject_id_is_rejected_when_no_subtopics_given()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateQuestionClassification(w.QuestionId, subjectId: 777);
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Geçersiz ders");
    }

    [Fact]
    public async Task An_explicit_empty_subtopic_array_clears_existing_mappings()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpdateQuestionClassification(w.QuestionId, subTopicIds: new[] { w.SubTopicA });
        await using (var ctx = _db.NewContext())
            await NewService(ctx).UpdateQuestionClassification(w.QuestionId, subTopicIds: Array.Empty<int>());

        await using var check = _db.NewContext();
        (await check.QuestionSubTopics.AnyAsync(x => x.QuestionId == w.QuestionId)).ShouldBeFalse();
    }

    public void Dispose() => _db.Dispose();
}
