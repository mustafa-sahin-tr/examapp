using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// Admin'in kişisel veri listelerine / detaylarına erişimini kalıcı olarak kaydeder (issue #246, #262).
/// </summary>
public interface IAdminDataAccessAuditService
{
    /// <summary>
    /// Kaydı yazar. Hata yutulmaz: kayıt yazılamazsa çağıran veriyi DÖNMEMELİDİR (fail-closed) —
    /// denetim izi olmayan kişisel veri erişimi KVKK hesap verebilirlik ilkesine aykırıdır.
    /// </summary>
    Task RecordListAccessAsync(AdminListAccessRecord record, CancellationToken ct = default);

    /// <summary>issue #262: detay ucu erişimi. <see cref="RecordListAccessAsync"/> ile aynı fail-closed sözleşme.</summary>
    Task RecordDetailAccessAsync(AdminDetailAccessRecord record, CancellationToken ct = default);

    /// <summary>
    /// issue #262: rate limit'e takılan (429) istek — <c>Outcome=RateLimited</c> satırı. Veri dönmediği için çağıran
    /// (429 yazıcısı) hatayı loglayıp yutabilir; 429 yanıtı audit hatası yüzünden değişmemeli.
    /// </summary>
    Task RecordRateLimitedAsync(AdminRateLimitedAccessRecord record, CancellationToken ct = default);
}
