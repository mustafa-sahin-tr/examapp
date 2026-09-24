using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #277 security re-review: profil sağlayıcı hata verdiğinde kullanıcı token'ı için <c>Role="Service"</c>'li sahte profil
/// ÜRETİLMEZ (StudyItemService sahiplik kontrolü bu rolde atlanıyordu). Fail-closed: <see cref="UserProfileUnavailableException"/>
/// → 503 ve iş kuralı hiç çalışmaz. Gerçek servis principal'ı (client-credentials) "Service" profiliyle çalışmaya devam eder.
/// </summary>
public class BaseControllerProfileFailClosedTests
{
    private readonly IStudyItemService _studyItems = Substitute.For<IStudyItemService>();

    private StudyItemsController NewController(ClaimsPrincipal principal, bool providerThrows)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        if (providerThrows)
            profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<UserProfileDto>(_ => throw new HttpRequestException("auth-api down"));

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(profiles)
            .BuildServiceProvider();

        return new StudyItemsController(_studyItems)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal, RequestServices = services }
            }
        };
    }

    private static ClaimsPrincipal UserToken(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "kc-user"), new("preferred_username", "teacher1") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public async Task Provider_failure_for_a_user_token_fails_closed_and_the_ownership_checked_service_never_runs()
    {
        var controller = NewController(UserToken("Teacher"), providerThrows: true);

        var ex = await Should.ThrowAsync<UserProfileUnavailableException>(() => controller.Delete(5));

        ex.InnerException.ShouldBeOfType<HttpRequestException>();
        await _studyItems.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!);
    }

    [Fact]
    public async Task Service_principal_still_gets_the_service_profile_without_calling_the_provider()
    {
        _studyItems.DeleteAsync(5, Arg.Any<UserProfileDto>()).Returns(new ResponseBaseDto { Success = true });
        var servicePrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "svc-sub"),
            new Claim(ClaimTypes.Role, ExamApp.Foundation.Security.ServicePrincipal.ServiceRole),
        }, "Test"));
        // Sağlayıcı hata verse bile servis principal'ı onu hiç çağırmaz.
        var controller = NewController(servicePrincipal, providerThrows: true);

        (await controller.Delete(5)).ShouldBeOfType<OkObjectResult>();

        await _studyItems.Received(1).DeleteAsync(5, Arg.Is<UserProfileDto>(u => u.Role == "Service" && u.Id == 0));
    }

    [Fact]
    public async Task Service_role_claimed_by_a_user_token_is_not_honoured_on_provider_failure()
    {
        // "Service" metni bir kullanıcı token'ında (ör. yanlış yapılandırılmış rol) servis principal'ı yapmaz.
        var controller = NewController(UserToken("Service"), providerThrows: true);

        await Should.ThrowAsync<UserProfileUnavailableException>(() => controller.Delete(5));
        await _studyItems.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!);
    }

    [Fact]
    public void Filter_maps_the_exception_to_503_with_a_localized_message_and_is_inherited_by_controllers()
    {
        typeof(StudyItemsController).GetCustomAttributes(typeof(UserProfileUnavailableFilterAttribute), inherit: true)
            .ShouldNotBeEmpty();

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        var context = new ExceptionContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = new UserProfileUnavailableException(new HttpRequestException("down"))
        };

        new UserProfileUnavailableFilterAttribute().OnException(context);

        context.ExceptionHandled.ShouldBeTrue();
        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        var message = result.Value!.GetType().GetProperty("message")!.GetValue(result.Value) as string;
        message.ShouldBe("Kullanıcı bilgileriniz şu anda doğrulanamıyor. Lütfen biraz sonra tekrar deneyin.");
    }

    [Fact]
    public void Filter_ignores_other_exceptions()
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        var context = new ExceptionContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = new InvalidOperationException("other")
        };

        new UserProfileUnavailableFilterAttribute().OnException(context);

        context.ExceptionHandled.ShouldBeFalse();
        context.Result.ShouldBeNull();
    }
}
