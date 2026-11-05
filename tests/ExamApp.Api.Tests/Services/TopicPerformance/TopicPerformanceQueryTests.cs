using ExamApp.Api.Services.TopicPerformance;

namespace ExamApp.Api.Tests.Services.TopicPerformance;

/// <summary>Issue #767: veli ucunun eşik alt sınırı; öğrenci ucu 1'i kabul etmeye devam eder.</summary>
public class TopicPerformanceQueryTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Student_floor_is_one(int minQuestions, bool valid)
    {
        TopicPerformanceQuery.TryParse(30, "topic", minQuestions, out _, out var problem).ShouldBe(valid);
        (problem?.ErrorCode).ShouldBe(valid ? null : "InvalidMinQuestions");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void Parent_floor_is_five(int minQuestions, bool valid)
    {
        TopicPerformanceQuery.TryParse(30, "topic", minQuestions, out _, out var problem, TopicPerformanceQuery.ParentMinQuestions)
            .ShouldBe(valid);
        (problem?.ErrorCode).ShouldBe(valid ? null : "InvalidMinQuestions");
    }
}
