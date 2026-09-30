using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Şikayet nedeni (issue #305). DB'de string saklanır; JSON sözleşmesi camelCase string'dir
/// (<c>"spam" | "abuse" | "personalInfo" | "other"</c>) — eşleme DTO katmanında (WorksheetCommentReportReasons).
/// </summary>
public enum WorksheetCommentReportReason
{
    Spam = 1,
    Abuse = 2,
    PersonalInfo = 3,
    Other = 4
}

/// <summary>
/// Bir kullanıcının bir yorumu şikayet etmesi (issue #305). Otomatik gizleme YOK (PO kararı): şikayet yalnız işaretler,
/// gizleme moderatörün elle yaptığı ayrı bir işlemdir. Aynı kullanıcı aynı yorumu bir kez şikayet eder
/// (<c>(CommentId, ReporterUserId)</c> tekil, soft-delete edilmemiş satırlar üzerinde). Şikayet anı
/// <see cref="BaseEntity.CreateTime"/>. Şikayet edenin kimliği hiçbir DTO'ya çıkmaz.
/// </summary>
public class WorksheetCommentReport : BaseEntity
{
    public const int NoteMaxLength = 500;

    [Key]
    public int Id { get; set; }

    public int CommentId { get; set; }

    [ForeignKey(nameof(CommentId))]
    public WorksheetComment Comment { get; set; } = null!;

    /// <summary>Şikayet edenin exam/auth user id'si (Student.UserId / Teacher.UserId).</summary>
    public int ReporterUserId { get; set; }

    /// <summary>Şikayet edenin Keycloak sub'ı (iz için; DTO'ya çıkmaz).</summary>
    [Required, MaxLength(WorksheetComment.KeycloakIdMaxLength)]
    public string ReporterKeycloakId { get; set; } = string.Empty;

    public WorksheetCommentReportReason Reason { get; set; }

    /// <summary>İsteğe bağlı açıklama (düz metin, 1..<see cref="NoteMaxLength"/>); yalnız moderatör listesinde döner.</summary>
    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }
}
