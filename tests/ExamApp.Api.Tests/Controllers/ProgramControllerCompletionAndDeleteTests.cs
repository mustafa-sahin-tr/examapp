using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Controllers;
using ExamApp.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;
using Xunit;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #109: study-page completion/uncompletion and program soft-delete endpoints map
/// service true/false results to 204/404, and reject requests without a resolvable user id.
/// </summary>
public class ProgramControllerCompletionAndDeleteTests
{
    private readonly IProgramService _programService = Substitute.For<IProgramService>();

    private ProgramController NewController(string? keycloakUserId)
    {
        var controller = new ProgramController(_programService);

        var identity = keycloakUserId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, keycloakUserId) }, authenticationType: "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }

    [Fact]
    public async Task CompleteStudyPage_ServiceReturnsTrue_ReturnsNoContent()
    {
        _programService.CompleteStudyPageAsync("kc-1", 1, 2, Arg.Any<CancellationToken>()).Returns(true);
        var controller = NewController("kc-1");

        var result = await controller.CompleteStudyPage(1, 2, default);

        result.ShouldBeOfType<NoContentResult>();
    }

    [Fact]
    public async Task CompleteStudyPage_ServiceReturnsFalse_ReturnsNotFound()
    {
        _programService.CompleteStudyPageAsync("kc-1", 1, 2, Arg.Any<CancellationToken>()).Returns(false);
        var controller = NewController("kc-1");

        var result = await controller.CompleteStudyPage(1, 2, default);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CompleteStudyPage_NoKeycloakIdClaim_ReturnsUnauthorizedWithoutCallingService()
    {
        var controller = NewController(keycloakUserId: null);

        var result = await controller.CompleteStudyPage(1, 2, default);

        result.ShouldBeOfType<UnauthorizedObjectResult>();
        await _programService.DidNotReceive().CompleteStudyPageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UncompleteStudyPage_ServiceReturnsTrue_ReturnsNoContent()
    {
        _programService.UncompleteStudyPageAsync("kc-1", 1, 2, Arg.Any<CancellationToken>()).Returns(true);
        var controller = NewController("kc-1");

        var result = await controller.UncompleteStudyPage(1, 2, default);

        result.ShouldBeOfType<NoContentResult>();
    }

    [Fact]
    public async Task UncompleteStudyPage_ServiceReturnsFalse_ReturnsNotFound()
    {
        _programService.UncompleteStudyPageAsync("kc-1", 1, 2, Arg.Any<CancellationToken>()).Returns(false);
        var controller = NewController("kc-1");

        var result = await controller.UncompleteStudyPage(1, 2, default);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteProgram_ServiceReturnsTrue_ReturnsNoContent()
    {
        _programService.DeleteUserProgramAsync("kc-1", 1, Arg.Any<CancellationToken>()).Returns(true);
        var controller = NewController("kc-1");

        var result = await controller.DeleteProgram(1, default);

        result.ShouldBeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteProgram_ServiceReturnsFalse_ReturnsNotFound()
    {
        _programService.DeleteUserProgramAsync("kc-1", 1, Arg.Any<CancellationToken>()).Returns(false);
        var controller = NewController("kc-1");

        var result = await controller.DeleteProgram(1, default);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteProgram_NoKeycloakIdClaim_ReturnsUnauthorizedWithoutCallingService()
    {
        var controller = NewController(keycloakUserId: null);

        var result = await controller.DeleteProgram(1, default);

        result.ShouldBeOfType<UnauthorizedObjectResult>();
        await _programService.DidNotReceive().DeleteUserProgramAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
