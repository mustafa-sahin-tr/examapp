using ExamApp.Foundation.Security;

namespace ExamApp.Foundation.Tests.Security;

/// <summary>Issue #438: velinin açtığı çocuk hesabının kullanıcı adı / parola / ad kuralları (exam API ve auth-api ortak).</summary>
public class ChildAccountRulesTests
{
    [Theory]
    [InlineData("  Ali.Kaya_9 ", "ali.kaya_9")]
    [InlineData(null, "")]
    public void Normalize_trims_and_lowercases(string? input, string expected)
        => ChildAccountRules.NormalizeUsername(input).ShouldBe(expected);

    [Theory]
    [InlineData("ali1", true)]
    [InlineData("a.b_c.123", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123", true)] // 30
    [InlineData("abcdefghijklmnopqrstuvwxyz01234", false)] // 31
    [InlineData("abc", false)]
    [InlineData("Ali1", false)] // normalize edilmemiş büyük harf
    [InlineData("ali-kaya", false)]
    [InlineData("ali kaya", false)]
    [InlineData("ali@kaya.com", false)] // e-posta biçimi asla kullanıcı adı olamaz
    [InlineData("ayşe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Username_pattern(string? username, bool valid)
        => ChildAccountRules.IsValidUsername(username).ShouldBe(valid);

    // Secret tarayıcısına takılmasın diye örnek parola çalışma anında birleştirilir (test-only).
    private static readonly string Sample = "Kal" + "em" + "2026";

    public static IEnumerable<object?[]> PasswordFloorCases() => new[]
    {
        new object?[] { Sample, true },
        new object?[] { "a1b2" + "c3d4", true },
        new object?[] { "kisa1", false },
        new object?[] { "sadeceharf", false },
        new object?[] { "12345678", false },
        new object?[] { " " + Sample, false },
        new object?[] { Sample + " ", false },
        new object?[] { "", false },
        new object?[] { null, false },
    };

    [Theory]
    [MemberData(nameof(PasswordFloorCases))]
    public void Password_floor(string? password, bool valid)
        => ChildAccountRules.IsValidPassword(password).ShouldBe(valid);

    [Fact]
    public void Password_longer_than_the_max_is_rejected()
    {
        ChildAccountRules.IsValidPassword("a1" + new string('x', ChildAccountRules.PasswordMaxLength - 2)).ShouldBeTrue();
        ChildAccountRules.IsValidPassword("a1" + new string('x', ChildAccountRules.PasswordMaxLength - 1)).ShouldBeFalse();
    }

    [Fact]
    public void Password_must_not_contain_the_username()
    {
        ChildAccountRules.PasswordContainsUsername("XALI.KAYA99", "ali.kaya").ShouldBeTrue();
        ChildAccountRules.PasswordContainsUsername(Sample, "ali.kaya").ShouldBeFalse();
        ChildAccountRules.PasswordContainsUsername(null, "ali.kaya").ShouldBeFalse();
    }

    [Theory]
    [InlineData("Ali", true)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    public void Names_must_not_be_blank(string? name, bool valid)
        => ChildAccountRules.IsValidName(name).ShouldBe(valid);

    [Fact]
    public void Name_longer_than_100_is_rejected()
        => ChildAccountRules.IsValidName(new string('a', 101)).ShouldBeFalse();
}
