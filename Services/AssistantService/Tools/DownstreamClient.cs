using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssistantService.Tools;

/// <summary>Result of a downstream call: status, parsed JSON body (if any) and the API's <c>errorCode</c>.</summary>
public sealed record DownstreamResponse(HttpStatusCode Status, JsonNode? Body)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    public string? ErrorCode => Body is JsonObject o && o["errorCode"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public string? Message => Body is JsonObject o && o["message"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Maps a failed call to a structured tool error the model can explain (#503: API errorCodes pass through).</summary>
    public ToolResult ToError() => ToolResult.Error(ErrorCode ?? Status switch
    {
        HttpStatusCode.Unauthorized => "Unauthorized",
        HttpStatusCode.Forbidden => "Forbidden",
        HttpStatusCode.NotFound => "NotFound",
        HttpStatusCode.Conflict => "Conflict",
        HttpStatusCode.TooManyRequests => "RateLimited",
        HttpStatusCode.BadRequest => "InvalidArguments",
        _ => "DownstreamFailed",
    }, Message);
}

/// <summary>
/// Issue #503: calls the exam API on the internal network (<c>ExamApi:BaseUrl</c>, not through the gateway — #502 spike)
/// with the user's own Bearer token and language. The assistant never reads another service's database.
/// </summary>
public sealed class DownstreamClient(HttpClient http)
{
    public const string HttpClientName = "exam-api";

    public Task<DownstreamResponse> GetAsync(ToolContext context, string path) => SendAsync(context, HttpMethod.Get, path, null);

    public Task<DownstreamResponse> PostAsync(ToolContext context, string path, object? body) =>
        SendAsync(context, HttpMethod.Post, path, body);

    private async Task<DownstreamResponse> SendAsync(ToolContext context, HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.BearerToken);
        if (!string.IsNullOrWhiteSpace(context.AcceptLanguage))
            request.Headers.TryAddWithoutValidation("Accept-Language", context.AcceptLanguage);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, context.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(context.CancellationToken);
        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                json = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                json = null;
            }
        }
        return new DownstreamResponse(response.StatusCode, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
