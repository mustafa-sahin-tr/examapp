using System.Text.Json;
using System.Text.Json.Nodes;
using static AssistantService.Tools.ToolJson;

namespace AssistantService.Tools;

/// <summary>
/// Issue #503: screens the assistant may open, per role. The model names a screen key; the route is built here, so it can
/// never navigate to an arbitrary path (and never to /testsolve: the assistant is closed during a test).
/// </summary>
public static class UiScreens
{
    private sealed record Screen(string Route, bool NeedsId, IReadOnlyCollection<string> Roles);

    private static readonly Dictionary<string, Screen> Screens = new(StringComparer.Ordinal)
    {
        ["dashboard"] = new("/dashboard", false, Tools.Roles.StudentOnly),
        ["practice"] = new("/practice", false, Tools.Roles.StudentOnly),
        ["practiceSession"] = new("/practice?session={id}", true, Tools.Roles.StudentOnly),
        ["mistakes"] = new("/mistakes", false, Tools.Roles.StudentOnly),
        ["savedQuestions"] = new("/saved-questions", false, Tools.Roles.StudentOnly),
        ["profile"] = new("/student-profile", false, Tools.Roles.StudentOnly),
        ["badges"] = new("/badges", false, Tools.Roles.StudentOnly),
        ["homework"] = new("/my-homework", false, Tools.Roles.StudentOnly),
        ["mockExams"] = new("/my-mock-exams", false, Tools.Roles.StudentOnly),
        ["worksheet"] = new("/test/{id}", true, Tools.Roles.StudentOnly),
        // #515: study programs and the calendar.
        ["programs"] = new("/programs", false, Tools.Roles.StudentOnly),
        ["program"] = new("/programs/{id}/detail", true, Tools.Roles.StudentOnly),
        ["calendar"] = new("/my-calendar", false, Tools.Roles.StudentOnly),

        // #1336: parent screens (the dashboard picks a child with ?child=<studentId>).
        ["parentDashboard"] = new("/parent", false, Tools.Roles.ParentOnly),
        ["childDashboard"] = new("/parent?child={id}", true, Tools.Roles.ParentOnly),
        ["myChildren"] = new("/my-children", false, Tools.Roles.ParentOnly),
        // #511: tutor search on the child's behalf (the parent books there).
        ["childTutors"] = new("/my-children/{id}/tutors", true, Tools.Roles.ParentOnly),
    };

    /// <summary>All keys; <see cref="Route"/> checks the caller's role again on every call.</summary>
    public static IEnumerable<string> AllKeys => Screens.Keys;

    public static IEnumerable<string> KeysFor(IReadOnlyCollection<string> roles) =>
        Screens.Where(s => s.Value.Roles.Any(roles.Contains)).Select(s => s.Key);

    public static string? Route(string key, int? id, IReadOnlyCollection<string> roles)
    {
        if (!Screens.TryGetValue(key, out var screen) || !screen.Roles.Any(roles.Contains))
            return null;
        if (screen.NeedsId != id is not null || id is <= 0)
            return null;
        return screen.Route.Replace("{id}", id?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Issue #515: the test screen, only after the student confirmed start_test (never a screen key the model can ask for).
    /// </summary>
    public static JsonObject NavigateToTest(int instanceId) => new()
    {
        ["type"] = "navigate",
        ["screen"] = "testSolve",
        ["route"] = "/testsolve/" + instanceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>A navigate action built by server code (write tools after commit).</summary>
    public static JsonObject Navigate(string key, int? id = null) => new()
    {
        ["type"] = "navigate",
        ["screen"] = key,
        ["route"] = Screens[key].Route.Replace("{id}", id?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    };
}

public sealed class UiOpenScreenTool : IAssistantTool
{
    public string Name => "ui_open_screen";
    public string Description => "Open a screen of the app for the user. Only screens of the user's role; practiceSession, worksheet and childDashboard (studentId) need an id. Students: dashboard, practice, practiceSession, mistakes, savedQuestions, profile, badges, homework, mockExams, worksheet. Parents: parentDashboard, childDashboard, myChildren.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentAndParent;
    public JsonObject InputSchema => Schema(
        ("screen", StringProp("Screen key.", 40, UiScreens.AllKeys), true),
        ("id", IntProp("Session or worksheet id when the screen needs one."), false));

    public Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var key = String(args, "screen") ?? "";
        var id = Int(args, "id");
        if (UiScreens.Route(key, id, context.Roles) is null)
            return Task.FromResult(ToolResult.Error("ScreenNotAllowed", "Unknown screen, not allowed for this role, or wrong id."));
        return Task.FromResult(ToolResult.Success(new JsonObject { ["opened"] = key }, uiActions: [UiScreens.Navigate(key, id)]));
    }
}

public sealed class UiSuggestActionsTool : IAssistantTool
{
    public const int MaxSuggestions = 4;
    public const int MaxLabelLength = 40;
    public const int MaxPromptLength = 200;

    public string Name => "ui_suggest_actions";
    public string Description => "Show up to 4 follow-up chips the user can tap; each sends its prompt as the user's next message.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentAndParent;
    public JsonObject InputSchema => Schema(("suggestions", new JsonObject
    {
        ["type"] = "array",
        ["maxItems"] = MaxSuggestions,
        ["items"] = Schema(("label", StringProp("Short chip text.", MaxLabelLength), true),
            ("prompt", StringProp("Message sent when tapped.", MaxPromptLength), true)),
    }, true));

    public Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var chips = new JsonArray();
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("suggestions", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in list.EnumerateArray().Take(MaxSuggestions))
            {
                var label = Clip(String(s, "label"), MaxLabelLength);
                var prompt = Clip(String(s, "prompt"), MaxPromptLength);
                if (label is not null && prompt is not null)
                    chips.Add(new JsonObject { ["label"] = label, ["prompt"] = prompt });
            }
        }
        if (chips.Count == 0)
            return Task.FromResult(ToolResult.Error("InvalidArguments", "No valid suggestions."));
        return Task.FromResult(ToolResult.Success(new JsonObject { ["shown"] = chips.Count },
            uiActions: [new JsonObject { ["type"] = "suggestions", ["items"] = chips }]));
    }

    private static string? Clip(string? text, int max)
    {
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
    }
}
