using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// Admin'in kişisel veri listelerine erişimini kalıcı olarak kaydeder (issue #246).
/// </summary>
public interface IAdminDataAccessAuditService
{
    /// <summary>
    /// Kaydı yazar. Hata yutulmaz: kayıt yazılamazsa çağıran veriyi DÖNMEMELİDİR (fail-closed) —
    /// denetim izi olmayan kişisel veri erişimi KVKK hesap verebilirlik ilkesine aykırıdır.
    /// </summary>
    Task RecordListAccessAsync(AdminListAccessRecord record, CancellationToken ct = default);
}
