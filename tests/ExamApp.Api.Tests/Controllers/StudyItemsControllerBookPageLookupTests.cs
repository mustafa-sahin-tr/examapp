using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>issue #365 (S3): kitap sayfası sorgusu — yalnız onaylı öğretmen, kullanıcı başına rate limit; servis sonucu → HTTP kodu.</summary>
public class StudyItemsControllerBookPageLookupTests
{
    private readonly IStudyItemService _service = Substitute.For<IStudyItemService>();

    private static readonly StudyBookPageLookupRequestDto Request = new()
    {
        Pages = [new StudyBookPageRefDto { Book = "Mat 5", PageNumber = 1 }],
    };

    [Fact]
    public void Endpoint_is_teacher_only_requires_an_approved_teacher_and_is_rate_limited()
    {
        var method = typeof(StudyItemsController).GetMethod(nameof(StudyItemsController.LookupBookPages))!;
        var attributes = method.GetCustomAttributes<AuthorizeAttribute>(inherit: false).ToList();

        // Create/Update Teacher-only → sorgu da (en az yetki, review).
        attributes.Single(a => !string.IsNullOrEmpty(a.Roles)).Roles!.Split(',').Select(r => r.Trim())
            .ShouldBe(new[] { "Teacher" });
        attributes.ShouldContain(a => a.Policy == ApprovedTeacherPolicies.TeacherCapability);
        method.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()!.PolicyName
            .ShouldBe(ExamApp.Api.Helpers.StudyBookPageLookupRateLimiting.Policy);
    }

    [Fact]
    public async Task Found_items_return_200()
    {
        IReadOnlyList<StudyBookPageLookupResultDto> items = [new() { Book = "Mat 5", PageNumber = 1, Exists = true }];
        _service.LookupBookPagesAsync(Arg.Any<IReadOnlyList<StudyBookPageRefDto>>(), Arg.Any<CancellationToken>())
            .Returns(StudyBookPageLookupResult.Ok(items));

        var result = await new StudyItemsController(_service).LookupBookPages(Request, CancellationToken.None);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(items);
    }

    [Fact]
    public async Task Validation_error_returns_400()
    {
        _service.LookupBookPagesAsync(Arg.Any<IReadOnlyList<StudyBookPageRefDto>>(), Arg.Any<CancellationToken>())
            .Returns(StudyBookPageLookupResult.Fail("bad book"));

        var result = await new StudyItemsController(_service).LookupBookPages(Request, CancellationToken.None);

        result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Storage_outage_returns_503()
    {
        _service.LookupBookPagesAsync(Arg.Any<IReadOnlyList<StudyBookPageRefDto>>(), Arg.Any<CancellationToken>())
            .Returns(StudyBookPageLookupResult.Unavailable("down"));

        var result = await new StudyItemsController(_service).LookupBookPages(Request, CancellationToken.None);

        result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
    }
}
