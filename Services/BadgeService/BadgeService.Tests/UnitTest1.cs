using System;
using System.Threading;
using System.Threading.Tasks;
using BadgeService;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BadgeService.Tests.Services;

public class AnswerSubmissionAggregationServiceTests
{
    [Fact]
    public async Task ProcessAsync_CreatesDailyActivityForNewSubmission()
    {
        var options = new DbContextOptionsBuilder<BadgeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new BadgeDbContext(options);
        var service = new AnswerSubmissionAggregationService(context);

        var submittedAt = new DateTime(2025, 10, 31, 9, 30, 0, DateTimeKind.Utc);
        var message = new AnswerSubmittedEvent
        {
            UserId = 42,
            SubmittedAt = submittedAt,
            TimeTakenInSeconds = 120,
            IsCorrect = true,
            QuestionPoint = 20,
            SubjectId = 5,
            Subject = "Mathematics",
        };

        var beforeCall = DateTime.UtcNow;
        await service.ProcessAsync(message, CancellationToken.None);
        var afterCall = DateTime.UtcNow;

        var activity = await context.StudentDailyActivities
            .SingleAsync(x => x.UserId == message.UserId && x.ActivityDate == submittedAt.Date);

        Assert.Equal(1, activity.QuestionCount);
        Assert.Equal(1, activity.CorrectCount);
        Assert.Equal(120, activity.TotalTimeSeconds);
        Assert.Equal(20, activity.TotalPoints);

        var expectedScore = (activity.QuestionCount * 10)
            + (activity.CorrectCount * 5)
            + Math.Min(activity.TotalTimeSeconds / 60, 60);
        Assert.Equal(expectedScore, activity.ActivityScore);

        Assert.Equal(submittedAt.Date, activity.ActivityDate);
        Assert.InRange(activity.LastUpdatedUtc, beforeCall, afterCall);
    }
}