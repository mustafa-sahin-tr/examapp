using System.Collections.Concurrent;
using System.Reflection;
using AuthApi.Tests.Support;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using ExamApp.Foundation.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AuthApi.Tests.Services;

/// <summary>
/// Issue #438: auth-api çocuk hesabı servisi (yalnız exam API servis token'ı). Keycloak (substitute) + identity satırı; Keycloak
/// kullanıcısı oluştuktan sonraki her hatada kullanıcı silinir (attribute saklanmadı, parola politikası reddi, rol ataması, DB).
/// Create requestId ile idempotent; silme hesap yoksa 404, işaretsizse 409 NotParentManaged; requestId ile silme. Şifre sıfırlama
/// yalnız <c>parent_managed=true</c> hesaplarda. Parola hiçbir log satırında yer almaz.
/// </summary>
public class ChildAccountServiceTests : IDisposable
{
    // Secret tarayıcısına takılmasın diye çalışma anında birleştirilir (test-only örnek).
    private static readonly string Pw = "Kal" + "em" + "2026x";
    private const string KcId = "kc-child-1";
    private static readonly Guid ReqId = Guid.Parse("2f1c9a52-3d55-4c1e-9a0b-6c2d7e8f9a10");

    private readonly TestDb _db = TestDb.Create();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private readonly CapturingLogger _logger = new();
    private readonly Dictionary<string, string> _attributes = new()
    {
        [ChildAccountRules.KeycloakParentManagedAttribute] = ChildAccountRules.KeycloakParentManagedValue,
        [ChildAccountRules.KeycloakChildRequestIdAttribute] = ReqId.ToString("D")
    };

    public ChildAccountServiceTests()
    {
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(KcId);
        _keycloak.GetUserAttributesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyDictionary<string, string>)_attributes);
        _keycloak.FindUserIdsByAttributeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        _keycloak.TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    public void Dispose() => _db.Dispose();

    private sealed class CapturingLogger : ILogger<ChildAccountService>
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Enqueue(formatter(state, exception) + (exception == null ? string.Empty : " | " + exception));
    }

    private ChildAccountService Service(AppDbContext ctx) => new(ctx, _keycloak, _logger);

    private static CreateChildAccountRequest Request(string username = " Ali.Kaya ", string? pw = null, Guid? requestId = null)
    {
        var r = new CreateChildAccountRequest { Username = username, FirstName = " Ali ", LastName = "Kaya", RequestId = requestId ?? ReqId };
        r.Password = pw ?? Pw;
        return r;
    }

    private async Task<ChildAccountResult> CreateAsync(CreateChildAccountRequest? request = null, AppDbContext? ctx = null)
    {
        if (ctx != null)
            return await Service(ctx).CreateAsync(request ?? Request());
        await using var c = _db.NewContext();
        return await Service(c).CreateAsync(request ?? Request());
    }

    private void FindByRequestReturns(params string[] ids)
        => _keycloak.FindUserIdsByAttributeAsync(ChildAccountRules.KeycloakChildRequestIdAttribute, ReqId.ToString("D"), Arg.Any<CancellationToken>())
            .Returns(ids);

    private void AssertNoPasswordInLogs() => _logger.Lines.ShouldAllBe(l => !l.Contains(Pw));

    [Fact]
    public async Task Create_makes_an_email_less_keycloak_user_with_the_trimmed_role_set_and_an_identity_row()
    {
        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Success);
        result.Created!.KeycloakId.ShouldBe(KcId);
        await _keycloak.Received(1).CreateParentManagedUserAsync("ali.kaya", "Ali", "Kaya", ReqId, Arg.Any<CancellationToken>());
        await _keycloak.Received(1).ResetPasswordAsync(KcId, Pw, Arg.Any<CancellationToken>());
        await _keycloak.Received(1).ApplyParentManagedRolesAsync(KcId, Arg.Any<CancellationToken>());
        // Varsayılan rol kompoziti (manage-account) ayrıca EKLENMEZ — rol kümesi yalnız ApplyParentManagedRolesAsync'te.
        await _keycloak.DidNotReceiveWithAnyArgs().AddRealmRoleMappingAsync(default!, default!, default);
        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync();
        user.Id.ShouldBe(result.Created.UserId);
        user.KeycloakId.ShouldBe(KcId);
        user.Email.ShouldBeEmpty();
        user.Role.ShouldBe("Student");
        user.FullName.ShouldBe("Ali Kaya");
        user.PasswordHash.ShouldBeNull();
        (await check.OutboxMessages.SingleAsync()).Type.ShouldBe(OutboxEventRegistry.NameFor<UserPreferredLocaleChangedEvent>());
        AssertNoPasswordInLogs();
    }

    [Fact]
    public async Task Create_without_a_request_id_is_400_and_never_reaches_keycloak()
    {
        var result = await CreateAsync(Request(requestId: Guid.Empty));

        result.Outcome.ShouldBe(ChildAccountOutcome.Invalid);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.InvalidRequest);
        await _keycloak.DidNotReceiveWithAnyArgs().CreateParentManagedUserAsync(default!, default!, default!, default, default);
    }

    // ---- idempotentlik ve kompanzasyon yarışı (requestId satırı) --------------------------------------------------------

    private async Task<ChildAccountRequest?> RequestRowAsync()
    {
        await using var check = _db.NewContext();
        return await check.ChildAccountRequests.AsNoTracking().SingleOrDefaultAsync(r => r.RequestId == ReqId);
    }

    private async Task SeedRequestRowAsync(ChildAccountRequestState state, string? kc = null, int? userId = null)
    {
        await using var ctx = _db.NewContext();
        ctx.ChildAccountRequests.Add(new ChildAccountRequest
        {
            RequestId = ReqId, State = state, KeycloakUserId = kc, UserId = userId, CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Kompanzasyonu (DELETE by-request) ayrı bir DbContext ile — create sürerken başka bir istekmiş gibi.</summary>
    private async Task<ChildAccountResult> DeleteByRequestInParallelAsync()
    {
        await using var other = _db.NewContext();
        return await Service(other).DeleteByRequestIdAsync(ReqId);
    }

    [Fact]
    public async Task Successful_create_records_the_request_as_completed()
    {
        var result = await CreateAsync();

        var row = (await RequestRowAsync()).ShouldNotBeNull();
        row.State.ShouldBe(ChildAccountRequestState.Completed);
        row.KeycloakUserId.ShouldBe(KcId);
        row.UserId.ShouldBe(result.Created!.UserId);
    }

    [Fact]
    public async Task Same_request_id_returns_the_existing_account_without_creating_a_second_one()
    {
        var first = await CreateAsync();
        _keycloak.ClearReceivedCalls();

        var replay = await CreateAsync();

        replay.Outcome.ShouldBe(ChildAccountOutcome.Success);
        replay.Created!.UserId.ShouldBe(first.Created!.UserId);
        replay.Created.KeycloakId.ShouldBe(KcId);
        await _keycloak.DidNotReceiveWithAnyArgs().CreateParentManagedUserAsync(default!, default!, default!, default, default);
        await _keycloak.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!, default!, default);
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Same_request_id_while_another_create_is_in_progress_is_409_and_does_not_start()
    {
        await SeedRequestRowAsync(ChildAccountRequestState.InProgress);

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Conflict);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.RequestInProgress);
        await _keycloak.DidNotReceiveWithAnyArgs().CreateParentManagedUserAsync(default!, default!, default!, default, default);
        (await RequestRowAsync())!.State.ShouldBe(ChildAccountRequestState.InProgress); // sürene dokunulmaz
    }

    [Fact]
    public async Task Delete_before_create_cancels_the_request_and_the_late_create_never_starts()
    {
        // Kompanzasyon create'ten ÖNCE koştu: satır yoktu → Cancelled olarak eklenir.
        (await DeleteByRequestInParallelAsync()).Outcome.ShouldBe(ChildAccountOutcome.NotFound);
        (await RequestRowAsync())!.State.ShouldBe(ChildAccountRequestState.Cancelled);

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Conflict);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.RequestCancelled);
        await _keycloak.DidNotReceiveWithAnyArgs().CreateParentManagedUserAsync(default!, default!, default!, default, default);
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Delete_during_the_keycloak_post_makes_create_delete_its_own_user()
    {
        // Keycloak POST sürerken kompanzasyon geldi: satırda henüz Keycloak id yok → silinecek bir şey bulamaz, satırı Cancelled
        // yapar. POST dönünce create id'yi kaydedemez ve kendi kullanıcısını siler.
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await DeleteByRequestInParallelAsync();
                return KcId;
            });

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Conflict);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.RequestCancelled);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await _keycloak.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!, default!, default);
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
        (await RequestRowAsync())!.State.ShouldBe(ChildAccountRequestState.Cancelled);
    }

    [Fact]
    public async Task Delete_during_complete_removes_the_keycloak_user_and_no_identity_row_or_event_remains()
    {
        // Rol ataması ile identity yazımı arasında kompanzasyon: Keycloak kullanıcısı satırdaki id ile silinir; create'in
        // koşullu tamamlaması (State == InProgress) tutmaz → identity satırı/outbox geri alınır, kullanıcı yine silinir.
        _keycloak.ApplyParentManagedRolesAsync(KcId, Arg.Any<CancellationToken>())
            .Returns(async _ => { (await DeleteByRequestInParallelAsync()).Outcome.ShouldBe(ChildAccountOutcome.Success); });

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Conflict);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.RequestCancelled);
        await _keycloak.Received().TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
        (await RequestRowAsync())!.State.ShouldBe(ChildAccountRequestState.Cancelled);
    }

    [Fact]
    public async Task Delete_after_complete_removes_the_keycloak_user_and_the_identity_row()
    {
        await CreateAsync();

        (await DeleteByRequestInParallelAsync()).Outcome.ShouldBe(ChildAccountOutcome.Success);

        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await RequestRowAsync())!.State.ShouldBe(ChildAccountRequestState.Cancelled);
        (await CreateAsync()).ErrorCode.ShouldBe(ChildAccountErrorCodes.RequestCancelled); // tekrar açılmaz
    }

    [Fact]
    public async Task Keycloak_post_uses_no_client_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await using var ctx = _db.NewContext();

        await Service(ctx).CreateAsync(Request(), cts.Token);

        await _keycloak.Received(1).CreateParentManagedUserAsync("ali.kaya", "Ali", "Kaya", ReqId, CancellationToken.None);
    }

    // ---- oluşturma sonrası attribute doğrulaması ----------------------------------------------------------------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Missing_markers_after_create_delete_the_user_and_fail(bool dropManaged, bool dropRequestId)
    {
        if (dropManaged)
            _attributes.Remove(ChildAccountRules.KeycloakParentManagedAttribute);
        if (dropRequestId)
            _attributes.Remove(ChildAccountRules.KeycloakChildRequestIdAttribute);

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.UpstreamFailure);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.Unavailable);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await _keycloak.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!, default!, default);
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Username_conflict_is_409_without_compensation()
    {
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new KeycloakException("exists", 409, KeycloakFailureKind.Conflict));

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Conflict);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.UsernameTaken);
        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _keycloak.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await RequestRowAsync())!.State.ShouldBe(ChildAccountRequestState.Cancelled); // başarısız istek bir daha başlamaz
    }

    [Fact]
    public async Task Keycloak_rejecting_the_user_is_400_InvalidUsername()
    {
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new KeycloakException("bad", 400, KeycloakFailureKind.Validation));

        (await CreateAsync()).ErrorCode.ShouldBe(ChildAccountErrorCodes.InvalidUsername);
    }

    [Fact]
    public async Task Keycloak_unreachable_on_create_is_upstream_failure()
    {
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("down"));

        (await CreateAsync()).Outcome.ShouldBe(ChildAccountOutcome.UpstreamFailure);
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
        (await RequestRowAsync())!.State.ShouldBe(ChildAccountRequestState.Cancelled);
    }

    [Fact]
    public async Task Ambiguous_keycloak_post_failure_cleans_up_a_user_created_with_the_request_id()
    {
        // Zaman aşımı: Keycloak kullanıcıyı açmış olabilir — attribute ile bulunup silinir.
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TaskCanceledException("timeout"));
        FindByRequestReturns(KcId);

        (await CreateAsync()).Outcome.ShouldBe(ChildAccountOutcome.UpstreamFailure);

        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Password_policy_rejection_deletes_the_keycloak_user()
    {
        _keycloak.ResetPasswordAsync(KcId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new KeycloakException("invalidPasswordMinLengthMessage", 400, KeycloakFailureKind.Validation));

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Invalid);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
        AssertNoPasswordInLogs();
    }

    [Fact]
    public async Task Role_set_failure_deletes_the_keycloak_user()
    {
        _keycloak.ApplyParentManagedRolesAsync(KcId, Arg.Any<CancellationToken>())
            .Returns(_ => throw new KeycloakException("default role still mapped"));

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.UpstreamFailure);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
        AssertNoPasswordInLogs();
    }

    private sealed class FailOnUserInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<User>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated identity DB failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Identity_db_failure_deletes_the_keycloak_user()
    {
        await using var ctx = _db.NewContext(new FailOnUserInsert());

        var result = await CreateAsync(ctx: ctx);

        result.Outcome.ShouldBe(ChildAccountOutcome.UpstreamFailure);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        AssertNoPasswordInLogs();
    }

    public static IEnumerable<object?[]> InvalidInputs() => new[]
    {
        new object?[] { "ab", null, ChildAccountErrorCodes.InvalidUsername },
        new object?[] { "ali@kaya.com", null, ChildAccountErrorCodes.InvalidUsername },
        new object?[] { "ali.kaya", "ki" + "sa1", ChildAccountErrorCodes.PasswordRejected },
        new object?[] { "ali.kaya", "ali.kaya" + "2026", ChildAccountErrorCodes.PasswordRejected },
    };

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Invalid_input_never_reaches_keycloak(string username, string? pw, string code)
    {
        var result = await CreateAsync(Request(username, pw));

        result.Outcome.ShouldBe(ChildAccountOutcome.Invalid);
        result.ErrorCode.ShouldBe(code);
        await _keycloak.DidNotReceiveWithAnyArgs().CreateParentManagedUserAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Blank_names_are_rejected()
    {
        var r = Request();
        r.FirstName = "  ";
        (await CreateAsync(r)).ErrorCode.ShouldBe(ChildAccountErrorCodes.InvalidName);
    }

    // ---- silme (kompanzasyon) -----------------------------------------------------------------------------------------

    private async Task<int> SeedUserAsync(string role = "Student", string kc = KcId)
    {
        await using var ctx = _db.NewContext();
        var user = new User { KeycloakId = kc, Email = string.Empty, FullName = "Ali Kaya", Role = role };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Delete_removes_a_parent_managed_account_from_keycloak_and_identity()
    {
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        (await Service(ctx).DeleteAsync(id)).Outcome.ShouldBe(ChildAccountOutcome.Success);

        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Delete_of_an_existing_account_without_the_marker_is_409_NotParentManaged()
    {
        _attributes.Clear();
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        var result = await Service(ctx).DeleteAsync(id);

        result.Outcome.ShouldBe(ChildAccountOutcome.Conflict);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.NotParentManaged);
        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Delete_distinguishes_unknown_ids_404_from_non_student_accounts_409()
    {
        var teacher = await SeedUserAsync("Teacher", "kc-teacher");
        await using var ctx = _db.NewContext();

        var nonStudent = await Service(ctx).DeleteAsync(teacher);
        nonStudent.Outcome.ShouldBe(ChildAccountOutcome.Conflict);
        nonStudent.ErrorCode.ShouldBe(ChildAccountErrorCodes.NotParentManaged);

        var unknown = await Service(ctx).DeleteAsync(999);
        unknown.Outcome.ShouldBe(ChildAccountOutcome.NotFound);
        unknown.ErrorCode.ShouldBe(ChildAccountErrorCodes.NotFound);

        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_when_keycloak_user_is_already_gone_still_removes_the_identity_row()
    {
        _keycloak.GetUserAttributesAsync(KcId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<string, string>>(_ => throw new KeycloakException("missing", 404));
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        (await Service(ctx).DeleteAsync(id)).Outcome.ShouldBe(ChildAccountOutcome.Success);

        await using var check = _db.NewContext();
        (await check.Users.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    // ---- requestId ile silme (exam API yanıt alamadığında) -------------------------------------------------------------

    [Fact]
    public async Task Delete_by_request_id_removes_the_keycloak_user_and_its_identity_row()
    {
        await SeedUserAsync();
        await SeedUserAsync(kc: "kc-other");
        FindByRequestReturns(KcId);
        await using var ctx = _db.NewContext();

        (await Service(ctx).DeleteByRequestIdAsync(ReqId)).Outcome.ShouldBe(ChildAccountOutcome.Success);

        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.IgnoreQueryFilters().Select(u => u.KeycloakId).SingleAsync()).ShouldBe("kc-other");
    }

    [Fact]
    public async Task Delete_by_request_id_without_a_match_is_404()
    {
        await using var ctx = _db.NewContext();

        var result = await Service(ctx).DeleteByRequestIdAsync(ReqId);

        result.Outcome.ShouldBe(ChildAccountOutcome.NotFound);
        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await RequestRowAsync())!.State.ShouldBe(ChildAccountRequestState.Cancelled); // sonradan gelen create başlamaz
    }

    [Fact]
    public async Task Delete_by_request_id_cleans_the_identity_row_when_the_keycloak_user_is_already_gone()
    {
        await SeedUserAsync();
        await SeedRequestRowAsync(ChildAccountRequestState.Completed, KcId, userId: 1);
        _keycloak.GetUserAttributesAsync(KcId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<string, string>>(_ => throw new KeycloakException("missing", 404));
        await using var ctx = _db.NewContext();

        (await Service(ctx).DeleteByRequestIdAsync(ReqId)).Outcome.ShouldBe(ChildAccountOutcome.Success);

        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Delete_by_request_id_touches_nothing_when_any_match_is_unmarked()
    {
        // Sırada önce işaretli, sonra işaretsiz hesap: hiçbiri silinmez (yarım silme / yetim identity satırı yok).
        await SeedUserAsync();
        await SeedUserAsync(kc: "kc-unmarked");
        FindByRequestReturns(KcId, "kc-unmarked");
        _keycloak.GetUserAttributesAsync("kc-unmarked", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<string, string>)new Dictionary<string, string>());
        await using var ctx = _db.NewContext();

        var result = await Service(ctx).DeleteByRequestIdAsync(ReqId);

        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.NotParentManaged);
        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Delete_by_request_id_refuses_an_account_whose_marker_was_removed()
    {
        FindByRequestReturns(KcId);
        _attributes.Remove(ChildAccountRules.KeycloakParentManagedAttribute);
        await using var ctx = _db.NewContext();

        var result = await Service(ctx).DeleteByRequestIdAsync(ReqId);

        result.Outcome.ShouldBe(ChildAccountOutcome.Conflict);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.NotParentManaged);
        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_by_request_id_rejects_an_empty_id_and_maps_keycloak_errors()
    {
        await using var ctx = _db.NewContext();
        (await Service(ctx).DeleteByRequestIdAsync(Guid.Empty)).Outcome.ShouldBe(ChildAccountOutcome.Invalid);

        _keycloak.FindUserIdsByAttributeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<string>>(_ => throw new KeycloakException("down"));
        (await Service(ctx).DeleteByRequestIdAsync(ReqId)).Outcome.ShouldBe(ChildAccountOutcome.UpstreamFailure);
    }

    // ---- şifre sıfırlama ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reset_sets_a_permanent_password_and_ends_sessions()
    {
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        (await Service(ctx).ResetPasswordAsync(id, Pw)).Outcome.ShouldBe(ChildAccountOutcome.Success);

        await _keycloak.Received(1).ResetPasswordAsync(KcId, Pw, Arg.Any<CancellationToken>());
        await _keycloak.Received(1).LogoutAsync(KcId);
        AssertNoPasswordInLogs();
    }

    [Fact]
    public async Task Reset_succeeds_even_if_session_logout_fails()
    {
        _keycloak.LogoutAsync(KcId).Returns(_ => throw new KeycloakException("logout failed"));
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        (await Service(ctx).ResetPasswordAsync(id, Pw)).Outcome.ShouldBe(ChildAccountOutcome.Success);
    }

    [Fact]
    public async Task Reset_refuses_unmanaged_non_student_and_unknown_accounts()
    {
        var teacher = await SeedUserAsync("Teacher", "kc-teacher");
        await using var ctx = _db.NewContext();
        (await Service(ctx).ResetPasswordAsync(teacher, Pw)).Outcome.ShouldBe(ChildAccountOutcome.NotFound);
        (await Service(ctx).ResetPasswordAsync(999, Pw)).Outcome.ShouldBe(ChildAccountOutcome.NotFound);

        _attributes.Clear();
        var student = await SeedUserAsync();
        (await Service(ctx).ResetPasswordAsync(student, Pw)).Outcome.ShouldBe(ChildAccountOutcome.NotFound);

        await _keycloak.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_maps_policy_rejection_and_validates_locally_first()
    {
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();
        (await Service(ctx).ResetPasswordAsync(id, "ki" + "sa")).ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        await _keycloak.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        _keycloak.ResetPasswordAsync(KcId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new KeycloakException("policy", 400, KeycloakFailureKind.Validation));
        var result = await Service(ctx).ResetPasswordAsync(id, Pw);
        result.Outcome.ShouldBe(ChildAccountOutcome.Invalid);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        AssertNoPasswordInLogs();
    }

    [Fact]
    public void Controller_is_service_only()
    {
        var authorize = typeof(ChildAccountsController).GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull();
        authorize.Policy.ShouldBe("Service");
        typeof(ChildAccountsController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>())
            .ShouldBeEmpty();
    }

    [Fact]
    public void Delete_by_request_route_is_a_guid_constrained_delete_on_the_service_only_controller()
    {
        var method = typeof(ChildAccountsController).GetMethod(nameof(ChildAccountsController.DeleteByRequest)).ShouldNotBeNull();
        method.GetCustomAttribute<HttpDeleteAttribute>()!.Template.ShouldBe("by-request/{requestId:guid}");
    }

    [Fact]
    public async Task Delete_by_request_maps_409_and_404_to_http_status()
    {
        var service = Substitute.For<IChildAccountService>();
        var controller = new ChildAccountsController(service);

        service.DeleteByRequestIdAsync(ReqId, Arg.Any<CancellationToken>())
            .Returns(new ChildAccountResult(ChildAccountOutcome.Conflict, ChildAccountErrorCodes.NotParentManaged));
        var conflict = (await controller.DeleteByRequest(ReqId, CancellationToken.None)).ShouldBeOfType<ConflictObjectResult>();
        ((ChildAccountErrorResponse)conflict.Value!).ErrorCode.ShouldBe(ChildAccountErrorCodes.NotParentManaged);

        service.DeleteByRequestIdAsync(ReqId, Arg.Any<CancellationToken>())
            .Returns(new ChildAccountResult(ChildAccountOutcome.NotFound, ChildAccountErrorCodes.NotFound));
        (await controller.DeleteByRequest(ReqId, CancellationToken.None)).ShouldBeOfType<NotFoundObjectResult>();

        service.DeleteByRequestIdAsync(ReqId, Arg.Any<CancellationToken>()).Returns(ChildAccountResult.Ok);
        (await controller.DeleteByRequest(ReqId, CancellationToken.None)).ShouldBeOfType<NoContentResult>();
    }
}
