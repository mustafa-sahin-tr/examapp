using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Tests.Services;

/// <summary>issue #396: one rule for "finished" (Completed or Expired) vs. "completed with score" (Completed).</summary>
public class WorksheetInstanceStatusRulesTests
{
    [Theory]
    [InlineData(WorksheetInstanceStatus.Started, false)]
    [InlineData(WorksheetInstanceStatus.Completed, true)]
    [InlineData(WorksheetInstanceStatus.Expired, true)]
    public void IsFinished_covers_completed_and_expired(WorksheetInstanceStatus status, bool finished)
    {
        WorksheetInstanceStatusRules.IsFinished(status).ShouldBe(finished);
        WorksheetInstanceStatusRules.IsFinished((WorksheetInstanceStatus?)status).ShouldBe(finished);
        WorksheetInstanceStatusRules.Finished.Compile()(new WorksheetInstance { Status = status }).ShouldBe(finished);
        WorksheetInstanceStatusRules.IsFinished((WorksheetInstanceStatus?)null).ShouldBeFalse();
    }

    [Theory]
    [InlineData(WorksheetInstanceStatus.Completed, true)]
    [InlineData(WorksheetInstanceStatus.Expired, true)]   // cannot be retaken → satisfies a later assignment ("süresi doldu")
    [InlineData(WorksheetInstanceStatus.Started, false)]  // an open session must have started inside the window
    public void A_finished_session_from_before_the_assignment_window_counts(WorksheetInstanceStatus status, bool counts)
    {
        var windowStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        AssignmentInstanceWindow.Counts(windowStart.AddDays(-2), status, windowStart, windowStart.AddDays(7)).ShouldBe(counts);
    }

    [Fact]
    public void AssignedWorksheetDto_separates_finished_from_completed()
    {
        var expired = new AssignedWorksheetDto { InstanceStatus = WorksheetInstanceStatus.Expired };
        expired.IsFinished.ShouldBeTrue();
        expired.IsCompleted.ShouldBeFalse();

        var completed = new AssignedWorksheetDto { InstanceStatus = WorksheetInstanceStatus.Completed };
        completed.IsFinished.ShouldBeTrue();
        completed.IsCompleted.ShouldBeTrue();

        new AssignedWorksheetDto { InstanceStatus = WorksheetInstanceStatus.Started }.IsFinished.ShouldBeFalse();
        new AssignedWorksheetDto().IsFinished.ShouldBeFalse();
    }
}
