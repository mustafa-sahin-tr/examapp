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
            User(new Claim("azp", "Exam-Admin"))).ShouldBeTrue();

    [Fact]
    public void Azp_not_in_allow_list_is_not_a_service()
        => ServicePrincipal.IsService(
            User(new Claim("azp", "some-spa")),
            new[] { "exam-admin" }).ShouldBeFalse();

    [Fact]
    public void Defaults_to_exam_admin_when_no_list_supplied()
    {
        ServicePrincipal.IsService(User(new Claim("azp", "exam-admin"))).ShouldBeTrue();
        ServicePrincipal.IsService(User(new Claim("azp", "exam-admin")), Array.Empty<string>()).ShouldBeTrue();
    }

    [Theory]
    [InlineData("exam-admin")]
    [InlineData("service-account-exam-admin")]
    [InlineData("EXAM-ADMIN")]
    public void Legacy_preferred_username_is_accepted(string username)
        => ServicePrincipal.IsService(User(new Claim("preferred_username", username))).ShouldBeTrue();

    [Fact]
    public void A_normal_user_is_not_a_service()
        => ServicePrincipal.IsService(User(
            new Claim("preferred_username", "ali.veli"),
            new Claim("azp", "exam-client"),
            new Claim(ClaimTypes.Role, "Teacher"))).ShouldBeFalse();
}
