using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.OpenText;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Worksheets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #583: değerlendirme kuyruğu uçlarının rota / yetki / rate limit attribute'ları (öğrenci ve veli rolü yok; onaylı öğretmen
/// policy'si; yazma ucu kullanıcı başına sınırlı) ve geçersiz kuyruk sorgusunun servise gitmeden 400 olması.
/// </summary>
public class OpenTextReviewsControllerTests
{
    private static MethodInfo Action(string name) => typeof(OpenTextReviewsController).GetMethod(name)!;

    [Theory]
    [InlineData(nameof(OpenTextReviewsController.GetQueue))]
    [InlineData(nameof(OpenTextReviewsController.Review))]
    public void Endpoints_AreTeacherOrAdminOnly_WithApprovedTeacherPolicy(string action)
    {
        var authorize = Action(action).GetCustomAttributes<AuthorizeAttribute>().ToList();
        authorize.Where(a => a.Roles != null).ShouldHaveSingleItem().Roles.ShouldBe("Teacher,Admin");
        authorize.Select(a => a.Policy).ShouldContain(ApprovedTeacherPolicies.TeacherCapability);
    }

    [Fact]
    public void Routes_AndRateLimit_AreAsDocumented()
    {
        typeof(OpenTextReviewsController).GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/open-text-reviews");
        Action(nameof(OpenTextReviewsController.GetQueue)).GetCustomAttribute<HttpGetAttribute>().ShouldNotBeNull();
        Action(nameof(OpenTextReviewsController.Review)).GetCustomAttribute<HttpPutAttribute>()!.Template.ShouldBe("{instanceQuestionId:int}");
        Action(nameof(OpenTextReviewsController.Review)).GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName
            .ShouldBe(OpenTextReviewRateLimiting.Policy);
        Action(nameof(OpenTextReviewsController.GetQueue)).GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName
            .ShouldBe(OpenTextReviewReadRateLimiting.Policy);
    }

    [Theory]
    [InlineData("done", 1, 20, null)]
    [InlineData(null, 0, 20, null)]
    [InlineData(null, 1, 51, null)]
    [InlineData(null, 1, 0, null)]
    [InlineData("pending", 1, 20, 0)]
    [InlineData("pending", 10_001, 20, null)]
    public async Task GetQueue_InvalidQuery_Is400WithoutCallingTheService(string? status, int page, int pageSize, int? worksheetId)
    {
        var service = Substitute.For<IOpenTextReviewService>();
        var controller = new OpenTextReviewsController(service);

        var result = await controller.GetQueue(
            new OpenTextReviewQueueQuery { Status = status, Page = page, PageSize = pageSize, WorksheetId = worksheetId }, default);

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<OpenTextReviewResultDto>().ErrorCode
            .ShouldBe(OpenTextReviewErrorCodes.InvalidQuery);
        await service.DidNotReceiveWithAnyArgs().GetQueueAsync(default, default!, default);
    }
}
