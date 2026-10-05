using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>Doğrudan mesaj şikayet nedeni (issue #106). DB'de string; JSON <c>"spam" | "abuse" | "personalInfo" | "other"</c>.</summary>
public enum DirectMessageReportReason
{
    Spam = 1,
    Abuse = 2,
    PersonalInfo = 3,
    Other = 4
}

/// <summary>Şikayet durumu (issue #106). MVP'de yalnız <see cref="Open"/> yazılır; inceleme ekranı kapsam dışı.</summary>
public enum DirectMessageReportStatus
{
    Open = 0,
    Reviewed = 1
}

/// <summary>
/// issue #106: konuşmanın tarafı olan kullanıcının bir konuşmayı ya da konuşmadaki bir mesajı şikayet etmesi. Mesaj içeriği
/// kopyalanmaz, Id ile referanslanır (admin ilgili mesajı görür). Aynı kullanıcı aynı mesajı (mesajsız şikayette aynı
/// konuşmayı) bir kez şikayet eder — iki filtreli unique index; tekrar istek mevcut kaydı döner. Otomatik yaptırım yok.
/// </summary>
public class DirectMessageReport : BaseEntity
{
    public const int NoteMaxLength = 500;

    [Key]
    public int Id { get; set; }

    public int ConversationId { get; set; }

    [ForeignKey(nameof(ConversationId))]
    public Conversation Conversation { get; set; } = null!;

    /// <summary>Şikayet edilen mesaj; null = konuşmanın tamamı.</summary>
    public int? MessageId { get; set; }

    [ForeignKey(nameof(MessageId))]
    public DirectMessage? Message { get; set; }

    /// <summary>Şikayet edenin exam/auth user id'si.</summary>
    public int ReporterUserId { get; set; }

    public DirectMessageSenderRole ReporterRole { get; set; }

    public DirectMessageReportReason Reason { get; set; }

    /// <summary>İsteğe bağlı not (düz metin, ≤ <see cref="NoteMaxLength"/>).</summary>
    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    public DirectMessageReportStatus Status { get; set; } = DirectMessageReportStatus.Open;
}
