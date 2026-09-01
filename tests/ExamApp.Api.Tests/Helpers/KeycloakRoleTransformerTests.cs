using System.Security.Claims;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

public class KeycloakRoleTransformerTests
{
    private readonly KeycloakRoleTransformer _sut = new();

    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));

    private async Task<ClaimsPrincipal> Transform(params Claim[] claims)
        => await _sut.TransformAsync(Principal(claims));

    [Fact]
    public async Task Maps_realm_access_roles_onto_role_claims()
    {
        var result = await Transform(new Claim("realm_access", """{"roles":["Teacher","Admin"]}"""));

        result.IsInRole("Teacher").ShouldBeTrue();
        result.IsInRole("Admin").ShouldBeTrue();
        result.IsInRole("Student").ShouldBeFalse();
    }

    [Fact]
    public async Task Does_not_duplicate_an_existing_role_claim()
    {
        var result = await Transform(
            new Claim(ClaimTypes.Role, "Teacher"),
            new Claim("realm_access", """{"roles":["Teacher"]}"""));

        result.Claims.Count(c => c.Type == ClaimTypes.Role && c.Value == "Teacher").ShouldBe(1);
    }

    [Fact]
    public async Task No_realm_access_claim_is_a_no_op()
    {
        var result = await Transform(new Claim("preferred_username", "x"));
        result.Claims.ShouldNotContain(c => c.Type == ClaimTypes.Role);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"roles": "not-an-array"}""")]
    [InlineData("""{"no_roles_key": 1}""")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Malformed_or_unexpected_realm_access_is_ignored_not_thrown(string value)
    {
        var result = await Transform(new Claim("realm_access", value));
        result.Claims.ShouldNotContain(c => c.Type == ClaimTypes.Role);
    }

    [Fact]
    public async Task Non_string_entries_in_the_roles_array_are_skipped()
    {
        var result = await Transform(new Claim("realm_access", """{"roles":["Teacher", 5, null, "Admin"]}"""));

        result.IsInRole("Teacher").ShouldBeTrue();
        result.IsInRole("Admin").ShouldBeTrue();
        result.Claims.Count(c => c.Type == ClaimTypes.Role).ShouldBe(2);
    }

    [Fact]
    public async Task Unauthenticated_identity_is_returned_untouched()
    {
        var anon = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type
        var result = await _sut.TransformAsync(anon);
        result.ShouldBeSameAs(anon);
    }
}
