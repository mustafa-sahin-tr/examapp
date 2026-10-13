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

    [Theory]
    [InlineData("Kalem2026", true)]
    [InlineData("a1b2c3d4", true)]
    [InlineData("kisa1", false)]
    [InlineData("sadeceharf", false)]
    [InlineData("12345678", false)]
    [InlineData(" Kalem2026", false)]
    [InlineData("Kalem2026 ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
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
        ChildAccountRules.PasswordContainsUsername("Kalem2026", "ali.kaya").ShouldBeFalse();
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
