using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Covers <see cref="QuestionService.SaveBulkQuestion"/> — the canvas/YOLO bulk import path.
/// Tests run without <c>ImageData</c> so the crop/upload branch is skipped and the focus stays
/// on taxonomy validation + entity creation.
/// </summary>
public class QuestionServiceSaveBulkTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    private sealed record Tax(int SubjectId, int TopicId, int SubTopicA, int SubTopicB, int OtherTopicSubTopic);

    private async Task<Tax> SeedTaxonomyAsync()
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        var grade = new Grade { Name = "6" };
        ctx.AddRange(subject, grade);
        await ctx.SaveChangesAsync();

        var topic = new Topic { Name = "Kuvvet", SubjectId = subject.Id, GradeId = grade.Id };
        var otherTopic = new Topic { Name = "Işık", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.AddRange(topic, otherTopic);
        await ctx.SaveChangesAsync();

        var a = new SubTopic { Name = "Sürtünme", TopicId = topic.Id };
        var b = new SubTopic { Name = "Yerçekimi", TopicId = topic.Id };
        var c = new SubTopic { Name = "Gölge", TopicId = otherTopic.Id };
        ctx.AddRange(a, b, c);
        await ctx.SaveChangesAsync();
        return new Tax(subject.Id, topic.Id, a.Id, b.Id, c.Id);
    }

    private static BulkQuestionCreateDto Dto(HeaderInfo header, params BulkQuestionDto[] questions) => new()
    {
        ImageData = null!,
        Passages = new List<BulkPassageDto>(),
        Questions = questions.ToList(),
        Header = header,
    };

    private static BulkQuestionDto Mcq(string name = "S1", string correct = "A") => new()
    {
        Name = name,
        InteractionType = "mcq",
        Answers = new List<BulkAnswerDto>
        {
            new() { Label = "A", IsCorrect = correct == "A" },
            new() { Label = "B", IsCorrect = correct == "B" },
        },
    };

    [Fact]
    public async Task Creates_questions_answers_correct_answer_link_and_one_outbox_event_per_question()
    {
        var tax = await SeedTaxonomyAsync();

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicA } },
                Mcq("S1", "A"), Mcq("S2", "B")));

        result.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var questions = await check.Questions.Include(q => q.Answers).Include(q => q.QuestionSubTopics).ToListAsync();
        questions.Count.ShouldBe(2);
        questions.ShouldAllBe(q => q.SubjectId == tax.SubjectId && q.TopicId == tax.TopicId);
        questions.ShouldAllBe(q => q.CorrectAnswerId != null);
        questions[0].QuestionSubTopics.ShouldHaveSingleItem().SubTopicId.ShouldBe(tax.SubTopicA);
        (await check.OutboxMessages.CountAsync(m => m.Type == OutboxEventRegistry.NameFor<QuestionCreatedEvent>())).ShouldBe(2);
    }

    [Fact]
    public async Task Inherits_missing_classification_from_the_worksheet()
    {
        var tax = await SeedTaxonomyAsync();
        int worksheetId;
        await using (var ctx = _db.NewContext())
        {
            var grade = await ctx.Grades.FirstAsync();
            var ws = new Worksheet
            {
                Name = "W", Description = "", GradeId = grade.Id,
                SubjectId = tax.SubjectId, TopicId = tax.TopicId, SubTopicId = tax.SubTopicB,
            };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            worksheetId = ws.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { TestId = worksheetId }, Mcq()));
            r.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.QuestionSubTopics).FirstAsync();
        q.SubjectId.ShouldBe(tax.SubjectId);
        q.TopicId.ShouldBe(tax.TopicId);
        q.QuestionSubTopics.ShouldHaveSingleItem().SubTopicId.ShouldBe(tax.SubTopicB);
        (await check.TestQuestions.CountAsync(tq => tq.TestId == worksheetId)).ShouldBe(1);
    }

    [Fact]
    public async Task Rejects_an_unknown_topic_id()
    {
        var tax = await SeedTaxonomyAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).SaveBulkQuestion(Dto(
            new HeaderInfo { SubjectId = tax.SubjectId, TopicId = 999_999 }, Mcq()));
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Geçersiz TopicId");
    }

    [Fact]
    public async Task Rejects_a_subject_that_does_not_own_the_topic()
    {
        var tax = await SeedTaxonomyAsync();
        int foreignSubjectId;
        await using (var ctx = _db.NewContext())
        {
            var s = new Subject { Name = "Matematik" };
            ctx.Subjects.Add(s);
            await ctx.SaveChangesAsync();
            foreignSubjectId = s.Id;
        }

        await using var ctx2 = _db.NewContext();
        var r = await NewService(ctx2).SaveBulkQuestion(Dto(
            new HeaderInfo { SubjectId = foreignSubjectId, TopicId = tax.TopicId }, Mcq()));
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("uyumsuz");
    }

    [Fact]
    public async Task Rejects_unknown_subtopic_ids()
    {
        var tax = await SeedTaxonomyAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).SaveBulkQuestion(Dto(
            new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicA, 888 } },
            Mcq()));
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Geçersiz SubTopicId");
    }

    [Fact]
    public async Task Rejects_subtopics_that_span_more_than_one_topic()
    {
        var tax = await SeedTaxonomyAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).SaveBulkQuestion(Dto(
            new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicA, tax.OtherTopicSubTopic } },
            Mcq()));
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("birden fazla TopicId");
    }

    [Fact]
    public async Task Rejects_a_subtopic_whose_topic_differs_from_the_header_topic()
    {
        var tax = await SeedTaxonomyAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).SaveBulkQuestion(Dto(
            new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.OtherTopicSubTopic } },
            Mcq()));
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("question üzerinde setlenen TopicId'den farklı");
    }

    [Fact]
    public async Task Rejects_an_mcq_question_without_a_correct_answer()
    {
        var tax = await SeedTaxonomyAsync();
        var bad = new BulkQuestionDto
        {
            Name = "S1", InteractionType = "mcq",
            Answers = new List<BulkAnswerDto> { new() { Label = "A" }, new() { Label = "B" } },
        };

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).SaveBulkQuestion(Dto(
            new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId }, bad));
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Doğru cevap belirtilmemiş");
    }

    [Fact]
    public async Task A_dragDropLabeling_question_does_not_require_a_correct_answer()
    {
        var tax = await SeedTaxonomyAsync();
        var labeling = new BulkQuestionDto
        {
            Name = "S1", InteractionType = "dragDropLabeling",
            Answers = new List<BulkAnswerDto> { new() { Label = "kalp" }, new() { Label = "akciğer" } },
        };

        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId }, labeling));
            r.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        (await check.Questions.FirstAsync()).CorrectAnswerId.ShouldBeNull();
    }

    [Fact]
    public async Task An_example_question_stores_the_practice_answer_and_no_answer_rows()
    {
        var tax = await SeedTaxonomyAsync();
        var example = new BulkQuestionDto { Name = "S1", IsExample = true, ExampleAnswer = "C", Answers = new() };

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId }, example))).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.Answers).FirstAsync();
        q.IsExample.ShouldBeTrue();
        q.PracticeCorrectAnswer.ShouldBe("C");
        q.Answers.ShouldBeEmpty();
    }

    public void Dispose() => _db.Dispose();
}
