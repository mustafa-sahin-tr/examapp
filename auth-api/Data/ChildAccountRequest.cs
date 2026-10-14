using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Issue #438: çocuk hesabı isteğinin durumu (<see cref="ChildAccountRequest.State"/>).</summary>
public enum ChildAccountRequestState
{
    /// <summary>Create sürüyor (Keycloak kullanıcısı henüz yok ya da aşamalar tamamlanmadı).</summary>
    InProgress = 1,

    /// <summary>Hesap tamamlandı (identity satırı yazıldı) — aynı requestId mevcut hesabı döner.</summary>
    Completed = 2,

    /// <summary>
    /// Geri alındı (exam API kompanzasyonu <c>DELETE by-request</c>) ya da create başarısız oldu. Bu requestId ile hiçbir create
    /// başlamaz/tamamlanmaz; süren create bunu gördüğünde Keycloak kullanıcısını kendisi siler.
    /// </summary>
    Cancelled = 3
}

/// <summary>
/// Issue #438 (re-review): exam API'nin çocuk hesabı requestId'si için identity DB'deki KİLİT NOKTASI. Create başında
/// <see cref="ChildAccountRequestState.InProgress"/> satırı eklenir (PK çakışması = aynı istek); <c>DELETE by-request</c> satırı
/// <see cref="ChildAccountRequestState.Cancelled"/> yapar (yoksa Cancelled olarak ekler, sonradan gelen create başlamaz);
/// create'in Keycloak id kaydı ve identity yazımı yalnız <c>State == InProgress</c> koşuluyla ilerler. Böylece kompanzasyon ile
/// süren create'in yarışı (silme Keycloak kullanıcısından önce / rol ataması ile identity yazımı arasında) yetim hesap bırakmaz.
/// Not: eski Completed/Cancelled satırları için temizlik işi yok (satır küçük, veli başına saatte 5 istek sınırı) — gerekirse
/// 30 günden eski satırları silen bir job eklenebilir.
/// </summary>
public class ChildAccountRequest
{
    [Key]
    public Guid RequestId { get; set; }

    public ChildAccountRequestState State { get; set; }

    /// <summary>Keycloak kullanıcısı oluşturulduktan sonra (yalnız InProgress iken) yazılır.</summary>
    [MaxLength(50)]
    public string? KeycloakUserId { get; set; }

    /// <summary>Completed olduğunda identity <c>User.Id</c>.</summary>
    public int? UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
