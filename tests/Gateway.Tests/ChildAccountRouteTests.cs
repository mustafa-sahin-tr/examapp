using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Gateway.Tests;

/// <summary>
/// Issue #438: auth-api'nin servis-only çocuk hesabı uçları (<c>/api/auth/child-accounts[/...]</c>) gateway'de engelli
/// (<c>users/lookup</c> deseni: Priority 2, <c>/__blocked/...</c> downstream) — üç ocelot dosyası da. Velinin çocuk hesabı açma
/// isteği (<c>POST /api/exam/parent/children</c>) giriş IP kovasından düşer; şifre sıfırlama ve okuma uçları düşmez.
/// </summary>
public sealed class ChildAccountRouteTests
{
    public static IEnumerable<object[]> OcelotFiles() => GatewayImgRouteTests.OcelotFiles();

    [Theory]
    [MemberData(nameof(OcelotFiles))]
    public void Auth_api_child_account_endpoints_are_blocked(string file)
    {
        var path = Path.Combine(GatewayWebSocketAuthTests.GatewayFactory.FindGatewayDir(), file);
        var routes = JsonNode.Parse(File.ReadAllText(path))!["Routes"]!.AsArray();

        foreach (var upstream in new[] { "/api/auth/child-accounts", "/api/auth/child-accounts/{everything}" })
        {
            var route = routes.SingleOrDefault(r => r!["UpstreamPathTemplate"]?.GetValue<string>() == upstream);
            route.ShouldNotBeNull($"{file}: {upstream}");
            route!["DownstreamPathTemplate"]!.GetValue<string>().ShouldStartWith("/__blocked/", Case.Sensitive, file);
            route["Priority"]!.GetValue<int>().ShouldBe(2, file);
        }
    }

    /// <summary>
    /// #438 review: kompanzasyon ucu <c>DELETE /api/auth/child-accounts/by-request/{requestId}</c> (ve userId'li silme) gateway'den
    /// dışarıya kapalı. Ocelot'un seçimi taklit edilir: path + metodu eşleşen route'lar içinde en yüksek Priority kazanır; kazanan
    /// HER route <c>/__blocked/</c>'a gitmeli (genel <c>/api/auth/{everything}</c> Priority 1'de kalır).
    /// </summary>
    [Theory]
    [MemberData(nameof(OcelotFiles))]
    public void Compensation_delete_paths_resolve_to_a_blocked_route(string file)
    {
        var path = Path.Combine(GatewayWebSocketAuthTests.GatewayFactory.FindGatewayDir(), file);
        var routes = JsonNode.Parse(File.ReadAllText(path))!["Routes"]!.AsArray().Select(r => r!).ToList();

        foreach (var (method, upstream) in new[]
                 {
                     ("DELETE", "/api/auth/child-accounts/by-request/2f1c9a52-3d55-4c1e-9a0b-6c2d7e8f9a10"),
                     ("DELETE", "/API/Auth/Child-Accounts/By-Request/2f1c9a52-3d55-4c1e-9a0b-6c2d7e8f9a10"),
                     ("DELETE", "/api/auth/child-accounts/42"),
                     ("POST", "/api/auth/child-accounts"),
                     ("PUT", "/api/auth/child-accounts/42/password"),
                 })
        {
            var matching = routes.Where(r => Matches(r, method, upstream)).ToList();
            matching.ShouldNotBeEmpty($"{file}: {method} {upstream}");
            var top = matching.Max(r => r["Priority"]?.GetValue<int>() ?? 1);
            top.ShouldBe(2, $"{file}: {method} {upstream}");
            matching.Where(r => (r["Priority"]?.GetValue<int>() ?? 1) == top)
                .ShouldAllBe(r => r["DownstreamPathTemplate"]!.GetValue<string>().StartsWith("/__blocked/"),
                    $"{file}: {method} {upstream}");
        }
    }

    /// <summary>
    /// Ocelot UpstreamPathTemplate eşleşmesi (yer tutucu → tek segment; sondaki yer tutucu kalan yolun tamamı; varsayılan büyük/küçük
    /// harf duyarsız; UpstreamHttpMethod boşsa her metot).
    /// </summary>
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

    [Theory]
    [InlineData("/api/exam/parent/children", true)]
    [InlineData("/API/EXAM/Parent/Children/", true)]
    [InlineData("/api/exam/parent%2Fchildren", true)]
    [InlineData("/api/exam/parent/%2563hildren", true)]
    [InlineData("/api/exam/parent/children/5/reset-password", false)]
    [InlineData("/api/exam/parent/children/5/summary", false)]
    public void Child_account_create_path_matching_is_encoding_resistant(string path, bool expected)
        => LoginRateLimitExtensions.IsParentChildAccountCreate(path).ShouldBe(expected);
}
