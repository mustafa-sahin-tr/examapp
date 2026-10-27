using System.Net;
using System.Net.Http.Headers;

namespace Gateway.Tests;

/// <summary>
/// Issue #366: gercek pipeline (Program.cs + JwtBearer + Ocelot) uzerinden question-detector rol kapisi.
/// Program.cs'ten UseRouteRoleAuth kaldirilirsa bu testler kirilir (anon 401 / student 403 gelmez).
/// </summary>
[Collection("Gateway")]
public sealed class GatewayRouteRoleWiringTests : IClassFixture<GatewayWebSocketAuthTests.GatewayFactory>
{
    private readonly GatewayWebSocketAuthTests.GatewayFactory _factory;

    public GatewayRouteRoleWiringTests(GatewayWebSocketAuthTests.GatewayFactory factory) => _factory = factory;

    private async Task<HttpStatusCode> PostAsync(string path, string? token)
    {
        using var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent("{}") };
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.SendAsync(req)).StatusCode;
    }

    [Theory]
    [InlineData("/question-detector-dev/send-to-fix")]
    [InlineData("/question-detector-dev/send-to-fix-for-answers")]
    [InlineData("/question-detector-dev/predict")]
    public async Task Anonymous_Returns401(string path) =>
        (await PostAsync(path, null)).ShouldBe(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("/question-detector-dev/send-to-fix")]
    [InlineData("/question-detector-dev/send-to-fix-for-answers")]
    [InlineData("/question-detector-dev/predict")]
    public async Task Student_Returns403(string path) =>
        (await PostAsync(path, _factory.CreateToken("Student"))).ShouldBe(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("/question-detector-dev/send-to-fix")]
    [InlineData("/question-detector-dev/send-to-fix-for-answers")]
    [InlineData("/question-detector-dev//send-to-fix")]
    public async Task Teacher_OnSendToFix_Returns403(string path) =>
        (await PostAsync(path, _factory.CreateToken("Teacher"))).ShouldBe(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("/question-detector-dev/docs")]
    [InlineData("/question-detector-dev/unknown")]
    [InlineData("/question-detector-dev/send-to-fix%3F")]
    public async Task Teacher_OnNonAllowlisted_IsDenied(string path) =>
        (await PostAsync(path, _factory.CreateToken("Teacher"))).ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("/question-detector-dev/predict")]
    [InlineData("/question-detector-dev/headerlist")]
    [InlineData("/question-detector-dev/read-qr")]
    public async Task Teacher_OnEachAllowlisted_PassesRoleGate(string path)
    {
        var status = await PostAsync(path, _factory.CreateToken("Teacher"));
        status.ShouldNotBe(HttpStatusCode.Unauthorized);
        status.ShouldNotBe(HttpStatusCode.Forbidden);
        status.ShouldNotBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Teacher_OnPredict_PassesRoleGate()
    {
        // Test Ocelot config'inde question-detector route'u yok -> 404; onemli olan 401/403 OLMAMASI.
        var status = await PostAsync("/question-detector-dev/predict", _factory.CreateToken("Teacher"));
        status.ShouldNotBe(HttpStatusCode.Unauthorized);
        status.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("QuestionTrainer")]
    [InlineData("Teacher", "QuestionTrainer")]
    public async Task QuestionTrainer_OnSendToFix_PassesRoleGate(params string[] roles)
    {
        var status = await PostAsync("/question-detector-dev/send-to-fix", _factory.CreateToken(roles));
        status.ShouldNotBe(HttpStatusCode.Unauthorized);
        status.ShouldNotBe(HttpStatusCode.Forbidden);
        status.ShouldNotBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/question-detector-dev/docs")]
    [InlineData("/question-detector-dev/unknown")]
    [InlineData("/question-detector-dev/send-to-fix-for-answers")]
    public async Task QuestionTrainer_OnAdminOnly_Returns403(string path) =>
        (await PostAsync(path, _factory.CreateToken("Teacher", "QuestionTrainer"))).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Admin_OnSendToFix_PassesRoleGate()
    {
        var status = await PostAsync("/question-detector-dev/send-to-fix", _factory.CreateToken("Admin"));
        status.ShouldNotBe(HttpStatusCode.Unauthorized);
        status.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    // İşaretleme (Claude ön-etiketleme dahil) uçları: yalnız Admin + QuestionTrainer.
    [Theory]
    [InlineData("/api/exam/labeling/ai-detect")]
    [InlineData("/api/exam/labeling/pages/manual")]
    public async Task Labeling_Anonymous_Returns401(string path) =>
        (await PostAsync(path, null)).ShouldBe(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("/api/exam/labeling/ai-detect")]
    [InlineData("/api/exam/labeling/pages/manual")]
    public async Task Labeling_Student_Returns403(string path) =>
        (await PostAsync(path, _factory.CreateToken("Student"))).ShouldBe(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("/api/exam/labeling/ai-detect")]
    [InlineData("/api/exam/labeling/pages/manual")]
    public async Task Labeling_Teacher_Returns403(string path) =>
        (await PostAsync(path, _factory.CreateToken("Teacher"))).ShouldBe(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("/api/exam/labeling/ai-detect")]
    [InlineData("/api/exam/labeling/pages/manual")]
    public async Task Labeling_QuestionTrainer_PassesRoleGate(string path)
    {
        var status = await PostAsync(path, _factory.CreateToken("QuestionTrainer"));
        status.ShouldNotBe(HttpStatusCode.Unauthorized);
        status.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    // issue #583: açık uçlu değerlendirme kuyruğu — yalnız Teacher + Admin.
    private async Task<HttpStatusCode> SendAsync(HttpMethod method, string path, string? token)
    {
        using var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get) req.Content = new StringContent("{}");
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.SendAsync(req)).StatusCode;
    }

    public static TheoryData<string, string> OpenTextReviewEndpoints => new()
    {
        { "GET", "/api/exam/open-text-reviews" },
        { "PUT", "/api/exam/open-text-reviews/42" },
    };

    [Theory]
    [MemberData(nameof(OpenTextReviewEndpoints))]
    public async Task OpenTextReviews_Anonymous_Returns401(string method, string path) =>
        (await SendAsync(new HttpMethod(method), path, null)).ShouldBe(HttpStatusCode.Unauthorized);

    [Theory]
    [MemberData(nameof(OpenTextReviewEndpoints))]
    public async Task OpenTextReviews_StudentOrParent_Returns403(string method, string path)
    {
        (await SendAsync(new HttpMethod(method), path, _factory.CreateToken("Student"))).ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(new HttpMethod(method), path, _factory.CreateToken("Parent"))).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(OpenTextReviewEndpoints))]
    public async Task OpenTextReviews_TeacherOrAdmin_PassesRoleGate(string method, string path)
    {
        foreach (var role in new[] { "Teacher", "Admin" })
        {
            var status = await SendAsync(new HttpMethod(method), path, _factory.CreateToken(role));
            status.ShouldNotBe(HttpStatusCode.Unauthorized);
            status.ShouldNotBe(HttpStatusCode.Forbidden);
        }
    }
}
