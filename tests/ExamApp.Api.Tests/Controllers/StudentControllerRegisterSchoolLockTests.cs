using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Constants;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.LoginEvents;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #259: <c>POST student/register</c> — okul kilidi 409'a eşlenir ve önbelleğe yazılan SchoolId istekten değil
/// DB'den (ISchoolContextResolver) gelir (#234 notu).
/// </summary>
public class StudentControllerRegisterSchoolLockTests : IDisposable
{
    private const string KeycloakId = "kc-259";
    private const int UserId = 259;

    private readonly TestDb _db = TestDb.Create();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private readonly UserProfileCacheService _cache = new(
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<UserProfileCacheService>>());

    public StudentControllerRegisterSchoolLockTests()
    {
        // Token alanları boş bırakılır: controller yalnızca yanıtı geçirir, bu testler token içeriğine bakmaz.
        _keycloak.RefreshTokenAsync(Arg.Any<string>()).Returns(new TokenResponseDto { ExpiresIn = 300 });
    }

    public void Dispose() => _db.Dispose();

    private StudentController NewController(AppDbContext ctx, IStudentService studentService)
    {
        var profileProvider = Substitute.For<IUserProfileProvider>();
        profileProvider.GetAsync(KeycloakId, Arg.Any<CancellationToken>())
            .Returns(_ => new UserProfileDto { Id = UserId, KeycloakId = KeycloakId, FullName = "Öğrenci", Email = "s@x", Role = "" });

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(profileProvider)
            .AddSingleton<ISchoolContextResolver>(new SchoolContextResolver(ctx))
            .BuildServiceProvider();

        var controller = new StudentController(
            Substitute.For<IMinIoService>(),
            studentService,
            _cache,
            Options.Create(new KeycloakSettings()),
            _keycloak,
            Substitute.For<IStudentResetScheduler>(),
            Substitute.For<ILoginEventService>(),
            Substitute.For<ILogger<StudentController>>());

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, KeycloakId) }, "TestAuth")),
            RequestServices = services
        };
        http.Request.Headers.Cookie = "refresh_token=cookie-value";
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private async Task<(int SchoolA, int SchoolB, int GradeId)> SeedAsync(bool existingStudentInSchoolA = false)
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A" };
        var b = new School { Name = "B" };
        var g = new Grade { Name = "8" };
        ctx.AddRange(a, b, g);
        await ctx.SaveChangesAsync();
        if (existingStudentInSchoolA)
        {
            ctx.Students.Add(new Student { UserId = UserId, StudentNumber = "s1", SchoolId = a.Id, GradeId = g.Id });
            await ctx.SaveChangesAsync();
        }
        return (a.Id, b.Id, g.Id);
    }

    [Fact]
    public async Task Register_with_a_different_school_after_lock_returns_409_and_touches_neither_cache_nor_keycloak()
    {
        var (schoolA, schoolB, gradeId) = await SeedAsync(existingStudentInSchoolA: true);
        await using var ctx = _db.NewContext();
        var controller = NewController(ctx, new StudentService(ctx, Substitute.For<IAuthApiClient>(), new SchoolAccessPolicy(ctx)));

        var result = await controller.RegisterStudent(new RegisterStudentDto { StudentNumber = "s1", SchoolId = schoolB, GradeId = gradeId });

        result.ShouldBeOfType<ConflictObjectResult>();
        (await _cache.GetAsync(KeycloakId)).ShouldBeNull();
        await _keycloak.DidNotReceive().SetRoleAsync(Arg.Any<string>(), Arg.Any<UserRole>());
        await _keycloak.DidNotReceive().SetSchoolIdAttributeAsync(Arg.Any<string>(), Arg.Any<int?>());
        await using var check = _db.NewContext();
        check.Students.Single(s => s.UserId == UserId).SchoolId.ShouldBe(schoolA);
    }

    [Fact]
    public async Task Register_success_caches_the_school_from_the_database_not_from_the_request()
    {
        // Servis "başarılı" döner ama DB'deki okul A kalır (ör. kilit istekteki B'yi yazmadı): önbellek ve Keycloak
        // school_id ipucu DB değerini (A) almalı — istek gövdesindeki okul (B) kapsama taşınmamalı.
        var (schoolA, schoolB, gradeId) = await SeedAsync(existingStudentInSchoolA: true);
        var studentService = Substitute.For<IStudentService>();
        studentService.Save(UserId, Arg.Any<RegisterStudentDto>(), Arg.Any<ExamApp.Api.Helpers.UserRoleChangeRequest?>()).Returns(new ResponseBaseDto { Success = true });
        await using var ctx = _db.NewContext();
        var controller = NewController(ctx, studentService);

        var result = await controller.RegisterStudent(new RegisterStudentDto { StudentNumber = "s1", SchoolId = schoolB, GradeId = gradeId });

        result.ShouldBeOfType<OkObjectResult>();
        var cached = await _cache.GetAsync(KeycloakId);
        cached.ShouldNotBeNull();
        cached!.SchoolId.ShouldBe(schoolA);
        cached.Role.ShouldBe(nameof(UserRole.Student));
        await _keycloak.Received(1).SetSchoolIdAttributeAsync(KeycloakId, schoolA);
    }

    [Fact]
    public async Task First_school_assignment_is_cached_from_the_database()
    {
        var (schoolA, _, gradeId) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var controller = NewController(ctx, new StudentService(ctx, Substitute.For<IAuthApiClient>(), new SchoolAccessPolicy(ctx)));

        var result = await controller.RegisterStudent(new RegisterStudentDto { StudentNumber = "s1", SchoolId = schoolA, GradeId = gradeId });

        result.ShouldBeOfType<OkObjectResult>();
        (await _cache.GetAsync(KeycloakId))!.SchoolId.ShouldBe(schoolA);
    }
}
