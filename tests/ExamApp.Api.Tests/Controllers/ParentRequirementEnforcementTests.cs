using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos.ParentInvitations;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Parents.Enforcement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #440 / #441 yaptırımı: kısıtlı uçlarda <c>ParentActivated</c> policy'si (booking talebi, iki DM gönderme ucu, yorum oluşturma,
/// liderlik tablosu), policy handler'ı (yalnız AwaitingActivation reddedilir), sonuç işleyicisi (yalnız bu gereksinim başarısızsa
/// ParentActivationRequired gövdesi) ve süresi dolmuş öğrenci middleware'i (allowlist / anonim uçlar açık, diğer yetkili uçlar 403
/// ParentRequired).
/// </summary>
public class ParentRequirementEnforcementTests
{
    private static IEnumerable<string> Policies(MethodInfo method)
        => method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Where(p => p != null)!;

    [Theory]
    [InlineData(typeof(BookingController), nameof(BookingController.CreateRequest))]
    [InlineData(typeof(DirectMessagesController), nameof(DirectMessagesController.SendToTeacher))]
    [InlineData(typeof(DirectMessagesController), nameof(DirectMessagesController.SendToConversation))]
    [InlineData(typeof(WorksheetCommentsController), nameof(WorksheetCommentsController.Create))]
    [InlineData(typeof(LeaderboardController), nameof(LeaderboardController.GetLeaderboard))]
    public void Restricted_endpoints_require_an_activated_parent(Type controller, string action)
        => Policies(controller.GetMethod(action)!).ShouldContain(ParentRequirementPolicies.ParentActivated);

    [Fact]
    public void Parent_flow_and_session_endpoints_stay_open_when_the_grace_period_is_over()
    {
        typeof(StudentParentController).GetCustomAttribute<AllowWhenParentRequiredAttribute>().ShouldNotBeNull();
        typeof(AuthController).GetMethod(nameof(AuthController.RefreshProfileInformation))!
            .GetCustomAttribute<AllowWhenParentRequiredAttribute>().ShouldNotBeNull();
        typeof(StudentController).GetMethod(nameof(StudentController.GetStudentProfile))!
            .GetCustomAttribute<AllowWhenParentRequiredAttribute>().ShouldNotBeNull();
        // Ödev/test uçları allowlist'te DEĞİL (süre dolunca kapanır).
        typeof(ExamController).GetMethods().Where(m => m.GetCustomAttribute<AllowWhenParentRequiredAttribute>() != null).ShouldBeEmpty();
    }

    private static StudentParentState State(StudentParentMode mode) => new(mode, true, 1, null, null, false, null);

    private static IStudentParentStatusResolver Resolver(StudentParentMode? mode)
    {
        var resolver = Substitute.For<IStudentParentStatusResolver>();
        resolver.ResolveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(mode is { } m ? State(m) : null);
        return resolver;
    }

    [Theory]
    [InlineData(StudentParentMode.AwaitingActivation, false)]
    [InlineData(StudentParentMode.Grace, true)]
    [InlineData(StudentParentMode.Ok, true)]
    [InlineData(StudentParentMode.Expired, true)] // Expired'ı middleware ParentRequired ile keser
    [InlineData(null, true)]
    public async Task Policy_handler_rejects_only_awaiting_activation(StudentParentMode? mode, bool succeeds)
    {
        var requirement = new ParentActivationRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(new ClaimsIdentity("t")), null);

        await new ParentActivationAuthorizationHandler(Resolver(mode)).HandleAsync(context);

        context.HasSucceeded.ShouldBe(succeeds);
    }

    [Fact]
    public void Result_handler_recognises_only_the_parent_activation_failure()
    {
        ParentRequirementAuthorizationResultHandler.IsParentActivationRequired(
            PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed(new[] { new ParentActivationRequirement() }))).ShouldBeTrue();
        ParentRequirementAuthorizationResultHandler.IsParentActivationRequired(
            PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed(new IAuthorizationRequirement[]
            {
                new ParentActivationRequirement(), new Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement(new[] { "Student" })
            }))).ShouldBeFalse();
        ParentRequirementAuthorizationResultHandler.IsParentActivationRequired(PolicyAuthorizationResult.Challenge()).ShouldBeFalse();
    }

    private static async Task<(int Status, bool NextCalled, string Body)> RunMiddlewareAsync(StudentParentMode? mode, params object[] metadata)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Student") }, "TestAuth")),
            RequestServices = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider()
        };
        http.Response.Body = new MemoryStream();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        var nextCalled = false;
        var middleware = new ParentRequiredMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(http, Resolver(mode));

        http.Response.Body.Position = 0;
        return (http.Response.StatusCode, nextCalled, await new StreamReader(http.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Expired_student_gets_parent_required_on_authorized_endpoints()
    {
        var (status, next, body) = await RunMiddlewareAsync(StudentParentMode.Expired, new AuthorizeAttribute());

        status.ShouldBe(StatusCodes.Status403Forbidden);
        next.ShouldBeFalse();
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("success").GetBoolean().ShouldBeFalse();
        json.GetProperty("errorCode").GetString().ShouldBe(ParentRequirementErrorCodes.ParentRequired);
        json.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(StudentParentMode.Grace)]
    [InlineData(StudentParentMode.AwaitingActivation)]
    [InlineData(StudentParentMode.Ok)]
    [InlineData(null)]
    public async Task Other_modes_pass_through(StudentParentMode? mode)
        => (await RunMiddlewareAsync(mode, new AuthorizeAttribute())).NextCalled.ShouldBeTrue();

    [Fact]
    public async Task Allowlisted_and_anonymous_endpoints_stay_open_for_an_expired_student()
    {
        (await RunMiddlewareAsync(StudentParentMode.Expired, new AuthorizeAttribute(), new AllowWhenParentRequiredAttribute())).NextCalled.ShouldBeTrue();
        (await RunMiddlewareAsync(StudentParentMode.Expired, new AllowAnonymousAttribute())).NextCalled.ShouldBeTrue();
        (await RunMiddlewareAsync(StudentParentMode.Expired)).NextCalled.ShouldBeTrue(); // yetki verisi olmayan uç
    }
}
