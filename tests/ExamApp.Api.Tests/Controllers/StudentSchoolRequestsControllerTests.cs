using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.StudentSchoolMemberships;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #361: <c>api/student-school-requests</c> — rol/onay kapısı (Admin,Teacher + ApprovedTeacher), aktörün (exam user id +
/// JWT Admin rolü) servise geçmesi, karar sonucu → HTTP eşlemesi (kapsam dışı/yok → 404, çakışma → 409).
/// </summary>
public class StudentSchoolRequestsControllerTests
{
    private const int UserId = 42;
    private readonly IStudentSchoolMembershipService _service = Substitute.For<IStudentSchoolMembershipService>();

    private StudentSchoolRequestsController NewController(params string[] roles)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = UserId, KeycloakId = "kc-sub", Role = roles.FirstOrDefault() ?? "Teacher" });
        var services = new ServiceCollection()
            .AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .AddSingleton(profiles)
            .BuildServiceProvider();

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "kc-sub") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new StudentSchoolRequestsController(_service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
                    RequestServices = services
                }
            }
        };
    }

    [Fact]
    public void Controller_requires_admin_or_teacher_and_approved_teacher_policy()
    {
        var attributes = typeof(StudentSchoolRequestsController).GetCustomAttributes<AuthorizeAttribute>().ToList();
        attributes.ShouldContain(a => a.Roles == "Admin,Teacher");
        attributes.ShouldContain(a => a.Policy == ApprovedTeacherPolicies.TeacherCapability);
        typeof(StudentSchoolRequestsController).GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/student-school-requests");
    }

    [Theory]
    [InlineData(nameof(StudentSchoolRequestsController.Approve))]
    [InlineData(nameof(StudentSchoolRequestsController.Reject))]
    public void Decisions_are_rate_limited(string method)
    {
        typeof(StudentSchoolRequestsController).GetMethod(method)!
            .GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(AdminStudentSchoolRateLimiting.Policy);
    }

    [Fact]
    public async Task Teacher_list_passes_non_admin_approver()
    {
        _service.ListPendingAsync(Arg.Any<StudentSchoolApprover>(), Arg.Any<int?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Paged<ExamApp.Api.Models.Dtos.StudentSchoolMemberships.StudentSchoolRequestDto> { Items = new() });

        var result = await NewController("Teacher").GetPending(schoolId: 5, page: 2, pageSize: 10);

        result.ShouldBeOfType<OkObjectResult>();
        await _service.Received(1).ListPendingAsync(new StudentSchoolApprover(UserId, false, "kc-sub"), 5, 2, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admin_approve_passes_admin_approver_and_returns_200()
    {
        _service.ApproveAsync(Arg.Any<StudentSchoolApprover>(), 9, Arg.Any<CancellationToken>())
            .Returns(new StudentSchoolDecisionResult(StudentSchoolDecisionStatus.Success, 3));

        var result = await NewController("Admin").Approve(9, default);

        result.ShouldBeOfType<OkObjectResult>();
        await _service.Received(1).ApproveAsync(new StudentSchoolApprover(UserId, true, "kc-sub"), 9, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StudentSchoolDecisionStatus.NotFound, 404)]
    [InlineData(StudentSchoolDecisionStatus.Conflict, 409)]
    public async Task Decision_failures_map_to_status(StudentSchoolDecisionStatus status, int code)
    {
        _service.RejectAsync(Arg.Any<StudentSchoolApprover>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new StudentSchoolDecisionResult(status));

        var result = await NewController("Teacher").Reject(9, default);

        result.ShouldBeAssignableTo<ObjectResult>()!.StatusCode.ShouldBe(code);
    }
}
