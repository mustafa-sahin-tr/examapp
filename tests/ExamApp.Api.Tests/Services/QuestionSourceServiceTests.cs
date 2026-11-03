using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #690: sorunun kaynağı, lisansı ve hak sahibine göre yayından kaldırma.</summary>
public class QuestionSourceServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private async Task<(int GradeId, int Question)> SeedQuestionAsync(string? holder = null, DateTime? licenseEndsAt = null)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();
        var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = subject.Id };
        ctx.Worksheets.Add(worksheet);
        var question = new Question { Text = "?", SubjectId = subject.Id, Point = 1, DifficultyLevel = 1, RightsHolder = holder, LicenseEndsAt = licenseEndsAt };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        var answer = new Answer { QuestionId = question.Id, Text = "ok", Tag = "A", Order = 0 };
        ctx.Answers.Add(answer);
        await ctx.SaveChangesAsync();
        question.CorrectAnswerId = answer.Id;
        ctx.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 });
        await ctx.SaveChangesAsync();
        return (grade.Id, question.Id);
    }

    private async Task<bool> InPoolAsync(int gradeId, int questionId)
    {
        await using var ctx = _db.NewContext();
        return await PracticeQuestionPool.Build(ctx, gradeId, Array.Empty<int>(), Array.Empty<int>()).AnyAsync(q => q.Id == questionId);
    }

    [Fact]
    public async Task NewQuestion_IsUnknown_NotMarkedAsLicensed()
    {
        var (_, id) = await SeedQuestionAsync();
        await using var ctx = _db.NewContext();
        (await ctx.Questions.FirstAsync(q => q.Id == id)).SourceType.ShouldBe(QuestionSourceType.Unknown);
    }

    [Fact]
    public async Task Assign_SetsFields_AndListFilters()
    {
        var (_, id) = await SeedQuestionAsync();
        await using var ctx = _db.NewContext();
        var service = new QuestionSourceService(ctx);

        (await service.AssignAsync(
            new QuestionSourceAssignDto { QuestionIds = [id], SourceType = "Publisher", RightsHolder = " Yayınevi ", LicenseReference = "S-1" },
            QuestionSourceType.Publisher)).ShouldBe(1);

        var listed = await service.ListAsync("Yayınevi", QuestionSourceType.Publisher, 1, 50);
        listed.Items.ShouldHaveSingleItem().LicenseReference.ShouldBe("S-1");
        (await service.ListAsync("Başka", null, 1, 50)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Unpublish_RemovesFromPool_WritesLog_AndCanBeUndone()
    {
        var (gradeId, id) = await SeedQuestionAsync("Yayınevi");
        (await InPoolAsync(gradeId, id)).ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(9);
            (await new QuestionSourceService(ctx).SetPublishedAsync(new QuestionSourcePublishDto { RightsHolder = "Yayınevi", Unpublished = true, Reason = "Şikâyet" })).ShouldBe(1);
        }
        (await InPoolAsync(gradeId, id)).ShouldBeFalse();

        await using (var ctx = _db.NewContext())
        {
            var log = await ctx.QuestionSourceActionLogs.SingleAsync();
            log.AffectedCount.ShouldBe(1);
            log.CreateUserId.ShouldBe(9);
            log.Reason.ShouldBe("Şikâyet");
            await new QuestionSourceService(ctx).SetPublishedAsync(new QuestionSourcePublishDto { RightsHolder = "Yayınevi", Unpublished = false });
        }
        (await InPoolAsync(gradeId, id)).ShouldBeTrue();
    }

    [Fact]
    public async Task ExpiredLicense_LeavesThePool_FutureOneStays()
    {
        var (expiredGrade, expired) = await SeedQuestionAsync("A", DateTime.UtcNow.AddDays(-1));
        var (futureGrade, future) = await SeedQuestionAsync("A", DateTime.UtcNow.AddDays(10));

        (await InPoolAsync(expiredGrade, expired)).ShouldBeFalse();
        (await InPoolAsync(futureGrade, future)).ShouldBeTrue();
    }

    public void Dispose() => _db.Dispose();
}
