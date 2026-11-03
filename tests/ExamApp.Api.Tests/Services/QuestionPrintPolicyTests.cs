using ExamApp.Api.Data;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #701: testi yazdırırken lisansa göre basılamayan sorular ve şık sırası.</summary>
public class QuestionPrintPolicyTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();

    [Theory]
    [InlineData(QuestionSourceType.Unknown)]
    [InlineData(QuestionSourceType.Commissioned)]
    [InlineData(QuestionSourceType.Teacher)]
    public void OwnOrUnknownSource_IsPrintable(QuestionSourceType source)
    {
        QuestionPrintPolicy.BlockReason(source, false, null, Now).ShouldBeNull();
        QuestionPrintPolicy.BlockReason(source, false, Now.AddDays(1), Now).ShouldBeNull();
    }

    [Fact]
    public void PublisherQuestion_IsNotPrintable_EvenWithValidLicense()
    {
        QuestionPrintPolicy.BlockReason(QuestionSourceType.Publisher, false, Now.AddDays(1), Now)
            .ShouldBe(QuestionPrintPolicy.PublisherLicense);
    }

    [Fact]
    public void ExpiredLicense_IsNotPrintable()
    {
        QuestionPrintPolicy.BlockReason(QuestionSourceType.Commissioned, false, Now, Now)
            .ShouldBe(QuestionPrintPolicy.LicenseExpired);
    }

    [Fact]
    public void Unpublished_WinsOverOtherReasons()
    {
        QuestionPrintPolicy.BlockReason(QuestionSourceType.Publisher, true, Now.AddDays(-1), Now)
            .ShouldBe(QuestionPrintPolicy.Unpublished);
    }

    [Fact]
    public async Task QuestionsByTest_ReportPrintBlock_AndKeepAnswerOrder()
    {
        int worksheetId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "7" };
            var subject = new Subject { Name = "Matematik" };
            ctx.AddRange(grade, subject);
            await ctx.SaveChangesAsync();
            var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = subject.Id };
            var open = new Question { Text = "open", SubjectId = subject.Id, Point = 1, DifficultyLevel = 1 };
            var licensed = new Question
            {
                Text = "licensed", SubjectId = subject.Id, Point = 1, DifficultyLevel = 1,
                SourceType = QuestionSourceType.Publisher, RightsHolder = "Publisher",
            };
            ctx.Worksheets.Add(worksheet);
            ctx.Questions.AddRange(open, licensed);
            await ctx.SaveChangesAsync();
            ctx.Answers.AddRange(
                new Answer { QuestionId = open.Id, Text = "second", Order = 1 },
                new Answer { QuestionId = open.Id, Text = "first", Order = 0 });
            ctx.TestQuestions.AddRange(
                new WorksheetQuestion { TestId = worksheet.Id, QuestionId = open.Id, Order = 1 },
                new WorksheetQuestion { TestId = worksheet.Id, QuestionId = licensed.Id, Order = 2 });
            await ctx.SaveChangesAsync();
            worksheetId = worksheet.Id;
        }

        await using var read = _db.NewContext();
        var questions = await new QuestionQueryService(read).GetQuestionByTestId(worksheetId);

        questions[0].PrintBlock.ShouldBeNull();
        questions[0].Answers.Select(a => a.Text).ShouldBe(new[] { "first", "second" });
        questions[1].PrintBlock.ShouldBe(QuestionPrintPolicy.PublisherLicense);
    }

    public void Dispose() => _db.Dispose();
}
