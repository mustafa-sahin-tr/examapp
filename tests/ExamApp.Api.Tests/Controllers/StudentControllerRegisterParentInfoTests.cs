using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Constants;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentInvitations;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.LoginEvents;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Api.Services.Tenancy;
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
/// Issue #440 (+ review M1): <c>POST student/register</c> — veli kuralına tabi ve velisi/daveti olmayan öğrenciden veli bilgisi zorunlu.
/// Satırdan bağımsız tüm kontroller (ön kontrol) kayıttan ÖNCE: başarısızsa öğrenci satırı yazılmaz, rol verilmez. Davet yazımı kayıttan
/// SONRA, rolden ÖNCE; o da başarısızsa bu istekte açılan öğrenci satırı geri alınır. Başarıda yanıtta <c>parentStatus</c>. Yetişkin rolü
/// olan hesaptan istenmez.
/// </summary>
public class StudentControllerRegisterParentInfoTests : IDisposable
{
    private const string KeycloakId = "kc-440";
    private const int UserId = 440;

    private readonly TestDb _db = TestDb.Create();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private readonly IParentInvitationService _invitations = Substitute.For<IParentInvitationService>();
    private readonly UserProfileCacheService _cache = new(
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<UserProfileCacheService>>());
    private int _gradeId;

    public StudentControllerRegisterParentInfoTests()
    {
        _keycloak.RefreshTokenAsync(Arg.Any<string>()).Returns(new TokenResponseDto { ExpiresIn = 300 });
        _invitations.NeedsParentInfoAsync(UserId, true, Arg.Any<CancellationToken>()).Returns(true);
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "8" };
        ctx.Grades.Add(grade);
        ctx.SaveChanges();
        _gradeId = grade.Id;
    }

    public void Dispose() => _db.Dispose();

    private StudentController NewController(AppDbContext ctx, params string[] roles)
    {
        var profileProvider = Substitute.For<IUserProfileProvider>();
        profileProvider.GetAsync(KeycloakId, Arg.Any<CancellationToken>())
            .Returns(_ => new UserProfileDto { Id = UserId, KeycloakId = KeycloakId, FullName = "Ali Kaya", Email = "s@x", Role = "" });

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(profileProvider)
            .AddSingleton<ISchoolContextResolver>(new SchoolContextResolver(ctx))
            .AddSingleton<ExamApp.Api.Services.UserRoles.IUserRoleChangeRecorder>(new ExamApp.Api.Services.UserRoles.UserRoleChangeRecorder(ctx))
            .AddSingleton<ExamApp.Api.Services.UserRoles.IUserRoleExclusivity>(new ExamApp.Api.Services.UserRoles.UserRoleExclusivity(ctx))
            .AddSingleton(_invitations)
            .BuildServiceProvider();

        var controller = new StudentController(
            Substitute.For<IMinIoService>(),
            new StudentService(ctx, Substitute.For<IAuthApiClient>(), new SchoolAccessPolicy(ctx)),
            _cache,
            Options.Create(new KeycloakSettings()),
            _keycloak,
            Substitute.For<IStudentResetScheduler>(),
            Substitute.For<ILoginEventService>(),
            Substitute.For<ILogger<StudentController>>());

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, KeycloakId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            RequestServices = services
        };
        http.Request.Headers.Cookie = "refresh_token=cookie-value";
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private RegisterStudentDto Request(string? name = "Ayşe Kaya", string? email = "veli@example.com", string? phone = "05321234567") => new()
    {
        StudentNumber = "s1", GradeId = _gradeId, ParentFullName = name, ParentEmail = email, ParentPhone = phone
    };

    private static string? ErrorCodeOf(IActionResult result)
        => ((ObjectResult)result).Value!.GetType().GetProperty("errorCode")!.GetValue(((ObjectResult)result).Value) as string;

    private static ParentInvitationResultDto Failure(string code, bool conflict = false, bool upstream = false) => new()
    {
        Success = false, Conflict = conflict, Upstream = upstream, ErrorCode = code, Message = "x"
    };

    private void PrecheckReturns(ParentInvitationPrecheck result)
        => _invitations.PrecheckAsync(Arg.Any<ParentInvitationStudent>(), Arg.Any<ParentContactRequestDto>(), true, Arg.Any<CancellationToken>())
            .Returns(result);

    private static PreparedParentInvitation Prepared(bool studentExisted = false)
        => new(new ParentContactValidation(null, "Ayşe Kaya", "veli@example.com", "+905321234567"), studentExisted, null, null, null);

    [Theory]
    [InlineData(ParentInvitationErrorCodes.ParentInfoRequired, false, typeof(BadRequestObjectResult))]
    [InlineData(ParentInvitationErrorCodes.InvalidParentEmail, false, typeof(BadRequestObjectResult))]
    [InlineData(ParentInvitationErrorCodes.ParentEmailNotUsable, true, typeof(ConflictObjectResult))]
    public async Task Failed_precheck_returns_before_saving_so_no_student_row_or_role_remains(string code, bool conflict, Type expected)
    {
        PrecheckReturns(new ParentInvitationPrecheck(Failure(code, conflict), null));

        await using var ctx = _db.NewContext();
        var result = await NewController(ctx).RegisterStudent(Request());

        result.ShouldBeOfType(expected);
        ErrorCodeOf(result).ShouldBe(code);
        (await ctx.Students.AnyAsync()).ShouldBeFalse();
        await _keycloak.DidNotReceive().SetRoleAsync(Arg.Any<string>(), Arg.Any<UserRole>());
        await _invitations.DidNotReceiveWithAnyArgs().CommitAsync(default!, default!, default);
    }

    [Fact]
    public async Task Failed_commit_after_save_discards_the_new_student_row_and_assigns_no_role()
    {
        PrecheckReturns(new ParentInvitationPrecheck(null, Prepared(studentExisted: false)));
        _invitations.CommitAsync(Arg.Any<ParentInvitationStudent>(), Arg.Any<PreparedParentInvitation>(), Arg.Any<CancellationToken>())
            .Returns(Failure(ParentInvitationErrorCodes.Unavailable, upstream: true));

        await using var ctx = _db.NewContext();
        var result = await NewController(ctx).RegisterStudent(Request());

        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status502BadGateway);
        await _invitations.Received(1).DiscardNewStudentAsync(UserId, Arg.Any<CancellationToken>());
        await _keycloak.DidNotReceive().SetRoleAsync(Arg.Any<string>(), Arg.Any<UserRole>());
    }

    [Fact]
    public async Task Failed_commit_for_an_existing_student_keeps_the_row()
    {
        PrecheckReturns(new ParentInvitationPrecheck(null, Prepared(studentExisted: true)));
        _invitations.CommitAsync(Arg.Any<ParentInvitationStudent>(), Arg.Any<PreparedParentInvitation>(), Arg.Any<CancellationToken>())
            .Returns(Failure(ParentInvitationErrorCodes.Busy, conflict: true));

        await using var ctx = _db.NewContext();
        (await NewController(ctx).RegisterStudent(Request())).ShouldBeOfType<ConflictObjectResult>();
        await _invitations.DidNotReceiveWithAnyArgs().DiscardNewStudentAsync(default, default);
    }

    [Fact]
    public async Task Success_commits_the_invitation_after_saving_and_returns_parent_status()
    {
        var status = new ParentStatusDto { Mode = "AwaitingActivation", RequiresParent = true };
        PrecheckReturns(new ParentInvitationPrecheck(null, Prepared()));
        _invitations.CommitAsync(Arg.Any<ParentInvitationStudent>(), Arg.Any<PreparedParentInvitation>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                // Yazım anında öğrenci satırı yazılmış olmalı (CommitAsync öğrenciyi DB'den okur).
                using var check = _db.NewContext();
                check.Students.Any(s => s.UserId == UserId).ShouldBeTrue();
                return new ParentInvitationResultDto { Success = true, Status = status };
            });

        await using var ctx = _db.NewContext();
        var result = await NewController(ctx).RegisterStudent(Request());

        var ok = result.ShouldBeOfType<OkObjectResult>();
        ok.Value!.GetType().GetProperty("parentStatus")!.GetValue(ok.Value).ShouldBe(status);
        await _invitations.Received(1).PrecheckAsync(
            Arg.Is<ParentInvitationStudent>(s => s.UserId == UserId && s.KeycloakId == KeycloakId),
            Arg.Is<ParentContactRequestDto>(c => c.ParentEmail == "veli@example.com"), true, Arg.Any<CancellationToken>());
        await _keycloak.Received(1).SetRoleAsync(KeycloakId, UserRole.Student);
    }

    [Fact]
    public async Task Adult_role_account_is_not_asked_for_parent_info()
    {
        _invitations.NeedsParentInfoAsync(UserId, false, Arg.Any<CancellationToken>()).Returns(false);

        await using var ctx = _db.NewContext();
        var result = await NewController(ctx, "Admin").RegisterStudent(Request(null, null, null));

        result.ShouldBeOfType<OkObjectResult>();
        await _invitations.Received(1).NeedsParentInfoAsync(UserId, false, Arg.Any<CancellationToken>());
        await _invitations.DidNotReceiveWithAnyArgs().PrecheckAsync(default!, default, default, default);
    }
}
