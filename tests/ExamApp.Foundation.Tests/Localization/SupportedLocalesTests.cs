using ExamApp.Foundation.Localization;

namespace ExamApp.Foundation.Tests.Localization;

/// <summary>
/// Tests for <see cref="SupportedLocales"/> (issue #181): normalized language codes,
/// culture name mapping, and validation.
/// </summary>
public class SupportedLocalesTests
{
    // ---- Normalize: valid locales ----

    [Theory]
    [InlineData("tr")]
    [InlineData("TR")]
    [InlineData("Tr")]
    public void Normalize_accepts_case_insensitive_language_code(string locale)
        => SupportedLocales.Normalize(locale).ShouldBe("tr");

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("tr-tr")]
    [InlineData("tr_TR")]
    [InlineData("tr_tr")]
    public void Normalize_strips_region_suffix_from_language_code(string locale)
        => SupportedLocales.Normalize(locale).ShouldBe("tr");

    [Fact]
    public void Normalize_handles_en_US_format()
        => SupportedLocales.Normalize("en-US").ShouldBe("en");

    [Fact]
    public void Normalize_handles_en_GB_format()
        => SupportedLocales.Normalize("en-GB").ShouldBe("en");

    [Fact]
    public void Normalize_trims_whitespace()
        => SupportedLocales.Normalize(" tr ").ShouldBe("tr");

    // ---- Normalize: invalid/empty values default to Default ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_null_or_whitespace_returns_default(string? locale)
        => SupportedLocales.Normalize(locale).ShouldBe(SupportedLocales.Default);

    [Fact]
    public void Normalize_unsupported_language_returns_default()
        => SupportedLocales.Normalize("de").ShouldBe(SupportedLocales.Default);

    [Fact]
    public void Normalize_nonsense_value_returns_default()
        => SupportedLocales.Normalize("xyz-ABC").ShouldBe(SupportedLocales.Default);

    // ---- TryNormalize: success cases ----

    [Theory]
    [InlineData("tr")]
    [InlineData("TR")]
    [InlineData("tr-TR")]
    public void TryNormalize_supported_locale_returns_true(string locale)
        => SupportedLocales.TryNormalize(locale, out _).ShouldBeTrue();

    [Fact]
    public void TryNormalize_en_returns_true_and_normalized()
    {
        var result = SupportedLocales.TryNormalize("en-US", out var normalized);
        result.ShouldBeTrue();
        normalized.ShouldBe("en");
    }

    [Fact]
    public void TryNormalize_tr_uppercase_returns_true_and_lowercase()
    {
        var result = SupportedLocales.TryNormalize("TR", out var normalized);
        result.ShouldBeTrue();
        normalized.ShouldBe("tr");
    }

    // ---- TryNormalize: failure cases ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_null_or_whitespace_returns_false(string? locale)
        => SupportedLocales.TryNormalize(locale, out _).ShouldBeFalse();

    [Fact]
    public void TryNormalize_unsupported_language_returns_false()
        => SupportedLocales.TryNormalize("de", out _).ShouldBeFalse();

    [Fact]
    public void TryNormalize_de_DE_returns_false()
        => SupportedLocales.TryNormalize("de-DE", out _).ShouldBeFalse();

    [Fact]
    public void TryNormalize_failure_sets_normalized_to_default()
    {
        SupportedLocales.TryNormalize("de", out var normalized);
        normalized.ShouldBe(SupportedLocales.Default);
    }

    // ---- ToCultureName ----

    [Theory]
    [InlineData("tr", "tr-TR")]
    [InlineData("TR", "tr-TR")]
    [InlineData("tr-TR", "tr-TR")]
    [InlineData("tr_TR", "tr-TR")]
    public void ToCultureName_returns_culture_name_for_locale(string locale, string expected)
        => SupportedLocales.ToCultureName(locale).ShouldBe(expected);

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("en-US", "en-US")]
    [InlineData("en-GB", "en-US")]
    public void ToCultureName_maps_en_variants_to_en_US(string locale, string expected)
        => SupportedLocales.ToCultureName(locale).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de")]
    public void ToCultureName_unsupported_or_empty_returns_default_culture_name(string? locale)
        => SupportedLocales.ToCultureName(locale).ShouldBe(SupportedLocales.DefaultCultureName);

    // ---- IsSupported ----

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    [InlineData("TR")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void IsSupported_returns_true_for_supported_locales(string locale)
        => SupportedLocales.IsSupported(locale).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("fr")]
    public void IsSupported_returns_false_for_unsupported_locales(string? locale)
        => SupportedLocales.IsSupported(locale).ShouldBeFalse();

    // ---- Constants ----

    [Fact]
    public void Default_is_tr()
        => SupportedLocales.Default.ShouldBe("tr");

    [Fact]
    public void All_contains_tr_and_en()
        => SupportedLocales.All.ShouldBe(new[] { "tr", "en" });

    [Fact]
    public void DefaultCultureName_is_tr_TR()
        => SupportedLocales.DefaultCultureName.ShouldBe("tr-TR");

    [Fact]
    public void AllCultureNames_contains_tr_TR_and_en_US()
        => SupportedLocales.AllCultureNames.ShouldBe(new[] { "tr-TR", "en-US" });
}
