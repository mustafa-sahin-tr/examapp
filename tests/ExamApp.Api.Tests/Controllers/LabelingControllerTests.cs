using NSubstitute.ExceptionExtensions;
using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// İşaretleme API'si: rol kapsamı (Admin + QuestionTrainer, anonim yok) ve servis istisnalarının HTTP durum
/// eşlemesi (400 / 404 / 429 + Retry-After / 502 / 503).
/// </summary>
public class LabelingControllerTests
{
    private const string TrainerKeycloakId = "kc-trainer-42";

    private readonly ILabelingService _service = Substitute.For<ILabelingService>();

    private LabelingController NewController(string? keycloakId = TrainerKeycloakId)
    {
        var claims = new List<Claim> { new("preferred_username", "trainer") };
        if (keycloakId is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, keycloakId));

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth")),
        };
        return new LabelingController(_service)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static LabelingAiDetectRequestDto DetectRequest() => new()
    {
        ImageBase64 = "iVBORw0KGgo=",
        Model = "claude-test",
    };

    private static LabelingSaveManualRequestDto SaveRequest() => new()
    {
        ImageBase64 = "iVBORw0KGgo=",
        Predictions = new PredictionListDto(),
    };

    // ---------- Yetkilendirme ----------

    [Fact]
    public void Controller_RequiresAdminOrQuestionTrainerRoles()
    {
        typeof(LabelingController).GetCustomAttributes<AuthorizeAttribute>()
            .Where(a => a.Roles is not null).Select(a => a.Roles!).ToArray()
            .ShouldBe(["Admin,QuestionTrainer"]);
        typeof(LabelingController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty();
    }

    [Fact]
    public void Every_Endpoint_IsNotAnonymous_AndOnlyNarrowsTheControllerRoles()
    {
        var actions = typeof(LabelingController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

        actions.Select(m => m.Name).OrderBy(n => n)
            .ShouldBe(new[] { "DetectWithAi", "GetAiModels", "GetPage", "SaveManual" });

        foreach (var m in actions)
        {
            m.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty(m.Name);
            m.GetCustomAttributes<AuthorizeAttribute>().Where(a => a.Roles is not null)
                .ShouldAllBe(a => a.Roles == "Admin,QuestionTrainer", m.Name);
        }
    }

    // ---------- Servis → HTTP eşlemesi ----------

    [Fact]
    public async Task DetectWithAi_Success_Returns200WithServiceResponse()
    {
        var response = new LabelingAiDetectResponseDto("live", new string('a', 64),
            new LabelingResultDto("Ai", "claude-test", new PredictionListDto(), null, null, 1, 2, 0.1m, null,
                DateTime.UtcNow, DateTime.UtcNow));
        _service.DetectWithAiAsync(Arg.Any<LabelingAiDetectRequestDto>(), Arg.Any<LabelingActor>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await NewController().DetectWithAi(DetectRequest(), CancellationToken.None);

        ((OkObjectResult)result).Value.ShouldBe(response);
    }

    [Fact]
    public async Task DetectWithAi_PassesCallerKeycloakIdAsActor()
    {
        _service.DetectWithAiAsync(Arg.Any<LabelingAiDetectRequestDto>(), Arg.Any<LabelingActor>(), Arg.Any<CancellationToken>())
            .Returns(new LabelingAiDetectResponseDto("cached", new string('a', 64), null!));

        await NewController().DetectWithAi(DetectRequest(), CancellationToken.None);

        await _service.Received(1).DetectWithAiAsync(
            Arg.Any<LabelingAiDetectRequestDto>(),
            Arg.Is<LabelingActor>(a => a.KeycloakId == TrainerKeycloakId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DetectWithAi_ValidationException_Returns400()
    {
        _service.DetectWithAiAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingValidationException("labeling.unknownModel"));

        var result = await NewController().DetectWithAi(DetectRequest(), CancellationToken.None);

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task DetectWithAi_RateLimited_Returns429WithRetryAfterRoundedUp()
    {
        _service.DetectWithAiAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingRateLimitException(TimeSpan.FromSeconds(30.2)));
        var controller = NewController();

        var result = await controller.DetectWithAi(DetectRequest(), CancellationToken.None);

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        controller.Response.Headers.RetryAfter.ToString().ShouldBe("31");
    }

    [Fact]
    public async Task DetectWithAi_RateLimitedWithSubSecondRetryAfter_SetsRetryAfterToAtLeastOne()
    {
        _service.DetectWithAiAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingRateLimitException(TimeSpan.FromMilliseconds(200)));
        var controller = NewController();

        await controller.DetectWithAi(DetectRequest(), CancellationToken.None);

        controller.Response.Headers.RetryAfter.ToString().ShouldBe("1");
    }

    [Fact]
    public async Task DetectWithAi_RateLimitedWithoutRetryAfter_OmitsHeader()
    {
        _service.DetectWithAiAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingRateLimitException(null));
        var controller = NewController();

        var result = await controller.DetectWithAi(DetectRequest(), CancellationToken.None);

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        controller.Response.Headers.ContainsKey("Retry-After").ShouldBeFalse();
    }

    [Fact]
    public async Task DetectWithAi_ClaudeFailure_Returns502()
    {
        _service.DetectWithAiAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingAiException("Claude response did not match the schema."));

        var result = await NewController().DetectWithAi(DetectRequest(), CancellationToken.None);

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task DetectWithAi_NotConfigured_Returns503()
    {
        _service.DetectWithAiAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingAiUnavailableException());

        var result = await NewController().DetectWithAi(DetectRequest(), CancellationToken.None);

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task GetPage_UnknownPage_Returns404()
    {
        _service.GetPageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((LabelingPageDto?)null);

        var result = await NewController().GetPage(new string('a', 64), CancellationToken.None);

        result.ShouldBeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetPage_InvalidHash_Returns400()
    {
        _service.GetPageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new LabelingValidationException("labeling.invalidHash"));

        var result = await NewController().GetPage("zz", CancellationToken.None);

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SaveManual_ValidationException_Returns400()
    {
        _service.SaveManualAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingValidationException("labeling.invalidBox"));

        var result = await NewController().SaveManual(SaveRequest(), CancellationToken.None);

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SaveManual_Success_ReturnsOkWithPage()
    {
        var page = new LabelingPageDto(new string('b', 64), 10, 10, []);
        _service.SaveManualAsync(Arg.Any<LabelingSaveManualRequestDto>(), Arg.Any<LabelingActor>(), Arg.Any<CancellationToken>())
            .Returns(page);

        var result = await NewController().SaveManual(SaveRequest(), CancellationToken.None);

        ((OkObjectResult)result).Value.ShouldBe(page);
    }

    [Fact]
    public void GetAiModels_ReturnsServiceList()
    {
        IReadOnlyList<LabelingAiModelDto> models = [new("claude-test", "Test", 3m, 15m, true)];
        _service.GetAiModels().Returns(models);

        var result = NewController().GetAiModels();

        ((OkObjectResult)result).Value.ShouldBe(models);
    }

    [Fact]
    public void Controller_AlsoRequiresApprovedTeacherPolicy()
    {
        typeof(LabelingController).GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy)
            .ShouldContain(ApprovedTeacherPolicies.TeacherCapability);
    }

    [Fact]
    public async Task DetectWithAi_BudgetExceeded_Returns429WithBudgetErrorCode()
    {
        _service.DetectWithAiAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingRateLimitException(TimeSpan.FromHours(3), budget: true));
        var controller = NewController();

        var result = await controller.DetectWithAi(DetectRequest(), CancellationToken.None);

        var obj = result.ShouldBeOfType<ObjectResult>();
        obj.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        obj.Value!.GetType().GetProperty("errorCode")!.GetValue(obj.Value).ShouldBe("LabelingBudgetExceeded");
        controller.Response.Headers.RetryAfter.ToString().ShouldBe("10800");
    }

    [Fact]
    public async Task DetectWithAi_RateLimited_CarriesRateLimitErrorCode()
    {
        _service.DetectWithAiAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingRateLimitException(TimeSpan.FromMinutes(5)));

        var result = await NewController().DetectWithAi(DetectRequest(), CancellationToken.None);

        var obj = result.ShouldBeOfType<ObjectResult>();
        obj.Value!.GetType().GetProperty("errorCode")!.GetValue(obj.Value).ShouldBe("LabelingRateLimited");
    }
}
