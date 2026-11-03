using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Tests;

/// <summary>Gerçek Kestrel üzerinde: TestServer gövde sınırı uygulamaz, 30 MB varsayılanı ancak burada görülür.</summary>
public sealed class UploadLimitTests : IAsyncLifetime
{
    private const int OverDefaultLimit = 31_000_000;
    private readonly TaskCompletionSource<int> _serverOutcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.UseQuestionTransferUploadLimit();
        _app.Run(async context =>
        {
            try
            {
                await context.Request.Body.CopyToAsync(Stream.Null);
                _serverOutcome.TrySetResult(StatusCodes.Status200OK);
            }
            catch (BadHttpRequestException ex)
            {
                _serverOutcome.TrySetResult(ex.StatusCode);
                context.Response.StatusCode = ex.StatusCode;
            }
        });
        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>Sonuç sunucudan okunur: Kestrel reddederken gövde hâlâ gönderiliyorsa istemci 413 yerine kopan bağlantı görebilir.</summary>
    private async Task<int> SendAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(new byte[OverDefaultLimit]) };
        try
        {
            using var response = await _client.SendAsync(request);
        }
        catch (HttpRequestException)
        {
        }
        return await _serverOutcome.Task.WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("/api/exam/question-transfer/imports")]
    [InlineData("/api/exam/question-transfer/imports/preview")]
    public async Task Package_over_the_default_limit_passes(string path) =>
        (await SendAsync(path)).ShouldBe(StatusCodes.Status200OK);

    [Theory]
    [InlineData("/api/exam/questions")]
    [InlineData("/api/exam/question-transfer/importsx")]
    [InlineData("/api/exam/question-transfer/exports")]
    public async Task Other_paths_keep_the_default_limit(string path) =>
        (await SendAsync(path)).ShouldBe(StatusCodes.Status413PayloadTooLarge);

    [Fact]
    public void Limit_matches_the_one_in_exam_api() =>
        UploadLimitExtensions.QuestionTransferPackageBytes.ShouldBe(200_000_000);
}
