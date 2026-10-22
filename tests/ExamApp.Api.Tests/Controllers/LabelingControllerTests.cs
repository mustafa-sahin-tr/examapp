using NSubstitute.ExceptionExtensions;
using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
            .ShouldBe(new[] { "DetectWithAi", "GetAiModels", "GetPage", "GetPrompt", "SaveManual", "SavePrompt", "SaveToBank" });

        foreach (var m in actions)
        {
            m.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty(m.Name);
            // SaveToBank (#470) ek olarak Teacher/Admin ister; diğerleri sınıf düzeyindeki kümeyi daraltmaz.
            var expected = m.Name == "SaveToBank" ? "Teacher,Admin" : "Admin,QuestionTrainer";
            m.GetCustomAttributes<AuthorizeAttribute>().Where(a => a.Roles is not null)
                .ShouldAllBe(a => a.Roles == expected, m.Name);
        }
    }

    // ---------- Servis → HTTP eşlemesi ----------

    [Fact]
    public async Task DetectWithAi_Success_Returns200WithServiceResponse()
    {
        var response = new LabelingAiDetectResponseDto("live", new string('a', 64),
            new LabelingResultDto("Ai", "claude-test", new PredictionListDto(), null, null, 1, 2, 0.1m, 1, null,
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

    // ---------- Soru bankası (POST api/labeling/pages/bank, issue #470) ----------

    private readonly ILabelingBankService _bank = Substitute.For<ILabelingBankService>();

    /// <summary>Çözülen profil (Id &gt; 0) ya da null (profil yok) ile; <c>GetAuthenticatedUserAsync</c> RequestServices'ten okur.</summary>
    private LabelingController NewBankController(UserProfileDto? profile)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(profiles)
            .BuildServiceProvider();

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, TrainerKeycloakId), new Claim("preferred_username", "trainer")],
                authenticationType: "TestAuth")),
            RequestServices = services,
        };
        return new LabelingController(_service)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static UserProfileDto TeacherProfile(int id = 42) => new()
    {
        Id = id,
        KeycloakId = TrainerKeycloakId,
        FullName = "Eğitmen",
        Email = string.Empty,
        Role = "Teacher",
    };

    private static LabelingBankSaveRequestDto BankRequest() => new()
    {
        ImageBase64 = "iVBORw0KGgo=",
        Questions = [new LabelingBankQuestionDto { Options = [new(), new()], CorrectOptionIndex = 0 }],
    };

    [Fact]
    public void SaveToBank_IsPostOnPagesBankRouteWithTeacherOrAdminRoles()
    {
        var method = typeof(LabelingController).GetMethod(nameof(LabelingController.SaveToBank))!;

        method.GetCustomAttributes<HttpPostAttribute>().Single().Template.ShouldBe("pages/bank");
        method.GetCustomAttributes<AuthorizeAttribute>().Where(a => a.Roles is not null)
            .Select(a => a.Roles!).ShouldBe(["Teacher,Admin"]);
    }

    [Fact]
    public async Task SaveToBank_Success_Returns200WithBankResponseAndPassesUserIdAndActor()
    {
        var response = new LabelingBankSaveResponseDto(new string('c', 64),
            new LabelingBankInfoDto(DateTime.UtcNow, "Eğitmen", 1));
        var request = BankRequest();
        _bank.SaveToBankAsync(Arg.Any<LabelingBankSaveRequestDto>(), Arg.Any<LabelingActor>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await NewBankController(TeacherProfile(id: 42)).SaveToBank(request, _bank, CancellationToken.None);

        ((OkObjectResult)result).Value.ShouldBe(response);
        await _bank.Received(1).SaveToBankAsync(
            request,
            Arg.Is<LabelingActor>(a => a.KeycloakId == TrainerKeycloakId),
            42,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveToBank_RateLimited_Returns429WithBankErrorCodeAndRetryAfter()
    {
        _bank.SaveToBankAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new LabelingRateLimitException(TimeSpan.FromMinutes(5)));
        var controller = NewBankController(TeacherProfile());

        var result = await controller.SaveToBank(BankRequest(), _bank, CancellationToken.None);

        var obj = result.ShouldBeOfType<ObjectResult>();
        obj.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        obj.Value!.GetType().GetProperty("errorCode")!.GetValue(obj.Value).ShouldBe("LabelingBankRateLimited");
        controller.Response.Headers.RetryAfter.ToString().ShouldBe("300");
    }

    [Fact]
    public async Task SaveToBank_UserProfileNotResolved_Returns404AndDoesNotSave()
    {
        var result = await NewBankController(profile: null).SaveToBank(BankRequest(), _bank, CancellationToken.None);

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        await _bank.DidNotReceiveWithAnyArgs().SaveToBankAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task SaveToBank_ValidationException_Returns400WithMessageKey()
    {
        _bank.SaveToBankAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new LabelingValidationException("labeling.bank.correctAnswerMissing"));

        var result = await NewBankController(TeacherProfile()).SaveToBank(BankRequest(), _bank, CancellationToken.None);

        var obj = result.ShouldBeOfType<BadRequestObjectResult>();
        obj.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SaveToBank_DuplicateWithExistingInfo_Returns409WithExistingRecord()
    {
        var existing = new LabelingBankInfoDto(DateTime.UtcNow, "Önceki Eğitmen", 4);
        _bank.SaveToBankAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new LabelingBankDuplicateException(existing));

        var result = await NewBankController(TeacherProfile()).SaveToBank(BankRequest(), _bank, CancellationToken.None);

        var obj = result.ShouldBeOfType<ConflictObjectResult>();
        obj.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        obj.Value!.GetType().GetProperty("errorCode")!.GetValue(obj.Value).ShouldBe("LabelingPageAlreadyInBank");
        obj.Value.GetType().GetProperty("existing")!.GetValue(obj.Value).ShouldBe(existing);
    }

    [Fact]
    public async Task SaveToBank_DuplicateWhileClaimInProgress_Returns409WithNullExisting()
    {
        _bank.SaveToBankAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new LabelingBankDuplicateException(null));

        var result = await NewBankController(TeacherProfile()).SaveToBank(BankRequest(), _bank, CancellationToken.None);

        var obj = result.ShouldBeOfType<ConflictObjectResult>();
        obj.Value!.GetType().GetProperty("existing")!.GetValue(obj.Value).ShouldBeNull();
    }

    [Fact]
    public async Task SaveToBank_BulkSaveFailure_Returns500WithErrorCodeAndNoDetail()
    {
        _bank.SaveToBankAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new LabelingBankSaveException());

        var result = await NewBankController(TeacherProfile()).SaveToBank(BankRequest(), _bank, CancellationToken.None);

        var obj = result.ShouldBeOfType<ObjectResult>();
        obj.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        var body = obj.Value!.ToString()!;
        body.ShouldContain("LabelingBankSaveFailed");
        body.ShouldNotContain("Saving labeling questions");
    }

    // ---------- Prompt uçları (GET/PUT api/labeling/prompt) ----------

    private static LabelingPromptStateDto PromptState() => new(
        new LabelingPromptDto(2, "q {width}", "o {width}", 6, 4, true, "Eğitmen", DateTime.UtcNow),
        new LabelingPromptDto(0, "dq", "do", 6, 4, true, null, null),
        [new LabelingPromptDto(2, "q {width}", "o {width}", 6, 4, true, "Eğitmen", DateTime.UtcNow)]);

    private static LabelingSavePromptRequestDto SavePromptRequest() => new()
    {
        QuestionInstruction = "q {width}",
        OptionInstruction = "o {width}",
        QuestionPadding = 6,
        OptionPadding = 4,
        ExpandQuestionToOptions = true,
    };

    [Fact]
    public async Task GetPrompt_ReturnsServiceStateWithOk()
    {
        var state = PromptState();
        _service.GetPromptStateAsync(Arg.Any<CancellationToken>()).Returns(state);

        var result = await NewController().GetPrompt(CancellationToken.None);

        ((OkObjectResult)result).Value.ShouldBe(state);
    }

    [Fact]
    public async Task SavePrompt_Success_ReturnsOkWithStateAndPassesActor()
    {
        var state = PromptState();
        var request = SavePromptRequest();
        _service.SavePromptAsync(Arg.Any<LabelingSavePromptRequestDto>(), Arg.Any<LabelingActor>(), Arg.Any<CancellationToken>())
            .Returns(state);

        var result = await NewController().SavePrompt(request, CancellationToken.None);

        ((OkObjectResult)result).Value.ShouldBe(state);
        await _service.Received(1).SavePromptAsync(
            request,
            Arg.Is<LabelingActor>(a => a.KeycloakId == TrainerKeycloakId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePrompt_ValidationException_Returns400()
    {
        _service.SavePromptAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingValidationException("labeling.promptTooLong"));

        var result = await NewController().SavePrompt(SavePromptRequest(), CancellationToken.None);

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SavePrompt_InvalidPadding_Returns400AndDoesNotReturnState()
    {
        _service.SavePromptAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingValidationException("labeling.invalidPadding"));

        var result = await NewController().SavePrompt(SavePromptRequest(), CancellationToken.None);

        var obj = (ObjectResult)result;
        obj.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        obj.Value!.GetType().GetProperty("errorCode")!.GetValue(obj.Value).ShouldBeNull();
    }

    [Fact]
    public void GetPrompt_And_SavePrompt_CarryRoleScopedAuthorizationFromController()
    {
        // Uçlar ayrıca [Authorize] almaz; sınıf düzeyindeki Admin/QuestionTrainer + onaylı öğretmen kapısını devralır.
        foreach (var name in new[] { "GetPrompt", "SavePrompt" })
        {
            var method = typeof(LabelingController).GetMethod(name)!;
            method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty(name);
            method.GetCustomAttributes<AuthorizeAttribute>().ShouldBeEmpty(name);
        }
    }
}
