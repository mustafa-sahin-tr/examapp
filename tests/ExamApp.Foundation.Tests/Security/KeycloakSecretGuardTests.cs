using ExamApp.Foundation.Security;

namespace ExamApp.Foundation.Tests.Security;

public class KeycloakSecretGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("devOnlyExamServiceClientSecretChangeMe12345678")]
    [InlineData("DEVONLYsomething")]
    public void Missing_or_dev_only_values_are_flagged(string? value)
        => KeycloakSecretGuard.IsMissingOrDevOnly(value).ShouldBeTrue();

    [Fact]
    public void Real_looking_value_is_accepted()
        => KeycloakSecretGuard.IsMissingOrDevOnly("k3Jx9-real-secret").ShouldBeFalse();

    [Fact]
    public void Development_never_throws()
        => Should.NotThrow(() => KeycloakSecretGuard.EnsureConfigured(true,
            ("Keycloak:ClientSecret", ""), ("Keycloak:ServiceClientSecret", null)));

    [Fact]
    public void Outside_development_all_real_secrets_pass()
        => Should.NotThrow(() => KeycloakSecretGuard.EnsureConfigured(false,
            ("Keycloak:ClientSecret", "a"), ("Keycloak:AdminClientSecret", "b"), ("Keycloak:ServiceClientSecret", "c")));

    [Fact]
    public void Outside_development_every_offending_key_is_reported_in_one_exception()
    {
        var ex = Should.Throw<InvalidOperationException>(() => KeycloakSecretGuard.EnsureConfigured(false,
            ("Keycloak:ClientSecret", ""), ("Keycloak:AdminClientSecret", "real-value-b"),
            ("Keycloak:ServiceClientSecret", "devOnlyX")));
        ex.Message.ShouldContain("Keycloak:ClientSecret");
        ex.Message.ShouldContain("Keycloak:ServiceClientSecret");
        ex.Message.ShouldNotContain("Keycloak:AdminClientSecret");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("devOnlyExamServiceClientSecretChangeMe12345678")]
    public void Outside_development_a_bad_service_secret_fails_fast_naming_the_key(string? bad)
    {
        var ex = Should.Throw<InvalidOperationException>(() => KeycloakSecretGuard.EnsureConfigured(false,
            ("Keycloak:ClientSecret", "a"), ("Keycloak:AdminClientSecret", "b"), ("Keycloak:ServiceClientSecret", bad)));
        ex.Message.ShouldContain("Keycloak:ServiceClientSecret");
        ex.Message.ShouldContain("Keycloak__ServiceClientSecret");
        ex.Message.ShouldNotContain("Keycloak:ClientSecret");
    }
}
