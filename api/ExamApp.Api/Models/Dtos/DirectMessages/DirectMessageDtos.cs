using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos.DirectMessages;

// issue #106 (dilim a): öğrenci ↔ öğretmen doğrudan mesajlaşma sözleşmesi. JSON camelCase (ASP.NET varsayılanı).

/// <summary>Sayfa numaralı liste sorgusu (page 1..10000'e, pageSize 1..50'ye sıkıştırılır; varsayılan 1 / 20).</summary>
public class DirectMessagePageQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DirectMessageLimits.DefaultPageSize;
}

/// <summary>
/// GET api/direct-messages/teachers sorgusu: ad araması (isteğe bağlı, Türkçe büyük/küçük harf duyarsız "içerir"). Boş/yalnız
/// boşluk = arama yok; trim sonrası 1 karakter → 400 <c>SearchTooShort</c>; en fazla 100 karakter dikkate alınır.
/// </summary>
public class MessageableTeacherQueryDto : DirectMessagePageQueryDto
{
    public string? Search { get; set; }
}

/// <summary>GET api/direct-messages/inbox sorgusu: <c>filter = "all" | "unread" | "blocked"</c> (varsayılan all).</summary>
public class TeacherInboxQueryDto : DirectMessagePageQueryDto
{
    public string? Filter { get; set; }
}

/// <summary>GET .../conversations/{id}/messages sorgusu: <c>beforeId</c> verilirse o Id'den eski mesajlar; take 1..50 (varsayılan 30).</summary>
public class DirectMessageHistoryQueryDto
{
    public int? BeforeId { get; set; }
    public int Take { get; set; } = DirectMessageLimits.DefaultMessageTake;
}

/// <summary>Mesaj gönderme gövdesi: düz metin, 1..2000 karakter (temizlik sonrası).</summary>
public class SendDirectMessageDto
{
    public string? Body { get; set; }
}

/// <summary>POST .../conversations/{id}/read gövdesi: karşı taraftan gelen ve Id'si &lt;= upToMessageId olan okunmamışlar okundu olur.</summary>
public class MarkDirectMessagesReadDto
{
    public int? UpToMessageId { get; set; }
}

/// <summary>Şikayet gövdesi: <c>reason</c> zorunlu (<c>spam | abuse | personalInfo | other</c>), <c>note</c> ≤ 500, <c>messageId</c> isteğe bağlı.</summary>
public class ReportDirectMessageDto
{
    public int? MessageId { get; set; }
    public string? Reason { get; set; }
    public string? Note { get; set; }
}

/// <summary>Öğrencinin mesajlaşabileceği öğretmen (A ∪ B kümesi, tekrarsız).</summary>
public class MessageableTeacherDto
{
    /// <summary>Teachers.Id — gönderme ucunun route parametresi.</summary>
    public int TeacherId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;

    /// <summary>İlişki nedeni: <c>"school" | "assignment" | "both"</c> (<see cref="DirectMessageRelations"/>).</summary>
    public string Relation { get; set; } = DirectMessageRelations.School;

    /// <summary>Bu öğretmenle mevcut konuşma varsa Id'si; yoksa null (ilk mesaj oluşturur).</summary>
    public int? ConversationId { get; set; }
}

public class MessageableTeacherPageDto
{
    public List<MessageableTeacherDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }

    /// <summary>
    /// Yalnız aramada: ilişkili öğretmen sayısı aday sınırını (200) aştı, arama ilk 200 aday içinde yapıldı — sonuç eksik
    /// olabilir. Aramasız listede her zaman false.
    /// </summary>
    public bool Truncated { get; set; }
}

/// <summary>Konuşma listesi satırı (öğrenci: kendi konuşmaları; öğretmen: gelen kutusu).</summary>
public class ConversationSummaryDto
{
    public int ConversationId { get; set; }

    /// <summary>Karşı tarafın adı (öğrenci görünümünde öğretmen, öğretmen görünümünde öğrenci).</summary>
    public string CounterpartName { get; set; } = string.Empty;
    public string CounterpartAvatar { get; set; } = string.Empty;

    /// <summary>Öğrenci görünümünde öğretmenin canlı Teachers.Id'si (yoksa null); öğretmen görünümünde null.</summary>
    public int? TeacherId { get; set; }

    /// <summary>Öğretmen görünümünde öğrencinin canlı Students.Id'si (yoksa null); öğrenci görünümünde null.</summary>
    public int? StudentId { get; set; }

    public DateTime LastMessageAt { get; set; }

    /// <summary>Son mesajın ilk 120 karakteri (düz metin).</summary>
    public string LastMessagePreview { get; set; } = string.Empty;

    /// <summary>Son mesaj istek sahibinin mi.</summary>
    public bool LastMessageIsMine { get; set; }

    /// <summary>Karşı taraftan gelen okunmamış mesaj sayısı.</summary>
    public int UnreadCount { get; set; }

    /// <summary>Yalnız öğretmen görünümünde dolu (öğrenci engellenmiş mi); öğrenci görünümünde her zaman null (engel sızdırılmaz).</summary>
    public bool? IsBlocked { get; set; }
}

public class ConversationPageDto
{
    public List<ConversationSummaryDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

public class DirectMessageDto
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public string Body { get; set; } = string.Empty;

    /// <summary><c>"Student" | "Teacher"</c>.</summary>
    public string SenderRole { get; set; } = string.Empty;
    public bool IsMine { get; set; }
    public DateTime SentAt { get; set; }
}

/// <summary>Konuşma mesaj geçmişi (bir sayfa, eskiden yeniye) + konuşma başlığı.</summary>
public class ConversationMessagesDto
{
    public int ConversationId { get; set; }
    public string CounterpartName { get; set; } = string.Empty;
    public string CounterpartAvatar { get; set; } = string.Empty;
    public int? TeacherId { get; set; }
    public int? StudentId { get; set; }

    /// <summary>
    /// İstek sahibi şu an bu konuşmaya yeni mesaj yazabilir mi. Öğrencide CanMessage (ilişki + onaylı öğretmen + engel yok —
    /// hangi koşulun düştüğü sızdırılmaz); öğretmende ilişki sürüyor mu (engel cevabı kapatmaz).
    /// </summary>
    public bool CanSend { get; set; }

    /// <summary>Yalnız öğretmen görünümünde dolu; öğrencide null.</summary>
    public bool? IsBlocked { get; set; }

    /// <summary>Bu sayfanın mesajları, eskiden yeniye.</summary>
    public List<DirectMessageDto> Items { get; set; } = new();

    /// <summary>Daha eski mesaj var mı; varsa sonraki istekte <c>beforeId = nextBeforeId</c>.</summary>
    public bool HasMore { get; set; }
    public int? NextBeforeId { get; set; }
}

/// <summary>Şikayet (admin listesi satırı). Şikayet edilen mesajın gövdesi Id ile okunur, şikayette kopyalanmaz.</summary>
public class DirectMessageReportItemDto
{
    public int ReportId { get; set; }
    public int ConversationId { get; set; }
    public int? MessageId { get; set; }

    /// <summary><c>spam | abuse | personalInfo | other</c>.</summary>
    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }

    /// <summary><c>"Open" | "Reviewed"</c>.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime ReportedAt { get; set; }

    /// <summary>Şikayet edenin rolü: <c>"Student" | "Teacher"</c>.</summary>
    public string ReporterRole { get; set; } = string.Empty;
    public int ReporterUserId { get; set; }
    public int StudentUserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public int TeacherUserId { get; set; }
    public string TeacherName { get; set; } = string.Empty;

    /// <summary>Şikayet edilen mesaj (messageId doluysa): gövde, gönderen rolü ve anı.</summary>
    public string? MessageBody { get; set; }
    public string? MessageSenderRole { get; set; }
    public DateTime? MessageSentAt { get; set; }
}

public class DirectMessageReportPageDto
{
    public List<DirectMessageReportItemDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

// ---- Servis sonuçları (ResponseBaseDto bayrakları + errorCode) -----------------------------------------------

public abstract class DirectMessageResponseDto : ResponseBaseDto
{
    /// <summary>Hata durumunda kod — <see cref="DirectMessageErrorCodes"/>. Başarıda null.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Kota aşımı (yeni konuşma / konuşma başına saatlik mesaj) → controller 429 + Retry-After.</summary>
    [JsonIgnore]
    public bool RateLimited { get; set; }

    /// <summary><see cref="RateLimited"/> iken Retry-After (saniye).</summary>
    [JsonIgnore]
    public int? RetryAfterSeconds { get; set; }

    /// <summary>Bağımlı servis (auth-api ad çözümü) kullanılamadı → controller 503.</summary>
    [JsonIgnore]
    public bool ServiceUnavailable { get; set; }
}

public class MessageableTeacherPageResultDto : DirectMessageResponseDto
{
    public MessageableTeacherPageDto? Page { get; set; }
}

public class ConversationPageResultDto : DirectMessageResponseDto
{
    public ConversationPageDto? Page { get; set; }
}

public class ConversationMessagesResultDto : DirectMessageResponseDto
{
    public ConversationMessagesDto? Conversation { get; set; }
}

/// <summary>Gönderim yanıtı (201): oluşan mesaj + konuşma Id'si (ilk mesaj konuşmayı açtıysa <c>conversationCreated</c> true).</summary>
public class SendDirectMessageResultDto : DirectMessageResponseDto
{
    public int ConversationId { get; set; }
    public bool ConversationCreated { get; set; }
    public DirectMessageDto? DirectMessage { get; set; }
}

/// <summary>Engelle / kaldır yanıtı (200). Durum zaten istenen gibiyse idempotent (<c>changed</c> false, audit yok).</summary>
public class DirectMessageBlockResultDto : DirectMessageResponseDto
{
    public int ConversationId { get; set; }
    public bool IsBlocked { get; set; }
    public bool Changed { get; set; }
}

/// <summary>Şikayet yanıtı (200). Tekrar şikayet idempotent: mevcut kaydın Id'si, <c>alreadyReported</c> true.</summary>
/// <summary>Okundu işaretleme yanıtı (200): bu çağrıda okundu yapılan mesaj sayısı (zaten okunmuşsa 0, idempotent).</summary>
public class MarkDirectMessagesReadResultDto : DirectMessageResponseDto
{
    public int ConversationId { get; set; }
    public int MarkedCount { get; set; }
}

public class DirectMessageReportResultDto : DirectMessageResponseDto
{
    public int ReportId { get; set; }
    public bool AlreadyReported { get; set; }
}

public class DirectMessageReportPageResultDto : DirectMessageResponseDto
{
    public DirectMessageReportPageDto? Page { get; set; }
}

// ---- Sabitler ------------------------------------------------------------------------------------------------

public static class DirectMessageLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPage = 10_000;
    public const int MinSearchLength = 2;
    public const int MaxPageSize = 50;
    public const int DefaultMessageTake = 30;
    public const int MaxMessageTake = 50;
    public const int PreviewLength = 120;

    /// <summary>Ad araması bellek içi yapılır (adlar auth-api'de): aday küme bu sayıyla sınırlı (aşımda <c>truncated</c>).</summary>
    public const int MaxSearchCandidates = 200;
}

public static class DirectMessageRelations
{
    public const string School = "school";
    public const string Assignment = "assignment";
    public const string Both = "both";

    public static string For(bool viaSchool, bool viaAssignment) =>
        viaSchool && viaAssignment ? Both : viaAssignment ? Assignment : School;
}

public static class TeacherInboxFilters
{
    public const string All = "all";
    public const string Unread = "unread";
    public const string Blocked = "blocked";
}

/// <summary>Şikayet nedeni JSON değerleri (camelCase) ↔ enum.</summary>
public static class DirectMessageReportReasons
{
    public static bool TryParse(string? value, out DirectMessageReportReason reason)
    {
        reason = default;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "spam": reason = DirectMessageReportReason.Spam; return true;
            case "abuse": reason = DirectMessageReportReason.Abuse; return true;
            case "personalinfo": reason = DirectMessageReportReason.PersonalInfo; return true;
            case "other": reason = DirectMessageReportReason.Other; return true;
            default: return false;
        }
    }

    public static string ToJson(DirectMessageReportReason reason) => reason switch
    {
        DirectMessageReportReason.Spam => "spam",
        DirectMessageReportReason.Abuse => "abuse",
        DirectMessageReportReason.PersonalInfo => "personalInfo",
        _ => "other"
    };
}

public static class DirectMessageErrorCodes
{
    /// <summary>
    /// 403 — öğrenci bu öğretmene şu an mesaj gönderemez. Nötr: ilişki yok, öğretmen onaysız/askıda ya da öğretmen öğrenciyi
    /// engellemiş olabilir; hangisi olduğu sızdırılmaz (engel belli olmasın).
    /// </summary>
    public const string CannotMessageTeacher = "CannotMessageTeacher";

    /// <summary>403 — öğretmen cevabı: öğrenciyle okul/atama ilişkisi artık yok (konuşma okunur kalır).</summary>
    public const string RelationshipEnded = "RelationshipEnded";

    /// <summary>404 — konuşma yok YA DA istek sahibi konuşmanın tarafı değil (varlık sızdırılmaz, security D2).</summary>
    public const string ConversationNotFound = "ConversationNotFound";

    /// <summary>403 — engel/kaldır yalnız konuşmanın öğretmenine açık (öğrenci kendi konuşmasında da engel koyamaz).</summary>
    public const string BlockTeacherOnly = "BlockTeacherOnly";

    /// <summary>404 — şikayet edilen mesaj bu konuşmada yok.</summary>
    public const string MessageNotFound = "MessageNotFound";

    /// <summary>403 — kendi mesajını şikayet edemez.</summary>
    public const string CannotReportOwnMessage = "CannotReportOwnMessage";

    /// <summary>403 — öğrenci profili (Students satırı) yok.</summary>
    public const string StudentProfileNotFound = "StudentProfileNotFound";

    /// <summary>403 — öğretmen profili (Teachers satırı) yok.</summary>
    public const string TeacherProfileNotFound = "TeacherProfileNotFound";

    public const string BodyRequired = "BodyRequired";
    public const string BodyTooLong = "BodyTooLong";
    public const string BodyInvalidCharacters = "BodyInvalidCharacters";
    public const string InvalidReportReason = "InvalidReportReason";
    public const string ReportNoteTooLong = "ReportNoteTooLong";
    public const string ReportNoteInvalidCharacters = "ReportNoteInvalidCharacters";
    public const string InvalidFilter = "InvalidFilter";

    /// <summary>400 — arama terimi trim sonrası 2 karakterden kısa.</summary>
    public const string SearchTooShort = "SearchTooShort";

    /// <summary>503 — arama aktifken auth-api ad çözümü başarısız/zaman aşımı (arama yapılamaz).</summary>
    public const string NameLookupUnavailable = "NameLookupUnavailable";

    /// <summary>400 — read gövdesinde upToMessageId eksik ya da &lt;= 0.</summary>
    public const string InvalidUpToMessageId = "InvalidUpToMessageId";

    /// <summary>429 — rate limit ya da kota (yeni konuşma günlük / konuşma başına saatlik); gövde <c>{ message, errorCode }</c> ya da servis yanıtı.</summary>
    public const string RateLimited = "RateLimited";
}

/// <summary>
/// <c>DirectMessaging</c> (issue #106) kural bayrakları.
/// </summary>
public sealed class DirectMessagingOptions
{
    public const string SectionName = "DirectMessaging";

    /// <summary>
    /// (B) "aynı okul" yolu açık mı. VARSAYILAN false: öğrencinin okul üyeliği şu an doğrulanmıyor (öğrenci kendi okulunu
    /// seçebiliyor — #361); doğrulanana kadar okul tek başına mesajlaşma hakkı vermez, CanMessage yalnız (A) atama yolunu
    /// uygular ve liste yalnız <c>relation = "assignment"</c> üretir. #361 kapanınca true yapılır (kural kodu değişmez).
    /// issue #361 (uygulandı): öğrencinin okulu artık YALNIZ doğrulanmış üyelikten çözülür (<c>UserSchoolResolver</c>) — bayrak
    /// açılsa da beklemedeki öğrenci B yolunu alamaz. Bayrağı açmak ayrı ürün kararı (appsettings, kod değişmez).
    /// </summary>
    public bool AllowSameSchoolMessaging { get; set; }
}

/// <summary>
/// <c>DirectMessaging:Quotas</c> (issue #106 security O2): kova (sub başına HTTP rate limit) dışında DB'den sayılan kotalar.
/// Aşımda 429 <c>RateLimited</c> + Retry-After.
/// </summary>
public sealed class DirectMessageQuotaOptions
{
    public const string SectionName = "DirectMessaging:Quotas";

    /// <summary>Öğrenci başına son 24 saatte açılabilecek yeni konuşma sayısı.</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 1000)]
    public int NewConversationsPerDay { get; set; } = 10;

    /// <summary>Gönderen başına bir konuşmada son 1 saatte gönderilebilecek mesaj sayısı.</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 10_000)]
    public int MessagesPerConversationPerHour { get; set; } = 30;
}
