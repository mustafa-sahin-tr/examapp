using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Bağımsız öğretmen kaydı akışı (issue #93) — bir öğretmen "bağımsız özel ders" olarak kayıt
/// olduğunda ya da okula bağlıyken bağımsıza geçtiğinde (ApprovalStatus yeniden Pending'e düşer)
/// exam API tarafından Teacher satırıyla aynı transaction'da outbox'a yazılır. BadgeService bunu
/// tüketip admin'e onay bekleyen kayıt bildirimi üretir.
///
/// Yalnızca Pending'e geçiş durumlarında üretilir: aynı değerle tekrar submit ya da
/// bağımsız → okula bağlı geçişte (Approved) event atılmaz.
/// Payload minimum tutulur: id'ler + zaman damgası. Hassas veri taşınmaz.
/// </summary>
public class IndependentTeacherRegisteredEvent
{
    /// <summary>Teacher.Id — idempotency anahtarı.</summary>
    public int TeacherId { get; set; }

    /// <summary>Öğretmenin exam/auth user id'si.</summary>
    public int UserId { get; set; }

    /// <summary>
    /// true: ilk kayıt bağımsız olarak yapıldı; false: mevcut okula bağlı öğretmen bağımsıza geçti.
    /// </summary>
    public bool IsNewRegistration { get; set; }

    /// <summary>Kaydın / geçişin gerçekleştiği an (UTC).</summary>
    public DateTime RegisteredAt { get; set; }
}
