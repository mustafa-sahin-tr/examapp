using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Bağımsız öğretmen onay akışı (issue #94) — bir kullanıcı bağımsız öğretmen (<c>IsIndependentTutor</c>)
/// olarak kayıt olduğunda ve bu, admin onayı bekleyen (<c>ApprovalStatus.Pending</c>) YENİ bir
/// başvuru oluşturduğunda exam API tarafından outbox'a yazılır. BadgeService bunu tüketip
/// bağlı olan tüm Admin'lere in-app bildirim (Notifications tablosu) oluşturur ve SignalR ile
/// rol bazlı ("role:Admin" grubu) push eder.
///
/// Payload minimum tutulur: id'ler + bildirim metninde kullanılacak başvuran adı. Hassas veri
/// (e-posta, token) taşınmaz — consumer detayı gerekirse kendisi okur.
/// </summary>
public class TeacherApplicationSubmittedEvent
{
    /// <summary>Teacher.Id — idempotency anahtarı.</summary>
    public int TeacherId { get; set; }

    /// <summary>Başvuran öğretmenin exam/auth user id'si.</summary>
    public int UserId { get; set; }

    /// <summary>Başvuranın görünen adı; bildirim metninde kullanılır (opsiyonel, best-effort).</summary>
    public string? ApplicantName { get; set; }

    /// <summary>Başvurunun oluşturulduğu an (UTC).</summary>
    public DateTime SubmittedAt { get; set; }
}
