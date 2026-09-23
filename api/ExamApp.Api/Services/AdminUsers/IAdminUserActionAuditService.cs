using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// Admin hesap aksiyonları (şifre sıfırlama #156; #155 aynı servisi kullanır) için <see cref="AdminUserActionLog"/> kaydı.
/// Sır/PII yazılmaz: aktör sub + hedef tür/id + aksiyon + sonuç.
/// </summary>
public interface IAdminUserActionAuditService
{
    /// <summary>
    /// Yeni satır yazar ve id'sini döner. Hata yutulmaz: yan etkili aksiyonlarda <see cref="AdminUserActionOutcome.Requested"/>
    /// kaydı yazılamazsa çağıran yan etkiyi BAŞLATMAMALIDIR (fail-closed).
    /// </summary>
    Task<long> RecordAsync(AdminUserActionRecord record, AdminUserActionOutcome outcome, CancellationToken ct = default);

    /// <summary>Var olan satırın sonucunu günceller. Hata yutulmaz; çağıran best-effort ise kendisi yakalar.</summary>
    Task UpdateOutcomeAsync(long id, AdminUserActionOutcome outcome, CancellationToken ct = default);
}
