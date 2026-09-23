using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #255: 401 yalnızca gerçek kimlik doğrulama yokluğunda (sub claim yok) dönmeli. Token geçerliyken
/// profil/kayıt bulunamaması → 404, sahiplik/erişim ihlali → 403. Aksi halde UI'ın 401 → refresh → logout
/// akışı (#241) kullanıcıyı gereksiz yere oturumdan atar. Yanıt gövdeleri (UI mesajı) korunur.
/// </summary>
public class ControllerAuthStatusMappingTests
{
    private readonly IUserProfileProvider _profileProvider = Substitute.For<IUserProfileProvider>();
    private readonly IStudentService _studentService = Substitute.For<IStudentService>();
    private readonly IWorksheetAuthoringService _authoring = Substitute.For<IWorksheetAuthoringService>();

    private static readonly string UnauthenticatedMessage = FallbackMessageLocalizer.Instance["exam.unauthenticated"].Value;

    private ControllerContext Context(string? sub)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(_profileProvider);

        var claims = new List<Claim> { new("preferred_username", "user1") };
        if (sub != null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, sub));

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth")),
                RequestServices = services.BuildServiceProvider()
            }
        };
    }

    private ExamController NewExamController(string? sub) =>
        new(Substitute.For<IMinIoService>(), Substitute.For<IExamService>(), _studentService,
            Substitute.For<IWorksheetAssignmentService>(), Substitute.For<ITestSessionService>(), _authoring,
            Substitute.For<IWorksheetDetailService>(), Substitute.For<IWorksheetReminderService>(),
            Substitute.For<IWorksheetCalendarService>(), Substitute.For<IWorksheetAccessRequestService>())
        {
            ControllerContext = Context(sub)
        };

    private void ProfileResolvesTo(UserProfileDto? profile) =>
        _profileProvider.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile!);

    // --- Kayıt/profil bulunamadı (token geçerli) → 404, gövde korunur ---

    [Fact]
    public async Task Valid_token_but_unresolved_profile_returns_404_with_original_message()
    {
        ProfileResolvesTo(null);

        var result = await NewExamController("kc-1").GetWorksheetReminder(5, CancellationToken.None);

        result.ShouldBeOfType<NotFoundObjectResult>().Value.ShouldBe(UnauthenticatedMessage);
    }

    [Fact]
    public async Task Valid_token_but_missing_student_record_returns_404()
    {
        ProfileResolvesTo(new UserProfileDto { Id = 42, KeycloakId = "kc-1", Role = "Student" });
        _studentService.GetStudentProfile(42).Returns((StudentProfileDto)null!);

        var result = await NewExamController("kc-1").GetWorksheetReminder(5, CancellationToken.None);

        result.ShouldBeOfType<NotFoundObjectResult>().Value
            .ShouldBe(FallbackMessageLocalizer.Instance["exam.studentProfileNotFound"].Value);
    }

    [Fact]
    public async Task Booking_unresolved_profile_returns_404()
    {
        ProfileResolvesTo(null);
        var controller = new BookingController(
            Substitute.For<IBookingService>(), Substitute.For<IRecurringAvailabilityService>(),
            FallbackMessageLocalizer.Instance)
        {
            ControllerContext = Context("kc-1")
        };

        var result = await controller.CreateSlot(null!, CancellationToken.None);

        result.ShouldBeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Parent_register_unresolved_profile_returns_404_with_message_body()
    {
        ProfileResolvesTo(null);
        var controller = new ParentController(null!, Substitute.For<IKeycloakService>())
        {
            ControllerContext = Context("kc-1")
        };

        var result = await controller.RegisterParent();

        var notFound = result.ShouldBeOfType<NotFoundObjectResult>();
        notFound.Value!.GetType().GetProperty("message")!.GetValue(notFound.Value)
            .ShouldBe(FallbackMessageLocalizer.Instance["auth.userNotResolved"].Value);
    }

    // --- Gerçek kimlik yok (sub claim yok) → 401 kalır ---

    [Fact]
    public async Task Missing_sub_claim_still_returns_401()
    {
        ProfileResolvesTo(null);

        var result = await NewExamController(sub: null).GetWorksheetReminder(5, CancellationToken.None);

        result.ShouldBeOfType<UnauthorizedObjectResult>().Value.ShouldBe(UnauthenticatedMessage);
    }

    // --- Sahiplik/erişim ihlali → 403 ---

    [Fact]
    public async Task DeleteWorksheet_UnauthorizedAccessException_returns_403_with_message()
    {
        ProfileResolvesTo(new UserProfileDto { Id = 42, KeycloakId = "kc-1", Role = "Teacher" });
        _authoring.DeleteWorksheetAsync(9, 42, false)
            .Returns<Task<ResponseBaseDto>>(_ => throw new UnauthorizedAccessException("Bu testi silemezsiniz."));

        var result = await NewExamController("kc-1").DeleteWorksheet(9);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        objectResult.Value!.GetType().GetProperty("message")!.GetValue(objectResult.Value)
            .ShouldBe("Bu testi silemezsiniz.");
    }

    [Fact]
    public async Task Middleware_maps_UnauthorizedAccessException_to_403_not_401()
    {
        var middleware = new ExceptionHandlingMiddleware(_ => throw new UnauthorizedAccessException("nope"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        context.Response.Body.Position = 0;
        using var json = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("message").GetString().ShouldBe("nope");
    }
}
