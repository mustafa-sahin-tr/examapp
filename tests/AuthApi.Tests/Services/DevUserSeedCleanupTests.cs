using AuthApi.Tests.Support;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthApi.Tests.Services;

/// <summary>
/// Dev-only seed temizliği (issue #218): yalnızca seed alanı + IsSeedData; gerçek kullanıcı (seed alanı dışı, ya da seed
/// alanında ama IsSeedData=false) ASLA silinmez — ExcludeUserIds/istekle bile kapsam genişletilemez; dry-run yazmaz;
/// Keycloak silme hatası raporlanır ve identity satırı kalır (yeniden deneme); Keycloak'ta yok → identity yine silinir;
/// ikinci koşu 0; Production reddi; controller 404 katmanları.
/// </summary>
public class DevUserSeedCleanupTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private static string Seed(string local) => $"seed.t.{local}@{SeedDataConventions.EmailDomain}";

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private DevUserSeedService NewService(AppDbContext ctx, IKeycloakService keycloak, string environment = "Development")
        => new(ctx, keycloak, Env(environment), NullLogger<DevUserSeedService>.Instance);

    /// <summary>Keycloak taklidi: <paramref name="users"/> username → id; arama seed alanı sonekini içerenleri döner; silme store'dan düşer.</summary>
    private static IKeycloakService FakeKeycloak(Dictionary<string, string> users, HashSet<string>? failDeleteIds = null)
    {
        var kc = Substitute.For<IKeycloakService>();
        kc.SearchUsersAsync(Arg.Any<string>(), Arg.Any<KeycloakUserSearchField>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var fragment = call.Arg<string>();
            return users.Where(kv => kv.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                .Select(kv => new KeycloakUserSummary(kv.Value, kv.Key, kv.Key)).ToList();
        });
        kc.TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.Arg<string>();
            if (failDeleteIds?.Contains(id) == true) throw new KeycloakException("simulated keycloak delete failure");
            var key = users.FirstOrDefault(kv => kv.Value == id).Key;
            if (key is null) return false;
            users.Remove(key);
            return true;
        });
        return kc;
    }

    private async Task<int> AddIdentityUserAsync(string email, string keycloakId, bool isSeed, bool deleted = false)
    {
        await using var ctx = _db.NewContext();
        var u = new User { Email = email, FullName = "X Y", Role = "Teacher", KeycloakId = keycloakId, IsSeedData = isSeed };
        ctx.Users.Add(u);
        await ctx.SaveChangesAsync();
        if (deleted)
        {
            ctx.Users.Remove(u);
            await ctx.SaveChangesAsync();
        }
        return u.Id;
    }

    private async Task<List<string>> IdentityEmailsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.Users.IgnoreQueryFilters().Select(u => u.Email).OrderBy(e => e).ToListAsync();
    }

    /// <summary>
    /// Fixture: 3 seed hesap (Keycloak + identity), 1 gerçek kullanıcı (ceo@x.com; Keycloak + identity, IsSeedData=false),
    /// 1 seed alanında ama elle açılmış (IsSeedData=false) hesap, 1 yalnızca Keycloak'ta seed deseni (identity yok),
    /// 1 yalnızca identity'de seed (Keycloak'ta yok).
    /// </summary>
    private async Task<(Dictionary<string, string> Keycloak, int SeedAId, int SeedBId, int SeedCId, int RealId, int ForeignSeedDomainId, int IdentityOnlyId)> FixtureAsync()
    {
        var kc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Seed("a")] = "kc-a", [Seed("b")] = "kc-b", [Seed("c")] = "kc-c",
            ["ceo@x.com"] = "kc-ceo",
            [Seed("manual")] = "kc-manual",
            [Seed("kconly")] = "kc-kconly"
        };
        var a = await AddIdentityUserAsync(Seed("a"), "kc-a", isSeed: true);
        var b = await AddIdentityUserAsync(Seed("b"), "kc-b", isSeed: true);
        var c = await AddIdentityUserAsync(Seed("c"), "kc-c", isSeed: true, deleted: true); // soft-delete kalıntısı da temizlenir
        var real = await AddIdentityUserAsync("ceo@x.com", "kc-ceo", isSeed: false);
        var foreign = await AddIdentityUserAsync(Seed("manual"), "kc-manual", isSeed: false);
        var idOnly = await AddIdentityUserAsync(Seed("idonly"), "kc-gone", isSeed: true);
        return (kc, a, b, c, real, foreign, idOnly);
    }

    // ---- guard ----

    [Fact]
    public async Task Production_refuses_before_touching_keycloak_or_db()
    {
        var (kcStore, _, _, _, _, _, _) = await FixtureAsync();
        var kc = FakeKeycloak(kcStore);
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<DevSeedEnvironmentException>(() =>
            NewService(ctx, kc, "Production").CleanupAsync(new DevSeedCleanupRequest { DryRun = false }));

        ex.Message.ShouldContain("Production");
        await kc.DidNotReceiveWithAnyArgs().SearchUsersAsync(default!, default, default);
        await kc.DidNotReceiveWithAnyArgs().TryDeleteUserAsync(default!, default);
        (await IdentityEmailsAsync()).Count.ShouldBe(6);
    }

    // ---- dry-run ----

    [Fact]
    public async Task Dry_run_lists_scope_and_deletes_nothing()
    {
        var (kcStore, aId, bId, cId, _, _, idOnlyId) = await FixtureAsync();
        var kc = FakeKeycloak(kcStore);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = true });

        r.DryRun.ShouldBeTrue();
        r.KeycloakDeleted.ShouldBe(0);
        r.IdentityDeleted.ShouldBe(0);
        r.Users.Where(u => u.IdentityStatus == DevSeedCleanupResponse.StatusPlanned).Select(u => u.UserId).ShouldBe([aId, bId, cId, idOnlyId], ignoreOrder: true);
        r.Users.Single(u => u.UserId == idOnlyId).KeycloakStatus.ShouldBe(DevSeedCleanupResponse.StatusPlanned); // aramada yok ama KeycloakId var: id ile denenecek
        r.KeycloakMissing.ShouldBe(0);
        r.Users.ShouldNotContain(u => u.Email == "ceo@x.com");
        r.Users.Single(u => u.Email == Seed("manual")).ShouldSatisfyAllConditions(
            u => u.KeycloakStatus.ShouldBe(DevSeedCleanupResponse.StatusSkippedForeign),
            u => u.IdentityStatus.ShouldBe(DevSeedCleanupResponse.StatusSkippedForeign));
        r.Users.Single(u => u.Email == Seed("kconly")).KeycloakStatus.ShouldBe(DevSeedCleanupResponse.StatusSkippedForeign);
        r.KeycloakSkippedForeign.ShouldBe(2);

        await kc.DidNotReceiveWithAnyArgs().TryDeleteUserAsync(default!, default);
        (await IdentityEmailsAsync()).Count.ShouldBe(6);
        kcStore.Count.ShouldBe(6);
    }

    // ---- apply ----

    [Fact]
    public async Task Apply_deletes_only_seed_accounts_in_both_systems_and_never_real_or_foreign_ones()
    {
        var (kcStore, _, _, _, _, _, _) = await FixtureAsync();
        var kc = FakeKeycloak(kcStore);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false });

        r.KeycloakDeleted.ShouldBe(3);   // a, b, c
        r.KeycloakMissing.ShouldBe(1);   // idonly: id ile denendi, 404
        r.IdentityDeleted.ShouldBe(4);   // a, b, c (soft-deleted), idonly
        await kc.Received(1).TryDeleteUserAsync("kc-gone", Arg.Any<CancellationToken>());
        r.KeycloakSkippedForeign.ShouldBe(2);
        r.KeycloakFailed.ShouldBe(0);

        (await IdentityEmailsAsync()).ShouldBe(["ceo@x.com", Seed("manual")]);
        kcStore.Keys.ShouldBe(["ceo@x.com", Seed("manual"), Seed("kconly")], ignoreOrder: true);
        await kc.DidNotReceive().TryDeleteUserAsync("kc-ceo", Arg.Any<CancellationToken>());
        await kc.DidNotReceive().TryDeleteUserAsync("kc-manual", Arg.Any<CancellationToken>());
        await kc.DidNotReceive().TryDeleteUserAsync("kc-kconly", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Real_user_id_in_exclude_list_or_anywhere_in_request_cannot_widen_scope()
    {
        var (kcStore, _, _, _, realId, foreignId, _) = await FixtureAsync();
        var kc = FakeKeycloak(kcStore);
        await using var ctx = _db.NewContext();

        // ExcludeUserIds yalnızca daraltır; gerçek/yabancı id'ler verilmesi hiçbir şeyi değiştirmez.
        var r = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false, ExcludeUserIds = [realId, foreignId, 999999] });

        r.KeycloakDeleted.ShouldBe(3);
        (await IdentityEmailsAsync()).ShouldBe(["ceo@x.com", Seed("manual")]);
        kcStore.ShouldContainKey("ceo@x.com");
    }

    [Fact]
    public async Task Exclude_user_ids_keeps_those_seed_accounts_in_both_systems()
    {
        var (kcStore, aId, _, _, _, _, _) = await FixtureAsync();
        var kc = FakeKeycloak(kcStore);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false, ExcludeUserIds = [aId] });

        r.KeycloakExcluded.ShouldBe(1);
        r.IdentityExcluded.ShouldBe(1);
        r.KeycloakDeleted.ShouldBe(2);
        r.IdentityDeleted.ShouldBe(3);
        r.Users.Single(u => u.UserId == aId).ShouldSatisfyAllConditions(
            u => u.KeycloakStatus.ShouldBe(DevSeedCleanupResponse.StatusExcluded),
            u => u.IdentityStatus.ShouldBe(DevSeedCleanupResponse.StatusExcluded));
        (await IdentityEmailsAsync()).ShouldContain(Seed("a"));
        kcStore.ShouldContainKey(Seed("a"));
    }

    [Fact]
    public async Task Keycloak_delete_failure_is_reported_and_identity_row_is_kept_for_retry_then_second_run_finishes()
    {
        var (kcStore, _, bId, _, _, _, _) = await FixtureAsync();
        var failing = new HashSet<string> { "kc-b" };
        var kc = FakeKeycloak(kcStore, failing);

        DevSeedCleanupResponse first;
        await using (var ctx = _db.NewContext())
            first = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false });

        first.KeycloakFailed.ShouldBe(1);
        first.IdentityFailed.ShouldBe(1);
        first.KeycloakDeleted.ShouldBe(2);
        first.IdentityDeleted.ShouldBe(3);
        first.Users.Single(u => u.UserId == bId).ShouldSatisfyAllConditions(
            u => u.KeycloakStatus.ShouldBe(DevSeedCleanupResponse.StatusFailed),
            u => u.IdentityStatus.ShouldBe(DevSeedCleanupResponse.StatusFailed),
            u => u.Error!.ShouldContain("simulated"));
        (await IdentityEmailsAsync()).ShouldContain(Seed("b"));
        kcStore.ShouldContainKey(Seed("b"));

        // İkinci koşu: Keycloak artık başarılı → kalan temizlenir; üçüncü koşu 0.
        failing.Clear();
        DevSeedCleanupResponse second, third;
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false });
        await using (var ctx = _db.NewContext())
            third = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false });

        second.KeycloakDeleted.ShouldBe(1);
        second.IdentityDeleted.ShouldBe(1);
        third.KeycloakDeleted.ShouldBe(0);
        third.IdentityDeleted.ShouldBe(0);
        third.KeycloakMissing.ShouldBe(0);
        (await IdentityEmailsAsync()).ShouldBe(["ceo@x.com", Seed("manual")]);
    }

    [Fact]
    public async Task Keycloak_search_failure_marks_all_failed_and_deletes_nothing()
    {
        var (kcStore, _, _, _, _, _, _) = await FixtureAsync();
        var kc = FakeKeycloak(kcStore);
        kc.SearchUsersAsync(Arg.Any<string>(), Arg.Any<KeycloakUserSearchField>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<KeycloakUserSummary>>(_ => throw new KeycloakException("down"));
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false });

        r.KeycloakDeleted.ShouldBe(0);
        r.IdentityDeleted.ShouldBe(0);
        r.KeycloakFailed.ShouldBe(4);
        r.IdentityFailed.ShouldBe(4);
        r.KeycloakMissing.ShouldBe(0);
        r.Users.Where(u => u.UserId != null && u.KeycloakStatus == DevSeedCleanupResponse.StatusFailed).Count().ShouldBe(4);
        await kc.DidNotReceiveWithAnyArgs().TryDeleteUserAsync(default!, default);
        (await IdentityEmailsAsync()).Count.ShouldBe(6);
    }

    [Fact]
    public async Task Empty_systems_return_empty_response()
    {
        var kc = FakeKeycloak(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ceo@x.com"] = "kc-ceo" });
        await AddIdentityUserAsync("ceo@x.com", "kc-ceo", isSeed: false);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false });

        r.Users.ShouldBeEmpty();
        r.KeycloakDeleted.ShouldBe(0);
        (await IdentityEmailsAsync()).ShouldBe(["ceo@x.com"]);
    }

    // ---- kalıntı satırlar / self-heal / look-alike ----

    [Fact]
    public async Task Excluded_active_row_protects_its_soft_deleted_remnant_in_both_systems_and_non_excluded_group_deletes_all_rows()
    {
        var kcStore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [Seed("dup")] = "kc-dup", [Seed("multi")] = "kc-multi" };
        var dupRemnant = await AddIdentityUserAsync(Seed("dup"), "kc-dup-old", isSeed: true, deleted: true);
        var dupActive = await AddIdentityUserAsync(Seed("dup"), "kc-dup", isSeed: true);
        var multi1 = await AddIdentityUserAsync(Seed("multi"), "kc-multi-old", isSeed: true, deleted: true);
        var multi2 = await AddIdentityUserAsync(Seed("multi"), "kc-multi", isSeed: true);
        var kc = FakeKeycloak(kcStore);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false, ExcludeUserIds = [dupActive] });

        var dup = r.Users.Single(u => u.Email == Seed("dup"));
        dup.UserId.ShouldBe(dupActive); // aktif satır önce
        dup.IdentityIds.ShouldBe([dupActive, dupRemnant], ignoreOrder: true);
        dup.IdentityStatus.ShouldBe(DevSeedCleanupResponse.StatusExcluded);
        dup.KeycloakStatus.ShouldBe(DevSeedCleanupResponse.StatusExcluded);
        var multi = r.Users.Single(u => u.Email == Seed("multi"));
        multi.IdentityIds.ShouldBe([multi2, multi1], ignoreOrder: true);
        multi.IdentityStatus.ShouldBe(DevSeedCleanupResponse.StatusDeleted);
        r.IdentityDeleted.ShouldBe(2);
        r.KeycloakDeleted.ShouldBe(1);

        await using var check = _db.NewContext();
        (await check.Users.IgnoreQueryFilters().Where(u => u.Email == Seed("dup")).CountAsync()).ShouldBe(2); // ikisi de korundu
        (await check.Users.IgnoreQueryFilters().Where(u => u.Email == Seed("multi")).CountAsync()).ShouldBe(0);
        kcStore.ShouldContainKey(Seed("dup"));
        kcStore.ShouldNotContainKey(Seed("multi"));
    }

    [Fact]
    public async Task Search_miss_self_heals_by_identity_keycloak_id_and_username_search_finds_username_not_equal_email()
    {
        // "hidden": Keycloak'ta var ama e-posta araması kaçırıyor → identity KeycloakId ile silinir.
        // "uname": Keycloak username seed deseninde, e-posta farklı → username araması bulur.
        var hiddenId = await AddIdentityUserAsync(Seed("hidden"), "kc-hidden", isSeed: true);
        var unameId = await AddIdentityUserAsync(Seed("uname"), "kc-uname", isSeed: true);
        var kc = Substitute.For<IKeycloakService>();
        kc.SearchUsersAsync(Arg.Any<string>(), KeycloakUserSearchField.Email, Arg.Any<CancellationToken>())
            .Returns(new List<KeycloakUserSummary>());
        kc.SearchUsersAsync(Arg.Any<string>(), KeycloakUserSearchField.Username, Arg.Any<CancellationToken>())
            .Returns(new List<KeycloakUserSummary> { new("kc-uname", Seed("uname"), "other@example.org") });
        var deleted = new List<string>();
        kc.TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => { deleted.Add(call.Arg<string>()); return true; });
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false });

        deleted.ShouldBe(["kc-hidden", "kc-uname"], ignoreOrder: true);
        r.KeycloakDeleted.ShouldBe(2);
        r.KeycloakMissing.ShouldBe(0);
        r.IdentityDeleted.ShouldBe(2);
        r.Users.Single(u => u.UserId == hiddenId).KeycloakStatus.ShouldBe(DevSeedCleanupResponse.StatusDeleted);
        r.Users.Single(u => u.UserId == unameId).KeycloakStatus.ShouldBe(DevSeedCleanupResponse.StatusDeleted);
        (await IdentityEmailsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Seed_domain_look_alikes_are_never_touched()
    {
        var lookAlikes = new[] { "ali@seed.examapp.localhost.com", "seed.x@notseed.examapp.local", "SEED.A@SEED.EXAMAPP.LOCAL", "seed.examapp.local@gmail.com" };
        var kcStore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [Seed("real-seed")] = "kc-rs" };
        foreach (var (email, i) in lookAlikes.Select((e, i) => (e, i)))
        {
            kcStore[email] = "kc-look-" + i;
            await AddIdentityUserAsync(email, "kc-look-" + i, isSeed: true); // IsSeedData=true olsa bile desen dışı: kapsam dışı
        }
        await AddIdentityUserAsync(Seed("real-seed"), "kc-rs", isSeed: true);
        var kc = FakeKeycloak(kcStore);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, kc).CleanupAsync(new DevSeedCleanupRequest { DryRun = false });

        r.KeycloakDeleted.ShouldBe(1);
        r.IdentityDeleted.ShouldBe(1);
        r.Users.Select(u => u.Email).ShouldBe([Seed("real-seed")]);
        (await IdentityEmailsAsync()).ShouldBe(lookAlikes.OrderBy(e => e, StringComparer.Ordinal).ToList());
        kcStore.Keys.ShouldBe(lookAlikes, ignoreOrder: true);
    }

    // ---- controller katmanı ----

    [Fact]
    public async Task Controller_returns_404_without_service_or_outside_dev_and_200_with_service()
    {
        var noService = new DevSeedController(Env("Development"), NullLogger<DevSeedController>.Instance, seedService: null);
        (await noService.CleanupSeedUsers(new DevSeedCleanupRequest(), CancellationToken.None)).ShouldBeOfType<NotFoundResult>();

        var service = Substitute.For<IDevUserSeedService>();
        var prod = new DevSeedController(Env("Production"), NullLogger<DevSeedController>.Instance, service);
        (await prod.CleanupSeedUsers(new DevSeedCleanupRequest(), CancellationToken.None)).ShouldBeOfType<NotFoundResult>();
        await service.DidNotReceiveWithAnyArgs().CleanupAsync(default!, default);

        service.CleanupAsync(Arg.Any<DevSeedCleanupRequest>(), Arg.Any<CancellationToken>()).Returns(new DevSeedCleanupResponse { DryRun = true });
        var dev = new DevSeedController(Env("Development"), NullLogger<DevSeedController>.Instance, service);
        var ok = (await dev.CleanupSeedUsers(new DevSeedCleanupRequest(), CancellationToken.None)).ShouldBeOfType<OkObjectResult>();
        ok.Value.ShouldBeOfType<DevSeedCleanupResponse>().DryRun.ShouldBeTrue();
    }

    public void Dispose() => _db.Dispose();
}
