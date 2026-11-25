using System.Net;
using System.Text.Json.Nodes;
using AssistantService.Tools;
using Microsoft.Extensions.Caching.Memory;

namespace AssistantService.Security;

/// <summary>
/// Issue #523: a parent can switch the assistant off for their child. Before every chat turn and every confirmed action a student's
/// access is read from the exam API with the student's own token (<c>GET api/student/assistant-access</c>, no database sharing) and
/// cached for <see cref="CacheFor"/>. Switched off → 403 <c>AssistantDisabledByParent</c>. Fails closed: if the exam API cannot answer,
/// the turn is refused (503 <c>AssistantUnavailable</c>, not cached); its own 403 code (e.g. <c>ParentRequired</c>) passes through.
/// Other roles are not checked. Issue #1353: the same answer says whether a timed test is running; chat is refused then
/// (409 <c>AssistantClosedDuringTest</c>) whatever route the client reports.
/// </summary>
public sealed class ParentControl(DownstreamClient exam, IMemoryCache cache, ILogger<ParentControl> logger)
{
    public const string AccessPath = "api/student/assistant-access";
    public const string DisabledCode = "AssistantDisabledByParent";
    public const string ClosedDuringTestCode = "AssistantClosedDuringTest";
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    /// <returns>null when the user may continue; otherwise the response to send.</returns>
    public async Task<IResult?> RejectAsync(ToolContext context)
    {
        if (!context.Roles.Contains("Student"))
            return null;

        var key = "assistant-access:" + context.UserSub;
        if (!cache.TryGetValue(key, out (bool Allowed, bool TestInProgress) access))
        {
            DownstreamResponse response;
            try
            {
                response = await exam.GetAsync(context, AccessPath);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "[Assistant] parent control check failed");
                return Unavailable();
            }

            if (response.Status == HttpStatusCode.Forbidden && response.ErrorCode is { } code)
                return Results.Json(new { code }, statusCode: StatusCodes.Status403Forbidden);
            if (!response.IsSuccess || response.Body is not JsonObject body || body["allowed"] is not JsonValue value
                || !value.TryGetValue(out bool allowed))
            {
                logger.LogWarning("[Assistant] parent control check returned {Status}", (int)response.Status);
                return Unavailable();
            }
            var testInProgress = body["testInProgress"] is JsonValue running && running.TryGetValue(out bool r) && r;
            access = (allowed, testInProgress);
            // A running test is not cached: right after finishing it the student asks about it.
            if (!testInProgress)
                cache.Set(key, access, CacheFor);
        }

        if (!access.Allowed)
            return Results.Json(new { code = DisabledCode }, statusCode: StatusCodes.Status403Forbidden);
        return access.TestInProgress
            ? Results.Json(new { code = ClosedDuringTestCode }, statusCode: StatusCodes.Status409Conflict)
            : null;
    }

    private static IResult Unavailable() =>
        Results.Json(new { code = "AssistantUnavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
}
