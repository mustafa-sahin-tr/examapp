using ExamApp.Foundation.Security;

namespace ExamApp.Foundation.Tests.Security;

/// <summary>Issue #440: öğrencinin verdiği veli iletişim bilgisi kuralları (e-posta, TR cep telefonu, ad) — exam API ve auth-api ortak.</summary>
public class ParentContactRulesTests
{
    [Theory]
    [InlineData(" Veli.Ad@Example.COM ", "veli.ad@example.com")]
    [InlineData("a@b.co", "a@b.co")]
    public void Valid_emails_are_trimmed_and_lowercased(string input, string expected)
        => ParentContactRules.NormalizeEmail(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("veli")]
    [InlineData("veli@")]
    [InlineData("veli@example")]
    [InlineData("veli @example.com")]
    [InlineData("veli@@example.com")]
    [InlineData("veli@example.c")]
    [InlineData("Ayşe <veli@example.com>")]
    public void Invalid_emails_are_rejected(string? input)
        => ParentContactRules.NormalizeEmail(input).ShouldBeNull();

    [Fact]
    public void Overlong_email_is_rejected()
        => ParentContactRules.NormalizeEmail(new string('a', 250) + "@x.com").ShouldBeNull();

    [Theory]
    [InlineData("05321234567", "+905321234567")]
    [InlineData("5321234567", "+905321234567")]
    [InlineData("+905321234567", "+905321234567")]
    [InlineData("905321234567", "+905321234567")]
    [InlineData("0532 123 45 67", "+905321234567")]
    [InlineData("(0532) 123-45-67", "+905321234567")]
    [InlineData(" +90 532 123 45 67 ", "+905321234567")]
    public void Turkish_mobile_formats_normalize_to_e164(string input, string expected)
        => ParentContactRules.NormalizePhone(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("02121234567")] // sabit hat
    [InlineData("0532123456")] // eksik hane
    [InlineData("053212345678")] // fazla hane
    [InlineData("+15321234567")] // başka ülke
    [InlineData("0532abc4567")]
    [InlineData("+90 212 123 45 67")]
    public void Non_mobile_or_malformed_phones_are_rejected(string? input)
        => ParentContactRules.NormalizePhone(input).ShouldBeNull();

    [Theory]
    [InlineData("  Ayşe   Nur  Yılmaz ", "Ayşe Nur Yılmaz")]
    [InlineData("Al", "Al")]
    [InlineData("Ayşe-Nur O'Brien", "Ayşe-Nur O'Brien")]
    [InlineData("A. Kaya", "A. Kaya")]
    public void Names_are_trimmed_and_collapsed(string input, string expected)
        => ParentContactRules.NormalizeName(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("1234")]
    [InlineData("http://spam.example")]
    [InlineData("Kazan www.bedava")]
    [InlineData("Ayşe bedava.com")]
    [InlineData("Ayşe site.com/kampanya")]
    [InlineData("Ayşe <b>")]
    [InlineData("Ayşe 2026")]
    public void Invalid_names_are_rejected(string? input)
        => ParentContactRules.NormalizeName(input).ShouldBeNull();

    [Fact]
    public void Name_longer_than_100_is_rejected()
        => ParentContactRules.NormalizeName(new string('a', 101)).ShouldBeNull();

    [Theory]
    [InlineData("Ayşe Nur Yılmaz", "Ayşe Nur", "Yılmaz")]
    [InlineData("Ayşe", "Ayşe", "")]
    public void Split_name_takes_the_last_word_as_last_name(string full, string first, string last)
        => ParentContactRules.SplitName(full).ShouldBe((first, last));
}
