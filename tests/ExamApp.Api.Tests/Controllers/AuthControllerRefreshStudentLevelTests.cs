using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #243 review: POST api/auth/refresh öğrenci profilinde XP/Level dolu döner (eskiden 0/0; seviye formülünün
/// minimumu 1). XP StudentPoints toplamından, Level <see cref="StudentLevel.FromXp"/> ile hesaplanır.
/// </summary>
public class AuthControllerRefreshStudentLevelTests : IDisposable
{
    private const int UserId = 42;
    private const string Sub = "kc-42";

    private readonly TestDb _db = TestDb.Create();
    private readonly IUserProfileProvider _profileProvider = Substitute.For<IUserProfileProvider>();

    public AuthControllerRefreshStudentLevelTests()
    {
        _profileProvider.GetAsync(Sub, Arg.Any<CancellationToken>())
            .Returns(_ => new UserProfileDto { Id = UserId, KeycloakId = Sub, Role = "Student", FullName = "Öğrenci", Avatar = "" });
    }

    private async Task<int> SeedStudentAsync(params int[] xpRows)
    {
        await using var ctx = _db.NewContext();
        var student = new Student { UserId = UserId, StudentNumber = "S42" };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        foreach (var xp in xpRows)
            ctx.StudentPoints.Add(new StudentPoint { StudentId = student.Id, XP = xp, Level = 0 });
        await ctx.SaveChangesAsync();
        return student.Id;
    }

    private async Task<StudentDto> RefreshAsync()
    {
        await using var ctx = _db.NewContext();
        var cache = new UserProfileCacheService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<UserProfileCacheService>>());
        var controller = new AuthController(
            ctx,
            Options.Create(new KeycloakSettings()),
            Substitute.For<IHttpClientFactory>(),
            cache,
            _profileProvider,
            Substitute.For<IKeycloakService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, Sub) }, authenticationType: "TestAuth"))
                }
            }
        };

        var ok = (await controller.RefreshProfileInformation()).ShouldBeOfType<OkObjectResult>();
        return ok.Value.ShouldBeOfType<UserProfileDto>().Student.ShouldNotBeNull();
    }

    [Fact]
    public async Task Student_without_points_gets_minimum_level_one_not_zero()
    {
        var studentId = await SeedStudentAsync();

        var dto = await RefreshAsync();

        dto.Id.ShouldBe(studentId);
        dto.XP.ShouldBe(0);
        dto.Level.ShouldBe(StudentLevel.MinLevel);
    }

    [Fact]
    public async Task Level_is_computed_from_total_xp()
    {
        await SeedStudentAsync(200); // StudentPoints.StudentId unique — öğrenci başına tek satır; 200 XP → seviye 3

        var dto = await RefreshAsync();

        dto.XP.ShouldBe(200);
        dto.Level.ShouldBe(3);
    }

    public void Dispose() => _db.Dispose();
}
