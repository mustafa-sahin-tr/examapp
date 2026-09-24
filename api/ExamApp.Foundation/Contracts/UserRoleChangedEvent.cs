using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #277 (madde 4): exam API'nin register/complete-profile uçları (Teacher/Student/Parent
/// controller'ları) bir kullanıcının rolünü Keycloak'ta değiştirdiğinde — ve kendi yerel
/// (worksheet DB) <c>Users.Role</c> kopyasını güncellediğinde — İŞLEMLE AYNI transaction'da
/// outbox'a yazması GEREKEN event. auth-api'nin kendi <c>Users.Role</c> kolonu (identity DB)
/// önceden yalnızca login/token-exchange akışında senkronlanıyordu; bu event ile bir sonraki
/// login'i beklemeden, promptly güncellenir.
///
/// Neden BadgeService'te değil auth-api'de tüketilir: yazılan veri (<c>Users.Role</c>,
/// identity DB) auth-api'nin kendi DB'sinde — kural (bkz. architecture.md, #225 örneği): event'in
/// yazdığı veri hangi serviste ise consumer orada olur.
///
/// Eşleştirme anahtarı KASITLI OLARAK <see cref="KeycloakId"/>: exam API'nin yerel
/// <c>Users.Id</c>'si (worksheet DB'nin kendi identity sequence'ı) auth-api'nin
/// <c>Users.Id</c>'siyle (identity DB, ayrı sequence) AYNI DEĞER UZAYINDA DEĞİL — sayısal id
/// eşleştirmesi yanlış satırı günceller. Keycloak subject (<c>KeycloakId</c>) iki DB'de de aynı,
/// tek güvenilir doğal anahtar. <see cref="UserId"/> yalnızca log/korelasyon amaçlı taşınır
/// (exam API'nin yerel id'si), auth-api tarafında eşleştirme için KULLANILMAZ.
///
/// Payload minimum tutulur: id'ler + yeni rol + zaman damgası. E-posta/token taşınmaz.
///
/// <para>
/// Exam API üreticisi ne yazmalı: <c>Teachers/Students/ParentController</c>'daki
/// <c>SetRoleAsync</c> çağrısından hemen sonra, aynı transaction'da (yerel <c>User.Role</c>
/// güncellemesiyle birlikte, <c>WorksheetAccessRequestService</c>'teki gibi outbox satırı ikinci
/// <c>SaveChanges</c>'te de olabilir — tek transaction yeter) bu event'i outbox'a ekle:
/// <c>EventId = Guid.NewGuid()</c>, <c>KeycloakId = user.KeycloakId</c>,
/// <c>UserId = user.Id</c> (yerel, log amaçlı), <c>NewRole</c> = Keycloak'a atanan rol adı
/// ("Teacher"/"Student"/"Parent"), <c>ChangedAtUtc = DateTime.UtcNow</c>.
/// </para>
/// </summary>
public class UserRoleChangedEvent
{
    /// <summary>Idempotency anahtarı — bu rol değişikliği için üretilen tek event'i tekilleştirir.</summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// Kullanıcının Keycloak subject'i — auth-api tarafında eşleştirme (<c>Users.KeycloakId</c>)
    /// BUNUNLA yapılır, <see cref="UserId"/> ile DEĞİL.
    /// </summary>
    public string KeycloakId { get; set; } = string.Empty;

    /// <summary>Exam API'nin yerel (worksheet DB) User.Id'si — yalnızca log/korelasyon amaçlı.</summary>
    public int UserId { get; set; }

    /// <summary>Keycloak'ta atanan yeni rol adı: "Teacher" | "Student" | "Parent".</summary>
    public string NewRole { get; set; } = string.Empty;

    /// <summary>Değişikliğin gerçekleştiği an (UTC). Sırasız/tekrar teslimde tazelik kontrolü için.</summary>
    public DateTime ChangedAtUtc { get; set; }
}
