using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExamApp.Foundation.Tests.Contracts;

/// <summary>
/// issue #505 (#279 permission model): <c>StudentProgressResetEvent</c> exchange. exam_outbox_pub is the only publisher
/// (configure + write, no read); assistant_service is the only consumer (configure + read, no write — it cannot forge a
/// reset) and touches no other event exchange; badge_service and the other users cannot touch it. Deploy scripts agree.
/// </summary>
public class StudentProgressResetRabbitPermissionsTests
{
    private const string Exchange = "ExamApp.Foundation.Contracts:StudentProgressResetEvent";

    private static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "rabbitmq", "definitions.json")))
            dir = dir.Parent;
        dir.ShouldNotBeNull("repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }

    private static Dictionary<string, JsonElement> Permissions()
    {
        using var doc = JsonDocument.Parse(RepoFile("rabbitmq/definitions.json"));
        return doc.RootElement.GetProperty("permissions").EnumerateArray()
            .Select(p => p.Clone())
            .ToDictionary(p => p.GetProperty("user").GetString()!);
    }

    private static bool Grants(Dictionary<string, JsonElement> perms, string user, string field, string name = Exchange)
        => Regex.IsMatch(name, perms[user].GetProperty(field).GetString()!);

    [Fact]
    public void Exam_outbox_publisher_may_declare_and_publish_but_never_read()
    {
        var perms = Permissions();
        Grants(perms, "exam_outbox_pub", "configure").ShouldBeTrue();
        Grants(perms, "exam_outbox_pub", "write").ShouldBeTrue();
        Grants(perms, "exam_outbox_pub", "read").ShouldBeFalse();
    }

    [Fact]
    public void Assistant_service_may_consume_on_its_own_queue_but_cannot_forge_or_read_other_events()
    {
        var perms = Permissions();
        Grants(perms, "assistant_service", "configure").ShouldBeTrue();
        Grants(perms, "assistant_service", "read").ShouldBeTrue();
        Grants(perms, "assistant_service", "write").ShouldBeFalse();
        Grants(perms, "assistant_service", "read", "assistant-service").ShouldBeTrue();
        Grants(perms, "assistant_service", "write", "assistant-service_error").ShouldBeTrue();
        Grants(perms, "assistant_service", "read", "ExamApp.Foundation.Contracts:AnswerSubmittedEvent").ShouldBeFalse();
        Grants(perms, "assistant_service", "read", "badge-service").ShouldBeFalse();
    }

    [Theory]
    [InlineData("badge_service")]
    [InlineData("exam_api")]
    [InlineData("auth_api")]
    [InlineData("identity_outbox_pub")]
    [InlineData("badge_outbox_pub")]
    public void Other_users_have_no_access(string user)
    {
        var perms = Permissions();
        Grants(perms, user, "configure").ShouldBeFalse($"{user} configure");
        Grants(perms, user, "write").ShouldBeFalse($"{user} write");
        Grants(perms, user, "read").ShouldBeFalse($"{user} read");
    }

    [Theory]
    [InlineData("deploy/scripts/rabbitmq-init.sh")]
    [InlineData("deploy/gcp/k8s/stateful-services.yaml")]
    public void Deploy_scripts_create_the_assistant_user_and_list_the_event(string relativePath)
    {
        var text = RepoFile(relativePath);
        var lines = text.Split('\n');
        lines.Single(l => l.Contains("EXAM_OUTBOX_EVENTS=\"")).ShouldContain("|StudentProgressResetEvent");
        lines.Single(l => l.Contains("BADGE_SERVICE_EVENTS=\"")).ShouldNotContain("StudentProgressResetEvent");
        lines.Single(l => l.Contains("ASSISTANT_SERVICE_EVENTS=\"")).ShouldContain("\"StudentProgressResetEvent\"");
        text.ShouldContain("create_user assistant_service \"${RABBITMQ_ASSISTANT_SERVICE_PASSWORD}\"");
    }
}
