using System.Reflection;
using BadgeService.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace BadgeService.Tests;

/// <summary>
/// Issue #148: admin badge-definition CRUD must be admin-only. BadgeService's JWT/Keycloak pipeline
/// (Program.cs) is what actually turns a missing "Admin" realm role into 403 for a real request — that
/// requires a live token issuer/TestServer this suite doesn't stand up elsewhere (see
/// ReportsControllerAuthorizationTests, which tests its *custom* Forbid logic directly instead). What we
/// CAN and must pin down at this level is that the controller is wired to the same
/// <c>[Authorize(Roles = "Admin")]</c> mechanism as every other admin-only endpoint, and that no action
/// method opts out of it with <c>[AllowAnonymous]</c>.
/// </summary>
public class BadgeDefinitionsAdminControllerAuthorizationTests
{
    [Fact]
    public void Controller_requires_the_Admin_role()
    {
        var attribute = typeof(BadgeDefinitionsAdminController).GetCustomAttribute<AuthorizeAttribute>();

        attribute.ShouldNotBeNull();
        attribute!.Roles.ShouldBe("Admin");
    }

    [Fact]
    public void No_action_allows_anonymous_access()
    {
        var actions = typeof(BadgeDefinitionsAdminController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        actions.ShouldAllBe(m => m.GetCustomAttribute<AllowAnonymousAttribute>() == null);
    }
}
