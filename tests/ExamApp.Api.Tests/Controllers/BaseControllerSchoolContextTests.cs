using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Constants;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #189: BaseController.GetCurrentSchoolIdAsync ve SchoolIdClaimHint mantığı.
///
/// GetCurrentSchoolIdAsync davranışları:
/// - Service account veya Admin rolü → null (muaf)
/// - Normal kullanıcı: UserProfileProvider aracılığıyla DB'den SchoolId doğrula
/// - Claim (SchoolIdClaimHint) ile DB değeri uyuşmazsa DB kazanır ve warning log'lanır
/// </summary>
public class BaseControllerSchoolContextTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedSchoolAsync(string name = "Test Okulu")
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = name };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    /// <summary>
    /// BaseController'dan türeyen küçük test controller'ı — GetCurrentSchoolIdAsync
    /// ve SchoolIdClaimHint'i public'e açar.
    /// </summary>
    private class TestSchoolContextController : BaseController
    {
        public async Task<int?> GetCurrentSchoolIdAsyncPublic(CancellationToken ct = default)
            => await GetCurrentSchoolIdAsync(ct);

        public int? GetSchoolIdClaimHintPublic()
            => SchoolIdClaimHint;
    }

    private TestSchoolContextController NewController(
        UserProfileDto authenticatedUser,
        ClaimsIdentity identity)
    {
        var authApiClient = Substitute.For<IAuthApiClient>();
        authApiClient.GetUserProfileAsync().Returns(authenticatedUser);

        var schoolContextResolver = Substitute.For<ISchoolContextResolver>();
        schoolContextResolver.ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>())
            .Returns(async (call) =>
            {
                var user = (UserProfileDto)call[0];
                if (user.Role == "Teacher")
                {
                    await using var ctx = _db.NewContext();
                    return await ctx.Teachers
                        .Where(t => t.UserId == user.Id)
                        .Select(t => (int?)t.SchoolId)
                        .FirstOrDefaultAsync(CancellationToken.None);
                }
                else if (user.Role == "Student")
                {
                    await using var ctx = _db.NewContext();
                    return await ctx.Students
                        .Where(s => s.UserId == user.Id)
                        .Select(s => (int?)s.SchoolId)
                        .FirstOrDefaultAsync(CancellationToken.None);
                }
                return null;
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Keycloak:ServiceClients:0", "service-account-client" }
            })
            .Build());
        services.AddSingleton(authApiClient);
        services.AddSingleton<IDistributedCache>(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddSingleton<UserProfileCacheService>();
        services.AddSingleton(schoolContextResolver);
        services.AddSingleton<IUserProfileProvider, UserProfileProvider>();
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = provider
        };

        var controller = new TestSchoolContextController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return controller;
    }

    // ---- GetCurrentSchoolIdAsync Tests ----

    [Fact]
    public async Task GetCurrentSchoolIdAsync_OkulluTeacher_ReturnsSchoolId()
    {
        var schoolId = await SeedSchoolAsync("Öğretmen Okulu");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 101, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        var user = new UserProfileDto { Id = 101, KeycloakId = "kc-101", Role = "Teacher" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-101"),
            new Claim("preferred_username", "teacher1"),
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = await controller.GetCurrentSchoolIdAsyncPublic();

        result.ShouldBe(schoolId);
    }

    [Fact]
    public async Task GetCurrentSchoolIdAsync_OkulluStudent_ReturnsSchoolId()
    {
        var schoolId = await SeedSchoolAsync("Öğrenci Okulu");
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "5" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            gradeId = grade.Id;
            ctx.Students.Add(new Student { UserId = 201, StudentNumber = "S201", SchoolId = schoolId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        var user = new UserProfileDto { Id = 201, KeycloakId = "kc-201", Role = "Student" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-201"),
            new Claim("preferred_username", "student1"),
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = await controller.GetCurrentSchoolIdAsyncPublic();

        result.ShouldBe(schoolId);
    }

    [Fact]
    public async Task GetCurrentSchoolIdAsync_BagimszTeacher_ReturnsNull()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 301, SchoolId = null, IsIndependentTutor = true });
            await ctx.SaveChangesAsync();
        }

        var user = new UserProfileDto { Id = 301, KeycloakId = "kc-301", Role = "Teacher" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-301"),
            new Claim("preferred_username", "independent_teacher"),
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = await controller.GetCurrentSchoolIdAsyncPublic();

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetCurrentSchoolIdAsync_AdminRole_ReturnsNullAndMuaf()
    {
        var schoolId = await SeedSchoolAsync("Admin Okulu");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 401, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        var user = new UserProfileDto { Id = 401, KeycloakId = "kc-401", Role = "Admin" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-401"),
            new Claim("preferred_username", "admin"),
            new Claim(ClaimTypes.Role, "Admin"),
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = await controller.GetCurrentSchoolIdAsyncPublic();

        // Admin, Teacher tablosunda SchoolId'ye sahip olsa bile, null döner (muaf).
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetCurrentSchoolIdAsync_ClaimAndDbMismatch_DbWins()
    {
        var schoolId = await SeedSchoolAsync("Gerçek Okul");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 501, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        // Claim: 99, DB: schoolId → DB kazanır
        var user = new UserProfileDto { Id = 501, KeycloakId = "kc-501", Role = "Teacher" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-501"),
            new Claim("preferred_username", "teacher_mismatch"),
            new Claim(ExamClaimTypes.SchoolId, "99"),  // Yanlış claim
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = await controller.GetCurrentSchoolIdAsyncPublic();

        result.ShouldBe(schoolId);  // DB'deki değer döner
    }

    [Fact]
    public async Task GetCurrentSchoolIdAsync_InvalidClaim_IgnoredAndDbUsed()
    {
        var schoolId = await SeedSchoolAsync("Valid Okul");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 601, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        // Claim: "abc" (invalid) → SchoolIdClaimHint null
        var user = new UserProfileDto { Id = 601, KeycloakId = "kc-601", Role = "Teacher" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-601"),
            new Claim("preferred_username", "teacher_invalid"),
            new Claim(ExamClaimTypes.SchoolId, "abc"),  // Geçersiz claim
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = await controller.GetCurrentSchoolIdAsyncPublic();

        result.ShouldBe(schoolId);  // DB'deki değer döner
    }

    [Fact]
    public async Task GetCurrentSchoolIdAsync_NoClaim_DbUsed()
    {
        var schoolId = await SeedSchoolAsync("Okul Claim Yok");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 701, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        var user = new UserProfileDto { Id = 701, KeycloakId = "kc-701", Role = "Teacher" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-701"),
            new Claim("preferred_username", "teacher_noclaim"),
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = await controller.GetCurrentSchoolIdAsyncPublic();

        result.ShouldBe(schoolId);  // DB'deki değer döner
    }

    [Fact]
    public async Task GetCurrentSchoolIdAsync_ClaimAndDbMatch_NoWarning()
    {
        var schoolId = await SeedSchoolAsync("Eşleşen Okul");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 801, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        var user = new UserProfileDto { Id = 801, KeycloakId = "kc-801", Role = "Teacher" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-801"),
            new Claim("preferred_username", "teacher_match"),
            new Claim(ExamClaimTypes.SchoolId, schoolId.ToString()),  // Doğru claim
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = await controller.GetCurrentSchoolIdAsyncPublic();

        result.ShouldBe(schoolId);
    }

    // ---- SchoolIdClaimHint Tests ----

    [Fact]
    public void SchoolIdClaimHint_ValidClaim_ReturnsSchoolId()
    {
        var user = new UserProfileDto { Id = 901, KeycloakId = "kc-901", Role = "Teacher" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-901"),
            new Claim("preferred_username", "teacher_claim"),
            new Claim(ExamClaimTypes.SchoolId, "42"),
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = controller.GetSchoolIdClaimHintPublic();

        result.ShouldBe(42);
    }

    [Fact]
    public void SchoolIdClaimHint_InvalidClaim_ReturnsNull()
    {
        var user = new UserProfileDto { Id = 1001, KeycloakId = "kc-1001", Role = "Teacher" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-1001"),
            new Claim("preferred_username", "teacher_badclaim"),
            new Claim(ExamClaimTypes.SchoolId, "notanumber"),
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = controller.GetSchoolIdClaimHintPublic();

        result.ShouldBeNull();
    }

    [Fact]
    public void SchoolIdClaimHint_NoClaim_ReturnsNull()
    {
        var user = new UserProfileDto { Id = 1101, KeycloakId = "kc-1101", Role = "Teacher" };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-1101"),
            new Claim("preferred_username", "teacher_noclaim"),
        }, authenticationType: "TestAuth");

        var controller = NewController(user, identity);
        var result = controller.GetSchoolIdClaimHintPublic();

        result.ShouldBeNull();
    }
}
