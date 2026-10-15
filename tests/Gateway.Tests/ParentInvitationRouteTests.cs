using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Gateway.Tests;

/// <summary>
/// Issue #440: auth-api'nin servis-only veli hesabı uçları (<c>/api/auth/parent-accounts[/...]</c>) gateway'de engelli (child-accounts
/// deseni) ve velinin anonim aktivasyon uçları (<c>POST /api/exam/parent-activation/{lookup|activate|resend}</c>) kimlik doğrulamasız ayrı
/// route'tan exam API'ye gider; oturum açmış velinin kabulü (<c>POST /api/exam/parent-activation/accept</c>, security C1) ise daha yüksek
/// öncelikli KİMLİK DOĞRULAMALI route'tan. Aynı yolun GET'i ve diğer exam uçları korumalı wildcard'da kalır. Aktivasyon POST'ları giriş IP
/// kovasından düşer. Üç ocelot dosyası.
/// </summary>
public sealed class ParentInvitationRouteTests
{
    public static IEnumerable<object[]> OcelotFiles() => GatewayImgRouteTests.OcelotFiles();

    private static List<JsonNode> Routes(string file)
    {
        var path = Path.Combine(GatewayWebSocketAuthTests.GatewayFactory.FindGatewayDir(), file);
        return JsonNode.Parse(File.ReadAllText(path))!["Routes"]!.AsArray().Select(r => r!).ToList();
    }

    /// <summary>Ocelot seçimi: path + metodu eşleşen route'lar içinde en yüksek Priority (varsayılan 1).</summary>
    private static List<JsonNode> Winners(List<JsonNode> routes, string method, string upstream)
    {
        var matching = routes.Where(r => Matches(r, method, upstream)).ToList();
        if (matching.Count == 0)
            return matching;
        var top = matching.Max(r => r["Priority"]?.GetValue<int>() ?? 1);
        return matching.Where(r => (r["Priority"]?.GetValue<int>() ?? 1) == top).ToList();
    }

    [Theory]
    [MemberData(nameof(OcelotFiles))]
    public void Auth_api_parent_account_endpoints_resolve_to_a_blocked_route(string file)
    {
        var routes = Routes(file);
        foreach (var (method, upstream) in new[]
                 {
                     ("POST", "/api/auth/parent-accounts/lookup"),
                     ("POST", "/API/Auth/Parent-Accounts/Lookup"),
                     ("POST", "/api/auth/parent-accounts/activate"),
                     ("POST", "/api/auth/parent-accounts/activate/"),
                     ("GET", "/api/auth/parent-accounts"),
                 })
        {
            var winners = Winners(routes, method, upstream);
            winners.ShouldNotBeEmpty($"{file}: {method} {upstream}");
            winners.ShouldAllBe(r => r["DownstreamPathTemplate"]!.GetValue<string>().StartsWith("/__blocked/"), $"{file}: {method} {upstream}");
        }
    }

    [Theory]
    [MemberData(nameof(OcelotFiles))]
    public void Parent_activation_post_is_anonymous_and_goes_to_the_exam_api(string file)
    {
        var routes = Routes(file);
        foreach (var action in new[] { "lookup", "activate", "resend" })
        {
            var winners = Winners(routes, "POST", $"/api/exam/parent-activation/{action}");
            winners.Count.ShouldBe(1, $"{file}: {action}");
            var route = winners[0];
            route["DownstreamPathTemplate"]!.GetValue<string>().ShouldBe("/api/parent-activation/{everything}", file);
            route["AuthenticationOptions"].ShouldBeNull($"{file}: aktivasyon anonim olmalı");
            route["DownstreamHostAndPorts"]![0]!["Host"]!.GetValue<string>().ShouldBe("exam-dotnet-api", file);
        }

        // GET aynı yolda anonim route'a düşmez: korumalı /api/exam/{everything} wildcard'ı kazanır.
        var get = Winners(routes, "GET", "/api/exam/parent-activation/lookup");
        get.ShouldAllBe(r => r["AuthenticationOptions"] != null, file);
        // Diğer exam uçları korumalı kalır.
        Winners(routes, "POST", "/api/exam/student-parent/invitation").ShouldAllBe(r => r["AuthenticationOptions"] != null, file);
    }

    [Theory]
    [MemberData(nameof(OcelotFiles))]
    public void Parent_invitation_accept_requires_authentication_at_the_gateway(string file)
    {
        var routes = Routes(file);
        foreach (var path in new[] { "/api/exam/parent-activation/accept", "/API/Exam/Parent-Activation/Accept" })
        {
            var winners = Winners(routes, "POST", path);
            winners.Count.ShouldBe(1, $"{file}: {path}");
            winners[0]["AuthenticationOptions"]!["AuthenticationProviderKey"]!.GetValue<string>().ShouldBe("Bearer", file);
            winners[0]["DownstreamPathTemplate"]!.GetValue<string>().ShouldBe("/api/parent-activation/accept", file);
        }
    }

    [Theory]
    [InlineData("/api/exam/parent-activation/activate", true)]
    [InlineData("/api/exam/parent-activation/accept", true)]
    [InlineData("/API/EXAM/Parent-Activation/Lookup", true)]
    [InlineData("/api/exam/parent-activation%2Fresend", true)]
    [InlineData("/api/exam/parent%252Dactivation/activate", true)]
    [InlineData("/api/exam/student-parent/invitation", false)]
    [InlineData("/api/exam/parent-links/my-children", false)]
    public void Parent_activation_path_matching_is_encoding_resistant(string path, bool expected)
        => LoginRateLimitExtensions.IsParentActivation(path).ShouldBe(expected);

    /// <summary>ChildAccountRouteTests ile aynı Ocelot eşleme taklidi.</summary>
    private static bool Matches(JsonNode route, string method, string upstreamPath)
    {
        var methods = route["UpstreamHttpMethod"]?.AsArray().Select(m => m!.GetValue<string>()).ToList();
        if (methods is { Count: > 0 } && !methods.Contains(method, StringComparer.OrdinalIgnoreCase))
            return false;

        var template = route["UpstreamPathTemplate"]!.GetValue<string>();
        var parts = Regex.Split(template, @"(\{[^}]+\})");
        var trailingPlaceholder = parts.Length >= 2 && parts[^1].Length == 0 && parts[^2].StartsWith('{') ? parts.Length - 2 : -1;
        var pattern = string.Concat(parts.Select((p, i) => p.StartsWith('{')
            ? (i == trailingPlaceholder ? ".+" : "[^/]+")
            : Regex.Escape(p)));
        var caseSensitive = route["RouteIsCaseSensitive"]?.GetValue<bool>() ?? false;
        return Regex.IsMatch(upstreamPath, $"^{pattern}/?$", caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
    }
}
