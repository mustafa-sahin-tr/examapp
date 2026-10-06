using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ExamApp.Api.Services.Storage;
using ExamApp.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Minio;
using Minio.DataModel.Args;

namespace Gateway.Tests;

/// <summary>
/// issue #365 (S2): API'nin ürettiği imzalı <c>/img/{bucket}/{key}?X-Amz-...</c> URL'si GERÇEK gateway hattından
/// (Program.cs + Ocelot + ocelot.json'daki gerçek <c>/img/{everything}</c> route'u) geçtikten sonra MinIO'da geçerli
/// mi? Downstream sahte "MinIO", isteği MinIO gibi doğrular: ham hedefi decode edip S3 kanonik biçimine yeniden encode
/// eder ve gelen Host başlığıyla SigV4 imzasını SDK'dan bağımsız olarak hesaplar. İmzalayıcının
/// <c>MinioConfig:PresignEndpoint</c>'i gateway'in downstream adresine ayarlıdır (üretimdeki gereklilik).
/// </summary>
[Collection("Gateway")]
public sealed class GatewayImgPresignTests : IClassFixture<GatewayImgPresignTests.ImgGatewayFactory>
{
    private const string TestAk = "gw-test-ak";
    private const string TestSk = "gw-test-sk-0001";
    private readonly ImgGatewayFactory _factory;

    public GatewayImgPresignTests(ImgGatewayFactory factory) => _factory = factory;

    private MinioStorageUrlSigner NewSigner(DateTimeOffset? now = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // API kendi MinIO bağlantısı için başka bir adres kullanıyor olabilir; imza gateway'in downstream'i için.
            ["MinioConfig:Endpoint"] = "api-side-minio:9000",
            ["MinioConfig:PresignEndpoint"] = _factory.DownstreamHost,
            ["MinioConfig:AccessKey"] = TestAk,
            ["MinioConfig:SecretKey"] = TestSk,
            ["MinioConfig:BucketName"] = "exam-questions",
        }).Build();
        return new MinioStorageUrlSigner(config, new StorageAreaPolicy(config),
            new FixedClock(now ?? DateTimeOffset.UtcNow), NullLogger<MinioStorageUrlSigner>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public static TheoryData<string, StorageArea> SignableObjects() => new()
    {
        { "/img/exam-questions/questions/0b9c8f1e-1d1a-4b55-9a0e-6c2f1f0c9d11/question.jpg", StorageArea.QuestionImage },
        { "/img/exam-questions/answers/12/7f3c.jpg", StorageArea.QuestionImage },
        { "/img/worksheets/42-background.png", StorageArea.WorksheetCover },
        { "/img/study-pages/books/Matematik Kitabı 5. Sınıf/page_1.webp", StorageArea.StudyPage },
        { "/img/study-pages/books/Çğıöşü ÇĞİÖŞÜ/page_12.webp", StorageArea.StudyPage },
        { "/img/study-pages/books/a+b (1) 'x'!,=;@$~/page_2.webp", StorageArea.StudyPage },
    };

    [Theory]
    [MemberData(nameof(SignableObjects))]
    public async Task Signed_url_survives_the_real_gateway_route_and_validates_downstream(string stored, StorageArea area)
    {
        using var signer = NewSigner();
        var signed = signer.SignForBrowser(stored, [area]);
        signed.ShouldNotBeNull();
        signed.ShouldContain("X-Amz-Signature=");

        var client = _factory.CreateClient();
        var res = await client.GetAsync(signed);
        var body = await res.Content.ReadAsStringAsync();

        res.IsSuccessStatusCode.ShouldBeTrue($"{(int)res.StatusCode} {body} — downstream saw {_factory.LastRawTarget}");
        // Downstream'e giden Host, imzanın bağlandığı adres (Ocelot Host'u downstream'e çevirir).
        _factory.LastHost.ShouldBe(_factory.DownstreamHost);
        // Downstream'in decode ettiği yol = /{bucket}/{ham anahtar}.
        body.ShouldBe("ok:" + stored["/img".Length..]);
    }

    [Fact]
    public async Task Tampered_or_expired_signature_is_rejected_downstream()
    {
        using var signer = NewSigner();
        var signed = signer.SignForBrowser("/img/exam-questions/questions/1/q.jpg", [StorageArea.QuestionImage])!;
        var client = _factory.CreateClient();

        // Yolu export paketine çevir: aynı imza artık geçersiz.
        var swapped = signed.Replace("questions/1/q.jpg", "question-transfer/exports/default/index.json");
        (await client.GetAsync(swapped)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);

        using var oldSigner = NewSigner(DateTimeOffset.UtcNow.AddHours(-5));
        var expired = oldSigner.SignForBrowser("/img/exam-questions/questions/1/q.jpg", [StorageArea.QuestionImage])!;
        (await client.GetAsync(expired)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Karakterizasyon: Ocelot <c>{everything}</c>'i bir kez decode edip yeniden iletir; <c>&amp;</c> ve geçerli bir
    /// <c>%XX</c> dizisi içeren anahtarlarda imza downstream'de tutmaz. İmzalayıcı bu yüzden bunları imzalamaz ve
    /// <see cref="StorageAreaPolicy"/> istemciden kabul etmez. Ocelot bu davranışı düzeltirse bu test kırılır — o zaman
    /// <c>MinioObjectUrl.IsGatewaySafeKey</c> gevşetilebilir.
    /// </summary>
    [Theory]
    [InlineData("books/A&B/page_1.webp")]
    [InlineData("books/100%41/page_1.webp")]
    public async Task Keys_with_ampersand_or_percent_escape_break_the_signature_through_ocelot_so_they_are_not_signed(string key)
    {
        using var signer = NewSigner();
        signer.SignForBrowser("/img/study-pages/" + key, [StorageArea.StudyPage]).ShouldBe("/img/study-pages/" + key);

        var raw = await new MinioClient().WithEndpoint(_factory.DownstreamHost).WithCredentials(TestAk, TestSk)
            .WithRegion(MinioStorageUrlSigner.Region).Build()
            .PresignedGetObjectAsync(new PresignedGetObjectArgs().WithBucket("study-pages").WithObject(key).WithExpiry(3600));
        var viaGateway = "/img" + new Uri(raw).PathAndQuery;

        (await _factory.CreateClient().GetAsync(viaGateway)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
    }

    public sealed class ImgGatewayFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly WebApplication _downstream;
        private readonly string _oldCwd = Directory.GetCurrentDirectory();
        private readonly string _tempDir;
        private readonly ConcurrentQueue<(string Host, string RawTarget)> _seen = new();

        public string DownstreamHost { get; }
        public string? LastHost => _seen.LastOrDefault().Host;
        public string? LastRawTarget => _seen.LastOrDefault().RawTarget;

        public ImgGatewayFactory()
        {
            _downstream = BuildFakeMinio();
            var port = new Uri(_downstream.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
            DownstreamHost = $"127.0.0.1:{port}";

            // Gerçek ocelot.json'daki /img route'u alınır; yalnız downstream adresi sahte MinIO'ya çevrilir.
            var real = JsonNode.Parse(File.ReadAllText(Path.Combine(GatewayWebSocketAuthTests.GatewayFactory.FindGatewayDir(), "ocelot.json")))!;
            var img = real["Routes"]!.AsArray()
                .Single(r => r!["UpstreamPathTemplate"]?.GetValue<string>() == "/img/{everything}")!.DeepClone();
            img["DownstreamHostAndPorts"] = new JsonArray(new JsonObject { ["Host"] = "127.0.0.1", ["Port"] = port });
            var config = new JsonObject
            {
                ["Routes"] = new JsonArray(img),
                ["GlobalConfiguration"] = new JsonObject { ["BaseUrl"] = "http://localhost" },
            };

            _tempDir = Directory.CreateTempSubdirectory("gateway-img-tests-").FullName;
            File.WriteAllText(Path.Combine(_tempDir, "ocelot.json"), config.ToJsonString());
            Directory.SetCurrentDirectory(_tempDir);
        }

        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _downstream.DisposeAsync();
            Directory.SetCurrentDirectory(_oldCwd);
            try { Directory.Delete(_tempDir, true); } catch (IOException) { }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(GatewayWebSocketAuthTests.GatewayFactory.FindGatewayDir());
        }

        /// <summary>MinIO gibi davranır: ham istek hedefinden SigV4'ü doğrular; geçerliyse 200 "ok:{decode edilmiş yol}".</summary>
        private WebApplication BuildFakeMinio()
        {
            var b = WebApplication.CreateBuilder();
            b.WebHost.UseUrls("http://127.0.0.1:0");
            var app = b.Build();
            app.Run(async ctx =>
            {
                var rawTarget = ctx.Features.Get<IHttpRequestFeature>()!.RawTarget;
                var host = ctx.Request.Headers.Host.ToString();
                _seen.Enqueue((host, rawTarget));

                var q = rawTarget.IndexOf('?');
                var decodedPath = Uri.UnescapeDataString(q < 0 ? rawTarget : rawTarget[..q]);
                var query = SigV4PresignVerifier.ParseQuery(q < 0 ? "" : rawTarget[(q + 1)..]);
                var result = SigV4PresignVerifier.Verify(ctx.Request.Method, host, decodedPath, query, TestSk, DateTime.UtcNow);

                ctx.Response.StatusCode = result.Valid ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync(result.Valid ? "ok:" + decodedPath : result.Reason);
            });
            app.Start();
            return app;
        }
    }
}
