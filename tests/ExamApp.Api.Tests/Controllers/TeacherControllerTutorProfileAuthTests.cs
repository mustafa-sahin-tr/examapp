using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Tutors;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
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
/// Issue #95: tutor profile endpoints authorization.
/// Testler: GetTutorProfile, UpdateTutorProfile, SearchTutors, GetPublicProfile endpoint davranışları.
/// </summary>
public class TeacherControllerTutorProfileAuthTests
{
    private readonly ITeacherService _teacherService = Substitute.For<ITeacherService>();
    private readonly UserProfileCacheService _cacheService;
    private readonly IKeycloakService _keycloakService = Substitute.For<IKeycloakService>();
    private readonly IAuthApiClient _authApiClient = Substitute.For<IAuthApiClient>();

    public TeacherControllerTutorProfileAuthTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(_authApiClient);
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddSingleton<UserProfileCacheService>();
        var provider = services.BuildServiceProvider();
        _cacheService = provider.GetRequiredService<UserProfileCacheService>();
    }

    private TeacherController NewController(UserProfileDto authenticatedUser)
    {
        _authApiClient.GetUserProfileAsync().Returns(authenticatedUser);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(_authApiClient);
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddSingleton<UserProfileCacheService>();
        var provider = services.BuildServiceProvider();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, authenticatedUser.KeycloakId),
            new Claim("preferred_username", "testuser"),
        }, authenticationType: "TestAuth");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = provider
        };

        var controller = new TeacherController(_teacherService, provider.GetRequiredService<UserProfileCacheService>(), _keycloakService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return controller;
    }

    // ---- GetTutorProfile Tests ----

    [Fact]
    public async Task GetTutorProfile_Success_ReturnsOkWithProfile()
    {
        var user = new UserProfileDto { Id = 1, KeycloakId = "kc-user-1", Role = "Teacher" };
        var profileDto = new TutorProfileDto
        {
            TeacherId = 10,
            ApprovalStatus = TeacherApprovalStatus.Approved,
            HourlyRate = 150,
            TeachesOnline = true,
            TeachesInPerson = false,
            Subjects = new List<TutorSubjectDto> { new() { SubjectId = 1, Name = "Matematik" } }
        };

        var resultDto = new TutorProfileResultDto
        {
            Success = true,
            Profile = profileDto
        };

        _teacherService.GetTutorProfileAsync(1, Arg.Any<CancellationToken>()).Returns(resultDto);

        var controller = NewController(user);
        var result = await controller.GetTutorProfile(CancellationToken.None);

        result.Result.ShouldBeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result;
        okResult.Value.ShouldBe(profileDto);
    }

    [Fact]
    public async Task GetTutorProfile_NotFound_Returns404()
    {
        var user = new UserProfileDto { Id = 1, KeycloakId = "kc-user-1", Role = "Teacher" };
        var resultDto = new TutorProfileResultDto { Success = false, NotFound = true, Message = "Öğretmen bulunamadı." };

        _teacherService.GetTutorProfileAsync(1, Arg.Any<CancellationToken>()).Returns(resultDto);

        var controller = NewController(user);
        var result = await controller.GetTutorProfile(CancellationToken.None);

        result.Result.ShouldBeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetTutorProfile_NotIndependent_Returns400()
    {
        var user = new UserProfileDto { Id = 1, KeycloakId = "kc-user-1", Role = "Teacher" };
        var resultDto = new TutorProfileResultDto { Success = false, Forbidden = true, Message = "Tutor profili yalnızca bağımsız öğretmenler için kullanılabilir." };

        _teacherService.GetTutorProfileAsync(1, Arg.Any<CancellationToken>()).Returns(resultDto);

        var controller = NewController(user);
        var result = await controller.GetTutorProfile(CancellationToken.None);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
    }

    // ---- UpdateTutorProfile Tests ----

    [Fact]
    public async Task UpdateTutorProfile_Success_ReturnsOkWithUpdatedProfile()
    {
        var user = new UserProfileDto { Id = 1, KeycloakId = "kc-user-1", Role = "Teacher" };
        var request = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { 1 },
            HourlyRate = 200,
            TeachesOnline = true,
            TeachesInPerson = false,
            Bio = "Bio text"
        };

        var profileDto = new TutorProfileDto
        {
            TeacherId = 10,
            ApprovalStatus = TeacherApprovalStatus.Approved,
            HourlyRate = 200,
            TeachesOnline = true,
            TeachesInPerson = false,
            Bio = "Bio text"
        };

        var resultDto = new TutorProfileResultDto
        {
            Success = true,
            Profile = profileDto,
            Message = "Tutor profili güncellendi."
        };

        _teacherService.UpdateTutorProfileAsync(1, request, Arg.Any<CancellationToken>()).Returns(resultDto);

        var controller = NewController(user);
        var result = await controller.UpdateTutorProfile(request, CancellationToken.None);

        result.Result.ShouldBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UpdateTutorProfile_ValidationError_Returns400()
    {
        var user = new UserProfileDto { Id = 1, KeycloakId = "kc-user-1", Role = "Teacher" };
        var request = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int>(),
            HourlyRate = 200,
            TeachesOnline = true,
            TeachesInPerson = false
        };

        var resultDto = new TutorProfileResultDto
        {
            Success = false,
            Message = "En az bir ders seçilmelidir."
        };

        _teacherService.UpdateTutorProfileAsync(1, request, Arg.Any<CancellationToken>()).Returns(resultDto);

        var controller = NewController(user);
        var result = await controller.UpdateTutorProfile(request, CancellationToken.None);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateTutorProfile_NotIndependent_Returns400()
    {
        var user = new UserProfileDto { Id = 1, KeycloakId = "kc-user-1", Role = "Teacher" };
        var request = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { 1 },
            HourlyRate = 200,
            TeachesOnline = true,
            TeachesInPerson = false
        };

        var resultDto = new TutorProfileResultDto
        {
            Success = false,
            Forbidden = true,
            Message = "Tutor profili yalnızca bağımsız öğretmenler için güncellenebilir."
        };

        _teacherService.UpdateTutorProfileAsync(1, request, Arg.Any<CancellationToken>()).Returns(resultDto);

        var controller = NewController(user);
        var result = await controller.UpdateTutorProfile(request, CancellationToken.None);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
    }

    // ---- SearchTutors Tests ----

    [Fact]
    public async Task SearchTutors_RequiresStudentRole_Returns200WithResults()
    {
        var user = new UserProfileDto { Id = 2, KeycloakId = "kc-student-1", Role = "Student" };
        var filter = new TeacherSearchFilterDto { SubjectId = 1 };
        var results = new List<TeacherSearchResultDto>
        {
            new() { TeacherId = 10, FullName = "Ahmet Öğretmen", HourlyRate = 150 }
        };

        _teacherService.SearchTutorsAsync(filter, Arg.Any<CancellationToken>()).Returns(results);

        var controller = NewController(user);
        var result = await controller.SearchTutors(filter, CancellationToken.None);

        var okResult = result.Result.ShouldBeOfType<OkObjectResult>();
        okResult.Value.ShouldBe(results);
    }

    [Fact]
    public async Task SearchTutors_InvalidPriceRange_Returns400()
    {
        var user = new UserProfileDto { Id = 2, KeycloakId = "kc-student-1", Role = "Student" };
        var filter = new TeacherSearchFilterDto { MinPrice = 200, MaxPrice = 100 };

        var controller = NewController(user);
        var result = await controller.SearchTutors(filter, CancellationToken.None);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SearchTutors_EmptyResults_ReturnsEmptyList()
    {
        var user = new UserProfileDto { Id = 2, KeycloakId = "kc-student-1", Role = "Student" };
        var filter = new TeacherSearchFilterDto { SubjectId = 999 };
        var results = new List<TeacherSearchResultDto>();

        _teacherService.SearchTutorsAsync(filter, Arg.Any<CancellationToken>()).Returns(results);

        var controller = NewController(user);
        var result = await controller.SearchTutors(filter, CancellationToken.None);

        var okResult = result.Result.ShouldBeOfType<OkObjectResult>();
        ((List<TeacherSearchResultDto>)okResult.Value).ShouldBeEmpty();
    }

    // ---- GetPublicProfile Tests ----

    [Fact]
    public async Task GetPublicProfile_ValidTeacherId_ReturnsOkWithProfile()
    {
        var user = new UserProfileDto { Id = 2, KeycloakId = "kc-student-1", Role = "Student" };
        var profile = new TeacherPublicProfileDto
        {
            TeacherId = 10,
            FullName = "Ahmet Öğretmen",
            Avatar = "http://avatar.url",
            HourlyRate = 150,
            TeachesOnline = true,
            TeachesInPerson = false,
            Bio = "Tam metin bio"
        };

        _teacherService.GetPublicProfileAsync(10, Arg.Any<CancellationToken>()).Returns(profile);

        var controller = NewController(user);
        var result = await controller.GetPublicProfile(10, CancellationToken.None);

        var okResult = result.Result.ShouldBeOfType<OkObjectResult>();
        okResult.Value.ShouldBe(profile);
    }

    [Fact]
    public async Task GetPublicProfile_TeacherNotFound_Returns404()
    {
        var user = new UserProfileDto { Id = 2, KeycloakId = "kc-student-1", Role = "Student" };

        _teacherService.GetPublicProfileAsync(999, Arg.Any<CancellationToken>()).Returns((TeacherPublicProfileDto?)null);

        var controller = NewController(user);
        var result = await controller.GetPublicProfile(999, CancellationToken.None);

        result.Result.ShouldBeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetPublicProfile_NotApprovedIndependent_Returns404()
    {
        var user = new UserProfileDto { Id = 2, KeycloakId = "kc-student-1", Role = "Student" };

        // Service returns null for pending/rejected/non-independent
        _teacherService.GetPublicProfileAsync(10, Arg.Any<CancellationToken>()).Returns((TeacherPublicProfileDto?)null);

        var controller = NewController(user);
        var result = await controller.GetPublicProfile(10, CancellationToken.None);

        result.Result.ShouldBeOfType<NotFoundObjectResult>();
    }
}
