using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.StudyLinks;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.StudyLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #61 — rol kısıtları (yönetim uçları yalnızca Admin + Teacher, öğrenci ucu yalnızca Student; diğer roller
/// ASP.NET yetkilendirmesiyle 403 alır) ve servis sonucu → HTTP kodu eşlemesi.
/// </summary>
public class StudyLinksControllerTests
{
    private static readonly string[] ManagementActions =
    {
        nameof(StudyLinksController.List),
        nameof(StudyLinksController.GetById),
        nameof(StudyLinksController.Create),
        nameof(StudyLinksController.Update),
        nameof(StudyLinksController.Delete),
        nameof(StudyLinksController.Reorder),
    };

    private static string[] RolesOf(string action)
    {
        var method = typeof(StudyLinksController).GetMethod(action)!;
        // issue #287: yönetim uçlarında rol attribute'unun yanında ApprovedTeacher policy attribute'u da var.
        var attribute = method.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Single(a => !string.IsNullOrEmpty(a.Roles));
        return attribute.Roles!.Split(',').Select(r => r.Trim()).OrderBy(r => r).ToArray();
    }

    [Fact]
    public void ManagementEndpoints_RequireApprovedTeacherPolicy()
    {
        foreach (var action in ManagementActions)
            typeof(StudyLinksController).GetMethod(action)!
                .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
                .ShouldContain(a => a.Policy == ExamApp.Api.Services.Teachers.Authorization.ApprovedTeacherPolicies.TeacherCapability,
                    $"{action} onaysız öğretmene kapalı olmalı (#287)");
    }

    [Fact]
    public void ManagementEndpoints_AreRestrictedToAdminAndTeacher()
    {
        foreach (var action in ManagementActions)
            RolesOf(action).ShouldBe(new[] { "Admin", "Teacher" }, $"{action} yalnızca Admin/Teacher'a açık olmalı");
    }

    [Fact]
    public void ManagementEndpoints_DoNotAllowStudentOrParent()
    {
        foreach (var action in ManagementActions)
        {
            RolesOf(action).ShouldNotContain("Student");
            RolesOf(action).ShouldNotContain("Parent");
        }
    }

    [Fact]
    public void ForResultEndpoint_IsRestrictedToStudent()
    {
        RolesOf(nameof(StudyLinksController.GetForResult)).ShouldBe(new[] { "Student" });
    }

    [Fact]
    public void Controller_HasNoClassLevelRoleRestriction_SoMethodRolesAreNotAnded()
    {
        typeof(StudyLinksController).GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .ShouldAllBe(a => string.IsNullOrEmpty(a.Roles));
    }

    // ---------------- HTTP eşlemesi ----------------

    private static StudyLinksController NewController(ITopicStudyLinkService service, int userId = 42, string role = "Teacher")
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = userId, FullName = "U", Role = role });

        var services = new ServiceCollection();
        services.AddSingleton(profiles);
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-1"),
            new Claim(ClaimTypes.Role, role),
        }, "Test");

        return new StudyLinksController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity),
                    RequestServices = services.BuildServiceProvider(),
                }
            }
        };
    }

    [Fact]
    public async Task Create_LimitReached_Returns409WithErrorCode()
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        service.CreateAsync(Arg.Any<CreateTopicStudyLinkDto>(), Arg.Any<StudyLinkActor>(), Arg.Any<CancellationToken>())
            .Returns(new TopicStudyLinkResultDto { Success = false, Conflict = true, ErrorCode = TopicStudyLinkErrorCodes.ActiveLimitReached, Message = "limit" });

        var result = await NewController(service).Create(new CreateTopicStudyLinkDto(), CancellationToken.None);

        var conflict = result.ShouldBeOfType<ConflictObjectResult>();
        conflict.Value.ShouldBeOfType<TopicStudyLinkResultDto>().ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.ActiveLimitReached);
    }

    [Fact]
    public async Task Create_Success_Returns201WithLink()
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        service.CreateAsync(Arg.Any<CreateTopicStudyLinkDto>(), Arg.Any<StudyLinkActor>(), Arg.Any<CancellationToken>())
            .Returns(new TopicStudyLinkResultDto { Success = true, ObjectId = 5, Link = new TopicStudyLinkDto { Id = 5 } });

        var result = await NewController(service).Create(new CreateTopicStudyLinkDto(), CancellationToken.None);

        var created = result.ShouldBeOfType<CreatedAtActionResult>();
        created.ActionName.ShouldBe(nameof(StudyLinksController.GetById));
        created.Value.ShouldBeOfType<TopicStudyLinkDto>().Id.ShouldBe(5);
    }

    [Fact]
    public async Task Create_ValidationFailure_Returns400()
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        service.CreateAsync(Arg.Any<CreateTopicStudyLinkDto>(), Arg.Any<StudyLinkActor>(), Arg.Any<CancellationToken>())
            .Returns(new TopicStudyLinkResultDto { Success = false, Message = "invalid url" });

        (await NewController(service).Create(new CreateTopicStudyLinkDto(), CancellationToken.None))
            .ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ForResult_PassesCallerUserId_AndMapsNotOwnedTo404()
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        service.GetSuggestionsForResultAsync(10, 77, Arg.Any<CancellationToken>())
            .Returns(new StudyLinkSuggestionsResultDto { Success = false, NotFound = true });

        var result = await NewController(service, userId: 77, role: "Student").GetForResult(10, CancellationToken.None);

        result.ShouldBeOfType<NotFoundObjectResult>();
        await service.Received(1).GetSuggestionsForResultAsync(10, 77, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForResult_Success_ReturnsBareArray()
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        var items = new List<QuestionStudyLinkSuggestionDto> { new() { QuestionId = 1 } };
        service.GetSuggestionsForResultAsync(10, 77, Arg.Any<CancellationToken>())
            .Returns(new StudyLinkSuggestionsResultDto { Success = true, Items = items });

        var result = await NewController(service, userId: 77, role: "Student").GetForResult(10, CancellationToken.None);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(items);
    }

    [Fact]
    public async Task Update_Forbidden_Returns403WithErrorCode()
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        service.UpdateAsync(Arg.Any<int>(), Arg.Any<UpdateTopicStudyLinkDto>(), Arg.Any<StudyLinkActor>(), Arg.Any<CancellationToken>())
            .Returns(new TopicStudyLinkResultDto { Success = false, Forbidden = true, ErrorCode = TopicStudyLinkErrorCodes.NotOwner });

        var result = await NewController(service).Update(1, new UpdateTopicStudyLinkDto(), CancellationToken.None);

        var obj = result.ShouldBeOfType<ObjectResult>();
        obj.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        obj.Value.ShouldBeOfType<TopicStudyLinkResultDto>().ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.NotOwner);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Teacher", false)]
    public async Task Create_BuildsActorFromTokenRole(string role, bool expectedAdmin)
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        service.CreateAsync(Arg.Any<CreateTopicStudyLinkDto>(), Arg.Any<StudyLinkActor>(), Arg.Any<CancellationToken>())
            .Returns(new TopicStudyLinkResultDto { Success = true, ObjectId = 1, Link = new TopicStudyLinkDto { Id = 1 } });

        await NewController(service, userId: 9, role: role).Create(new CreateTopicStudyLinkDto(), CancellationToken.None);

        await service.Received(1).CreateAsync(Arg.Any<CreateTopicStudyLinkDto>(),
            Arg.Is<StudyLinkActor>(a => a.UserId == 9 && a.IsAdmin == expectedAdmin && a.Role == role),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WriteEndpoints_AreRateLimited_ReadEndpointsAreNot()
    {
        string? PolicyOf(string action) => typeof(StudyLinksController).GetMethod(action)!
            .GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName;

        foreach (var action in new[] { nameof(StudyLinksController.Create), nameof(StudyLinksController.Update),
                     nameof(StudyLinksController.Delete), nameof(StudyLinksController.Reorder) })
            PolicyOf(action).ShouldBe(ExamApp.Api.Helpers.StudyLinkWriteRateLimiting.Policy, action);

        PolicyOf(nameof(StudyLinksController.List)).ShouldBeNull();
        PolicyOf(nameof(StudyLinksController.GetForResult)).ShouldBeNull();
    }

    [Fact]
    public async Task Delete_Success_Returns204()
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        service.DeleteAsync(3, Arg.Is<StudyLinkActor>(a => a.UserId == 42), Arg.Any<CancellationToken>())
            .Returns(new TopicStudyLinkResultDto { Success = true });

        (await NewController(service).Delete(3, CancellationToken.None)).ShouldBeOfType<NoContentResult>();
    }

    // Profil servisi hata verince BaseController Id=0 / Role=Service sahte profil döner — yazma kullanıcı 0'a atfedilmemeli.
    [Fact]
    public async Task Create_WhenProfileUnresolved_Id0_DoesNotCallService_EvenForAdmin()
    {
        var service = Substitute.For<ITopicStudyLinkService>();

        var result = await NewController(service, userId: 0, role: "Admin").Create(new CreateTopicStudyLinkDto(), CancellationToken.None);

        // sub claim'i var → #255 kuralı: oturumu kapattırmamak için 404 (sub yoksa 401).
        result.ShouldBeOfType<NotFoundObjectResult>();
        await service.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Writes_WhenProfileProviderThrows_AreRejectedWithoutCallingService()
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        var controller = NewController(service, role: "Admin");
        var profiles = controller.HttpContext.RequestServices.GetRequiredService<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<UserProfileDto>(_ => throw new HttpRequestException("auth-api down"));

        // issue #277 security re-review: provider hatası artık sahte "Service"/Id=0 profili değil, fail-closed istisna
        // (UserProfileUnavailableFilter → 503). Servis hiçbir yolda çağrılmaz.
        await Should.ThrowAsync<ExamApp.Api.Helpers.UserProfileUnavailableException>(() => controller.Update(1, new UpdateTopicStudyLinkDto(), CancellationToken.None));
        await Should.ThrowAsync<ExamApp.Api.Helpers.UserProfileUnavailableException>(() => controller.Delete(1, CancellationToken.None));
        await Should.ThrowAsync<ExamApp.Api.Helpers.UserProfileUnavailableException>(() => controller.Reorder(new ReorderTopicStudyLinksDto(), CancellationToken.None));
        await Should.ThrowAsync<ExamApp.Api.Helpers.UserProfileUnavailableException>(() => controller.List(new TopicStudyLinkQueryDto(), CancellationToken.None));

        await service.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default!, default);
        await service.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default);
        await service.DidNotReceiveWithAnyArgs().ReorderAsync(default!, default!, default);
        await service.DidNotReceiveWithAnyArgs().ListAsync(default!, default!, default);
    }

    [Fact]
    public async Task Writes_WithoutSubClaim_Return401()
    {
        var service = Substitute.For<ITopicStudyLinkService>();
        var controller = NewController(service, userId: 0, role: "Admin");
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, "Admin") }, "Test"));

        (await controller.Create(new CreateTopicStudyLinkDto(), CancellationToken.None)).ShouldBeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ForResult_WhenProfileUnresolved_Id0_DoesNotQuery()
    {
        var service = Substitute.For<ITopicStudyLinkService>();

        var result = await NewController(service, userId: 0, role: "Student").GetForResult(10, CancellationToken.None);

        result.ShouldBeOfType<NotFoundObjectResult>();
        await service.DidNotReceiveWithAnyArgs().GetSuggestionsForResultAsync(default, default, default);
    }
}
