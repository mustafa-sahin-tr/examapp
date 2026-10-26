using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573: "Yanlışlarımdan test üret" (<c>CreateWorksheetFromMistakesAsync</c>) her zaman <c>Kind = Test</c> sayfası üretir;
/// kaynak etkinliğin türü kopyalanmaz (etkinlik kopyası değil, çalışma testi).
/// </summary>
public class WorksheetKindFromMistakesTests : IDisposable
{
    private const int StudentUserId = 4801;

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    /// <summary>Bitmiş (Completed) bir oturum: bir yanlış MCQ cevabı. Sayfa verilen türde.</summary>
    private async Task<(int InstanceId, int StudentId)> SeedFinishedInstanceAsync(WorksheetKind kind)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = StudentUserId, StudentNumber = "S4801", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Kaynak", Description = "", GradeId = grade.Id, MaxDurationSeconds = 0, Kind = kind };
        var question = new Question { Text = "2+2", Point = 10 };
        ctx.AddRange(student, worksheet, question);
        await ctx.SaveChangesAsync();

        var right = new Answer { QuestionId = question.Id, Text = "4", Tag = "A" };
        var wrong = new Answer { QuestionId = question.Id, Text = "5", Tag = "B" };
        ctx.Answers.AddRange(right, wrong);
        await ctx.SaveChangesAsync();
        question.CorrectAnswerId = right.Id;
        await ctx.SaveChangesAsync();

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        ctx.TestQuestions.Add(wq);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Completed,
            StartTime = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2020, 1, 2, 0, 5, 0, DateTimeKind.Utc),
            MaxDurationSeconds = 0,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
        {
            WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id, SelectedAnswerId = wrong.Id,
        });
        await ctx.SaveChangesAsync();
        return (instance.Id, student.Id);
    }

    [Theory]
    [InlineData(WorksheetKind.Test)]
    [InlineData(WorksheetKind.Activity)]
    public async Task CreateWorksheetFromMistakes_AnySourceKind_ProducesTestKindPracticeSheet(WorksheetKind sourceKind)
    {
        var (instanceId, studentId) = await SeedFinishedInstanceAsync(sourceKind);

        WorksheetFromMistakesResultDto? result;
        await using (var ctx = _db.NewContext())
            result = await new WorksheetDetailService(ctx, Substitute.For<IAuthApiClient>())
                .CreateWorksheetFromMistakesAsync(instanceId, studentId, StudentUserId);

        result.ShouldNotBeNull();
        await using var check = _db.NewContext();
        var produced = await check.Worksheets.AsNoTracking().SingleAsync(w => w.Id == result!.WorksheetId);
        produced.Kind.ShouldBe(WorksheetKind.Test);
        produced.IsPracticeTest.ShouldBeTrue();
    }
}
