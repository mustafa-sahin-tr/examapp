using NSubstitute.ExceptionExtensions;
using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
/// Issue #577: kitap kaydı API'si — yetki (LabelingController ile aynı: Admin + QuestionTrainer + onaylı öğretmen, anonim
/// yok, hiçbir uç genişletmez) ve servis sonuçlarının HTTP eşlemesi (201 / 200 / 204 / 400 / 404 / 409).
/// </summary>
public class LabelingBooksControllerTests
{
    private readonly IBookRegistryService _books = Substitute.For<IBookRegistryService>();

    private LabelingBooksController NewController(bool admin = false, int userId = 42)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new UserProfileDto
        {
            Id = userId, KeycloakId = "kc-1", FullName = "Eğitmen", Email = string.Empty, Role = admin ? "Admin" : "Teacher",
        });
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(profiles)
            .BuildServiceProvider();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "kc-1"), new("preferred_username", "trainer") };
        if (admin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return new LabelingBooksController(_books)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth")),
                    RequestServices = services,
                },
            },
        };
    }

    private static LabelingBookDto Book(int id = 5) => new(id, "TT MatLab 5", null, null, null, [], [], []);

    private static LabelingBookSaveRequestDto Request() => new() { Name = "TT MatLab 5" };

    [Fact]
    public void Controller_HasTheSameAuthorizationAsLabelingController()
    {
        var type = typeof(LabelingBooksController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/labeling/books");
        type.GetCustomAttributes<AuthorizeAttribute>().Where(a => a.Roles is not null).Select(a => a.Roles!).ToArray()
            .ShouldBe(["Admin,QuestionTrainer"]);
        type.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy)
            .ShouldContain(ApprovedTeacherPolicies.TeacherCapability);
        type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty();
    }

    [Fact]
    public void Every_Endpoint_IsNotAnonymous_AndDoesNotChangeRoles()
    {
        var actions = typeof(LabelingBooksController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

        actions.Select(m => m.Name).OrderBy(n => n)
            .ShouldBe(new[] { "Create", "Delete", "FindFilePages", "Get", "GetPage", "List", "Update" });
        foreach (var m in actions)
        {
            m.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty(m.Name);
            m.GetCustomAttributes<AuthorizeAttribute>().ShouldBeEmpty(m.Name);
        }
    }

    [Fact]
    public async Task Create_Returns201WithLocation()
    {
        _books.CreateAsync(Arg.Any<LabelingBookSaveRequestDto>(), Arg.Any<CancellationToken>()).Returns(Book(9));

        var result = await NewController().Create(Request(), CancellationToken.None);

        var created = result.ShouldBeOfType<CreatedAtActionResult>();
        created.ActionName.ShouldBe(nameof(LabelingBooksController.Get));
        created.RouteValues!["id"].ShouldBe(9);
    }

    [Fact]
    public async Task Create_Validation_Returns400()
    {
        _books.CreateAsync(default!, default).ThrowsAsyncForAnyArgs(new LabelingValidationException("labeling.books.tocOverlap"));

        var result = await NewController().Create(Request(), CancellationToken.None);

        result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_NameTaken_Returns409WithErrorCode()
    {
        _books.CreateAsync(default!, default)
            .ThrowsAsyncForAnyArgs(new BookRegistryConflictException("labeling.books.nameTaken", "LabelingBookNameTaken"));

        var result = await NewController().Create(Request(), CancellationToken.None);

        var obj = result.ShouldBeOfType<ConflictObjectResult>();
        obj.Value!.GetType().GetProperty("errorCode")!.GetValue(obj.Value).ShouldBe("LabelingBookNameTaken");
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        _books.UpdateAsync(3, Arg.Any<LabelingBookSaveRequestDto>(), Arg.Any<BookEditor>(), Arg.Any<CancellationToken>()).Returns((LabelingBookDto?)null);

        (await NewController().Update(3, Request(), CancellationToken.None)).ShouldBeOfType<NotFoundObjectResult>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // review M2: düzenleyen kullanıcı ve Admin bilgisi servise gider.
    public async Task Update_PassesTheEditor(bool admin)
    {
        _books.UpdateAsync(3, Arg.Any<LabelingBookSaveRequestDto>(), Arg.Any<BookEditor>(), Arg.Any<CancellationToken>()).Returns(Book(3));

        (await NewController(admin, userId: 7).Update(3, Request(), CancellationToken.None)).ShouldBeOfType<OkObjectResult>();

        await _books.Received(1).UpdateAsync(3, Arg.Any<LabelingBookSaveRequestDto>(),
            Arg.Is<BookEditor>(e => e.UserId == 7 && e.IsAdmin == admin), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_RenameForbidden_Returns403WithErrorCode()
    {
        _books.UpdateAsync(default, default!, default!, default).ThrowsAsyncForAnyArgs(
            new BookRegistryForbiddenException("labeling.books.renameForbidden", "LabelingBookRenameForbidden"));

        var obj = (await NewController().Update(3, Request(), CancellationToken.None)).ShouldBeOfType<ObjectResult>();
        obj.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        obj.Value!.GetType().GetProperty("errorCode")!.GetValue(obj.Value).ShouldBe("LabelingBookRenameForbidden");
    }

    [Fact]
    public async Task Delete_InUse_Returns409_Deleted_Returns204_Missing_Returns404()
    {
        _books.DeleteAsync(1, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BookRegistryConflictException("labeling.books.inUse", "LabelingBookInUse"));
        _books.DeleteAsync(2, Arg.Any<CancellationToken>()).Returns(true);
        _books.DeleteAsync(3, Arg.Any<CancellationToken>()).Returns(false);

        var inUse = (await NewController().Delete(1, CancellationToken.None)).ShouldBeOfType<ConflictObjectResult>();
        inUse.Value!.GetType().GetProperty("errorCode")!.GetValue(inUse.Value).ShouldBe("LabelingBookInUse");
        (await NewController().Delete(2, CancellationToken.None)).ShouldBeOfType<NoContentResult>();
        (await NewController().Delete(3, CancellationToken.None)).ShouldBeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetPage_And_FindFilePages_MapServiceResults()
    {
        var page = new LabelingBookPageDto(60, 10, false, null);
        _books.GetPageAsync(5, 60, Arg.Any<CancellationToken>()).Returns(page);
        _books.GetPageAsync(5, 0, Arg.Any<CancellationToken>()).ThrowsAsync(new LabelingValidationException("labeling.books.invalidPage"));
        var printed = new LabelingBookPrintedPageDto(10, [60, 12]);
        _books.FindFilePagesAsync(5, 10, 70, Arg.Any<CancellationToken>()).Returns(printed);
        _books.FindFilePagesAsync(6, 10, null, Arg.Any<CancellationToken>()).Returns((LabelingBookPrintedPageDto?)null);

        ((OkObjectResult)await NewController().GetPage(5, 60, CancellationToken.None)).Value.ShouldBe(page);
        (await NewController().GetPage(5, 0, CancellationToken.None)).ShouldBeOfType<BadRequestObjectResult>();
        ((OkObjectResult)await NewController().FindFilePages(5, 10, 70, CancellationToken.None)).Value.ShouldBe(printed);
        (await NewController().FindFilePages(6, 10, null, CancellationToken.None)).ShouldBeOfType<NotFoundObjectResult>();
    }
}
