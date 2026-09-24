using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// Admin onay panelinde listelenen öğretmen başvurusu: bağımsız öğretmen (issue #94) ya da okul bağlantısı talebi (issue #234).
/// issue #187: <c>?status=all</c> ile karar verilmiş (Approved/Rejected) başvurular da döner; satır <c>Paged&lt;T&gt;</c> içindedir.
/// </summary>
public class TeacherApplicationListItemDto
{
    public int TeacherId { get; set; }

    // issue #262 (güvenlik review'u): auth-api iç kullanıcı id'si (UserId) dönülmez; UI ad fallback'i için teacherId kullanır.

    /// <summary>auth-api'den çözümlenir; erişilemezse boş string.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// MASKELİ e-posta (issue #262): <c>a***@okul.k12.tr</c> — bkz. <see cref="ExamApp.Api.Helpers.EmailMask"/>.
    /// auth-api'den çözümlenemezse boş string. Tam adres yalnızca bekleyen başvurunun detayında (<see cref="TeacherApplicationDetailDto"/>).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Teacher kaydının oluşturulma zamanı (BaseEntity.CreateTime).</summary>
    public DateTime AppliedAt { get; set; }

    /// <summary>
    /// issue #234: başvuru türü. true → bağımsız öğretmen başvurusu (#94); false → okul bağlantısı talebi
    /// (<see cref="RequestedSchoolId"/> dolu). Onay/red aynı uçlardan yapılır.
    /// </summary>
    public bool IsIndependentTutor { get; set; }

    /// <summary>
    /// issue #234: okul bağlantısı talebinin okulu; bağımsız başvuruda null. issue #187: onaylanmış okul talebinde onay
    /// RequestedSchoolId'yi temizleyip SchoolId'ye taşıdığından öğretmenin (onaylı) okulu döner.
    /// </summary>
    public int? RequestedSchoolId { get; set; }

    /// <summary>issue #234: talep edilen okulun adı; bağımsız başvuruda null (onaylı okul talebinde: bkz. <see cref="RequestedSchoolId"/>).</summary>
    public string? RequestedSchoolName { get; set; }

    /// <summary>issue #187: <c>"Pending"</c> | <c>"Approved"</c> | <c>"Rejected"</c> (<see cref="ExamApp.Api.Data.TeacherApprovalStatus"/> adı).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>issue #187: admin'in ret gerekçesi; yalnızca <c>Rejected</c> iken dolu, aksi halde null.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// issue #187: kararın verildiği an (UTC) — admin karar audit'inden (<c>AdminUserActionLogs</c>, #157) okunur.
    /// Pending'de null. Karar verilmiş başvuruda da null olabilir: #157 öncesi kararlar ya da audit yazımı başarısız olduysa
    /// (audit best-effort). <c>Teacher.UpdateTime</c> KULLANILMAZ — sonraki her profil güncellemesinde değişir.
    /// </summary>
    public DateTime? DecidedAt { get; set; }
}

/// <summary>
/// issue #262: <c>GET api/admin/teacher-applications/{id}</c> yanıtı — liste satırıyla aynı alanlar, fark: <see cref="Email"/> bekleyen başvuruda TAM.
/// issue #187: her durumdaki (Pending/Approved/Rejected) başvuru için döner; başvuru olmayan öğretmen → 404.
/// Her çağrı <c>AdminDataAccessLogs</c>'a (Resource=TeacherApplicationDetail, TargetId=TeacherId) yazılır; bulunamayan id
/// de <c>Outcome=NotFound</c> ile (id tarama denemeleri görünür olsun).
/// </summary>
public class TeacherApplicationDetailDto
{
    public int TeacherId { get; set; }

    // issue #262: UserId dönülmez (bkz. TeacherApplicationListItemDto).

    /// <summary>auth-api'den çözümlenir; erişilemezse boş string.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// TAM e-posta YALNIZCA <see cref="Status"/>=Pending iken (karar için); Approved/Rejected'da maskeli (<c>a***@x.com</c>,
    /// issue #187 security review). auth-api'den çözümlenemezse boş string.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    public DateTime AppliedAt { get; set; }

    public bool IsIndependentTutor { get; set; }

    public int? RequestedSchoolId { get; set; }

    public string? RequestedSchoolName { get; set; }

    /// <summary>issue #187: bkz. <see cref="TeacherApplicationListItemDto.Status"/>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>issue #187: bkz. <see cref="TeacherApplicationListItemDto.RejectionReason"/>.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>issue #187: bkz. <see cref="TeacherApplicationListItemDto.DecidedAt"/>.</summary>
    public DateTime? DecidedAt { get; set; }
}

public class TeacherRejectRequestDto
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "admin.teacherApplication.rejectReasonRequired")]
    [MaxLength(500, ErrorMessage = "admin.teacherApplication.rejectReasonMaxLength")]
    public string Reason { get; set; } = string.Empty;
}
