using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// Issue #246: liste görünümünde e-posta maskesi — yerel kısmın ilk karakteri + *** + @ + domain;
/// eksik/bozuk girdide hiçbir parça sızmaz.
/// </summary>
public class EmailMaskTests
{
    [Theory]
    [InlineData("ali@x.com", "a***@x.com")]
    [InlineData("ali.veli@okul.k12.tr", "a***@okul.k12.tr")]
    [InlineData("  Ayse@Okul.k12.tr  ", "A***@Okul.k12.tr")]     // kırpılır, harf büyüklüğü korunur
    [InlineData("ab@x.com", "a***@x.com")]
    [InlineData("\"a@b\"@x.com", "\"***@x.com")]                 // ayırıcı son @
    [InlineData("\U0001F600abc@x.com", "\U0001F600***@x.com")]   // surrogate çifti bölünmez
    public void Masks_local_part_keeping_first_character_and_domain(string input, string expected)
        => EmailMask.Apply(input).ShouldBe(expected);

    [Fact]
    public void Single_character_local_part_is_fully_hidden()
        => EmailMask.Apply("a@x.com").ShouldBe("***@x.com");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_email_stays_empty_string(string? input)
        => EmailMask.Apply(input).ShouldBe(string.Empty);

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("@x.com")]
    [InlineData("ali@")]
    [InlineData("@")]
    public void Malformed_email_leaks_nothing(string input)
        => EmailMask.Apply(input).ShouldBe("***");
}
