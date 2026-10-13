using System.Net;
using System.Text.Json.Nodes;

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
