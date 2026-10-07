using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #424 (epic #407 V6): veli uç kataloğunun DB gerektirmeyen yansıma kilitleri. Matris (IDOR/yetki), yasak içerik taraması
/// ve ters keşif (Parent rolüyle erişilebilen her uç) gerçek Postgres'le <c>ExamApp.Api.IntegrationTests</c>'te koşar.
/// </summary>
public class ParentEndpointCatalogTests
{
    [Fact]
    public void Every_parent_endpoint_is_in_the_matrix()
    {
        var discovered = ParentEndpointCatalog.DiscoverParentActions().Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
        var known = ParentEndpointCatalog.ReadEndpoints.Select(e => e.Action)
            .Concat(ParentEndpointCatalog.Exempt.Keys)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = discovered.Except(known).OrderBy(k => k, StringComparer.Ordinal).ToList();
        uncovered.ShouldBeEmpty(
            "Veli kapsamında yeni uç(lar) matriste yok. ParentEndpointCatalog.ReadEndpoints'e ekleyin (IDOR matrisi + yasak içerik " +
            "taraması otomatik koşar) ya da gerekçesiyle ParentEndpointCatalog.Exempt'e yazın: " + string.Join(", ", uncovered));

        var stale = known.Except(discovered).OrderBy(k => k, StringComparer.Ordinal).ToList();
        stale.ShouldBeEmpty("Katalogda artık var olmayan uç(lar): " + string.Join(", ", stale));

        ParentEndpointCatalog.ReadEndpoints.Select(e => e.Action).ShouldBeUnique();
        ParentEndpointCatalog.ReadEndpoints.Where(e => e.Scope == ParentEndpointScope.Child)
            .Select(e => e.AuditEndpoint).ShouldAllBe(a => !string.IsNullOrEmpty(a));
    }

    [Fact]
    public void Every_parent_read_endpoint_is_a_get_marked_no_store()
    {
        var actions = ParentEndpointCatalog.DiscoverParentActions().ToDictionary(a => a.Key, StringComparer.Ordinal);
        foreach (var endpoint in ParentEndpointCatalog.ReadEndpoints)
        {
            var (_, method, controller) = actions[endpoint.Action];
            method.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                .ShouldAllBe(h => h.HttpMethods.SequenceEqual(new[] { "GET" }), $"{endpoint.Action} salt okunur olmalı.");

            // Aksiyon ya da controller seviyesinde no-store: 200'ün yanında 400/404 da önbelleğe alınmasın.
            var cache = method.GetCustomAttribute<ResponseCacheAttribute>(inherit: true)
                ?? controller.GetCustomAttribute<ResponseCacheAttribute>(inherit: true);
            cache.ShouldNotBeNull($"{endpoint.Action}: [ResponseCache(NoStore = true, Location = None)] yok.");
            cache.NoStore.ShouldBeTrue($"{endpoint.Action}: NoStore = true değil.");
            cache.Location.ShouldBe(ResponseCacheLocation.None, $"{endpoint.Action}: Location = None değil.");
        }
    }

    [Fact]
    public void Allowlists_do_not_overlap_the_read_matrix()
    {
        var read = ParentEndpointCatalog.ReadEndpoints.Select(e => e.Action).ToHashSet(StringComparer.Ordinal);
        ParentEndpointCatalog.Exempt.Keys.ShouldAllBe(k => !read.Contains(k));
        ParentEndpointCatalog.ParentReachableAllowlist.Keys.ShouldAllBe(k => !read.Contains(k));
        ParentEndpointCatalog.ParentReachableAllowlist.Values.ShouldAllBe(reason => !string.IsNullOrWhiteSpace(reason));
        ParentEndpointCatalog.MustDenyParent.ShouldAllBe(k => !read.Contains(k) && !ParentEndpointCatalog.ParentReachableAllowlist.ContainsKey(k));
    }

    [Fact]
    public void Leaderboard_is_closed_to_parents()
    {
        // issue #424 review: başka öğrencilerin ad/puanı veliye açılmaz; veli çocuğunun sırasını progress (V4) ile görür.
        var method = typeof(LeaderboardController).GetMethod(nameof(LeaderboardController.GetLeaderboard))!;
        var roles = method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Roles).Where(r => r != null).ShouldHaveSingleItem()!;
        roles.Split(',', StringSplitOptions.TrimEntries).ShouldBe(new[] { "Student", "Teacher", "Admin" }, ignoreOrder: true);
    }
}
