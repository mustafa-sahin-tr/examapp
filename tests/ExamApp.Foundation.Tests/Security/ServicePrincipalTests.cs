using System.Security.Claims;
using ExamApp.Foundation.Security;

namespace ExamApp.Foundation.Tests.Security;

public class ServicePrincipalTests
{
    // authenticationType non-null => Identity.IsAuthenticated == true.
    private static ClaimsPrincipal User(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));

    private static ClaimsPrincipal Anonymous(params Claim[] claims)
        => new(new ClaimsIdentity(claims));

    [Fact]
    public void Null_principal_is_not_a_service()
        => ServicePrincipal.IsService(null).ShouldBeFalse();

    [Fact]
    public void Unauthenticated_principal_is_not_a_service()
        => ServicePrincipal.IsService(Anonymous(new Claim("azp", "exam-admin"))).ShouldBeFalse();

    [Fact]
    public void Realm_role_exam_service_is_a_service()
        => ServicePrincipal.IsService(User(new Claim(ClaimTypes.Role, "exam-service"))).ShouldBeTrue();

    [Fact]
    public void Azp_in_allow_list_is_a_service()
        => ServicePrincipal.IsService(
            User(new Claim("azp", "my-service")),
            new[] { "my-service", "other" }).ShouldBeTrue();

    [Fact]
    public void Client_id_claim_is_also_checked()
        => ServicePrincipal.IsService(
            User(new Claim("client_id", "my-service")),
            new[] { "my-service" }).ShouldBeTrue();

    [Fact]
    public void Azp_match_is_case_insensitive()
        => ServicePrincipal.IsService(
            User(new Claim("azp", "Exam-Service")),
            new[] { "exam-service" }).ShouldBeTrue();

    [Fact]
    public void Azp_not_in_allow_list_is_not_a_service()
        => ServicePrincipal.IsService(
            User(new Claim("azp", "some-spa")),
            new[] { "exam-admin" }).ShouldBeFalse();

    [Fact]
    public void There_is_no_implicit_default_azp_list()
    {
        // Issue #372: exam-admin is a Keycloak admin-REST client, not a service caller.
        ServicePrincipal.IsService(User(new Claim("azp", "exam-admin"))).ShouldBeFalse();
        ServicePrincipal.IsService(User(new Claim("azp", "exam-admin")), Array.Empty<string>()).ShouldBeFalse();
        ServicePrincipal.IsService(User(new Claim("azp", "exam-service"))).ShouldBeFalse();
    }

    [Theory]
    [InlineData("exam-admin")]
    [InlineData("service-account-exam-admin")]
    [InlineData("EXAM-ADMIN")]
    public void Legacy_preferred_username_is_rejected(string username)
    {
        ServicePrincipal.IsService(User(new Claim("preferred_username", username))).ShouldBeFalse();
        // Even with an unrelated allow-list: the username claim is never consulted.
        ServicePrincipal.IsService(User(new Claim("preferred_username", username)), new[] { "exam-service" })
            .ShouldBeFalse();
    }

    [Fact]
    public void Exam_admin_service_account_without_the_role_is_rejected()
        => ServicePrincipal.IsService(User(
            new Claim("preferred_username", "service-account-exam-admin"),
            new Claim("azp", "exam-admin"))).ShouldBeFalse();

    [Fact]
    public void Exam_service_service_account_token_is_accepted()
        => ServicePrincipal.IsService(User(
            new Claim("preferred_username", "service-account-exam-service"),
            new Claim("azp", "exam-service"),
            new Claim(ClaimTypes.Role, "exam-service"))).ShouldBeTrue();

    [Theory]
    [InlineData("Teacher")]
    [InlineData("Student")]
    [InlineData("Admin")]
    public void User_tokens_are_not_services(string role)
        => ServicePrincipal.IsService(User(
            new Claim("preferred_username", "ali.veli"),
            new Claim("azp", "exam-client"),
            new Claim(ClaimTypes.Role, role)),
            new[] { "exam-service" }).ShouldBeFalse();

    [Fact]
    public void A_normal_user_is_not_a_service()
        => ServicePrincipal.IsService(User(
            new Claim("preferred_username", "ali.veli"),
            new Claim("azp", "exam-client"),
            new Claim(ClaimTypes.Role, "Teacher"))).ShouldBeFalse();
}
