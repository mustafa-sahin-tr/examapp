using BadgeService.Services;

namespace BadgeService.Tests;

/// <summary>Issue #148 owner decision #2: strict per-RuleType schema validation.</summary>
public class BadgeRuleTypeCatalogTests
{
    [Fact]
    public void Unknown_rule_type_is_rejected()
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize("NotARealRuleType", "{\"target\":1}", out _, out var errors);

        ok.ShouldBeFalse();
        errors.ShouldContain(e => e.Field == "ruleType");
    }

    [Theory]
    [InlineData("AnswerCount")]
    [InlineData("CorrectStreak")]
    [InlineData("TotalStudyTimeMinutes")]
    [InlineData("TotalCorrectAnswers")]
    [InlineData("ActiveDays")]
    [InlineData("DailyStreak")]
    public void Simple_target_rule_types_accept_a_valid_target(string ruleType)
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize(ruleType, "{\"target\":10}", out var normalized, out var errors);

        ok.ShouldBeTrue();
        errors.ShouldBeEmpty();
        normalized.ShouldBe("{\"target\":10}");
    }

    [Theory]
    [InlineData("AnswerCount")]
    [InlineData("CorrectStreak")]
    public void Simple_target_rule_types_reject_missing_target(string ruleType)
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize(ruleType, "{}", out _, out var errors);

        ok.ShouldBeFalse();
        errors.ShouldContain(e => e.Field == "target");
    }

    [Fact]
    public void Target_out_of_range_is_rejected()
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize("AnswerCount", "{\"target\":0}", out _, out var errors);

        ok.ShouldBeFalse();
        errors.ShouldContain(e => e.Field == "target");
    }

    [Fact]
    public void Unknown_field_is_rejected()
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize(
            "AnswerCount", "{\"target\":10,\"somethingElse\":1}", out _, out var errors);

        ok.ShouldBeFalse();
        errors.ShouldContain(e => e.Field == "ruleConfigJson");
    }

    [Fact]
    public void Malformed_json_is_rejected()
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize("AnswerCount", "not json", out _, out var errors);

        ok.ShouldBeFalse();
        errors.ShouldContain(e => e.Field == "ruleConfigJson");
    }

    [Fact]
    public void Subject_rule_requires_subjectId_or_subjectName()
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize(
            "SubjectAnswerCount", "{\"target\":10}", out _, out var errors);

        ok.ShouldBeFalse();
        errors.ShouldContain(e => e.Field == "subjectId");
    }

    [Fact]
    public void Subject_rule_accepts_subjectId()
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize(
            "SubjectAnswerCount", "{\"target\":10,\"subjectId\":3}", out var normalized, out var errors);

        ok.ShouldBeTrue();
        errors.ShouldBeEmpty();
        normalized.ShouldBe("{\"target\":10,\"subjectId\":3,\"subjectName\":null}");
    }

    [Fact]
    public void Subject_rule_accepts_subjectName()
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize(
            "SubjectCorrectCount", "{\"target\":10,\"subjectName\":\"Matematik\"}", out var normalized, out var errors);

        ok.ShouldBeTrue();
        errors.ShouldBeEmpty();
        normalized.ShouldBe("{\"target\":10,\"subjectId\":null,\"subjectName\":\"Matematik\"}");
    }

    [Fact]
    public void Subject_rule_rejects_non_positive_subjectId()
    {
        var ok = BadgeRuleTypeCatalog.TryValidateAndNormalize(
            "SubjectAnswerCount", "{\"target\":10,\"subjectId\":0}", out _, out var errors);

        ok.ShouldBeFalse();
        errors.ShouldContain(e => e.Field == "subjectId");
    }

    [Fact]
    public void All_returns_a_schema_per_canonical_rule_type()
    {
        BadgeRuleTypeCatalog.All.Count.ShouldBe(9);
        BadgeRuleTypeCatalog.All.ShouldAllBe(s => s.Fields.Count > 0);
    }
}
