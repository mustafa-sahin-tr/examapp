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

    /// <summary>
    /// issue #305: moderatör görünümü. true ise ve istek sahibi bir yorumun moderatörüyse (worksheet sahibi, thread'in
    /// sorumlu öğretmeni, admin) o GİZLİ yorumun gövdesi, yazar adı ve gizleme nedeni döner. Diğerlerinde etkisiz.
    /// </summary>
    public bool ModeratorView { get; set; }
}

/// <summary>GET /api/worksheet/{worksheetId}/comments/{rootId}/replies sorgu parametreleri (eskiden yeniye).</summary>
public class WorksheetCommentRepliesQueryDto
{
    /// <summary>Önceki sayfanın <see cref="WorksheetCommentRepliesPageDto.NextCursor"/> değeri (opak). İlk sayfada boş.</summary>
    public string? Cursor { get; set; }

    /// <summary>Sayfa başına reply; 1..50'ye sıkıştırılır, varsayılan 20.</summary>
    public int Take { get; set; } = WorksheetCommentLimits.DefaultPageSize;

    /// <summary>issue #305: bkz. <see cref="WorksheetCommentQueryDto.ModeratorView"/>.</summary>
    public bool ModeratorView { get; set; }
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

    /// <summary>
    /// Gövde. issue #305: gizli yorumda (<see cref="IsHidden"/>) null — moderatör görünümü hariç
    /// (<see cref="WorksheetCommentQueryDto.ModeratorView"/>).
    /// </summary>
    public string? Body { get; set; } = string.Empty;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// issue #305: yorum moderasyonla gizlendi. Thread'de "kaldırıldı" yer tutucusu olarak kalır: <see cref="Body"/> null,
    /// <see cref="AuthorDisplayName"/> sabit metin (moderatör görünümünde gerçek değerler).
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>issue #305: istek sahibi bu yorumu şikayet etti mi (herkese).</summary>
    public bool ReportedByMe { get; set; }

    /// <summary>issue #305: istek sahibi bu yorumu gizleyebilir/açabilir mi (sahip, thread'in sorumlu öğretmeni, admin).</summary>
    public bool CanModerate { get; set; }

    /// <summary>issue #305: aktif şikayet sayısı — yalnız <see cref="CanModerate"/> ise dolu, diğerlerine null.</summary>
    public int? ReportCount { get; set; }

    /// <summary>issue #305: gizleme nedeni — yalnız moderatör görünümünde ve gizli yorumda dolu.</summary>
    public string? HiddenReason { get; set; }

    /// <summary>issue #305: gizlenme anı (UTC) — yalnız moderatör görünümünde ve gizli yorumda dolu.</summary>
    public DateTime? HiddenAt { get; set; }
}

/// <summary>
/// Kök yorum + en son <see cref="WorksheetCommentLimits.RepliesPreviewCount"/> reply'ı (eskiden yeniye). Tamamı için
/// <see cref="ReplyCount"/> &gt; Replies.Count ise replies ucu (eskiden yeniye, cursor'lı) çağrılır.
/// </summary>
public class WorksheetCommentThreadDto : WorksheetCommentDto
{
    /// <summary>Son (en yeni) en fazla 5 reply, kendi içinde eskiden yeniye.</summary>
    public List<WorksheetCommentDto> Replies { get; set; } = new();

    /// <summary>
    /// Kökün silinmemiş reply'larının toplam sayısı — issue #305: yalnız okuyucunun okul kapsamındakiler (gizliler dahil,
    /// yer tutucu olarak döndükleri için).
    /// </summary>
    public int ReplyCount { get; set; }

    /// <summary>
    /// İstek sahibi bu thread'e reply yazabilir mi (#305: kök gizliyse herkese false). Öğrencide sayfanın <see cref="WorksheetCommentPageDto.CanWrite"/>'ı ile
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

    /// <summary>
    /// issue #326 (O2): öğrencinin bu worksheet'e yazacağı yeni yorumun kime görüneceği — bkz.
    /// <see cref="WorksheetCommentVisibilities"/>. UI yazma alanında bilgi satırını buna göre gösterir
    /// (<c>school</c> → "Bu yorum yalnız okulunda görünür."). Öğretmen/admin için her zaman null.
    /// </summary>
    public string? CommentVisibility { get; set; }
}

/// <summary>
/// issue #326 (O2): öğrenci yorumunun görünürlük kapsamı (<see cref="WorksheetCommentPageDto.CommentVisibility"/>). Karar
/// öğrencinin sorumlu öğretmeni (ilgili aktif atamayı yapan; yoksa yalnız aynı okuldaysa sahip) ve okuluna göre verilir.
/// Admin her durumda görür; burada sayılmaz.
/// </summary>
public static class WorksheetCommentVisibilities
{
    /// <summary>Okullu öğrenci + sorumlu öğretmen var: okulundaki öğretmen/öğrenciler ve sorumlu öğretmen görür.</summary>
    public const string Teacher = "teacher";

    /// <summary>Okullu öğrenci, sorumlu öğretmen YOK: yalnız okulundakiler görür (sahip okul dışıysa görmez, bildirim gitmez).</summary>
    public const string School = "school";

    /// <summary>Okulsuz öğrenci + sorumlu öğretmen var (ör. bağımsız öğretmenin ataması): yalnız kendisi ve sorumlu öğretmen görür.</summary>
    public const string SelfAndTeacher = "self-and-teacher";

    /// <summary>Okulsuz öğrenci, sorumlu öğretmen YOK: yalnız kendisi görür.</summary>
    public const string Self = "self";

    /// <summary>Tek karar noktası.</summary>
    public static string For(int? studentSchoolId, bool hasResponsibleTeacher) =>
        (studentSchoolId.HasValue, hasResponsibleTeacher) switch
        {
            (true, true) => Teacher,
            (true, false) => School,
            (false, true) => SelfAndTeacher,
            (false, false) => Self
        };
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

    /// <summary>Kökün silinmemiş, okuyucunun okul kapsamındaki reply'larının toplam sayısı (#305).</summary>
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

// ---- issue #305: moderasyon ---------------------------------------------------------------------------------

/// <summary>POST .../comments/{commentId}/report gövdesi.</summary>
public class ReportWorksheetCommentDto
{
    /// <summary><c>"spam" | "abuse" | "personalInfo" | "other"</c> (büyük/küçük harf duyarsız).</summary>
    public string? Reason { get; set; }

    /// <summary>İsteğe bağlı açıklama; düz metin, trim edilir, en fazla 500 karakter.</summary>
    public string? Note { get; set; }
}

/// <summary>POST .../comments/{commentId}/hide gövdesi.</summary>
public class HideWorksheetCommentDto
{
    /// <summary>Gizleme nedeni; düz metin, trim edilir, 1..500 karakter. Audit'e yazılmaz, yorumda saklanır.</summary>
    public string? Reason { get; set; }
}

/// <summary>Şikayet yanıtı (200). Tekrar şikayet idempotenttir: <see cref="AlreadyReported"/> true döner, yeni kayıt yazılmaz.</summary>
public class WorksheetCommentReportResultDto : WorksheetCommentResponseDto
{
    /// <summary>İstek sahibi bu yorumu daha önce şikayet etmişti (bu çağrı yeni kayıt yazmadı).</summary>
    public bool AlreadyReported { get; set; }

    /// <summary>Her başarılı yanıtta true (UI şikayet düğmesini buna göre kapatır).</summary>
    public bool ReportedByMe { get; set; }
}

/// <summary>GET .../comments/reports ve GET api/admin/comments/reports sorgusu (sayfa numaralı).</summary>
public class WorksheetCommentReportsQueryDto
{
    /// <summary>1 tabanlı sayfa; &lt; 1 ise 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>1..50'ye sıkıştırılır, varsayılan 20.</summary>
    public int PageSize { get; set; } = WorksheetCommentLimits.DefaultPageSize;
}

/// <summary>Şikayet nedeni başına sayı.</summary>
public class WorksheetCommentReportReasonCountsDto
{
    public int Spam { get; set; }
    public int Abuse { get; set; }
    public int PersonalInfo { get; set; }
    public int Other { get; set; }
}

/// <summary>Şikayet edilmiş tek yorum (moderatör listesi). Şikayet edenlerin kimliği yok.</summary>
public class WorksheetCommentReportItemDto
{
    /// <summary>Yorum — moderatör görünümüyle (gizliyse de gövde, gerçek yazar adı ve gizleme nedeni).</summary>
    public WorksheetCommentDto Comment { get; set; } = new();

    /// <summary>Worksheet adı (global admin listesinde bağlam için; retire edilmiş worksheet'te de dolu).</summary>
    public string WorksheetTitle { get; set; } = string.Empty;

    public int ReportCount { get; set; }

    public WorksheetCommentReportReasonCountsDto Reasons { get; set; } = new();

    /// <summary>En son şikayet anı (UTC).</summary>
    public DateTime LastReportedAt { get; set; }

    /// <summary>En yeni en fazla 3 şikayet notu (boş notlar hariç), yeniden eskiye.</summary>
    public List<string> Notes { get; set; } = new();
}

public class WorksheetCommentReportsPageDto
{
    /// <summary>En son şikayet edilen önce.</summary>
    public List<WorksheetCommentReportItemDto> Items { get; set; } = new();

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

public class WorksheetCommentReportsResultDto : WorksheetCommentResponseDto
{
    public WorksheetCommentReportsPageDto? Page { get; set; }
}

/// <summary>Şikayet nedeni JSON değerleri (camelCase) ↔ enum.</summary>
public static class WorksheetCommentReportReasons
{
    public const int MaxNotesPerItem = 3;

    public static bool TryParse(string? value, out WorksheetCommentReportReason reason)
    {
        reason = default;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "spam": reason = WorksheetCommentReportReason.Spam; return true;
            case "abuse": reason = WorksheetCommentReportReason.Abuse; return true;
            case "personalinfo": reason = WorksheetCommentReportReason.PersonalInfo; return true;
            case "other": reason = WorksheetCommentReportReason.Other; return true;
            default: return false;
        }
    }
}

public static class WorksheetCommentErrorCodes
{
    /// <summary>404 — worksheet yok (öğretmen için: görme yetkisi de yok — varlık sızdırılmaz).</summary>
    public const string WorksheetNotFound = "WorksheetNotFound";

    /// <summary>
    /// Artık dönmüyor (issue #305, varlık sızıntısı): öğrencinin erişemediği worksheet için de 404
    /// <see cref="WorksheetNotFound"/> döner (#255 test başlatma davranışıyla hizalı). Sabit, eski istemciler için duruyor.
    /// </summary>
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

    /// <summary>404 — (#305) yorum bu worksheet'te yok ya da istek sahibinin okul kapsamında değil (varlık sızdırılmaz).</summary>
    public const string CommentNotFound = "CommentNotFound";

    /// <summary>403 — (#305) kendi yorumunu şikayet edemezsin.</summary>
    public const string CannotReportOwnComment = "CannotReportOwnComment";

    /// <summary>400 — (#305) reason "spam" | "abuse" | "personalInfo" | "other" değil.</summary>
    public const string InvalidReportReason = "InvalidReportReason";

    /// <summary>400 — (#305) şikayet notu 500 karakterden uzun.</summary>
    public const string ReportNoteTooLong = "ReportNoteTooLong";

    /// <summary>400 — (#305) şikayet notu / gizleme nedeni geçersiz karakter içeriyor.</summary>
    public const string ModerationTextInvalidCharacters = "ModerationTextInvalidCharacters";

    /// <summary>400 — (#305) gizleme nedeni boş.</summary>
    public const string HideReasonRequired = "HideReasonRequired";

    /// <summary>400 — (#305) gizleme nedeni 500 karakterden uzun.</summary>
    public const string HideReasonTooLong = "HideReasonTooLong";

    /// <summary>403 — (#305) istek sahibi bu yorumun moderatörü değil (sahip / thread'in sorumlu öğretmeni / admin).</summary>
    public const string NotModerator = "NotModerator";

    /// <summary>403 — (#305) kök yorum gizlendi; thread'e yeni reply yazılamaz.</summary>
    public const string RootCommentHidden = "RootCommentHidden";

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
