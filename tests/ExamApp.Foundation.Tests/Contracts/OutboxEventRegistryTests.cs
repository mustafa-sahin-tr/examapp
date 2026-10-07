using ExamApp.Foundation.Contracts;

namespace ExamApp.Foundation.Tests.Contracts;

public class OutboxEventRegistryTests
{
    [Fact]
    public void NameFor_generic_returns_the_full_name()
        => OutboxEventRegistry.NameFor<QuestionCreatedEvent>()
            .ShouldBe("ExamApp.Foundation.Contracts.QuestionCreatedEvent");

    [Fact]
    public void NameFor_type_returns_the_full_name()
        => OutboxEventRegistry.NameFor(typeof(AnswerSubmittedEvent))
            .ShouldBe("ExamApp.Foundation.Contracts.AnswerSubmittedEvent");

    [Fact]
    public void Resolve_round_trips_a_name_written_by_NameFor()
    {
        var name = OutboxEventRegistry.NameFor<AnswerSubmittedEvent>();
        OutboxEventRegistry.Resolve(name).ShouldBe(typeof(AnswerSubmittedEvent));
    }

    [Fact]
    public void Resolve_accepts_a_legacy_assembly_qualified_name()
    {
        var legacy = typeof(QuestionCreatedEvent).AssemblyQualifiedName!;
        OutboxEventRegistry.Resolve(legacy).ShouldBe(typeof(QuestionCreatedEvent));
    }

    [Fact]
    public void Resolve_accepts_a_legacy_name_with_only_the_assembly_short_name()
        => OutboxEventRegistry.Resolve("ExamApp.Foundation.Contracts.QuestionCreatedEvent, ExamApp.Foundation")
            .ShouldBe(typeof(QuestionCreatedEvent));

    [Fact]
    public void Resolve_knows_the_worksheet_comment_notification_events_issue_105()
    {
        OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<WorksheetCommentCreatedEvent>())
            .ShouldBe(typeof(WorksheetCommentCreatedEvent));
        OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<WorksheetCommentRepliedEvent>())
            .ShouldBe(typeof(WorksheetCommentRepliedEvent));
        OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<WorksheetCommentHiddenEvent>())
            .ShouldBe(typeof(WorksheetCommentHiddenEvent)); // issue #326 D4
    }

    [Fact]
    public void Resolve_knows_the_direct_message_events_issue_106()
    {
        OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<DirectMessageSentEvent>())
            .ShouldBe(typeof(DirectMessageSentEvent));
        OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<DirectMessageReportedEvent>())
            .ShouldBe(typeof(DirectMessageReportedEvent));
    }

    [Fact]
    public void Resolve_knows_the_booking_teacher_unavailable_event_issue_298()
        => OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<BookingTeacherUnavailableEvent>())
            .ShouldBe(typeof(BookingTeacherUnavailableEvent));

    [Fact]
    public void Resolve_knows_the_parent_link_events_issue_419()
    {
        OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<ParentLinkedEvent>()).ShouldBe(typeof(ParentLinkedEvent));
        OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<ParentUnlinkedEvent>()).ShouldBe(typeof(ParentUnlinkedEvent));
    }

    /// <summary>
    /// issue #419: exam-outbox-publisher (exam_outbox_pub) yeni exchange'leri declare + publish edebilmeli — üç izin kaynağı
    /// (rabbitmq/definitions.json, deploy/scripts/rabbitmq-init.sh, deploy/gcp/k8s/stateful-services.yaml) aynı listeyi taşır.
    /// </summary>
    [Theory]
    [InlineData("rabbitmq/definitions.json")]
    [InlineData("deploy/scripts/rabbitmq-init.sh")]
    [InlineData("deploy/gcp/k8s/stateful-services.yaml")]
    public void Exam_outbox_publisher_may_publish_the_parent_link_events_issue_419(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "rabbitmq", "definitions.json")))
            dir = dir.Parent;
        dir.ShouldNotBeNull("repo kökü bulunamadı");

        var text = File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
        foreach (var name in new[] { nameof(ParentLinkedEvent), nameof(ParentUnlinkedEvent) })
            text.ShouldContain("|" + name, Case.Sensitive, $"{relativePath} exam_outbox_pub listesinde {name} yok");
    }

    [Fact]
    public void Resolve_knows_the_parent_notification_events_issue_423()
    {
        OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<ParentHomeworkOverdueEvent>()).ShouldBe(typeof(ParentHomeworkOverdueEvent));
        OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<ParentChildTestCompletedEvent>()).ShouldBe(typeof(ParentChildTestCompletedEvent));
    }

    private static string[] ParentNotificationEvents => new[]
    {
        nameof(ParentLinkedEvent), nameof(ParentUnlinkedEvent),
        nameof(ParentHomeworkOverdueEvent), nameof(ParentChildTestCompletedEvent)
    };

    private static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "rabbitmq", "definitions.json")))
            dir = dir.Parent;
        dir.ShouldNotBeNull("repo kökü bulunamadı");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }

    /// <summary>
    /// issue #423: dört izin kaynağından biri (definitions.json) gerçek regex olarak değerlendirilir — exam_outbox_pub dört
    /// exchange'i declare + publish eder ve hiçbirini okuyamaz; badge_service dört exchange'i declare + bind/consume eder ama
    /// onlara ASLA write (publish) hakkı almaz (sahte event riski, #279).
    /// </summary>
    [Fact]
    public void Definitions_json_grants_parent_notification_events_to_the_right_users_issue_423()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(RepoFile("rabbitmq/definitions.json"));
        var perms = doc.RootElement.GetProperty("permissions").EnumerateArray()
            .ToDictionary(p => p.GetProperty("user").GetString()!);
        bool Match(string user, string field, string exchange)
            => System.Text.RegularExpressions.Regex.IsMatch(
                $"ExamApp.Foundation.Contracts:{exchange}", perms[user].GetProperty(field).GetString()!);

        foreach (var name in ParentNotificationEvents)
        {
            Match("exam_outbox_pub", "configure", name).ShouldBeTrue($"exam_outbox_pub configure {name}");
            Match("exam_outbox_pub", "write", name).ShouldBeTrue($"exam_outbox_pub write {name}");
            Match("exam_outbox_pub", "read", name).ShouldBeFalse($"exam_outbox_pub read {name}");

            Match("badge_service", "configure", name).ShouldBeTrue($"badge_service configure {name}");
            Match("badge_service", "read", name).ShouldBeTrue($"badge_service read {name}");
            Match("badge_service", "write", name).ShouldBeFalse($"badge_service write {name} (consumer publish edemez)");

            // Başka yayıncılar/tüketiciler bu exchange'lere dokunamaz.
            foreach (var other in new[] { "identity_outbox_pub", "badge_outbox_pub", "exam_api" })
            {
                Match(other, "write", name).ShouldBeFalse($"{other} write {name}");
            }
        }
    }

    /// <summary>issue #423: deploy betikleri (docker-compose dışı kurulumlar) aynı listeleri taşır: yayıncı listesi + BadgeService listesi.</summary>
    [Theory]
    [InlineData("deploy/scripts/rabbitmq-init.sh")]
    [InlineData("deploy/gcp/k8s/stateful-services.yaml")]
    public void Deploy_scripts_list_parent_notification_events_for_publisher_and_badge_service_issue_423(string relativePath)
    {
        var lines = RepoFile(relativePath).Split('\n');
        var exam = lines.Single(l => l.Contains("EXAM_OUTBOX_EVENTS=\""));
        var badge = lines.Single(l => l.Contains("BADGE_SERVICE_EVENTS=\""));
        foreach (var name in ParentNotificationEvents)
        {
            exam.ShouldContain("|" + name, Case.Sensitive, $"{relativePath} EXAM_OUTBOX_EVENTS {name}");
            badge.ShouldContain("|" + name, Case.Sensitive, $"{relativePath} BADGE_SERVICE_EVENTS {name}");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Not.A.Real.Type")]
    [InlineData("Not.A.Real.Type, Some.Assembly, Version=1.0.0.0")]
    public void Resolve_returns_null_for_an_unknown_or_empty_type(string? stored)
        => OutboxEventRegistry.Resolve(stored!).ShouldBeNull();
}
