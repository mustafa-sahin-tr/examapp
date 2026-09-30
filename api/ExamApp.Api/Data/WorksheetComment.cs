using System;
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
/// işaret edebilir (servis doğrular). Oluşturulma anı <see cref="BaseEntity.CreateTime"/>. Worksheet retire (soft-delete)
/// olsa da yorumlar görünür kalır.
/// <para>issue #305: moderasyon gizlemesi <see cref="BaseEntity.IsDeleted"/> DEĞİLDİR — gizlenen yorum thread'de
/// "kaldırıldı" yer tutucusu olarak kalır (reply'ları bağlamını korur), bu yüzden global !IsDeleted filtresine takılmamalı.
/// Gizli = <see cref="HiddenAt"/> dolu. Okul kapsamı yazarın okulu üzerinden (<see cref="AuthorSchoolId"/>).</para>
/// </summary>
public class WorksheetComment : BaseEntity
{
    public const int BodyMaxLength = 2000;
    public const int KeycloakIdMaxLength = 64;
    public const int HiddenReasonMaxLength = 500;

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
    /// öğretmen cevap yazabilmeye devam eder; dilim 2 bildirim hedefi de budur. Öğretmen kökünde null.
    /// issue #305: öğrencinin ÖĞRETMEN köküne yazdığı reply'da da o öğrencinin ilgili öğretmeni sabitlenir (bildirim alan
    /// öğretmen okul kapsamı dışında olsa da o reply'ı görebilsin). Cevap yetkisi yalnız KÖKÜN değerinden gelir.
    /// Öğrenci kökündeki reply'larda ve öğretmen reply'larında null.
    /// </summary>
    public int? ResponsibleTeacherUserId { get; set; }

    /// <summary>Reply ise kök yorumun Id'si; kök yorumda null.</summary>
    public int? ParentCommentId { get; set; }

    [ForeignKey(nameof(ParentCommentId))]
    public WorksheetComment? ParentComment { get; set; }

    /// <summary>Düz metin, trim edilmiş, 1..<see cref="BodyMaxLength"/> karakter.</summary>
    [Required, MaxLength(BodyMaxLength)]
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// issue #305: yazarın yorum anındaki okulu (öğrencide Students.SchoolId, öğretmende Teachers.SchoolId) — okul kapsamı
    /// okuma anında join'siz bununla uygulanır; yazarın okulu sonradan değişse de yorum yazıldığı okulda kalır. Okulsuz
    /// öğrencide / bağımsız öğretmende null (okul eşleşmesi yok — güvenli taraf).
    /// </summary>
    public int? AuthorSchoolId { get; set; }

    /// <summary>issue #305: moderasyonla gizlendiği an (UTC); null = görünür. Gizli yorum yer tutucu olarak döner.</summary>
    public DateTime? HiddenAt { get; set; }

    /// <summary>issue #305: gizleyen moderatörün exam/auth user id'si (sahip / sorumlu öğretmen / admin).</summary>
    public int? HiddenByUserId { get; set; }

    /// <summary>
    /// issue #305: gizleme nedeni (1..<see cref="HiddenReasonMaxLength"/>, düz metin). Audit satırına YAZILMAZ (audit PII
    /// taşımaz); yalnızca moderatör görünümünde döner. Açılınca (unhide) temizlenir.
    /// </summary>
    [MaxLength(HiddenReasonMaxLength)]
    public string? HiddenReason { get; set; }
}
