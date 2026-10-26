using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using ExamApp.Api.Helpers;
using System.Security.Claims;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #571: <c>ExamController</c> — <c>save-answer</c> (geçersiz etkileşim cevabı → 400, eski sözleşme korunur),
/// <c>POST test-instances/{id}/check</c> ve <c>GET test-instances/{id}/solutions</c> durum eşlemesi (404 / 400 / 409 / 200),
/// kullanıcı kimliğinin servise geçmesi ve uçların yetkilendirme/rota attribute'ları.
/// </summary>
public class ExamControllerActivityCheckTests
{
    private const int UserId = 42;

    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IExamService _examService = Substitute.For<IExamService>();
    private readonly IStudentService _studentService = Substitute.For<IStudentService>();
    private readonly IWorksheetAssignmentService _assignmentService = Substitute.For<IWorksheetAssignmentService>();
    private readonly ITestSessionService _testSession = Substitute.For<ITestSessionService>();
    private readonly IWorksheetAuthoringService _authoring = Substitute.For<IWorksheetAuthoringService>();
    private readonly IWorksheetDetailService _worksheetDetail = Substitute.For<IWorksheetDetailService>();
    private readonly IWorksheetReminderService _reminderService = Substitute.For<IWorksheetReminderService>();
    private readonly IWorksheetCalendarService _calendarService = Substitute.For<IWorksheetCalendarService>();
    private readonly IWorksheetAccessRequestService _accessRequestService = Substitute.For<IWorksheetAccessRequestService>();
    private readonly IAuthApiClient _authApiClient = Substitute.For<IAuthApiClient>();

    private ExamController NewController()
    {
        _authApiClient.GetUserProfileAsync(Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = UserId, KeycloakId = "kc-42", Role = "Student" });

        var schoolContextResolver = Substitute.For<ISchoolContextResolver>();
        schoolContextResolver.ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>()).Returns((int?)null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(_authApiClient);
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddSingleton<UserProfileCacheService>();
        services.AddSingleton(schoolContextResolver);
        services.AddSingleton<IUserProfileProvider, UserProfileProvider>();
        var provider = services.BuildServiceProvider();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-42"),
            new Claim("preferred_username", "student42"),
        }, authenticationType: "TestAuth");

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity), RequestServices = provider };

        return new ExamController(_minio, _examService, _studentService, _assignmentService,
            _testSession, _authoring, _worksheetDetail, _reminderService, _calendarService, _accessRequestService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static ActivityCheckRequestDto CheckRequest() => new()
    {
        Answers = new List<ActivityCheckAnswerDto> { new() { InstanceQuestionId = 1, SelectedAnswerId = 3 } }
    };

    // ---------------------------------------------------------------- save-answer

    [Fact]
    public async Task SaveAnswer_InvalidAnswerPayload_Returns400WithTheServiceResponse()
    {
        var response = new TestSessionResultDto
        {
            Success = false, InvalidInput = true, ErrorCode = TestSessionErrorCodes.InvalidAnswerPayload, Message = "geçersiz"
        };
        _testSession.SaveAnswer(Arg.Any<SaveAnswerDto>(), Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>()).Returns(response);

        var result = await NewController().SaveAnswer(new SaveAnswerDto { TestInstanceId = 5, TestQuestionId = 6 }, default);

        var badRequest = result.ShouldBeOfType<BadRequestObjectResult>();
        badRequest.Value.ShouldBe(response);
    }

    [Fact]
    public async Task SaveAnswer_NotInProgress_Returns409()
    {
        var response = new TestSessionResultDto { Success = false, Conflict = true, ErrorCode = TestSessionErrorCodes.TestNotInProgress };
        _testSession.SaveAnswer(Arg.Any<SaveAnswerDto>(), Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>()).Returns(response);

        var result = await NewController().SaveAnswer(new SaveAnswerDto(), default);

        result.ShouldBeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task SaveAnswer_Accepted_Returns200()
    {
        var response = new TestSessionResultDto { Success = true, Message = "ok" };
        _testSession.SaveAnswer(Arg.Any<SaveAnswerDto>(), Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>()).Returns(response);

        var result = await NewController().SaveAnswer(new SaveAnswerDto(), default);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(response);
    }

    [Fact]
    public async Task SaveAnswer_UnknownQuestion_KeepsTheLegacyContractAs200WithSuccessFalse()
    {
        // Eski sözleşme: soru/oturum yok → 200 + success:false (yalnız InvalidInput 400'e döner).
        var response = new TestSessionResultDto { Success = false, Message = "bulunamadı" };
        _testSession.SaveAnswer(Arg.Any<SaveAnswerDto>(), Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>()).Returns(response);

        var result = await NewController().SaveAnswer(new SaveAnswerDto(), default);

        result.ShouldBeOfType<OkObjectResult>();
    }

    // ---------------------------------------------------------------- check

    [Fact]
    public async Task CheckActivity_Success_Returns200WithTheServiceResultAndPassesTheCallerId()
    {
        var request = CheckRequest();
        var response = new ActivityCheckResultDto { Success = true, CorrectCount = 1, TotalCount = 1 };
        _testSession.CheckActivityAsync(5, request, UserId, Arg.Any<CancellationToken>()).Returns(response);

        var result = await NewController().CheckActivity(5, request, default);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(response);
        await _testSession.Received(1).CheckActivityAsync(5, request, UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckActivity_NotFound_Returns404()
    {
        _testSession.CheckActivityAsync(Arg.Any<int>(), Arg.Any<ActivityCheckRequestDto>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ActivityCheckResultDto { Success = false, NotFound = true });

        var result = await NewController().CheckActivity(5, CheckRequest(), default);

        result.ShouldBeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CheckActivity_InvalidRequest_Returns400()
    {
        _testSession.CheckActivityAsync(Arg.Any<int>(), Arg.Any<ActivityCheckRequestDto>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ActivityCheckResultDto { Success = false, InvalidInput = true, ErrorCode = TestSessionErrorCodes.InvalidAnswerPayload });

        var result = await NewController().CheckActivity(5, CheckRequest(), default);

        result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(TestSessionErrorCodes.NotAnActivity)]
    [InlineData(TestSessionErrorCodes.TestNotInProgress)]
    public async Task CheckActivity_Conflict_Returns409(string errorCode)
    {
        _testSession.CheckActivityAsync(Arg.Any<int>(), Arg.Any<ActivityCheckRequestDto>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ActivityCheckResultDto { Success = false, Conflict = true, ErrorCode = errorCode });

        var result = await NewController().CheckActivity(5, CheckRequest(), default);

        result.ShouldBeOfType<ConflictObjectResult>();
    }

    // ---------------------------------------------------------------- solutions

    [Fact]
    public async Task GetActivitySolutions_Success_Returns200AndPassesTheCallerId()
    {
        var response = new ActivitySolutionsResultDto { Success = true };
        _testSession.GetActivitySolutionsAsync(5, UserId, Arg.Any<CancellationToken>()).Returns(response);

        var result = await NewController().GetActivitySolutions(5, default);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(response);
    }

    [Fact]
    public async Task GetActivitySolutions_CheckRequired_Returns409()
    {
        _testSession.GetActivitySolutionsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ActivitySolutionsResultDto { Success = false, Conflict = true, ErrorCode = TestSessionErrorCodes.ActivityCheckRequired });

        var result = await NewController().GetActivitySolutions(5, default);

        result.ShouldBeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GetActivitySolutions_NotFound_Returns404()
    {
        _testSession.GetActivitySolutionsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ActivitySolutionsResultDto { Success = false, NotFound = true });

        var result = await NewController().GetActivitySolutions(5, default);

        result.ShouldBeOfType<NotFoundObjectResult>();
    }

    // ---------------------------------------------------------------- rota ve yetkilendirme attribute'ları

    [Fact]
    public void CheckActivity_Endpoint_IsPostOnTheInstanceCheckRouteWithSaveAnswerRoleGate()
    {
        var method = typeof(ExamController).GetMethod(nameof(ExamController.CheckActivity))!;

        method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>()!.Template
            .ShouldBe("~/api/test-instances/{instanceId:int}/check");
        method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Roles)
            .ShouldContain("Student,Teacher,Admin");
        method.GetCustomAttributes<AuthorizeAttribute>().Where(a => a.Policy != null).ShouldNotBeEmpty();
    }

    [Fact]
    public void GetActivitySolutions_Endpoint_IsGetOnTheInstanceSolutionsRouteWithRoleGate()
    {
        var method = typeof(ExamController).GetMethod(nameof(ExamController.GetActivitySolutions))!;

        method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>()!.Template
            .ShouldBe("~/api/test-instances/{instanceId:int}/solutions");
        method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Roles)
            .ShouldContain("Student,Teacher,Admin");
    }

    // ---------------------------------------------------------------- review #571: kontrol ucu rate limit (kullanıcı başına)

    private static ActivityCheckRateLimitPolicy NewPolicy(int permitLimit, int windowSeconds = 60)
    {
        var options = Substitute.For<IOptionsMonitor<ActivityCheckRateLimitOptions>>();
        options.CurrentValue.Returns(new ActivityCheckRateLimitOptions { PermitLimit = permitLimit, WindowSeconds = windowSeconds });
        return new ActivityCheckRateLimitPolicy(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<ActivityCheckRateLimitPolicy>.Instance);
    }

    private static HttpContext HttpFor(string? sub) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            sub == null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.NameIdentifier, sub) }, "Test")),
        Response = { Body = new MemoryStream() },
        RequestServices = new ServiceCollection().BuildServiceProvider(),
    };

    [Fact]
    public void ActivityCheckRateLimitPolicy_PartitionsPerUser_AndRejectsAfterPermitLimit()
    {
        var policy = NewPolicy(permitLimit: 2);

        var first = policy.GetPartition(HttpFor("user-1"));
        var second = policy.GetPartition(HttpFor("user-2"));
        first.PartitionKey.ShouldNotBe(second.PartitionKey);

        using var limiter = first.Factory(first.PartitionKey);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeFalse();

        // Başka kullanıcının kendi kovası hâlâ dolu değil.
        using var otherLimiter = second.Factory(second.PartitionKey);
        otherLimiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
    }

    [Fact]
    public async Task ActivityCheckRateLimitPolicy_Rejection_Returns429WithRateLimitedErrorCode()
    {
        var policy = NewPolicy(permitLimit: 1);
        var context = HttpFor("user-429");
        var partition = policy.GetPartition(context);
        using var limiter = partition.Factory(partition.PartitionKey);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        var rejected = limiter.AttemptAcquire();
        rejected.IsAcquired.ShouldBeFalse();

        await policy.OnRejected!(new OnRejectedContext { HttpContext = context, Lease = rejected }, CancellationToken.None);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = JsonDocument.Parse(context.Response.Body);
        doc.RootElement.GetProperty("errorCode").GetString().ShouldBe("RateLimited");
        doc.RootElement.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ActivityCheckRateLimitOptions_DefaultTo30PerMinute()
    {
        var o = new ActivityCheckRateLimitOptions();
        o.PermitLimit.ShouldBe(30);
        o.WindowSeconds.ShouldBe(60);
    }

    [Theory]
    [InlineData("0", "60")]
    [InlineData("30", "0")]
    [InlineData("30", "86401")]
    public void ActivityCheckRateLimitOptions_OutOfRangeConfiguration_FailsValidation(string permit, string window)
    {
        var provider = BuildRateLimitProvider(permit, window);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ActivityCheckRateLimitOptions>>().Value);
    }

    [Fact]
    public void ActivityCheckRateLimitOptions_ValidConfiguration_BindsFromSection()
    {
        var provider = BuildRateLimitProvider("12", "90");

        var options = provider.GetRequiredService<IOptions<ActivityCheckRateLimitOptions>>().Value;
        options.PermitLimit.ShouldBe(12);
        options.WindowSeconds.ShouldBe(90);
    }

    private static ServiceProvider BuildRateLimitProvider(string permit, string window)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{ActivityCheckRateLimitOptions.SectionName}:PermitLimit"] = permit,
            [$"{ActivityCheckRateLimitOptions.SectionName}:WindowSeconds"] = window,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddActivityCheckRateLimiting();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void CheckActivity_Endpoint_UsesThePerUserActivityCheckRateLimitPolicy()
    {
        var method = typeof(ExamController).GetMethod(nameof(ExamController.CheckActivity))!;

        method.GetCustomAttributes<EnableRateLimitingAttribute>().ShouldHaveSingleItem()
            .PolicyName.ShouldBe(ActivityCheckRateLimiting.Policy);
    }

    // ---------------------------------------------------------------- review #571: DTO'da liste sınırı attribute'u yok (servis doğrular)

    [Fact]
    public void ActivityCheckRequestDto_Answers_HasNoListCountDataAnnotations()
    {
        // Sayı sınırı servis tarafında (InvalidCheckRequest) uygulanır; model binding'de 400 ModelState yerine tek tip yanıt.
        var answers = typeof(ActivityCheckRequestDto).GetProperty(nameof(ActivityCheckRequestDto.Answers))!;

        answers.GetCustomAttributes(typeof(ValidationAttribute), inherit: true).ShouldBeEmpty();
    }
}
