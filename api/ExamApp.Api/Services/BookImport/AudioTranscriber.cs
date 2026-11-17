using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.BookImport;

/// <summary>#1219: ses kaydını yazıya çevirir (Türkçe). Hizmet yoksa sesli QR çözümlerinden anahtar alınmaz.</summary>
public interface IAudioTranscriber
{
    /// <summary>Hizmet ayarlı ve whisper yüklü mü (kısa süre önbelleklenir).</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct);

    Task<string> TranscribeAsync(byte[] audio, string fileName, CancellationToken ct);
}

/// <summary>
/// question-detector'ın <c>/transcribe</c> ucu (faster-whisper). Adres <c>BookImport:TranscriberUrl</c>; boşsa ya da servis whisper'sız
/// çalışıyorsa (<c>/transcribe/status</c> available=false) hizmet yok sayılır.
/// </summary>
public sealed class DetectorAudioTranscriber(IHttpClientFactory httpClientFactory, IConfiguration configuration,
    ILogger<DetectorAudioTranscriber> logger) : IAudioTranscriber
{
    public const string HttpClientName = "transcriber";
    private static readonly TimeSpan StatusTtl = TimeSpan.FromMinutes(1);
    private static (string Url, bool Available, DateTimeOffset At)? _status;

    private string? BaseUrl => configuration["BookImport:TranscriberUrl"] is { Length: > 0 } url ? url.TrimEnd('/') : null;

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (BaseUrl is not { } url)
            return false;
        if (_status is { } cached && cached.Url == url && DateTimeOffset.UtcNow - cached.At < StatusTtl)
            return cached.Available;
        var available = false;
        try
        {
            var status = await httpClientFactory.CreateClient(HttpClientName)
                .GetFromJsonAsync<TranscriberStatus>($"{url}/transcribe/status", ct);
            available = status?.Available == true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Transcriber status check failed: {Message}", e.Message);
        }

        _status = (url, available, DateTimeOffset.UtcNow);
        return available;
    }

    public async Task<string> TranscribeAsync(byte[] audio, string fileName, CancellationToken ct)
    {
        var url = BaseUrl ?? throw new InvalidOperationException("Transcriber is not configured.");
        using var body = new ByteArrayContent(audio);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4" : "audio/mpeg");
        using var response = await httpClientFactory.CreateClient(HttpClientName).PostAsync($"{url}/transcribe", body, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TranscriberResult>(ct);
        return result?.Text ?? string.Empty;
    }

    private sealed record TranscriberStatus(bool Available);

    private sealed record TranscriberResult(string? Text);
}
