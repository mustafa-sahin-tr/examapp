using ExamApp.Api.Data;

using ExamApp.Api.Services;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

public class QuestionQueryServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private QuestionQueryService NewService(AppDbContext ctx) => new(ctx);
    private QuestionClassificationService NewClassification(AppDbContext ctx) => new(ctx);

    // ---- GetQuestionById ----

    [Fact]
    public async Task GetQuestionById_returns_null_when_missing()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetQuestionById(404)).ShouldBeNull();
    }

    [Fact]
    public async Task GetQuestionById_projects_subject_answers_and_subtopics()
    {
        int qId;
        await using (var ctx = _db.NewContext())
        {
            var subject = new Subject { Name = "Türkçe" };
            var grade = new Grade { Name = "4" };
            ctx.AddRange(subject, grade);
            await ctx.SaveChangesAsync();
            var topic = new Topic { Name = "T", SubjectId = subject.Id, GradeId = grade.Id };
            ctx.Topics.Add(topic);
            await ctx.SaveChangesAsync();
            var st = new SubTopic { Name = "ST", TopicId = topic.Id };
            ctx.SubTopics.Add(st);
            var q = new Question { Text = "soru", SubjectId = subject.Id, Point = 5 };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            qId = q.Id;
            ctx.Answers.Add(new Answer { QuestionId = q.Id, Text = "cevap" });
            ctx.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = q.Id, SubTopicId = st.Id });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var dto = await NewService(ctx2).GetQuestionById(qId);
        dto.ShouldNotBeNull();
        dto!.CategoryName.ShouldBe("Türkçe");
        dto.Answers.ShouldHaveSingleItem().Text.ShouldBe("cevap");
        dto.SubTopics.ShouldHaveSingleItem().Name.ShouldBe("ST");
    }

    // ---- GetQuestionByTestId ----

    [Fact]
    public async Task GetQuestionByTestId_returns_ordered_non_deleted_questions()
    {
        int testId;
        await using (var ctx = _db.NewContext())
        {
            var subject = new Subject { Name = "Mat" };
            var grade = new Grade { Name = "5" };
            ctx.AddRange(subject, grade);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
            var q1 = new Question { Text = "q1", SubjectId = subject.Id };
            var q2 = new Question { Text = "q2", SubjectId = subject.Id };
            var q3 = new Question { Text = "q3", SubjectId = subject.Id };
            ctx.AddRange(ws, q1, q2, q3);
            await ctx.SaveChangesAsync();
            testId = ws.Id;
            ctx.TestQuestions.AddRange(
                new WorksheetQuestion { TestId = testId, QuestionId = q2.Id, Order = 2 },
                new WorksheetQuestion { TestId = testId, QuestionId = q1.Id, Order = 1 },
                new WorksheetQuestion { TestId = testId, QuestionId = q3.Id, Order = 3, IsDeleted = true });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var list = await NewService(ctx2).GetQuestionByTestId(testId);
        list.Select(q => q.Text).ShouldBe(new[] { "q1", "q2" });
    }

    // ---- GetLastTenPassages ----

    [Fact]
    public async Task GetLastTenPassages_returns_the_ten_newest()
    {
        await using (var ctx = _db.NewContext())
        {
            for (var i = 1; i <= 12; i++)
                ctx.Passage.Add(new Passage { Title = $"P{i}", Text = "t" });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var passages = await NewService(ctx2).GetLastTenPassages();
        passages.Count.ShouldBe(10);
        passages.First().Title.ShouldBe("P12");
    }

    // ---- UpdateCorrectAnswer ----

    [Fact]
    public async Task UpdateCorrectAnswer_validates_the_question_and_that_the_answer_belongs_to_it()
    {
        int qId, ownAnswerId, foreignAnswerId;
        await using (var ctx = _db.NewContext())
        {
            var q = new Question { Text = "q" };
            var other = new Question { Text = "other" };
            ctx.AddRange(q, other);
            await ctx.SaveChangesAsync();
            qId = q.Id;
            var a1 = new Answer { QuestionId = q.Id, Text = "a1" };
            var a2 = new Answer { QuestionId = other.Id, Text = "foreign" };
            ctx.Answers.AddRange(a1, a2);
            await ctx.SaveChangesAsync();
            ownAnswerId = a1.Id;
            foreignAnswerId = a2.Id;
        }

        await using var ctx2 = _db.NewContext();
        var svc = NewClassification(ctx2);

        (await svc.UpdateCorrectAnswer(9999, ownAnswerId)).Message.ShouldContain("bulunamadı");
        (await svc.UpdateCorrectAnswer(qId, foreignAnswerId)).Message.ShouldContain("ait değil");

        var ok = await svc.UpdateCorrectAnswer(qId, ownAnswerId);
        ok.Success.ShouldBeTrue();
        (await _db.NewContext().Questions.FindAsync(qId))!.CorrectAnswerId.ShouldBe(ownAnswerId);
    }

    // ---- RemoveQuestionFromTest ----

    [Fact]
    public async Task RemoveQuestionFromTest_soft_deletes_the_link()
    {
        int testId, questionId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "5" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
            var q = new Question { Text = "q" };
            ctx.AddRange(ws, q);
            await ctx.SaveChangesAsync();
            testId = ws.Id; questionId = q.Id;
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = ws.Id, QuestionId = q.Id, Order = 1 });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            (await NewClassification(ctx).RemoveQuestionFromTest(testId, questionId)).Success.ShouldBeTrue();
            (await NewClassification(ctx).RemoveQuestionFromTest(testId, questionId)).Success.ShouldBeFalse(); // already gone
        }
    }

    public void Dispose() => _db.Dispose();
}
