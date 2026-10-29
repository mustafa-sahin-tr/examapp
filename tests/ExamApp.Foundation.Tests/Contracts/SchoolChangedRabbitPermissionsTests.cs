using System.Text.RegularExpressions;
using System.Text.Json;
using ExamApp.Foundation.Contracts;

namespace ExamApp.Foundation.Tests.Contracts;

/// <summary>
/// issue #542 (#279 izin modeli): <see cref="SchoolChangedEvent"/> exchange'i için RabbitMQ izinleri. exam_outbox_pub tek
/// yayıncıdır (configure + write, read yok); badge_service tüketicidir (configure + read, write YOK — sahte okul değişimi
/// event'i yayınlayamaz); exam_api ve diğer kullanıcılar bu exchange'e hiç dokunamaz. Deploy betikleri aynı listeleri taşır.
/// </summary>
public class SchoolChangedRabbitPermissionsTests
{
    private const string Exchange = "ExamApp.Foundation.Contracts:SchoolChangedEvent";

    private static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "rabbitmq", "definitions.json")))
            dir = dir.Parent;
        dir.ShouldNotBeNull("repo kökü bulunamadı");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }

    private static Dictionary<string, System.Text.Json.JsonElement> Permissions()
    {
        using var doc = JsonDocument.Parse(RepoFile("rabbitmq/definitions.json"));
        return doc.RootElement.GetProperty("permissions").EnumerateArray()
            .Select(p => p.Clone())
            .ToDictionary(p => p.GetProperty("user").GetString()!);
    }

    private static bool Grants(Dictionary<string, JsonElement> perms, string user, string field)
        => Regex.IsMatch(Exchange, perms[user].GetProperty(field).GetString()!);

    [Fact]
    public void Exam_outbox_publisher_may_declare_and_publish_school_changed_but_never_read_it()
    {
        var perms = Permissions();
        Grants(perms, "exam_outbox_pub", "configure").ShouldBeTrue();
        Grants(perms, "exam_outbox_pub", "write").ShouldBeTrue();
        Grants(perms, "exam_outbox_pub", "read").ShouldBeFalse();
    }

    [Fact]
    public void Badge_service_may_declare_and_consume_school_changed_but_cannot_forge_it()
    {
        var perms = Permissions();
        Grants(perms, "badge_service", "configure").ShouldBeTrue();
        Grants(perms, "badge_service", "read").ShouldBeTrue();
        Grants(perms, "badge_service", "write").ShouldBeFalse();
    }

    [Theory]
    [InlineData("exam_api")]
    [InlineData("identity_outbox_pub")]
    [InlineData("badge_outbox_pub")]
    public void Other_users_have_no_access_to_school_changed(string user)
    {
        var perms = Permissions();
        Grants(perms, user, "configure").ShouldBeFalse($"{user} configure");
        Grants(perms, user, "write").ShouldBeFalse($"{user} write");
        Grants(perms, user, "read").ShouldBeFalse($"{user} read");
    }

    [Theory]
    [InlineData("rabbitmq/definitions.json")]
    [InlineData("deploy/scripts/rabbitmq-init.sh")]
    [InlineData("deploy/gcp/k8s/stateful-services.yaml")]
    public void Deploy_sources_list_school_changed_for_publisher_and_badge_service(string relativePath)
    {
        var lines = RepoFile(relativePath).Split('\n');
        if (relativePath.EndsWith("definitions.json"))
        {
            // JSON kaynağı: iki izin satırında da olmalı (yukarıdaki testler regex'i zaten değerlendirir).
            RepoFile(relativePath).ShouldContain("|SchoolChangedEvent");
            return;
        }

        var exam = lines.Single(l => l.Contains("EXAM_OUTBOX_EVENTS=\""));
        var badge = lines.Single(l => l.Contains("BADGE_SERVICE_EVENTS=\""));
        exam.ShouldContain("|SchoolChangedEvent", Case.Sensitive, $"{relativePath} EXAM_OUTBOX_EVENTS");
        badge.ShouldContain("|SchoolChangedEvent", Case.Sensitive, $"{relativePath} BADGE_SERVICE_EVENTS");
    }
}
