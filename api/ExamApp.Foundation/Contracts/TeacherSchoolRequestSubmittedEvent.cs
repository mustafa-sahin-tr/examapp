using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Okul bağlantısı talebi (issue #234) admin bildirimi — issue #277 (madde 1). Öğretmen bir okula bağlanmak için talep
/// açtığında (<c>Teachers.RequestedSchoolId</c> dolu + <c>ApprovalStatus = Pending</c>) exam API tarafından İŞLEMLE AYNI
/// transaction'da outbox'a yazılır:
/// <list type="bullet">
/// <item>yeni öğretmen kaydı okul talebiyle (<see cref="IsNewRegistration"/> = true), ya da</item>
/// <item>mevcut okulsuz öğretmen (talebi reddedilmiş ya da hiç talebi olmamış) yeni talep açtığında (false).</item>
/// </list>
/// Aynı talebin idempotent tekrarı (aynı okul, hâlâ Pending) event üretmez. Bağımsız öğretmen başvurusu bu event'i
/// DEĞİL <see cref="TeacherApplicationSubmittedEvent"/>'i üretir — o event'in tüketicisi metni "bağımsız öğretmen
/// başvurusu" olarak kurar ve <c>TeacherId</c> ile tekilleştirir; bir öğretmen zamanla birden fazla okul talebi
/// açabildiği için burada ayrı tip + <see cref="EventId"/> kullanılır.
/// <para>
/// Beklenen tüketici (BadgeService): tüm Admin'lere in-app bildirim + SignalR "role:Admin" push (bkz.
/// TeacherApplicationSubmittedEvent tüketicisi). Payload minimum: id'ler + bildirim metni için başvuran adı ve okul adı
/// (best-effort). E-posta/token taşınmaz.
/// </para>
/// </summary>
public class TeacherSchoolRequestSubmittedEvent
{
    /// <summary>Idempotency anahtarı — bu talep için üretilen tek event'i tekilleştirir.</summary>
    public Guid EventId { get; set; }

    /// <summary>Teacher.Id.</summary>
    public int TeacherId { get; set; }

    /// <summary>Talep eden öğretmenin exam/auth user id'si.</summary>
    public int UserId { get; set; }

    /// <summary>Talep edilen okul (Schools.Id).</summary>
    public int RequestedSchoolId { get; set; }

    /// <summary>Talep edilen okulun adı; bildirim metni için (best-effort, null olabilir).</summary>
    public string? RequestedSchoolName { get; set; }

    /// <summary>Başvuranın görünen adı; bildirim metni için (best-effort — auth-api erişilemezse null).</summary>
    public string? ApplicantName { get; set; }

    /// <summary>true → ilk öğretmen kaydı okul talebiyle; false → mevcut kaydın yeni okul talebi.</summary>
    public bool IsNewRegistration { get; set; }

    /// <summary>Talebin açıldığı an (UTC).</summary>
    public DateTime SubmittedAtUtc { get; set; }
}
