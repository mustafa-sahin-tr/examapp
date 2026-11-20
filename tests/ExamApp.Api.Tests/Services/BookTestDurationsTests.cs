using ExamApp.Api.Services.BookImport;
using Shouldly;
using Xunit;

namespace ExamApp.Api.Tests.Services;

public class BookTestDurationsTests
{
    [Theory]
    [InlineData("3-sinif", "matematik", 10, 20)] // ilkokul: 2 dk / soru
    [InlineData("3-sinif", "turkce", 10, 20)]
    [InlineData("7-sinif", "turkce", 10, 15)] // sözel: 1,5 dk / soru (LGS sözel 50 soru 75 dk)
    [InlineData("7-sinif", "fen-bilimleri", 10, 20)] // sayısal: 2 dk / soru (LGS sayısal 40 soru 80 dk)
    [InlineData("11-sinif", "fizik", 12, 24)]
    [InlineData("hazirlik", "ingilizce", 13, 20)] // 19,5 yukarı yuvarlanır
    [InlineData(null, null, 4, 6)] // bilinmiyorsa sözel ölçü
    public void Default_minutes_follow_the_grade_level_and_subject(string? grade, string? subject, int questions, int minutes)
    {
        BookTestDurations.DefaultMinutes(grade, subject, questions).ShouldBe(minutes);
    }

    [Fact]
    public void A_test_without_a_question_count_stays_untimed_and_long_tests_are_capped()
    {
        BookTestDurations.DefaultMinutes("7-sinif", "turkce", 0).ShouldBeNull();
        BookTestDurations.DefaultMinutes("3-sinif", "matematik", 1000).ShouldBe(600);
    }
}
