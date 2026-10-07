using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #419 (epic #407 V1) — aktif veli–öğrenci bağlantısı taraflardan biri tarafından koparıldığında (soft revoke:
/// Status=Revoked) exam API tarafından AYNI transaction'da outbox'a yazılır. Tüketici V5'te (#423) gelir.
/// Güvenlik: id + Keycloak sub + kısa görünen ad; asla e-posta/token/davet kodu (bkz. <see cref="ParentLinkedEvent"/>).
/// </summary>
public class ParentUnlinkedEvent
{
    /// <summary>Idempotency anahtarı — bu koparma için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    public int LinkId { get; set; }

    public int ParentId { get; set; }

    public int ParentUserId { get; set; }

    public int StudentId { get; set; }

    public int StudentUserId { get; set; }

    /// <summary>Koparan taraf: "Student" ya da "Parent".</summary>
    public string RevokedByRole { get; set; } = string.Empty;

    /// <summary>Koparan kullanıcının exam/auth user id'si.</summary>
    public int RevokedByUserId { get; set; }

    /// <summary>Velinin Keycloak sub'ı (#423); boş olabilir — bkz. <see cref="ParentLinkedEvent.ParentKeycloakId"/>.</summary>
    public string ParentKeycloakId { get; set; } = string.Empty;

    /// <summary>Öğrencinin Keycloak sub'ı (#423); boş olabilir.</summary>
    public string StudentKeycloakId { get; set; } = string.Empty;

    /// <summary>Velinin kısa görünen adı ("Ad S."); boş olabilir.</summary>
    public string ParentDisplayName { get; set; } = string.Empty;

    /// <summary>Öğrencinin kısa görünen adı ("Ad S."); boş olabilir.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;

    /// <summary>Koparma anı (UTC).</summary>
    public DateTime RevokedAtUtc { get; set; }
}
