using System.Net.Http;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #156 (#155 ortak): admin hesap aksiyonu hedef çözme zinciri.</summary>
public class AdminAccountTargetResolverTests : IDisposable
{
    private const string ActorSub = "kc-admin-actor";
    private const string TargetSub = "7f6e5d4c-3b2a-4100-9f8e-aabbccddeeff";

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();

    public AdminAccountTargetResolverTests()
    {
        _keycloak.GetUserRolesAsync(TargetSub, Arg.Any<CancellationToken>())
            .Returns(new KeycloakUserRolesDto(["Teacher", "default-roles-exam-realm"], []));
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedTeacherAsync(int userId = 42, bool deleted = false)
    {
        await using var ctx = _db.NewContext();
        var teacher = new Teacher { UserId = userId, IsDeleted = deleted };
        ctx.Teachers.Add(teacher);
        await ctx.SaveChangesAsync();
        return teacher.Id;
    }

    private async Task<int> SeedStudentAsync(int userId = 43)
    {
        await using var ctx = _db.NewContext();
        var student = new Student { UserId = userId, StudentNumber = "S1" };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        return student.Id;
    }

    private void ResolvesTo(int userId, string sub) =>
        _authApi.GetUsersByIdsOrThrowAsync(Arg.Is<IEnumerable<int>>(ids => ids.Contains(userId)), Arg.Any<CancellationToken>())
            .Returns(new[] { new UserLookupResultDto { Id = userId, KeycloakId = sub } });

    private async Task<AdminAccountTargetResolution> RunAsync(AdminUserTargetType type, int id, string actor = ActorSub)
    {
        await using var ctx = _db.NewContext();
        return await new AdminAccountTargetResolver(ctx, _authApi, _keycloak).ResolveAsync(type, id, actor);
    }

    [Fact]
    public async Task Resolves_teacher_and_student_to_keycloak_sub()
    {
        var teacherId = await SeedTeacherAsync();
        var studentId = await SeedStudentAsync();
        ResolvesTo(42, TargetSub);
        ResolvesTo(43, TargetSub);

        (await RunAsync(AdminUserTargetType.Teacher, teacherId)).ShouldBe(new(AdminAccountTargetStatus.Resolved, TargetSub));
        (await RunAsync(AdminUserTargetType.Student, studentId)).ShouldBe(new(AdminAccountTargetStatus.Resolved, TargetSub));
        await _authApi.DidNotReceiveWithAnyArgs().GetUsersByIdsAsync(default!, default); // fail-soft varyant kullanılmaz
    }

    [Fact]
    public async Task Missing_or_deleted_record_is_TargetNotFound_without_upstream_calls()
    {
        var deletedId = await SeedTeacherAsync(deleted: true);

        (await RunAsync(AdminUserTargetType.Teacher, 9999)).Status.ShouldBe(AdminAccountTargetStatus.TargetNotFound);
        (await RunAsync(AdminUserTargetType.Teacher, deletedId)).Status.ShouldBe(AdminAccountTargetStatus.TargetNotFound);
        _authApi.ReceivedCalls().ShouldBeEmpty();
        _keycloak.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("../roles")]
    public async Task Unresolvable_or_malformed_sub_is_AccountNotFound(string sub)
    {
        var teacherId = await SeedTeacherAsync();
        ResolvesTo(42, sub);

        (await RunAsync(AdminUserTargetType.Teacher, teacherId)).Status.ShouldBe(AdminAccountTargetStatus.AccountNotFound);
        _keycloak.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Auth_api_or_service_token_failure_is_UpstreamFailure_not_404()
    {
        var teacherId = await SeedTeacherAsync();
        _authApi.GetUsersByIdsOrThrowAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("auth-api service token could not be obtained."));

        (await RunAsync(AdminUserTargetType.Teacher, teacherId)).Status.ShouldBe(AdminAccountTargetStatus.UpstreamFailure);
    }

    [Fact]
    public async Task Self_target_is_ForbiddenSelf_before_any_keycloak_call()
    {
        var teacherId = await SeedTeacherAsync();
        ResolvesTo(42, ActorSub);

        (await RunAsync(AdminUserTargetType.Teacher, teacherId)).Status.ShouldBe(AdminAccountTargetStatus.ForbiddenSelf);
        _keycloak.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("ADMIN")]
    [InlineData("exam-service")]
    public async Task Protected_realm_role_is_ForbiddenProtectedRole(string role)
    {
        var teacherId = await SeedTeacherAsync();
        ResolvesTo(42, TargetSub);
        _keycloak.GetUserRolesAsync(TargetSub, Arg.Any<CancellationToken>()).Returns(new KeycloakUserRolesDto(["Teacher", role], []));

        (await RunAsync(AdminUserTargetType.Teacher, teacherId)).Status.ShouldBe(AdminAccountTargetStatus.ForbiddenProtectedRole);
    }

    [Theory]
    [InlineData("realm-admin")]
    [InlineData("manage-users")]
    [InlineData("view-users")]
    public async Task Any_realm_management_client_role_is_ForbiddenProtectedRole(string clientRole)
    {
        var teacherId = await SeedTeacherAsync();
        ResolvesTo(42, TargetSub);
        _keycloak.GetUserRolesAsync(TargetSub, Arg.Any<CancellationToken>()).Returns(new KeycloakUserRolesDto(["Teacher"], [clientRole]));

        (await RunAsync(AdminUserTargetType.Teacher, teacherId)).Status.ShouldBe(AdminAccountTargetStatus.ForbiddenProtectedRole);
    }

    [Fact]
    public async Task Keycloak_404_on_role_lookup_is_AccountNotFound()
    {
        var teacherId = await SeedTeacherAsync();
        ResolvesTo(42, TargetSub);
        _keycloak.GetUserRolesAsync(TargetSub, Arg.Any<CancellationToken>())
            .Returns<KeycloakUserRolesDto>(_ => throw new KeycloakException("Keycloak role lookup failed: 404", 404));

        (await RunAsync(AdminUserTargetType.Teacher, teacherId)).Status.ShouldBe(AdminAccountTargetStatus.AccountNotFound);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)] // admin token alınamadı / access_token yok
    public async Task Other_keycloak_errors_are_UpstreamFailure(int status)
    {
        var teacherId = await SeedTeacherAsync();
        ResolvesTo(42, TargetSub);
        _keycloak.GetUserRolesAsync(TargetSub, Arg.Any<CancellationToken>())
            .Returns<KeycloakUserRolesDto>(_ => throw new KeycloakException("x", status));

        (await RunAsync(AdminUserTargetType.Teacher, teacherId)).Status.ShouldBe(AdminAccountTargetStatus.UpstreamFailure);
    }
}
