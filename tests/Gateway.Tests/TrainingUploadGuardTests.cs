using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Tests;

/// <summary>Issue #461: send-to-fix per-user rate limit and the gateway-owned submitter header.</summary>
public sealed class TrainingUploadGuardTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:TrainingUpload:PermitLimit"] = "2",
            ["RateLimiting:TrainingUpload:WindowSeconds"] = "600"
        });
        _app = builder.Build();
        // Stands in for RouteRoleAuth: the test sends the subject it wants to be authenticated as.
        _app.Use((context, next) =>
        {
            var sub = context.Request.Headers["X-Test-Sub"].ToString();
            if (sub.Length > 0)
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], "Test"));
            return next();
        });
        _app.UseTrainingUploadGuard(_app.Configuration);
        _app.Run(context => context.Response.WriteAsync(
            context.Request.Headers[TrainingUploadGuardExtensions.SubmittedByHeader].ToString()));
        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpMethod method, string path, string? sub, string? spoofedSubmitter = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (sub is not null) request.Headers.Add("X-Test-Sub", sub);
        if (spoofedSubmitter is not null) request.Headers.Add(TrainingUploadGuardExtensions.SubmittedByHeader, spoofedSubmitter);
        using var response = await _client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendToFix_ForwardsAuthenticatedSubject_IgnoringClientValue()
    {
        var (status, body) = await SendAsync(HttpMethod.Post, "/question-detector-dev/send-to-fix", "user-a", "someone-else");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("user-a");
    }

    [Fact]
    public async Task OtherDetectorPaths_DropClientSubmitterHeader()
    {
        var (status, body) = await SendAsync(HttpMethod.Post, "/question-detector-dev/predict", "user-a", "someone-else");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendToFix_OverLimit_Returns429PerUser()
    {
        (await SendAsync(HttpMethod.Post, "/question-detector-dev/send-to-fix", "user-b")).Status.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, "/question-detector-dev//send-to-fix", "user-b")).Status.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, "/question-detector-dev/send-to-fix", "user-b")).Status.ShouldBe(HttpStatusCode.TooManyRequests);

        // Another trainer has their own window; read endpoints are not counted.
        (await SendAsync(HttpMethod.Post, "/question-detector-dev/send-to-fix", "user-c")).Status.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, "/question-detector-dev/predict", "user-b")).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendToFix_WithoutSubject_Returns401()
    {
        (await SendAsync(HttpMethod.Post, "/question-detector-dev/send-to-fix", null)).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OtherPaths_AreUntouched()
    {
        var (status, body) = await SendAsync(HttpMethod.Post, "/api/exam/anything", null, "kept");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("kept");
    }
}
