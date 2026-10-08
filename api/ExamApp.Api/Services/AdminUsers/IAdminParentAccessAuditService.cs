using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>Admin veli erişim kaydı okuma yüzeyi (issue #424, epic #407 V6). Salt okunur.</summary>
public interface IAdminParentAccessAuditService
{
    /// <summary>
    /// Sayfalı liste, en yeni erişim önce (eşitlikte Id azalan). Filtreler opsiyonel: veli (<c>Parent.Id</c>), öğrenci
    /// (<c>Student.Id</c>), <c>From</c> (dahil) / <c>To</c> (hariç) UTC aralığı. İkisi birlikte verilirse <c>From &lt; To</c>
    /// (yoksa <see cref="AdminParentAccessAuditListStatus.InvalidRange"/>) ve en fazla
    /// <see cref="AdminParentAccessAuditQuery.MaxRangeDays"/> gün (yoksa <see cref="AdminParentAccessAuditListStatus.RangeTooLong"/>);
    /// hata durumunda veri okunmaz. <paramref name="page"/> &lt; 1 → 1; <paramref name="pageSize"/>
    /// [1, <see cref="AdminListPaging.MaxPageSize"/>] aralığına kırpılır.
    /// </summary>
    Task<AdminParentAccessAuditListResult> ListAsync(
        AdminParentAccessAuditQuery query, int page, int pageSize, CancellationToken ct = default);
}
