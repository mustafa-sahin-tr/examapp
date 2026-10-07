using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #419 (epic #407 V1) — veli, öğrencinin ürettiği davet kodunu kullanıp bağlantı Active olduğunda exam API tarafından
/// bağlantıyla AYNI transaction'da outbox'a yazılır. Tüketici V5'te (#423, bildirimler) gelir; şimdilik yalnız yayınlanır.
///
/// Güvenlik: davet kodu (ya da hash'i), ad, e-posta ve token TAŞINMAZ — yalnızca id'ler. Tüketici bildirim hedefini
/// (Keycloak sub, ad) kendi verisinden / auth-api'den çözer.
/// </summary>
public class ParentLinkedEvent
{
    /// <summary>Idempotency anahtarı — bu bağlantı aktivasyonu için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>ParentStudentLinks.Id.</summary>
    public int LinkId { get; set; }

    /// <summary>Parents.Id.</summary>
    public int ParentId { get; set; }

    /// <summary>Velinin exam/auth user id'si (Parents.UserId).</summary>
    public int ParentUserId { get; set; }

    /// <summary>Students.Id.</summary>
    public int StudentId { get; set; }

    /// <summary>Öğrencinin exam/auth user id'si (Students.UserId).</summary>
    public int StudentUserId { get; set; }

    /// <summary>Bağlantının Active olduğu an (UTC).</summary>
    public DateTime LinkedAtUtc { get; set; }
}
