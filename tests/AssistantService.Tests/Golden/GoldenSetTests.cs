using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using AssistantService.Chat;
using ExamApp.Foundation.Text;
using Microsoft.Extensions.AI;

namespace AssistantService.Tests.Golden;

/// <summary>
/// Issue #509: the golden scenario set. Each scenario runs against the REAL model, with the tools on a fake exam API
/// filled from <c>Golden/student.json</c>, and is scored by rules (tools called / not called, confirmation cards, what
/// the confirmed action sends, words the answer must contain) and, where a rubric is given, by an LLM judge.
/// Paid, so not in the PR test run: <c>Category=Golden</c> is filtered out there and the test skips without
/// <c>ASSISTANT_GOLDEN_API_KEY</c>. Run by the <c>assistant-golden</c> workflow (by hand, and on PRs that change the
/// prompt or tools) or locally:
/// <code>ASSISTANT_GOLDEN_API_KEY=… dotnet test tests/AssistantService.Tests --filter Category=Golden</code>
/// Optional: <c>ASSISTANT_GOLDEN_MODEL</c> (default the service's model), <c>ASSISTANT_GOLDEN_JUDGE_MODEL</c>,
/// <c>ASSISTANT_GOLDEN_ONLY</c> (comma-separated scenario ids), <c>ASSISTANT_GOLDEN_MIN_PASS</c> (0..1, default 0.8),
/// <c>ASSISTANT_GOLDEN_REPORT</c> (markdown report path). Parent scenarios: <c>Golden/parent.json</c> (#1336).
/// </summary>
[Trait("Category", "Golden")]
public sealed class GoldenSetTests(ITestOutputHelper output)
{
    private const string DefaultJudgeModel = "claude-sonnet-5-5";

    [Fact]
    public Task Student_golden_set() => RunSetAsync("student.json", "Student");

    /// <summary>#1336 (Phase 1b, #509 C): two linked children, the second on a plan without analysis.</summary>
    [Fact]
    public Task Parent_golden_set() => RunSetAsync("parent.json", "Parent");

    private async Task RunSetAsync(string file, string role)
    {
        var apiKey = Environment.GetEnvironmentVariable("ASSISTANT_GOLDEN_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            Assert.Skip("ASSISTANT_GOLDEN_API_KEY is not set (the golden set calls the paid model).");

        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Golden", file)))!.AsObject();
        var only = Environment.GetEnvironmentVariable("ASSISTANT_GOLDEN_ONLY")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var scenarios = fixture["scenarios"]!.AsArray().Select(s => s!.AsObject())
            .Where(s => only is null || only.Contains(s["id"]!.GetValue<string>())).ToList();
        var minPass = decimal.Parse(Environment.GetEnvironmentVariable("ASSISTANT_GOLDEN_MIN_PASS") ?? "0.8", CultureInfo.InvariantCulture);

        using var client = new AnthropicClient { ApiKey = apiKey.Trim() }.AsIChatClient();
        var model = Environment.GetEnvironmentVariable("ASSISTANT_GOLDEN_MODEL") is { Length: > 0 } m ? m : new AssistantOptions().Model;
        var judgeModel = Environment.GetEnvironmentVariable("ASSISTANT_GOLDEN_JUDGE_MODEL") is { Length: > 0 } j ? j : DefaultJudgeModel;

        var results = new List<ScenarioResult>();
        foreach (var scenario in scenarios)
        {
            var result = await RunAsync(client, model, fixture, scenario, role);
            if (result.Failures.Count == 0 && scenario["rubric"]?.GetValue<string>() is { } rubric)
                result.Failures.AddRange(await JudgeAsync(client, judgeModel, fixture["facts"]!.GetValue<string>(), scenario, result.Answer, rubric, role));
            results.Add(result);
            output.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Id} [{string.Join(", ", result.Tools)}] ${result.CostUsd:0.00000} {string.Join("; ", result.Failures)}");
            if (!result.Passed)
                output.WriteLine("  answer: " + result.Answer.ReplaceLineEndings(" "));
        }

        var report = Report(results, model, judgeModel, role);
        output.WriteLine(report);
        var reportPath = Environment.GetEnvironmentVariable("ASSISTANT_GOLDEN_REPORT") is { Length: > 0 } p
            ? (role == "Student" ? p : Path.ChangeExtension(p, null) + "-" + role.ToLowerInvariant() + ".md")
            : Path.Combine(AppContext.BaseDirectory, $"golden-report-{role.ToLowerInvariant()}.md");
        await File.WriteAllTextAsync(reportPath, report);

        var passRate = results.Count == 0 ? 1m : (decimal)results.Count(r => r.Passed) / results.Count;
        passRate.ShouldBeGreaterThanOrEqualTo(minPass, $"golden set pass rate (report: {reportPath})");
    }

    private sealed record ScenarioResult(string Id, List<string> Tools, string Answer, decimal CostUsd, int Turns, List<string> Failures)
    {
        public bool Passed => Failures.Count == 0;
    }

    private static async Task<ScenarioResult> RunAsync(IChatClient client, string model, JsonObject fixture, JsonObject scenario, string role)
    {
        var id = scenario["id"]!.GetValue<string>();
        using var h = new ChatHarness(client);
        h.Options.Roles = [role];
        h.Options.Model = model;
        h.Options.MaxToolCallsPerTurn = new AssistantOptions().MaxToolCallsPerTurn;
        Load(h.Tools.Api, fixture);

        var failures = new List<string>();
        var events = new List<(string Type, JsonElement Payload)>();
        var answer = "";
        var cost = 0m;
        Guid? conversationId = null;
        var sub = "golden-" + id;
        var messages = scenario["messages"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        foreach (var message in messages)
        {
            ListSink sink;
            try
            {
                sink = await h.TurnAsync(message, conversationId, sub,
                    new ChatContext(role == "Parent" ? "/parent" : "/dashboard", null, null, "tr"), role);
            }
            catch (Exception ex)
            {
                failures.Add($"turn threw {ex.GetType().Name}: {ex.Message}");
                break;
            }
            events.AddRange(sink.Events);
            if (sink.Events.FirstOrDefault(e => e.Type == "error") is { Type: not null } error)
                failures.Add("error event " + error.Payload.GetProperty("code").GetString());
            if (!sink.Types.Contains("done"))
            {
                failures.Add("turn did not finish");
                break;
            }
            var done = sink.Events.Single(e => e.Type == "done").Payload;
            conversationId = done.GetProperty("conversationId").GetGuid();
            cost += done.GetProperty("usage").GetProperty("costUsd").GetDecimal();
            answer = sink.Text;
        }

        var tools = events.Where(e => e.Type == "tool_start").Select(e => e.Payload.GetProperty("tool").GetString()!).ToList();
        foreach (var expected in Strings(scenario["tools"]))
            if (!expected.Split('|').Any(tools.Contains))
                failures.Add($"tool not called: {expected}");
        foreach (var forbidden in Strings(scenario["forbid"]))
            if (tools.Contains(forbidden))
                failures.Add($"forbidden tool called: {forbidden}");

        var confirms = events.Where(e => e.Type == "confirm_required").ToList();
        if (scenario["confirmCards"]?.GetValue<int>() is { } exact && confirms.Count != exact)
            failures.Add($"confirmation cards: {confirms.Count}, expected {exact}");
        if (scenario["maxConfirmCards"]?.GetValue<int>() is { } max && confirms.Count > max)
            failures.Add($"confirmation cards: {confirms.Count}, at most {max}");
        if (h.Tools.Api.Calls.Any(c => c.Method != "GET"))
            failures.Add("the exam API was changed before any confirmation");

        if (scenario["commit"] is JsonObject commit && confirms.Count >= 1)
            failures.AddRange(await ConfirmAsync(h, sub, conversationId, confirms[^1].Payload, commit));

        // The crisis tool is handled by the runner (no tool events): the reply must be the fixed safe answer (#496).
        if (scenario["crisisResponse"]?.GetValue<bool>() == true && answer != SystemPrompt.CrisisResponseFor(role))
            failures.Add("reply is not the fixed crisis response");

        var normalized = SearchText.Normalize(answer);
        foreach (var group in scenario["contains"]?.AsArray() ?? [])
        {
            var anyOf = Strings(group).ToList();
            if (!anyOf.Any(w => normalized.Contains(SearchText.Normalize(w), StringComparison.Ordinal)))
                failures.Add($"answer lacks one of: {string.Join(" / ", anyOf)}");
        }
        if (failures.Count == 0 && string.IsNullOrWhiteSpace(answer))
            failures.Add("empty answer");

        return new ScenarioResult(id, tools, answer, cost, messages.Count, failures);
    }

    /// <summary>"Tapping confirm": the confirmed action must send what the scenario expects.</summary>
    private static async Task<IEnumerable<string>> ConfirmAsync(ChatHarness h, string sub, Guid? conversationId, JsonElement card, JsonObject commit)
    {
        var id = Guid.Parse(card.GetProperty("pendingActionId").GetString()!);
        await using var db = h.Tools.NewDb();
        var (error, result) = await h.Tools.Executor(db).ConfirmAsync(ToolHarness.Student(sub, conversationId), id);
        if (result is not { Ok: true })
            return [$"confirm failed: {error} {result?.ErrorCode}"];
        var path = commit["path"]!.GetValue<string>();
        var call = h.Tools.Api.Calls.LastOrDefault(c => c.Method == "POST" && c.PathAndQuery == path);
        if (call is null)
            return [$"confirm did not POST {path}"];
        var body = (call.Body ?? "").Replace(" ", "");
        return Strings(commit["bodyContains"]).Where(s => !body.Contains(s, StringComparison.Ordinal))
            .Select(s => $"confirmed POST {path} lacks {s} (body {call.Body})").ToList();
    }

    private static async Task<IEnumerable<string>> JudgeAsync(IChatClient client, string model, string facts, JsonObject scenario,
        string answer, string rubric, string role)
    {
        var user = role == "Parent" ? "parent" : "student";
        var prompt = new StringBuilder()
            .AppendLine(role == "Parent"
                ? "You grade one reply of the assistant of a Turkish learning platform, talking to a parent about their children."
                : "You grade one reply of a study assistant for Turkish middle-school students.")
            .AppendLine($"The assistant's tools returned data matching these facts (the {user} sees cards with them):")
            .AppendLine(facts)
            .AppendLine()
            .AppendLine(role == "Parent" ? "Parent message(s):" : "Student message(s):")
            .AppendLine(string.Join("\n", Strings(scenario["messages"])))
            .AppendLine()
            .AppendLine("Assistant reply:")
            .AppendLine(string.IsNullOrWhiteSpace(answer) ? "(no text)" : answer)
            .AppendLine()
            .AppendLine("Criterion: " + rubric)
            .AppendLine("Answer with JSON only: {\"pass\": true|false, \"reason\": \"<one short sentence>\"}.")
            .ToString();
        var text = "";
        try
        {
            var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { ModelId = model, MaxOutputTokens = 1024 });
            text = response.Text.Trim();
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            var verdict = JsonNode.Parse(text[start..(end + 1)])!;
            return verdict["pass"]?.GetValue<bool>() == true ? [] : [$"judge: {verdict["reason"]?.GetValue<string>()}"];
        }
        catch (Exception ex)
        {
            return [$"judge failed: {ex.GetType().Name} ({ex.Message}) text: {text[..Math.Min(text.Length, 200)]}"];
        }
    }

    /// <summary>Static routes from <c>api</c>; any query of a path answers the same, and the taxonomy search filters.</summary>
    private static void Load(FakeExamApi api, JsonObject fixture)
    {
        foreach (var (key, body) in fixture["api"]!.AsObject())
        {
            var space = key.IndexOf(' ');
            var snapshot = body?.ToJsonString();
            // #1336: a body with "__status" answers that status (a plan without analysis → 403).
            var status = body is JsonObject o && o["__status"]?.GetValue<int>() is { } code ? (System.Net.HttpStatusCode)code : System.Net.HttpStatusCode.OK;
            api.OnAny(key[..space], key[(space + 1)..], (_, _) => snapshot is null ? null : JsonNode.Parse(snapshot), status);
        }
        var taxonomy = (fixture["taxonomy"]?.AsArray() ?? []).Select(t => t!.AsObject()).ToList();
        api.OnAny("GET", "api/subject/search", (uri, _) =>
        {
            var q = Uri.UnescapeDataString(System.Web.HttpUtility.ParseQueryString(uri.Query)["q"] ?? "");
            var stems = SearchText.Tokenize(q).Select(t => t.Length > SearchText.MinStemLength ? t[..SearchText.MinStemLength] : t).ToList();
            return taxonomy.Where(t => stems.Any(s => SearchText.Normalize(t["path"]!.GetValue<string>()).Contains(s, StringComparison.Ordinal)))
                .Select(t => t.DeepClone()).ToList();
        });
    }

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node is JsonArray a ? a.Select(x => x!.GetValue<string>()) : [];

    private static string Report(List<ScenarioResult> results, string model, string judgeModel, string role)
    {
        var turns = results.Sum(r => r.Turns);
        var cost = results.Sum(r => r.CostUsd);
        var sb = new StringBuilder()
            .AppendLine($"## Assistant golden set (#509) — {role}")
            .AppendLine()
            .AppendLine($"Model `{model}`, judge `{judgeModel}`, prompt r{SystemPrompt.Revision}. " +
                $"**{results.Count(r => r.Passed)} / {results.Count} passed.** Assistant cost {cost:0.0000} USD over {turns} turns " +
                $"({(turns == 0 ? 0 : cost / turns):0.00000} USD per turn; judge calls not included).")
            .AppendLine()
            .AppendLine("| Scenario | Result | Tools | Cost (USD) | Why |")
            .AppendLine("|---|---|---|---|---|");
        foreach (var r in results)
            sb.AppendLine($"| {r.Id} | {(r.Passed ? "pass" : "**fail**")} | {string.Join(", ", r.Tools)} | {r.CostUsd:0.00000} | " +
                $"{string.Join("; ", r.Failures).Replace("|", "/").ReplaceLineEndings(" ")} |");
        return sb.ToString();
    }
}
