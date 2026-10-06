using System.Text.Json.Nodes;

namespace Gateway.Tests;

/// <summary>
/// issue #365 (S1): <c>/img/{everything}</c> MinIO'ya kimliksiz geçen tek route; yalnız GET kabul etmeli (POST ile
/// MinIO S3 API'sine — ör. çoklu silme / POST policy upload — yol açılmasın) ve anlamsız
/// <c>AddHeadersToRequest: true</c> taşımamalı. Üç ocelot dosyası da kontrol edilir.
/// </summary>
public sealed class GatewayImgRouteTests
{
    public static IEnumerable<object[]> OcelotFiles() =>
        new[] { "ocelot.json", "ocelot.Development.json", "ocelot.Production.json" }.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(OcelotFiles))]
    public void Img_route_accepts_only_get(string file)
    {
        var path = Path.Combine(GatewayWebSocketAuthTests.GatewayFactory.FindGatewayDir(), file);
        var routes = JsonNode.Parse(File.ReadAllText(path))!["Routes"]!.AsArray();

        var img = routes.Where(r => r!["UpstreamPathTemplate"]?.GetValue<string>() == "/img/{everything}").ToList();
        img.Count.ShouldBe(1, file);

        var methods = img[0]!["UpstreamHttpMethod"]?.AsArray().Select(m => m!.GetValue<string>().ToUpperInvariant()).ToList();
        methods.ShouldNotBeNull($"{file}: missing UpstreamHttpMethod means every method is allowed");
        string.Join(",", methods).ShouldBe("GET", file);
        img[0]!["AddHeadersToRequest"].ShouldBeNull(file);
    }

    [Theory]
    [MemberData(nameof(OcelotFiles))]
    public void No_other_route_proxies_to_minio(string file)
    {
        var path = Path.Combine(GatewayWebSocketAuthTests.GatewayFactory.FindGatewayDir(), file);
        var routes = JsonNode.Parse(File.ReadAllText(path))!["Routes"]!.AsArray();

        var minioRoutes = routes
            .Where(r => r!["DownstreamHostAndPorts"]?.AsArray()
                .Any(h => h!["Host"]?.GetValue<string>() is "minio" or "exam-minio") == true)
            .Select(r => r!["UpstreamPathTemplate"]?.GetValue<string>())
            .ToList();

        string.Join(",", minioRoutes).ShouldBe("/img/{everything}", file);
    }
}
