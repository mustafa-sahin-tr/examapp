using Shouldly;
using System.Text.Json.Nodes;

namespace AssistantService.Tests;

/// <summary>
/// Issue #502: /api/assistant/* goes to assistant-service with a Bearer token and a timeout long enough for a streamed
/// answer (Ocelot's default is 90 s); /mcp is not routed until Phase 4 (#518).
/// </summary>
public sealed class AssistantGatewayRouteTests
{
    [Theory]
    [InlineData("ocelot.json")]
    [InlineData("ocelot.Development.json")]
    [InlineData("ocelot.Production.json")]
    public void Assistant_route_requires_a_token_and_allows_long_streams(string file)
    {
        var routes = Routes(file);
        var assistant = routes.Where(r => r["UpstreamPathTemplate"]?.GetValue<string>() == "/api/assistant/{everything}").ToList();
        assistant.Count.ShouldBe(1, file);
        var route = assistant[0];
        route["DownstreamPathTemplate"]!.GetValue<string>().ShouldBe("/api/assistant/{everything}");
        route["DownstreamHostAndPorts"]![0]!["Host"]!.GetValue<string>().ShouldBe("assistant-service");
        route["AuthenticationOptions"]!["AuthenticationProviderKey"]!.GetValue<string>().ShouldBe("Bearer");
        route["QoSOptions"]!["TimeoutValue"]!.GetValue<int>().ShouldBeGreaterThanOrEqualTo(180_000);

        routes.Where(r => r["DownstreamHostAndPorts"]?.AsArray().Any(h => h!["Host"]?.GetValue<string>() == "assistant-service") == true)
            .Select(r => r["UpstreamPathTemplate"]!.GetValue<string>())
            .ShouldBe(["/api/assistant/{everything}"], file);
        routes.Any(r => r["UpstreamPathTemplate"]!.GetValue<string>().StartsWith("/mcp", StringComparison.Ordinal)).ShouldBeFalse(file);
    }

    private static List<JsonNode> Routes(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExamApp.slnx")))
            dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "Services", "Gateway", file);
        return JsonNode.Parse(File.ReadAllText(path))!["Routes"]!.AsArray().Select(r => r!).ToList();
    }
}
