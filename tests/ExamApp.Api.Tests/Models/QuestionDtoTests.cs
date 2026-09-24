using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Tests.Models;

/// <summary>
/// issue #279 review (security L2): <c>QuestionDto.Point</c> upper bound — matches
/// <c>AnswerPointOptions.MaxQuestionPoint</c>'s default (BadgeService consumer-side cap) so a malicious/
/// buggy client can't create a question whose point value inflates a student's score far beyond what the
/// consumer-side cap alone would still allow through (100 &gt; the UI's own 1-20 input range, kept
/// generous). <c>[ApiController]</c> on <c>QuestionsController</c> runs DataAnnotations validation before
/// the action executes and short-circuits to 400 — this exercises that contract directly.
/// </summary>
public class QuestionDtoTests
{
    private static IList<ValidationResult> Validate(QuestionDto dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateProperty(dto.Point, new ValidationContext(dto) { MemberName = nameof(QuestionDto.Point) }, results);
        return results;
    }

    private static QuestionDto Dto(int point) => new()
    {
        Text = "q",
        CategoryName = "c",
        Point = point,
    };

    [Fact]
    public void Point_within_range_is_valid()
        => Validate(Dto(20)).ShouldBeEmpty();

    [Fact]
    public void Point_at_the_upper_bound_is_valid()
        => Validate(Dto(100)).ShouldBeEmpty();

    [Fact]
    public void Point_above_the_upper_bound_is_rejected()
        => Validate(Dto(101)).ShouldNotBeEmpty();

    [Fact]
    public void Point_far_above_the_upper_bound_is_rejected()
        => Validate(Dto(999_999)).ShouldNotBeEmpty();

    [Fact]
    public void Negative_point_is_rejected()
        => Validate(Dto(-1)).ShouldNotBeEmpty();

    [Fact]
    public void Zero_point_is_valid()
        => Validate(Dto(0)).ShouldBeEmpty();
}
