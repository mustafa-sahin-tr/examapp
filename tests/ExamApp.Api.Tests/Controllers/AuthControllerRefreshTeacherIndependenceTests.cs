using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
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
/// issue #418: <c>POST api/auth/refresh</c> öğretmen profilindeki <c>IsIndependentTutor</c> (UI menü/guard bayrağı) randevu
/// servisleriyle aynı kuralı taşır: <c>IsIndependentTutor &amp;&amp; SchoolId == null</c>. Hibrit (bağımsız başvurulu ama
/// okula bağlı) öğretmen bağımsız değildir.
/// </summary>
public class AuthControllerRefreshTeacherIndependenceTests : IDisposable
{
    private const int UserId = 43;
    private const string Sub = "kc-43";

    private readonly TestDb _db = TestDb.Create();
    private readonly IUserProfileProvider _profileProvider = Substitute.For<IUserProfileProvider>();

    public AuthControllerRefreshTeacherIndependenceTests()
    {
        _profileProvider.GetAsync(Sub, Arg.Any<CancellationToken>())
            .Returns(_ => new UserProfileDto { Id = UserId, KeycloakId = Sub, Role = "Teacher", FullName = "Öğretmen", Avatar = "" });
    }

    private async Task SeedTeacherAsync(bool isIndependentTutor, bool withSchool)
    {
        await using var ctx = _db.NewContext();
        int? schoolId = null;
        if (withSchool)
        {
            var school = new School { Name = "Okul" };
            ctx.Schools.Add(school);
            await ctx.SaveChangesAsync();
            schoolId = school.Id;
        }
        ctx.Teachers.Add(new Teacher
        {
            UserId = UserId, IsIndependentTutor = isIndependentTutor, SchoolId = schoolId,
            ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<TeacherDto> RefreshAsync()
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
                        new[] { new Claim(ClaimTypes.NameIdentifier, Sub), new Claim(ClaimTypes.Role, "Teacher") }, authenticationType: "TestAuth"))
                }
            }
        };

        var ok = (await controller.RefreshProfileInformation()).ShouldBeOfType<OkObjectResult>();
        return ok.Value.ShouldBeOfType<UserProfileDto>().Teacher.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(true, false, true)]   // bağımsız, okulsuz
    [InlineData(true, true, false)]   // hibrit: bağımsız başvurulu ama okula bağlı
    [InlineData(false, true, false)]  // okula bağlı
    [InlineData(false, false, false)] // okulsuz, bağımsız değil (okul talebi bekliyor vb.)
    public async Task Refresh_teacher_independence_flag_follows_the_booking_rule(bool isIndependentTutor, bool withSchool, bool expected)
    {
        await SeedTeacherAsync(isIndependentTutor, withSchool);

        var dto = await RefreshAsync();

        dto.IsIndependentTutor.ShouldBe(expected);
    }

    public void Dispose() => _db.Dispose();
}
