using System.Net.Http;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #156: AdminPasswordResetService — audit/yan etki sırası (Requested fail-closed, yan etkiden önce), sonuç
/// güncellemesi, reddedilen/bulunamayan hedeflerin izi, SessionRevokeFailed, Keycloak 404 ve loglarda şifre olmaması.
/// </summary>
public class AdminPasswordResetServiceTests : IDisposable
{
    private const string ActorSub = "kc-admin-actor";
    private const string TargetSub = "7f6e5d4c-3b2a-4100-9f8e-aabbccddeeff";
    private const string Issued = "Xy7#kP2m-Qa9$Rt4";
    private const int TargetId = 5;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAdminAccountTargetResolver _targets = Substitute.For<IAdminAccountTargetResolver>();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private readonly CapturingLogger<AdminPasswordResetService> _logger = new();

    public AdminPasswordResetServiceTests()
    {
        Target(AdminAccountTargetStatus.Resolved, TargetSub);
        _keycloak.ResetPasswordAsync(TargetSub, Arg.Any<CancellationToken>()).Returns(Issued);
    }

    public void Dispose() => _db.Dispose();

    private void Target(AdminAccountTargetStatus status, string? sub = null) =>
        _targets.ResolveAsync(Arg.Any<AdminUserTargetType>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AdminAccountTargetResolution(status, sub));

    private async Task<AdminPasswordResetResult> RunAsync(IAdminUserActionAuditService? audit = null)
    {
        await using var ctx = _db.NewContext();
        var service = new AdminPasswordResetService(_targets, _keycloak, audit ?? new AdminUserActionAuditService(ctx), _logger);
        return await service.ResetAsync(AdminUserTargetType.Teacher, TargetId, ActorSub);
    }

    private async Task<List<AdminUserActionLog>> RowsAsync()
    {
        await using var read = _db.NewContext();
        return await read.AdminUserActionLogs.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    [Fact]
    public async Task Success_writes_Requested_before_side_effects_then_Succeeded_and_returns_password()
    {
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), Arg.Any<AdminUserActionOutcome>(), Arg.Any<CancellationToken>()).Returns(77L);

        var result = await RunAsync(audit);

        result.Status.ShouldBe(AdminPasswordResetStatus.Success);
        result.TemporaryPassword.ShouldBe(Issued);
        result.ToString().ShouldNotContain(Issued);
        Received.InOrder(() =>
        {
            audit.RecordAsync(new AdminUserActionRecord(ActorSub, AdminUserAction.PasswordReset, AdminUserTargetType.Teacher, TargetId),
                AdminUserActionOutcome.Requested, Arg.Any<CancellationToken>());
            _keycloak.ResetPasswordAsync(TargetSub, CancellationToken.None);
            _keycloak.LogoutUserSessionsAsync(TargetSub, CancellationToken.None);
            audit.UpdateOutcomeAsync(77L, AdminUserActionOutcome.Succeeded, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Success_row_ends_as_Succeeded_and_logs_never_contain_password()
    {
        (await RunAsync()).Status.ShouldBe(AdminPasswordResetStatus.Success);

        var row = (await RowsAsync()).Single();
        row.ActorKeycloakId.ShouldBe(ActorSub);
        row.Action.ShouldBe(AdminUserAction.PasswordReset);
        row.TargetType.ShouldBe(AdminUserTargetType.Teacher);
        row.TargetId.ShouldBe(TargetId);
        row.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
        await using var read = _db.NewContext();
        var raw = await read.Database.SqlQueryRaw<string>(
            "SELECT \"ActorKeycloakId\" || '|' || \"Action\" || '|' || \"TargetType\" || '|' || \"TargetId\" || '|' || \"Outcome\" AS \"Value\" FROM \"AdminUserActionLogs\"").SingleAsync();
        raw.ShouldBe($"{ActorSub}|PasswordReset|Teacher|{TargetId}|Succeeded");
        _logger.Entries.ShouldAllBe(e => !e.Contains(Issued));
        _logger.Entries.ShouldContain(e => e.Contains("Şifre sıfırlandı"));
    }

    [Fact]
    public async Task Requested_audit_failure_propagates_and_no_keycloak_side_effect_happens()
    {
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), AdminUserActionOutcome.Requested, Arg.Any<CancellationToken>())
            .Returns<long>(_ => throw new DbUpdateException("db down"));

        await Should.ThrowAsync<DbUpdateException>(() => RunAsync(audit));

        await _keycloak.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!, default);
        await _keycloak.DidNotReceiveWithAnyArgs().LogoutUserSessionsAsync(default!, default);
    }

    [Theory]
    [InlineData(AdminAccountTargetStatus.ForbiddenSelf, AdminPasswordResetStatus.ForbiddenSelf, AdminUserActionOutcome.Denied)]
    [InlineData(AdminAccountTargetStatus.ForbiddenProtectedRole, AdminPasswordResetStatus.ForbiddenProtectedRole, AdminUserActionOutcome.Denied)]
    [InlineData(AdminAccountTargetStatus.TargetNotFound, AdminPasswordResetStatus.TargetNotFound, AdminUserActionOutcome.NotFound)]
    [InlineData(AdminAccountTargetStatus.AccountNotFound, AdminPasswordResetStatus.AccountNotFound, AdminUserActionOutcome.NotFound)]
    public async Task Rejected_targets_are_audited_and_have_no_side_effect(
        AdminAccountTargetStatus resolution, AdminPasswordResetStatus expected, AdminUserActionOutcome outcome)
    {
        Target(resolution);

        (await RunAsync()).Status.ShouldBe(expected);

        (await RowsAsync()).Single().Outcome.ShouldBe(outcome);
        _keycloak.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Rejection_audit_is_best_effort()
    {
        Target(AdminAccountTargetStatus.ForbiddenProtectedRole);
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), Arg.Any<AdminUserActionOutcome>(), Arg.Any<CancellationToken>())
            .Returns<long>(_ => throw new DbUpdateException("db down"));

        (await RunAsync(audit)).Status.ShouldBe(AdminPasswordResetStatus.ForbiddenProtectedRole);
    }

    [Fact]
    public async Task Resolution_upstream_failure_is_UpstreamFailure_without_audit_or_side_effect()
    {
        Target(AdminAccountTargetStatus.UpstreamFailure);

        (await RunAsync()).Status.ShouldBe(AdminPasswordResetStatus.UpstreamFailure);
        (await RowsAsync()).ShouldBeEmpty();
        _keycloak.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Keycloak_reset_404_is_AccountNotFound_with_NotFound_outcome()
    {
        _keycloak.ResetPasswordAsync(TargetSub, Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new KeycloakException("Keycloak password reset failed: 404", 404));

        (await RunAsync()).Status.ShouldBe(AdminPasswordResetStatus.AccountNotFound);
        (await RowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.NotFound);
        await _keycloak.DidNotReceiveWithAnyArgs().LogoutUserSessionsAsync(default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Keycloak_reset_failure_is_UpstreamFailure_with_ResetFailed_outcome_and_no_logout(bool keycloakError)
    {
        _keycloak.ResetPasswordAsync(TargetSub, Arg.Any<CancellationToken>())
            .Returns<string>(_ => keycloakError
                ? throw new KeycloakException("Keycloak password reset failed: 400", 400)
                : throw new HttpRequestException("down"));

        (await RunAsync()).Status.ShouldBe(AdminPasswordResetStatus.UpstreamFailure);
        (await RowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.ResetFailed);
        await _keycloak.DidNotReceiveWithAnyArgs().LogoutUserSessionsAsync(default!, default);
    }

    [Fact]
    public async Task Logout_failure_after_reset_is_SessionRevokeFailed_password_not_returned()
    {
        _keycloak.LogoutUserSessionsAsync(TargetSub, Arg.Any<CancellationToken>())
            .Returns(_ => throw new KeycloakException("Keycloak session logout failed: 500", 500));

        var result = await RunAsync();

        result.Status.ShouldBe(AdminPasswordResetStatus.SessionRevokeFailed);
        result.TemporaryPassword.ShouldBeNull();
        (await RowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.SessionRevokeFailed);
        _logger.Entries.ShouldAllBe(e => !e.Contains(Issued));
    }

    [Fact]
    public async Task Outcome_update_failure_does_not_change_the_response()
    {
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), Arg.Any<AdminUserActionOutcome>(), Arg.Any<CancellationToken>()).Returns(1L);
        audit.UpdateOutcomeAsync(Arg.Any<long>(), Arg.Any<AdminUserActionOutcome>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new DbUpdateException("db down"));

        var result = await RunAsync(audit);

        result.Status.ShouldBe(AdminPasswordResetStatus.Success);
        _logger.Entries.ShouldContain(e => e.Contains("Audit sonucu güncellenemedi"));
        _logger.Entries.ShouldAllBe(e => !e.Contains(Issued));
    }
}
