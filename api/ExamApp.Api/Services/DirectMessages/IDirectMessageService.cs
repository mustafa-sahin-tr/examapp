using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.DirectMessages;

namespace ExamApp.Api.Services.DirectMessages;

public enum DirectMessageActorKind
{
    Student = 0,
    Teacher = 1
}

/// <summary>İstek sahibi: JWT ile doğrulanmış etkin rol (#277) + exam/auth user id + Keycloak sub (audit).</summary>
public sealed record DirectMessageActor(int UserId, string KeycloakId, DirectMessageActorKind Kind);

/// <summary>
/// issue #106 (dilim a): öğrenci ↔ öğretmen doğrudan mesajlaşma. Yetki kuralı <see cref="IDirectMessagePolicy"/>'de;
/// bu servis konuşma/mesaj/engel/şikayet iş kurallarını ve IDOR kontrollerini (konuşmanın tarafı mı) uygular.
/// Bildirim yok (dilim b).
/// </summary>
public interface IDirectMessageService
{
    /// <summary>Öğrenci: mesajlaşabileceği öğretmenler (A ∪ B, tekrarsız, ilişki nedeni; ad araması + sayfalama).</summary>
    Task<MessageableTeacherPageResultDto> GetMessageableTeachersAsync(DirectMessageActor actor, MessageableTeacherQueryDto query, CancellationToken ct = default);

    /// <summary>Öğrenci: öğretmene mesaj — konuşma yoksa ilk mesaj oluşturur (çift başına tek). CanMessage değilse nötr 403.</summary>
    Task<SendDirectMessageResultDto> SendToTeacherAsync(DirectMessageActor actor, int teacherId, SendDirectMessageDto dto, CancellationToken ct = default);

    /// <summary>
    /// Mevcut konuşmaya mesaj. Öğrenci: CanMessage. Öğretmen (cevap): konuşmanın öğretmeni + ilişki sürüyor (engel cevabı
    /// kapatmaz). Taraf değilse 404.
    /// </summary>
    Task<SendDirectMessageResultDto> SendToConversationAsync(DirectMessageActor actor, int conversationId, SendDirectMessageDto dto, CancellationToken ct = default);

    /// <summary>Öğrenci: kendi konuşmaları (son mesaj önce, sayfalı).</summary>
    Task<ConversationPageResultDto> GetStudentConversationsAsync(DirectMessageActor actor, DirectMessagePageQueryDto query, CancellationToken ct = default);

    /// <summary>Öğretmen: gelen kutusu (all / unread / blocked filtresi, sayfalı).</summary>
    Task<ConversationPageResultDto> GetTeacherInboxAsync(DirectMessageActor actor, TeacherInboxQueryDto query, CancellationToken ct = default);

    /// <summary>Konuşmanın mesajları (taraflar; ilişki bitse de okunur). Yan etkisizdir (okundu işaretlemez; bkz. MarkReadAsync).</summary>
    Task<ConversationMessagesResultDto> GetMessagesAsync(DirectMessageActor actor, int conversationId, DirectMessageHistoryQueryDto query, CancellationToken ct = default);

    /// <summary>Taraf: karşı taraftan gelen, Id &lt;= upToMessageId okunmamış mesajları okundu işaretler (idempotent).</summary>
    Task<MarkDirectMessagesReadResultDto> MarkReadAsync(DirectMessageActor actor, int conversationId, MarkDirectMessagesReadDto dto, CancellationToken ct = default);

    /// <summary>Öğretmen: konuşmasındaki öğrenciyi engelle / engeli kaldır (idempotent, audit'li).</summary>
    Task<DirectMessageBlockResultDto> SetBlockedAsync(DirectMessageActor actor, int conversationId, bool blocked, CancellationToken ct = default);

    /// <summary>Konuşmanın tarafı: konuşmayı ya da karşı tarafın mesajını şikayet eder (idempotent).</summary>
    Task<DirectMessageReportResultDto> ReportAsync(DirectMessageActor actor, int conversationId, ReportDirectMessageDto dto, CancellationToken ct = default);

    /// <summary>Admin: Open şikayetler (salt okunur, en yeni önce, sayfalı).</summary>
    Task<DirectMessageReportPageResultDto> GetOpenReportsAsync(DirectMessagePageQueryDto query, CancellationToken ct = default);
}
