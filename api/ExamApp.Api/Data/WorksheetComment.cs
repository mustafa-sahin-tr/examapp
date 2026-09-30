using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ExamApp.Api.Data;

/// <summary>
/// Yorumu yazanın rolü (issue #105). JSON'da "Student" | "Teacher"; DB'de string saklanır
/// (üye sırası değişse bile geçmiş satırların anlamı kaymasın).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorksheetCommentAuthorRole>))]
public enum WorksheetCommentAuthorRole
{
    Student = 0,
    Teacher = 1
}

/// <summary>
/// Worksheet / soru altındaki yorum-soru thread'i (issue #105). <see cref="QuestionId"/> null ise worksheet
/// seviyesi (genel) thread, doluysa worksheet içindeki o sorunun (<c>Question.Id</c>) thread'i — ikisi ayrı tutulur.
/// Tek seviye reply: <see cref="ParentCommentId"/> yalnızca aynı worksheet + aynı QuestionId'deki bir KÖK yoruma
/// işaret edebilir (servis doğrular). Oluşturulma anı <see cref="BaseEntity.CreateTime"/>; silme soft-delete
/// (moderasyon kapsam dışı, uç yok). Worksheet retire (soft-delete) olsa da yorumlar görünür kalır.
/// </summary>
public class WorksheetComment : BaseEntity
{
    public const int BodyMaxLength = 2000;
    public const int KeycloakIdMaxLength = 64;

    [Key]
    public int Id { get; set; }

    public int WorksheetId { get; set; }

    [ForeignKey(nameof(WorksheetId))]
    public Worksheet Worksheet { get; set; } = null!;

    /// <summary>Soru bazlı thread için worksheet içindeki sorunun <c>Question.Id</c>'si; worksheet seviyesinde null.</summary>
    public int? QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public Question? Question { get; set; }

    /// <summary>Yazanın exam/auth user id'si (öğrenci için Student.UserId, öğretmen için Teacher.UserId).</summary>
    public int AuthorUserId { get; set; }

    /// <summary>Yazanın Keycloak sub'ı — bildirim hedeflemesi (dilim 2, SignalR sub ile hedefler). DTO'ya çıkmaz.</summary>
    [Required, MaxLength(KeycloakIdMaxLength)]
    public string AuthorKeycloakId { get; set; } = string.Empty;

    public WorksheetCommentAuthorRole AuthorRole { get; set; }

    /// <summary>
    /// Öğrenci KÖK yorumunda, yazıldığı anda çözülen ilgili öğretmen (atama &gt; kopya sahibi &gt; sahip) kayda sabitlenir.
    /// Bu thread'e öğretmen cevap yetkisi yalnızca bu değerle verilir — atama sonradan bitse/değişse de bildirim giden
    /// öğretmen cevap yazabilmeye devam eder; dilim 2 bildirim hedefi de budur. Öğretmen kökünde ve reply'larda null.
    /// </summary>
    public int? ResponsibleTeacherUserId { get; set; }

    /// <summary>Reply ise kök yorumun Id'si; kök yorumda null.</summary>
    public int? ParentCommentId { get; set; }

    [ForeignKey(nameof(ParentCommentId))]
    public WorksheetComment? ParentComment { get; set; }

    /// <summary>Düz metin, trim edilmiş, 1..<see cref="BodyMaxLength"/> karakter.</summary>
    [Required, MaxLength(BodyMaxLength)]
    public string Body { get; set; } = string.Empty;
}
