using System;
using System.Collections.Generic;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos.WorksheetComments;

// Worksheet / soru yorum-soru thread'leri (issue #105). Gateway üzerinden /api/exam/worksheet/{worksheetId}/comments
// (mevcut /api/exam/{everything} wildcard route'u) ile erişilir.
// Kişisel veri: DTO'larda AuthorUserId, Keycloak id ve e-posta YOK — yalnızca görünen ad ve rol.

/// <summary>GET /api/worksheet/{worksheetId}/comments sorgu parametreleri.</summary>
public class WorksheetCommentQueryDto
{
    /// <summary>Soru bazlı thread için worksheet içindeki sorunun <c>Question.Id</c>'si; boşsa worksheet seviyesi thread.</summary>
    public int? QuestionId { get; set; }

    /// <summary>Önceki sayfanın <see cref="WorksheetCommentPageDto.NextCursor"/> değeri (opak). İlk sayfada boş.</summary>
    public string? Cursor { get; set; }

    /// <summary>Sayfa başına kök yorum sayısı; 1..50'ye sıkıştırılır, varsayılan 20.</summary>
    public int Take { get; set; } = WorksheetCommentLimits.DefaultPageSize;
}

/// <summary>GET /api/worksheet/{worksheetId}/comments/{rootId}/replies sorgu parametreleri (eskiden yeniye).</summary>
public class WorksheetCommentRepliesQueryDto
{
    /// <summary>Önceki sayfanın <see cref="WorksheetCommentRepliesPageDto.NextCursor"/> değeri (opak). İlk sayfada boş.</summary>
    public string? Cursor { get; set; }

    /// <summary>Sayfa başına reply; 1..50'ye sıkıştırılır, varsayılan 20.</summary>
    public int Take { get; set; } = WorksheetCommentLimits.DefaultPageSize;
}

/// <summary>POST /api/worksheet/{worksheetId}/comments gövdesi.</summary>
public class CreateWorksheetCommentDto
{
    /// <summary>Soru bazlı thread için <c>Question.Id</c> (worksheet'te olmalı); boşsa worksheet seviyesi.</summary>
    public int? QuestionId { get; set; }

    /// <summary>Reply ise aynı worksheet + aynı QuestionId'deki silinmemiş KÖK yorumun Id'si. Reply'a reply yok.</summary>
    public int? ParentCommentId { get; set; }

    /// <summary>Düz metin; trim edilir, 1..2000 karakter.</summary>
    public string? Body { get; set; }
}

/// <summary>Tek yorum (kök veya reply).</summary>
public class WorksheetCommentDto
{
    public int Id { get; set; }
    public int WorksheetId { get; set; }
    public int? QuestionId { get; set; }

    /// <summary>
    /// Soru thread'inde sorunun kullanıcıya gösterilen 1 tabanlı numarası (issue #309; <c>WorksheetQuestionNumbering</c>,
    /// test çözme ekranındaki "Soru n" ile aynı). Worksheet seviyesi thread'de null.
    /// </summary>
    public int? QuestionOrder { get; set; }

    public int? ParentCommentId { get; set; }

    /// <summary>Öğrenci: "Ad S." (soyadın baş harfi); öğretmen: görünen ad. Çözülemezse yerelleştirilmiş rol adı.</summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>JSON'da "Student" | "Teacher".</summary>
    public WorksheetCommentAuthorRole AuthorRole { get; set; }

    /// <summary>Yorum istek sahibine mi ait.</summary>
    public bool IsMine { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Kök yorum + en son <see cref="WorksheetCommentLimits.RepliesPreviewCount"/> reply'ı (eskiden yeniye). Tamamı için
/// <see cref="ReplyCount"/> &gt; Replies.Count ise replies ucu (eskiden yeniye, cursor'lı) çağrılır.
/// </summary>
public class WorksheetCommentThreadDto : WorksheetCommentDto
{
    /// <summary>Son (en yeni) en fazla 5 reply, kendi içinde eskiden yeniye.</summary>
    public List<WorksheetCommentDto> Replies { get; set; } = new();

    /// <summary>Kökün silinmemiş reply'larının toplam sayısı.</summary>
    public int ReplyCount { get; set; }

    /// <summary>
    /// İstek sahibi bu thread'e reply yazabilir mi. Öğrencide sayfanın <see cref="WorksheetCommentPageDto.CanWrite"/>'ı ile
    /// aynı; öğretmende thread bazında (kök yazarı öğrencinin ilgili öğretmeni mi).
    /// </summary>
    public bool CanReply { get; set; }
}

/// <summary>Thread sayfası.</summary>
public class WorksheetCommentPageDto
{
    /// <summary>Kök yorumlar yeniden eskiye.</summary>
    public List<WorksheetCommentThreadDto> Items { get; set; } = new();

    /// <summary>Sonraki sayfa için opak imleç; son sayfada null.</summary>
    public string? NextCursor { get; set; }

    /// <summary>
    /// İstek sahibi bu thread'e (worksheet veya soru) yeni KÖK yorum yazabilir mi. UI yazma alanını buna göre çizer;
    /// backend POST'ta aynı kuralı yeniden uygular.
    /// </summary>
    public bool CanWrite { get; set; }

    /// <summary>
    /// Öğrenci için yazma kilidinin nedeni — bkz. <see cref="WorksheetCommentLockReasons"/>; yazabiliyorsa null.
    /// Öğretmen/admin için her zaman null.
    /// </summary>
    public string? LockReason { get; set; }

    /// <summary>
    /// issue #309: sayfanın ait olduğu soru thread'inin 1 tabanlı numarası (öğelerdeki <c>QuestionOrder</c> ile aynı;
    /// sayfa boşken de dolu). Worksheet seviyesi thread'de null.
    /// </summary>
    public int? QuestionOrder { get; set; }

    /// <summary>
    /// issue #309: öğretmen/admin için öğrencilerin efektif yorum durumu özeti (worksheet varsayılanı + çağıranın görebildiği
    /// AKTİF atamalardaki override sayıları). Öğrenci için her zaman null. Kişisel veri yok (yalnız sayılar).
    /// </summary>
    public WorksheetCommentStudentSummaryDto? StudentCommentsSummary { get; set; }
}

/// <summary>
/// Öğretmen görünümü için öğrenci yorum durumu (issue #309). Bir öğrencinin efektif değeri
/// <c>ilgili aktif atamanın override'ı ?? WorksheetDefault</c>'tur; override'sız (null) atamalar sayılmaz.
/// </summary>
public class WorksheetCommentStudentSummaryDto
{
    /// <summary>Worksheet varsayılanı (<c>Worksheet.CommentsEnabled</c>).</summary>
    public bool WorksheetDefault { get; set; }

    /// <summary>
    /// Çağıranın görebildiği aktif atamalardaki override sayıları: admin worksheet'in TÜM aktif atamalarını, sahip dahil
    /// her öğretmen yalnızca KENDİ oluşturduğu aktif atamaları görür.
    /// </summary>
    public WorksheetCommentOverrideCountsDto AssignmentOverrides { get; set; } = new();
}

public class WorksheetCommentOverrideCountsDto
{
    /// <summary><c>CommentsEnabledOverride == true</c> olan aktif atama sayısı.</summary>
    public int Enabled { get; set; }

    /// <summary><c>CommentsEnabledOverride == false</c> olan aktif atama sayısı.</summary>
    public int Disabled { get; set; }
}

/// <summary>GET .../comments/{rootId}/replies yanıtı.</summary>
public class WorksheetCommentRepliesPageDto
{
    /// <summary>Reply'lar eskiden yeniye.</summary>
    public List<WorksheetCommentDto> Items { get; set; } = new();

    /// <summary>Sonraki (daha yeni) sayfa için opak imleç; son sayfada null.</summary>
    public string? NextCursor { get; set; }

    /// <summary>İstek sahibi bu thread'e reply yazabilir mi (thread sayfasındaki CanReply ile aynı kural).</summary>
    public bool CanReply { get; set; }

    /// <summary>Kökün silinmemiş reply'larının toplam sayısı.</summary>
    public int ReplyCount { get; set; }

    /// <summary>issue #309: kök soru thread'indeyse 1 tabanlı soru numarası; worksheet seviyesinde null.</summary>
    public int? QuestionOrder { get; set; }
}

/// <summary>Yazma kilidi nedenleri (maket sözleşmesi).</summary>
public static class WorksheetCommentLockReasons
{
    public const string CommentsDisabled = "comments-disabled";
    public const string QuestionNotAnswered = "question-not-answered";
    public const string WorksheetNotStarted = "worksheet-not-started";
}

/// <summary>Servis yanıtlarının ortak tabanı: ResponseBaseDto bayrakları + makine tarafından okunabilir hata kodu.</summary>
public abstract class WorksheetCommentResponseDto : ResponseBaseDto
{
    /// <summary>Hata durumunda kod — bkz. <see cref="WorksheetCommentErrorCodes"/>. Başarıda null.</summary>
    public string? ErrorCode { get; set; }
}

public class WorksheetCommentPageResultDto : WorksheetCommentResponseDto
{
    public WorksheetCommentPageDto? Page { get; set; }
}

public class WorksheetCommentRepliesResultDto : WorksheetCommentResponseDto
{
    public WorksheetCommentRepliesPageDto? Page { get; set; }
}

public class WorksheetCommentResultDto : WorksheetCommentResponseDto
{
    public WorksheetCommentDto? Comment { get; set; }
}

public static class WorksheetCommentErrorCodes
{
    /// <summary>404 — worksheet yok (öğretmen için: görme yetkisi de yok — varlık sızdırılmaz).</summary>
    public const string WorksheetNotFound = "WorksheetNotFound";

    /// <summary>403 — öğrencinin worksheet'e erişimi yok (atama yok, keşfedilebilir değil, hiç çözmemiş).</summary>
    public const string AccessDenied = "AccessDenied";

    /// <summary>400 — questionId bu worksheet'in sorusu değil.</summary>
    public const string QuestionNotInWorksheet = "QuestionNotInWorksheet";

    /// <summary>400 — parentCommentId aynı worksheet/QuestionId'de silinmemiş bir kök yorum değil (reply'a reply dahil).</summary>
    public const string InvalidParent = "InvalidParent";

    /// <summary>400 — gövde boş (trim sonrası).</summary>
    public const string BodyRequired = "BodyRequired";

    /// <summary>400 — gövde 2000 karakterden uzun (trim sonrası).</summary>
    public const string BodyTooLong = "BodyTooLong";

    /// <summary>
    /// 400 — gövdede LF ve TAB dışında kontrol karakteri (C0/C1, NUL) veya geçersiz Unicode (eşlenmemiş surrogate) var.
    /// CRLF / CR önce LF'e çevrilir.
    /// </summary>
    public const string BodyInvalidCharacters = "BodyInvalidCharacters";

    /// <summary>404 — replies ucunda rootId bu worksheet'in silinmemiş bir kök yorumu değil.</summary>
    public const string RootCommentNotFound = "RootCommentNotFound";

    /// <summary>400 — cursor çözülemedi.</summary>
    public const string InvalidCursor = "InvalidCursor";

    /// <summary>403 — öğrenci: yorumlar kapalı (etkin ayar).</summary>
    public const string CommentsDisabled = "CommentsDisabled";

    /// <summary>403 — öğrenci: worksheet'i hiç başlatmamış (soru thread'i).</summary>
    public const string WorksheetNotStarted = "WorksheetNotStarted";

    /// <summary>403 — öğrenci: soruyu cevaplamamış (soru thread'i).</summary>
    public const string QuestionNotAnswered = "QuestionNotAnswered";

    /// <summary>403 — öğretmen bu thread'in ilgili öğretmeni değil (veya kök yorum açma yetkisi yok).</summary>
    public const string NotResponsibleTeacher = "NotResponsibleTeacher";

    /// <summary>429 — yorum okuma/yazma rate limit'i aşıldı (issue #309; gövde <c>{ message, errorCode }</c>, Retry-After başlığı).</summary>
    public const string RateLimited = "RateLimited";
}

public static class WorksheetCommentLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    /// <summary>Thread listesinde kök başına dönen en son reply sayısı.</summary>
    public const int RepliesPreviewCount = 5;
}
