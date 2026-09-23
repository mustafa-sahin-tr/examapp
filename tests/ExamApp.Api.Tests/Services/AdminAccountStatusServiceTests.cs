using System.Net.Http;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #155: AdminAccountStatusService — #156 ile aynı audit/yan etki sırası (Requested fail-closed, yan etkiden önce),
/// disable'da Keycloak enabled=false + oturum kapatma + profil önbelleği düşürme (logout hatasında da), enable'da logout yok, idempotentlik,
/// reddedilen/bulunamayan hedeflerin izi, Keycloak 404/hata ve SessionRevokeFailed.
/// </summary>
public class AdminAccountStatusServiceTests : IDisposable
{
    private const string ActorSub = "kc-admin-actor";
    private const string TargetSub = "7f6e5d4c-3b2a-4100-9f8e-aabbccddeeff";
    private const int TargetId = 5;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAdminAccountTargetResolver _targets = Substitute.For<IAdminAccountTargetResolver>();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly CapturingLogger<AdminAccountStatusService> _logger = new();

    public AdminAccountStatusServiceTests()
    {
        Target(AdminAccountTargetStatus.Resolved, TargetSub);
    }

    public void Dispose() => _db.Dispose();

    private void Target(AdminAccountTargetStatus status, string? sub = null) =>
        _targets.ResolveAsync(Arg.Any<AdminUserTargetType>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AdminAccountTargetResolution(status, sub));

    private UserProfileCacheService ProfileCache => new(_cache, NullLogger<UserProfileCacheService>.Instance);

    private async Task<AdminAccountStatusChangeResult> RunAsync(bool enabled, IAdminUserActionAuditService? audit = null)
    {
        await using var ctx = _db.NewContext();
        var service = new AdminAccountStatusService(
            _targets, _keycloak, audit ?? new AdminUserActionAuditService(ctx), ProfileCache, _logger);
        return await service.SetEnabledAsync(AdminUserTargetType.Teacher, TargetId, enabled, ActorSub);
    }

    private async Task<List<AdminUserActionLog>> RowsAsync()
    {
        await using var read = _db.NewContext();
        return await read.AdminUserActionLogs.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    [Fact]
    public async Task Disable_writes_Requested_first_then_disables_revokes_sessions_and_marks_Succeeded()
    {
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), Arg.Any<AdminUserActionOutcome>(), Arg.Any<CancellationToken>()).Returns(77L);

        var result = await RunAsync(enabled: false, audit);

        result.ShouldBe(new AdminAccountStatusChangeResult(AdminAccountStatusChangeStatus.Success, false));
        Received.InOrder(() =>
        {
            audit.RecordAsync(new AdminUserActionRecord(ActorSub, AdminUserAction.AccountDisabled, AdminUserTargetType.Teacher, TargetId),
                AdminUserActionOutcome.Requested, Arg.Any<CancellationToken>());
            _keycloak.SetEnabledAsync(TargetSub, false, CancellationToken.None);
            _keycloak.LogoutUserSessionsAsync(TargetSub, CancellationToken.None);
            audit.TryUpdateOutcomeAsync(77L, AdminUserActionOutcome.Succeeded);
        });
    }

    [Fact]
    public async Task Disable_audit_row_is_AccountDisabled_Succeeded_stored_as_strings()
    {
        (await RunAsync(enabled: false)).Status.ShouldBe(AdminAccountStatusChangeStatus.Success);

        await using var read = _db.NewContext();
        var raw = await read.Database.SqlQueryRaw<string>(
            "SELECT \"ActorKeycloakId\" || '|' || \"Action\" || '|' || \"TargetType\" || '|' || \"TargetId\" || '|' || \"Outcome\" AS \"Value\" FROM \"AdminUserActionLogs\"").SingleAsync();
        raw.ShouldBe($"{ActorSub}|AccountDisabled|Teacher|{TargetId}|Succeeded");
        _logger.Entries.ShouldContain(e => e.Contains("Hesap durumu değişti"));
    }

    [Fact]
    public async Task Disable_drops_the_cached_user_profile()
    {
        await ProfileCache.SetAsync(TargetSub, new UserProfileDto { KeycloakId = TargetSub });
        (await ProfileCache.GetAsync(TargetSub)).ShouldNotBeNull();

        (await RunAsync(enabled: false)).Status.ShouldBe(AdminAccountStatusChangeStatus.Success);

        (await ProfileCache.GetAsync(TargetSub)).ShouldBeNull();
    }

    [Fact]
    public async Task Enable_sets_enabled_true_without_logout_and_audits_AccountEnabled()
    {
        var result = await RunAsync(enabled: true);

        result.ShouldBe(new AdminAccountStatusChangeResult(AdminAccountStatusChangeStatus.Success, true));
        await _keycloak.Received(1).SetEnabledAsync(TargetSub, true, CancellationToken.None);
        await _keycloak.DidNotReceiveWithAnyArgs().LogoutUserSessionsAsync(default!, default);
        var row = (await RowsAsync()).Single();
        row.Action.ShouldBe(AdminUserAction.AccountEnabled);
        row.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
    }

    [Fact]
    public async Task Disabling_an_already_disabled_account_is_idempotent_success()
    {
        // Keycloak PUT enabled=false zaten kapalı hesapta da 204 döner; servis durum sorgusu yapmadan aynı yolu izler.
        (await RunAsync(enabled: false)).Status.ShouldBe(AdminAccountStatusChangeStatus.Success);
        (await RunAsync(enabled: false)).Status.ShouldBe(AdminAccountStatusChangeStatus.Success);

        await _keycloak.Received(2).SetEnabledAsync(TargetSub, false, CancellationToken.None);
        (await RowsAsync()).Select(r => r.Outcome).ShouldBe([AdminUserActionOutcome.Succeeded, AdminUserActionOutcome.Succeeded]);
    }

    [Fact]
    public async Task Requested_audit_failure_propagates_and_no_keycloak_side_effect_happens()
    {
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), AdminUserActionOutcome.Requested, Arg.Any<CancellationToken>())
            .Returns<long>(_ => throw new DbUpdateException("db down"));

        await Should.ThrowAsync<DbUpdateException>(() => RunAsync(enabled: false, audit));

        await _keycloak.DidNotReceiveWithAnyArgs().SetEnabledAsync(default!, default, default);
        await _keycloak.DidNotReceiveWithAnyArgs().LogoutUserSessionsAsync(default!, default);
    }

    [Theory]
    [InlineData(AdminAccountTargetStatus.ForbiddenSelf, AdminAccountStatusChangeStatus.ForbiddenSelf, AdminUserActionOutcome.Denied)]
    [InlineData(AdminAccountTargetStatus.ForbiddenProtectedRole, AdminAccountStatusChangeStatus.ForbiddenProtectedRole, AdminUserActionOutcome.Denied)]
    [InlineData(AdminAccountTargetStatus.TargetNotFound, AdminAccountStatusChangeStatus.TargetNotFound, AdminUserActionOutcome.NotFound)]
    [InlineData(AdminAccountTargetStatus.AccountNotFound, AdminAccountStatusChangeStatus.AccountNotFound, AdminUserActionOutcome.NotFound)]
    public async Task Rejected_targets_are_audited_and_have_no_side_effect(
        AdminAccountTargetStatus resolution, AdminAccountStatusChangeStatus expected, AdminUserActionOutcome outcome)
    {
        Target(resolution);

        (await RunAsync(enabled: false)).Status.ShouldBe(expected);

        var row = (await RowsAsync()).Single();
        row.Outcome.ShouldBe(outcome);
        row.Action.ShouldBe(AdminUserAction.AccountDisabled);
        _keycloak.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Rejection_audit_goes_through_best_effort_TryRecord()
    {
        Target(AdminAccountTargetStatus.ForbiddenSelf);
        var audit = Substitute.For<IAdminUserActionAuditService>();

        (await RunAsync(enabled: false, audit)).Status.ShouldBe(AdminAccountStatusChangeStatus.ForbiddenSelf);

        await audit.Received(1).TryRecordAsync(
            new AdminUserActionRecord(ActorSub, AdminUserAction.AccountDisabled, AdminUserTargetType.Teacher, TargetId),
            AdminUserActionOutcome.Denied);
        await audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default, default);
    }

    [Fact]
    public async Task Resolution_upstream_failure_is_UpstreamFailure_without_audit_or_side_effect()
    {
        Target(AdminAccountTargetStatus.UpstreamFailure);

        (await RunAsync(enabled: false)).Status.ShouldBe(AdminAccountStatusChangeStatus.UpstreamFailure);
        (await RowsAsync()).ShouldBeEmpty();
        _keycloak.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Keycloak_404_is_AccountNotFound_with_NotFound_outcome_and_no_logout()
    {
        _keycloak.SetEnabledAsync(TargetSub, false, Arg.Any<CancellationToken>())
            .Returns(_ => throw new KeycloakException("Keycloak account status update failed: 404", 404));

        (await RunAsync(enabled: false)).Status.ShouldBe(AdminAccountStatusChangeStatus.AccountNotFound);
        (await RowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.NotFound);
        await _keycloak.DidNotReceiveWithAnyArgs().LogoutUserSessionsAsync(default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Keycloak_failure_is_UpstreamFailure_with_StatusChangeFailed_and_no_logout(bool keycloakError)
    {
        _keycloak.SetEnabledAsync(TargetSub, false, Arg.Any<CancellationToken>())
            .Returns(_ => keycloakError
                ? throw new KeycloakException("Keycloak account status update failed: 500", 500)
                : throw new HttpRequestException("down"));

        (await RunAsync(enabled: false)).Status.ShouldBe(AdminAccountStatusChangeStatus.UpstreamFailure);
        (await RowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.StatusChangeFailed);
        await _keycloak.DidNotReceiveWithAnyArgs().LogoutUserSessionsAsync(default!, default);
    }

    [Fact]
    public async Task Logout_failure_after_disable_is_SessionRevokeFailed_and_profile_cache_is_still_dropped()
    {
        await ProfileCache.SetAsync(TargetSub, new UserProfileDto { KeycloakId = TargetSub });
        _keycloak.LogoutUserSessionsAsync(TargetSub, Arg.Any<CancellationToken>())
            .Returns(_ => throw new KeycloakException("Keycloak session logout failed: 500", 500));

        var result = await RunAsync(enabled: false);

        result.Status.ShouldBe(AdminAccountStatusChangeStatus.SessionRevokeFailed);
        result.Enabled.ShouldBeNull();
        await _keycloak.Received(1).SetEnabledAsync(TargetSub, false, CancellationToken.None);
        (await RowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.SessionRevokeFailed);
        (await ProfileCache.GetAsync(TargetSub)).ShouldBeNull();
    }

    [Fact]
    public async Task Profile_cache_failure_does_not_stop_the_flow()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("redis down"));
        _cache = cache;

        var result = await RunAsync(enabled: false);

        result.ShouldBe(new AdminAccountStatusChangeResult(AdminAccountStatusChangeStatus.Success, false));
        await _keycloak.Received(1).LogoutUserSessionsAsync(TargetSub, CancellationToken.None);
        await cache.Received(1).RemoveAsync(TargetSub, Arg.Any<CancellationToken>());
        (await RowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
        _logger.Entries.ShouldContain(e => e.Contains("Profil önbelleği düşürülemedi"));
    }

    [Fact]
    public async Task Enable_does_not_touch_the_profile_cache()
    {
        var cache = Substitute.For<IDistributedCache>();
        _cache = cache;

        (await RunAsync(enabled: true)).Status.ShouldBe(AdminAccountStatusChangeStatus.Success);

        cache.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Outcome_update_failure_does_not_change_the_response()
    {
        // Gerçek audit servisi; nihai sonuç güncellemesi başarısız olsun (satır arada silinir) → yanıt değişmez.
        await using var ctx = _db.NewContext();
        var audit = new AdminUserActionAuditService(ctx);
        _keycloak.SetEnabledAsync(TargetSub, false, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await using var del = _db.NewContext();
            await del.AdminUserActionLogs.ExecuteDeleteAsync();
        });

        (await RunAsync(enabled: false, audit)).Status.ShouldBe(AdminAccountStatusChangeStatus.Success);
        (await RowsAsync()).ShouldBeEmpty();
    }
}
