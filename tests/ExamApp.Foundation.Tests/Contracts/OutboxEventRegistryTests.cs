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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Not.A.Real.Type")]
    [InlineData("Not.A.Real.Type, Some.Assembly, Version=1.0.0.0")]
    public void Resolve_returns_null_for_an_unknown_or_empty_type(string? stored)
        => OutboxEventRegistry.Resolve(stored!).ShouldBeNull();
}
