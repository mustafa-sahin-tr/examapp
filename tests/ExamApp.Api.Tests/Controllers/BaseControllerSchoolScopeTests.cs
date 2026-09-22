using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Constants;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #190: BaseController.GetSchoolScopeAsync — servislere geçilen tenant bağlamı.
/// - Admin → Unrestricted (Teacher tablosunda SchoolId olsa bile; resolver stub'ı ROLE'e bakmaz,
///   yani kısa devre silinirse SchoolId dolu gelir ve test kırılır)
/// - Servis hesabı → Unrestricted, profil hiç çözülmez
/// - Okullu öğretmen/öğrenci → For(userId, DB'deki SchoolId)
/// - Okulsuz öğretmen / Teacher satırı olmayan Teacher rolü → For(userId, null)
/// - Profil çözülemezse fırlatır (fail-closed); claim ≠ DB → DB kazanır
/// - Profil provider'ı tek sefer çağrılır (tek Redis okuması)
/// </summary>
public class BaseControllerSchoolScopeTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private class TestController : BaseController
    {
        public Task<SchoolScope> GetSchoolScopeAsyncPublic(CancellationToken ct = default) => GetSchoolScopeAsync(ct);
    }

    /// <summary>
    /// Resolver stub'ı rolden BAĞIMSIZ: önce Teachers, sonra Students satırını UserId ile çözer.
    /// Böylece admin testinde SchoolId'nin null gelmesi yalnızca IsAdmin kısa devresine bağlıdır.
    /// </summary>
    private ISchoolContextResolver RoleAgnosticResolver()
    {
        var resolver = Substitute.For<ISchoolContextResolver>();
        resolver.ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var user = (UserProfileDto)call[0];
                await using var ctx = _db.NewContext();
                var teacher = await ctx.Teachers.Where(t => t.UserId == user.Id).Select(t => new { t.SchoolId }).FirstOrDefaultAsync();
                if (teacher != null) return teacher.SchoolId;
                var student = await ctx.Students.Where(s => s.UserId == user.Id).Select(s => new { s.SchoolId }).FirstOrDefaultAsync();
                return student?.SchoolId;
            });
        return resolver;
    }

    private static TestController Build(ClaimsIdentity identity, IUserProfileProvider provider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Keycloak:ServiceClients:0", "exam-admin" } })
            .Build());
        services.AddSingleton(provider);

        return new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity),
                    RequestServices = services.BuildServiceProvider()
                }
            }
        };
    }

    private TestController NewController(UserProfileDto authenticatedUser, ClaimsIdentity identity)
    {
        var authApiClient = Substitute.For<IAuthApiClient>();
        authApiClient.GetUserProfileAsync().Returns(authenticatedUser);

        var cache = new UserProfileCacheService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<UserProfileCacheService>>());

        return Build(identity, new UserProfileProvider(cache, authApiClient, RoleAgnosticResolver()));
    }

    private static ClaimsIdentity Identity(string keycloakId, params Claim[] extra)
        => new(new[] { new Claim(ClaimTypes.NameIdentifier, keycloakId), new Claim("preferred_username", keycloakId) }.Concat(extra),
            authenticationType: "TestAuth");

    private async Task<int> SeedSchoolAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Okul" };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    [Fact]
    public async Task SchoolBoundTeacher_ScopeIsForItsSchool()
    {
        var schoolId = await SeedSchoolAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 101, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        var controller = NewController(
            new UserProfileDto { Id = 101, KeycloakId = "kc-101", Role = "Teacher" },
            Identity("kc-101", new Claim(ClaimTypes.Role, "Teacher")));

        var scope = await controller.GetSchoolScopeAsyncPublic();

        scope.ShouldBe(SchoolScope.For(101, schoolId));
        scope.IsIndependent.ShouldBeFalse();
    }

    [Fact]
    public async Task SchoolBoundStudent_ScopeIsForItsSchool()
    {
        var schoolId = await SeedSchoolAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Students.Add(new Student { UserId = 201, StudentNumber = "S201", SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        var controller = NewController(
            new UserProfileDto { Id = 201, KeycloakId = "kc-201", Role = "Student" },
            Identity("kc-201", new Claim(ClaimTypes.Role, "Student")));

        var scope = await controller.GetSchoolScopeAsyncPublic();

        scope.ShouldBe(SchoolScope.For(201, schoolId));
    }

    [Fact]
    public async Task IndependentTeacher_ScopeIsIndependent()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 301, SchoolId = null, IsIndependentTutor = true });
            await ctx.SaveChangesAsync();
        }

        var controller = NewController(
            new UserProfileDto { Id = 301, KeycloakId = "kc-301", Role = "Teacher" },
            Identity("kc-301", new Claim(ClaimTypes.Role, "Teacher")));

        var scope = await controller.GetSchoolScopeAsyncPublic();

        scope.ShouldBe(SchoolScope.For(301, null));
        scope.IsIndependent.ShouldBeTrue();
    }

    [Fact]
    public async Task TeacherRoleWithoutTeacherRow_ScopeIsForNull()
    {
        // Role=Teacher ama Teachers satırı yok (legacy/eksik profil) → okulsuz sayılır, fırlatmaz.
        var controller = NewController(
            new UserProfileDto { Id = 302, KeycloakId = "kc-302", Role = "Teacher" },
            Identity("kc-302", new Claim(ClaimTypes.Role, "Teacher")));

        var scope = await controller.GetSchoolScopeAsyncPublic();

        scope.ShouldBe(SchoolScope.For(302, null));
    }

    [Fact]
    public async Task Admin_ScopeIsUnrestricted_EvenWithTeacherRecord()
    {
        var schoolId = await SeedSchoolAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 401, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        var controller = NewController(
            new UserProfileDto { Id = 401, KeycloakId = "kc-401", Role = "Admin" },
            Identity("kc-401", new Claim(ClaimTypes.Role, "Admin")));

        var scope = await controller.GetSchoolScopeAsyncPublic();

        // Resolver rolden bağımsız olduğu için DB'den schoolId çözülürdü; null olması IsAdmin kısa devresini kanıtlar.
        scope.ShouldBe(SchoolScope.Unrestricted(401));
    }

    [Fact]
    public async Task ServiceAccount_ScopeIsUnrestricted_WithoutResolvingProfile()
    {
        var provider = Substitute.For<IUserProfileProvider>();
        var controller = Build(Identity("svc", new Claim("azp", "exam-admin")), provider);

        var scope = await controller.GetSchoolScopeAsyncPublic();

        scope.IsUnrestricted.ShouldBeTrue();
        await provider.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProfileCannotBeResolved_Throws_FailClosed()
    {
        // GetAuthenticatedUserAsync burada sahte Id=0/Role=Service DTO'su üretirdi; scope buna güvenmez.
        var provider = Substitute.For<IUserProfileProvider>();
        provider.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<UserProfileDto>(_ => throw new InvalidOperationException("redis down"));
        var controller = Build(Identity("kc-500", new Claim(ClaimTypes.Role, "Teacher")), provider);

        await Should.ThrowAsync<InvalidOperationException>(() => controller.GetSchoolScopeAsyncPublic());
    }

    [Fact]
    public async Task ProfileResolvesToNull_Throws_FailClosed()
    {
        var provider = Substitute.For<IUserProfileProvider>();
        provider.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((UserProfileDto?)null!);
        var controller = Build(Identity("kc-501", new Claim(ClaimTypes.Role, "Teacher")), provider);

        await Should.ThrowAsync<InvalidOperationException>(() => controller.GetSchoolScopeAsyncPublic());
    }

    [Fact]
    public async Task ClaimDiffersFromDb_DbWins()
    {
        var schoolId = await SeedSchoolAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 601, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        var controller = NewController(
            new UserProfileDto { Id = 601, KeycloakId = "kc-601", Role = "Teacher" },
            Identity("kc-601", new Claim(ClaimTypes.Role, "Teacher"), new Claim(ExamClaimTypes.SchoolId, "99")));

        var scope = await controller.GetSchoolScopeAsyncPublic();

        scope.SchoolId.ShouldBe(schoolId);
    }

    [Fact]
    public async Task Profile_IsResolvedExactlyOnce()
    {
        var provider = Substitute.For<IUserProfileProvider>();
        provider.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = 700, KeycloakId = "kc-700", Role = "Teacher", SchoolId = 5 });
        var controller = Build(Identity("kc-700", new Claim(ClaimTypes.Role, "Teacher")), provider);

        var scope = await controller.GetSchoolScopeAsyncPublic();

        scope.ShouldBe(SchoolScope.For(700, 5));
        await provider.Received(1).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
