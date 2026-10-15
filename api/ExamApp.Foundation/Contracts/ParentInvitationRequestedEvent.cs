using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #440 — öğrenci kayıt sırasında veli e-postası verdiğinde exam API davetle AYNI transaction'da outbox'a yazar.
/// Tüketici: BadgeService ParentInvitationRequestedConsumer (IEmailSender; ExistingParentApproval için ayrıca uygulama içi bildirim).
/// Keycloak execute-actions-email KULLANILMAZ.
///
/// Güvenlik notu: ProtectedActivationUrl tek kullanımlık aktivasyon token'ını şifreli taşır (yalnız NewParentActivation); consumer
/// çözer ve Development dışında ASLA loglamaz.
/// </summary>
public class ParentInvitationRequestedEvent
{
    /// <summary>Gönderim başına tekil idempotency anahtarı: producer her emit'te (yeniden gönderim dahil) Guid.NewGuid() yazar. E-posta/bildirim tekilleştirmesi bununla yapılır.</summary>
    public Guid EventId { get; set; }

    /// <summary>Davet kaydının id'si (yeniden gönderimde AYNI kalır; korelasyon için).</summary>
    public Guid InvitationId { get; set; }

    /// <summary>Bu davet için kaçıncı gönderim (1 = ilk).</summary>
    public int SendCount { get; set; }

    /// <summary>Students.Id.</summary>
    public int StudentId { get; set; }

    /// <summary>Öğrencinin Keycloak sub'ı.</summary>
    public string StudentUserId { get; set; } = string.Empty;

    /// <summary>Öğrencinin adı (e-posta metni için).</summary>
    public string StudentFirstName { get; set; } = string.Empty;

    /// <summary>Velinin Keycloak sub'ı (bildirim hedefi).</summary>
    public string ParentUserId { get; set; } = string.Empty;

    public string ParentEmail { get; set; } = string.Empty;

    public string ParentFullName { get; set; } = string.Empty;

    /// <summary>"NewParentActivation" | "ExistingParentApproval".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Yalnız NewParentActivation için aktivasyon bağlantısı, <c>ActivationLinkProtector.Protect</c> ile ŞİFRELİ (AES-256-GCM, "v1.nonce.ct");
    /// düz metin outbox/RabbitMQ'da durmaz. Çözülmüş URL biçimi <c>/app/parent-activate#token=...</c>. ExistingParentApproval'da null.
    /// </summary>
    public string? ProtectedActivationUrl { get; set; }

    /// <summary>Davetin/aktivasyon bağlantısının geçerlilik sonu (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime OccurredAt { get; set; }
}
