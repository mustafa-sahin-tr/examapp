namespace ExamApp.Foundation.Contracts;

/// <summary>
/// issue #439: randevu bildirimlerinde bir veli alıcısı. Veli–öğrenci bağlantıları exam DB'de olduğundan (servisler arası veri
/// paylaşımı yasak) "hangi veliler" kararı exam API'de, event yazılırken verilir. <see cref="ParentUserId"/> bildirim satırının
/// sahibidir (Notification.UserId); <see cref="KeycloakId"/> SignalR hedefidir ve auth-api'ye erişilemediyse boş gelebilir —
/// consumer o durumda sub'ı kendi verisinden çözer (NotificationRecipientResolver).
/// </summary>
public class BookingParentRecipient
{
    /// <summary>Velinin exam/auth user id'si (<c>Parent.UserId</c>).</summary>
    public int ParentUserId { get; set; }

    /// <summary>Velinin Keycloak sub'ı; boş olabilir.</summary>
    public string KeycloakId { get; set; } = string.Empty;
}
