using System;

namespace ExamApp.Foundation.Contracts;

public class LoginAttemptedEvent
{
    /// <summary>
    /// Producer-assigned correlation id (matches the outbox row's <c>OutboxMessage.Id</c>).
    /// Consumers must dedupe on this, not on the (user, timestamp, success) tuple — two
    /// distinct attempts can legitimately land in the same UTC tick under load.
    /// </summary>
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Keycloak <c>sub</c> claim'i. Yalnızca kimliği Keycloak tarafından doğrulanmış denemelerde
    /// (Success=true) doludur; başarısız denemede henüz sub yoktur ve <c>null</c> kalır (issue #100 —
    /// eskiden buraya doğrulanmamış e-posta ya da "unknown" yazılıyordu).
    /// </summary>
    public string? KeycloakUserId { get; set; }

    /// <summary>
    /// Başarısız denemede kullanıcının login formuna girdiği, DOĞRULANMAMIŞ tanımlayıcı (e-posta) —
    /// PII (issue #100). Gerçek kimlik değildir; <see cref="KeycloakUserId"/> ile karıştırılmaz ve
    /// okuma uçlarında (dashboard vb.) DTO'ya taşınmaz. Başarılı denemede ve code-exchange hatasında
    /// <c>null</c>. Opsiyonel alan: eski üreticilerin mesajları bu alan olmadan da okunur.
    /// </summary>
    public string? AttemptedIdentifier { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public bool Success { get; set; }
}
