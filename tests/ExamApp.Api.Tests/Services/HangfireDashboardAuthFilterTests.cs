using System.Security.Claims;
using ExamApp.Api.Services.QuestionTransfer;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #365 (S1): Hangfire dashboard tüm işlerin argümanlarını kiracılar arası gösterir — production ve development
/// filtresi yalnız Admin/SuperAdmin'e açık. Önceki (#287) onaylı öğretmen erişimi kaldırıldı; öğretmen onaylı olsa da
/// reddedilir.
/// </summary>
public class HangfireDashboardAuthFilterTests
{
    private static DashboardContext Context(bool authenticated, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "kc-1") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "Test") : new ClaimsIdentity(claims)),
            RequestServices = new ServiceCollection().BuildServiceProvider(), // AspNetCoreDashboardContext ctor needs it
        };
        return new AspNetCoreDashboardContext(Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task Admins_are_allowed(string role)
    {
        (await new HangfireDashboardAuthFilter().AuthorizeAsync(Context(true, role))).ShouldBeTrue();
        new HangfireDashboardDevAuthFilter().Authorize(Context(true, role)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Teacher")]
    [InlineData("Student")]
    [InlineData("Parent")]
    public async Task Every_other_role_is_denied_including_approved_teachers(string role)
    {
        (await new HangfireDashboardAuthFilter().AuthorizeAsync(Context(true, role))).ShouldBeFalse();
        new HangfireDashboardDevAuthFilter().Authorize(Context(true, role)).ShouldBeFalse();
    }

    [Fact]
    public async Task Unauthenticated_principal_is_denied_even_with_an_admin_role_claim()
    {
        (await new HangfireDashboardAuthFilter().AuthorizeAsync(Context(false, "Admin"))).ShouldBeFalse();
        new HangfireDashboardDevAuthFilter().Authorize(Context(false, "Admin")).ShouldBeFalse();
    }
}
