using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #287 security review L3: production Hangfire dashboard'u yalnızca Admin/SuperAdmin ya da hesabı ONAYLI
/// öğretmene açılır — Teacher rolü tek başına yetmez.
/// </summary>
public class HangfireDashboardAuthFilterTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public HangfireDashboardAuthFilterTests()
    {
        using var ctx = _db.NewContext();
        ctx.Teachers.AddRange(
            new Teacher { UserId = 1, AccountApprovedAt = DateTime.UtcNow },
            new Teacher { UserId = 2, ApprovalStatus = TeacherApprovalStatus.Pending });
        ctx.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private async Task<bool> AuthorizeAsync(int userId, params string[] roles)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = userId });

        var services = new ServiceCollection();
        services.AddSingleton(profiles);
        services.AddScoped(_ => _db.NewContext());
        services.AddScoped<IApprovedTeacherGuard, ApprovedTeacherGuard>();

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, $"kc-{userId}") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
            RequestServices = services.BuildServiceProvider().CreateScope().ServiceProvider,
        };

        var context = new AspNetCoreDashboardContext(Substitute.For<JobStorage>(), new DashboardOptions(), httpContext);
        return await new HangfireDashboardAuthFilter().AuthorizeAsync(context);
    }

    [Fact]
    public async Task Approved_teacher_and_admins_are_allowed()
    {
        (await AuthorizeAsync(1, "Teacher")).ShouldBeTrue();
        (await AuthorizeAsync(99, "Admin")).ShouldBeTrue();
        (await AuthorizeAsync(98, "SuperAdmin")).ShouldBeTrue();
    }

    [Fact]
    public async Task Pending_or_unregistered_teacher_and_other_roles_are_denied()
    {
        (await AuthorizeAsync(2, "Teacher")).ShouldBeFalse();
        (await AuthorizeAsync(3, "Teacher")).ShouldBeFalse();
        (await AuthorizeAsync(4, "Student")).ShouldBeFalse();
    }
}
