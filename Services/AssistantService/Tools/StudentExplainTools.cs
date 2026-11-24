using System.Text.Json;
using System.Text.Json.Nodes;
using AssistantService.Chat;
using Microsoft.Extensions.Options;
using static AssistantService.Tools.ToolJson;

namespace AssistantService.Tools;

/// <summary>
/// Issue #510 (Phase 2): explaining a question the student already answered. Decision (2026-11-24): only answered questions
/// (finished test or answered practice question), no hints for unanswered ones, <see cref="AssistantOptions.ExplanationsPerDay"/>
/// (20) per student per day. The exam API refuses questions of a running or unfinished assigned test (403 QuestionInOpenTest).
/// </summary>
public sealed class ExplainQuestionTool(DownstreamClient api, ICounterStore counters, IOptionsMonitor<AssistantOptions> options,
    TimeProvider clock) : IAssistantTool
{
    public const string QuotaExceeded = "ExplainQuotaExceeded";
    private static readonly TimeSpan Window = TimeSpan.FromHours(25);

    public string Name => "explain_question";
    public string Description =>
        "Explain one question the student already answered (from get_my_recent_mistakes or a finished test): returns the question " +
        "image and text, the student's choice, the correct choice and the topic. Refused for questions not answered yet or in a test still open.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("questionId", IntProp("Question id."), true));

    internal string Key(string sub) => $"assistant:explain:{sub}:{clock.GetUtcNow():yyyyMMdd}";

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (Int(args, "questionId") is not { } questionId)
            return ToolResult.Error("InvalidArguments", "questionId is required");
        var limit = options.CurrentValue.ExplanationsPerDay;
        if (await counters.GetAsync(Key(context.UserSub)) >= limit)
            return ToolResult.Error(QuotaExceeded, $"Daily limit of {limit} explanations reached; it resets tomorrow.");

        var response = await api.GetAsync(context, $"api/student/me/questions/{questionId}/explain-context");
        if (!response.IsSuccess)
            return response.ToError();
        var body = response.Body;

        var data = Pick.Only(body, "questionId", "source", "answeredAt", "outcome", "subjectName", "topicName", "subTopicNames",
            "interactionType", "choiceCount", "studentChoice", "correctChoice");
        // Question text is written by teachers / imported from books: data, not instructions.
        data["text"] = Untrusted(Pick.Str(body, "text"));

        IReadOnlyList<ToolImage>? images = null;
        if (Pick.Get(body, "image") is JsonObject image && Pick.Str(image, "base64") is { Length: > 0 } base64)
        {
            try
            {
                images = [new ToolImage(Convert.FromBase64String(base64), Pick.Str(image, "mediaType") ?? "image/jpeg")];
            }
            catch (FormatException)
            {
                images = null;
            }
        }
        data["hasImage"] = images is not null;

        var (used, _) = await counters.AddAsync(Key(context.UserSub), 1, Window);
        data["explanationsLeftToday"] = (int)Math.Max(0, limit - used);
        return ToolResult.Success(data, images: images);
    }
}

/// <summary>Issue #510: the student's recent wrong answers (finished tests and practice), one row per question.</summary>
public sealed class GetMyRecentMistakesTool(DownstreamClient api) : IAssistantTool
{
    public const int DefaultTake = 10;

    public string Name => "get_my_recent_mistakes";
    public string Description =>
        "The student's recent wrong answers in finished tests and practice, newest first: questionId, subject, topic, how often wrong " +
        "and whether answered correctly later. Use for 'son yanlışlarım', then explain_question for one of them.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("days", IntProp("Look-back window in days.", 1, 90), false),
        ("subjectId", IntProp("Limit to one subject."), false),
        ("topicId", IntProp("Limit to one topic."), false),
        ("take", IntProp("How many questions.", 1, 20), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var days = Math.Clamp(Int(args, "days") ?? 30, 1, 90);
        var take = Math.Clamp(Int(args, "take") ?? DefaultTake, 1, 20);
        var path = $"api/student/me/mistakes?days={days}&take={take}";
        if (Int(args, "subjectId") is { } subjectId)
            path += $"&subjectId={subjectId}";
        if (Int(args, "topicId") is { } topicId)
            path += $"&topicId={topicId}";
        var response = await api.GetAsync(context, path);
        if (!response.IsSuccess)
            return response.ToError();
        var items = Pick.Items(response.Body).Take(take).Select(i => (JsonNode)Pick.Only(i, "questionId", "subjectName", "topicName",
            "subTopicNames", "lastWrongAt", "wrongCount", "answeredCorrectlyLater")).ToArray();
        return ToolResult.Success(new JsonObject { ["days"] = days, ["mistakes"] = new JsonArray(items) });
    }
}
