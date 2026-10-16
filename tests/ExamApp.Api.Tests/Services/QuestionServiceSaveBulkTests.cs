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

    // ---- Issue #454: per-question classification override ----

    private static BulkQuestionDto WithClassification(BulkQuestionDto q, BulkQuestionClassificationDto c)
    {
        q.Classification = c;
        return q;
    }

    private async Task<(int SubjectId, int TopicId, int SubTopicId)> SeedSecondSubjectAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = await ctx.Grades.FirstAsync();
        var subject = new Subject { Name = "Matematik" };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync();
        var topic = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();
        var st = new SubTopic { Name = "Denk kesirler", TopicId = topic.Id };
        ctx.SubTopics.Add(st);
        await ctx.SaveChangesAsync();
        return (subject.Id, topic.Id, st.Id);
    }

    private async Task<int> OtherTopicIdAsync(int subTopicId)
    {
        await using var ctx = _db.NewContext();
        return (await ctx.SubTopics.FirstAsync(s => s.Id == subTopicId)).TopicId;
    }

    [Fact]
    public async Task Per_question_classification_overrides_the_header()
    {
        var tax = await SeedTaxonomyAsync();
        var math = await SeedSecondSubjectAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicA } },
                WithClassification(Mcq("S1"), new() { SubjectId = math.SubjectId, TopicId = math.TopicId, SubTopicIds = new() { math.SubTopicId } }))))
                .Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.QuestionSubTopics).SingleAsync();
        q.SubjectId.ShouldBe(math.SubjectId);
        q.TopicId.ShouldBe(math.TopicId);
        q.QuestionSubTopics.ShouldHaveSingleItem().SubTopicId.ShouldBe(math.SubTopicId);
        q.ClassificationSource.ShouldBe(ClassificationSource.Human);
    }

    [Fact]
    public async Task Null_per_question_classification_keeps_the_header()
    {
        var tax = await SeedTaxonomyAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicB }, ClassificationSource = "AI" },
                Mcq("S1")))).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.QuestionSubTopics).SingleAsync();
        q.SubjectId.ShouldBe(tax.SubjectId);
        q.TopicId.ShouldBe(tax.TopicId);
        q.QuestionSubTopics.ShouldHaveSingleItem().SubTopicId.ShouldBe(tax.SubTopicB);
        q.ClassificationSource.ShouldBe(ClassificationSource.AI);
    }

    [Fact]
    public async Task Partial_override_falls_back_to_the_header_subject()
    {
        var tax = await SeedTaxonomyAsync();
        var otherTopicId = await OtherTopicIdAsync(tax.OtherTopicSubTopic);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicA } },
                WithClassification(Mcq("S1"), new() { SubjectId = 0, TopicId = otherTopicId, SubTopicIds = new() { tax.OtherTopicSubTopic } }))))
                .Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.QuestionSubTopics).SingleAsync();
        q.SubjectId.ShouldBe(tax.SubjectId);
        q.TopicId.ShouldBe(otherTopicId);
        q.QuestionSubTopics.ShouldHaveSingleItem().SubTopicId.ShouldBe(tax.OtherTopicSubTopic);
    }

    [Fact]
    public async Task A_different_per_question_topic_without_subtopics_does_not_inherit_header_subtopics()
    {
        var tax = await SeedTaxonomyAsync();
        var otherTopicId = await OtherTopicIdAsync(tax.OtherTopicSubTopic);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicA } },
                WithClassification(Mcq("S1"), new() { TopicId = otherTopicId }))))
                .Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.QuestionSubTopics).SingleAsync();
        q.TopicId.ShouldBe(otherTopicId);
        q.QuestionSubTopics.ShouldBeEmpty();
    }

    [Fact]
    public async Task Per_question_source_AI_is_stored_and_emitted_in_the_outbox_event()
    {
        var tax = await SeedTaxonomyAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId },
                WithClassification(Mcq("S1"), new() { Source = "ai" }),
                Mcq("S2")))).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var questions = await check.Questions.OrderBy(q => q.Id).ToListAsync();
        questions[0].ClassificationSource.ShouldBe(ClassificationSource.AI);
        questions[1].ClassificationSource.ShouldBe(ClassificationSource.Human);

        var events = (await check.OutboxMessages
                .Where(m => m.Type == OutboxEventRegistry.NameFor<QuestionCreatedEvent>())
                .ToListAsync())
            .Select(m => System.Text.Json.JsonSerializer.Deserialize<QuestionCreatedEvent>(m.Content)!)
            .ToDictionary(e => e.QuestionId);
        events[questions[0].Id].ClassificationSource.ShouldBe("AI");
        events[questions[1].Id].ClassificationSource.ShouldBe("Human");
    }

    [Fact]
    public async Task Rejects_a_per_question_subtopic_of_another_topic_and_saves_nothing()
    {
        var tax = await SeedTaxonomyAsync();

        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId },
                Mcq("S1"),
                WithClassification(Mcq("S2"), new() { TopicId = tax.TopicId, SubTopicIds = new() { tax.OtherTopicSubTopic } })));
            r.Success.ShouldBeFalse();
            r.Message.ShouldContain("question üzerinde setlenen TopicId'den farklı");
        }

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Rejects_a_per_question_topic_from_another_subject()
    {
        var tax = await SeedTaxonomyAsync();
        var math = await SeedSecondSubjectAsync();

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).SaveBulkQuestion(Dto(
            new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId },
            WithClassification(Mcq("S1"), new() { TopicId = math.TopicId })));
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("uyumsuz");
    }

    [Fact]
    public async Task Two_questions_with_different_topics_are_saved_with_their_own_topics()
    {
        var tax = await SeedTaxonomyAsync();
        var otherTopicId = await OtherTopicIdAsync(tax.OtherTopicSubTopic);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId },
                WithClassification(Mcq("S1"), new() { TopicId = tax.TopicId, SubTopicIds = new() { tax.SubTopicA, tax.SubTopicB }, Source = "AI" }),
                WithClassification(Mcq("S2"), new() { TopicId = otherTopicId, SubTopicIds = new() { tax.OtherTopicSubTopic }, Source = "AI" }))))
                .Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var questions = await check.Questions.Include(x => x.QuestionSubTopics).OrderBy(q => q.Id).ToListAsync();
        questions.Count.ShouldBe(2);
        questions[0].TopicId.ShouldBe(tax.TopicId);
        questions[0].QuestionSubTopics.Select(s => s.SubTopicId).OrderBy(x => x)
            .ShouldBe(new[] { tax.SubTopicA, tax.SubTopicB }.OrderBy(x => x));
        questions[1].TopicId.ShouldBe(otherTopicId);
        questions[1].QuestionSubTopics.ShouldHaveSingleItem().SubTopicId.ShouldBe(tax.OtherTopicSubTopic);
        questions.ShouldAllBe(q => q.SubjectId == tax.SubjectId && q.ClassificationSource == ClassificationSource.AI);
    }

    [Fact]
    public async Task Same_topic_as_header_without_subtopics_inherits_header_subtopics()
    {
        var tax = await SeedTaxonomyAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicA } },
                WithClassification(Mcq("S1"), new() { TopicId = tax.TopicId, SubTopicIds = new(), Source = "AI" }))))
                .Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.QuestionSubTopics).SingleAsync();
        q.TopicId.ShouldBe(tax.TopicId);
        q.QuestionSubTopics.ShouldHaveSingleItem().SubTopicId.ShouldBe(tax.SubTopicA);
        q.ClassificationSource.ShouldBe(ClassificationSource.AI);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("nonsense")]
    public async Task An_undefined_header_source_falls_back_to_Human(string source)
    {
        var tax = await SeedTaxonomyAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, ClassificationSource = source },
                Mcq("S1")))).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Questions.SingleAsync()).ClassificationSource.ShouldBe(ClassificationSource.Human);
    }

    [Fact]
    public async Task An_undefined_per_question_source_falls_back_to_the_header_source()
    {
        var tax = await SeedTaxonomyAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, ClassificationSource = "AI" },
                WithClassification(Mcq("S1"), new() { Source = "7" })))).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Questions.SingleAsync()).ClassificationSource.ShouldBe(ClassificationSource.AI);
    }

    [Fact]
    public async Task Rejects_an_unknown_header_subject_id()
    {
        await SeedTaxonomyAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).SaveBulkQuestion(Dto(
            new HeaderInfo { SubjectId = 777_777 }, Mcq()));
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Geçersiz SubjectId");
    }

    [Fact]
    public async Task Rejects_an_unknown_per_question_subject_id_and_saves_nothing()
    {
        var tax = await SeedTaxonomyAsync();

        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).SaveBulkQuestion(Dto(
                new HeaderInfo { SubjectId = tax.SubjectId },
                Mcq("S1"),
                WithClassification(Mcq("S2"), new() { SubjectId = 777_777 })));
            r.Success.ShouldBeFalse();
            r.Message.ShouldContain("Geçersiz SubjectId");
        }

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public void Subtopic_lists_are_capped_at_50_entries_by_model_validation()
    {
        static bool IsValid(object model) => System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            model, new System.ComponentModel.DataAnnotations.ValidationContext(model), new List<System.ComponentModel.DataAnnotations.ValidationResult>(),
            validateAllProperties: true);

        var fifty = Enumerable.Range(1, 50).ToList();
        var fiftyOne = Enumerable.Range(1, 51).ToList();

        IsValid(new BulkQuestionClassificationDto { SubTopicIds = fifty }).ShouldBeTrue();
        IsValid(new BulkQuestionClassificationDto { SubTopicIds = fiftyOne }).ShouldBeFalse();
        IsValid(new HeaderInfo { Subtopics = fifty }).ShouldBeTrue();
        IsValid(new HeaderInfo { Subtopics = fiftyOne }).ShouldBeFalse();
    }

    public void Dispose() => _db.Dispose();
}
