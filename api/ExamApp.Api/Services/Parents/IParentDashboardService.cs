using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.ParentDashboard;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Veli paneli okuma modeli (issue #420, epic #407 V2). Her çağrı önce <see cref="IParentChildAccess"/> kapısından geçer,
/// erişim verilirse <see cref="IParentAccessAuditLog"/>'a yazar, sonra yalnızca toplamları hesaplar.
/// </summary>
public interface IParentDashboardService
{
    /// <summary>Çocuğun özet sayıları. Erişim yoksa (Active bağlantı değil, başkasının çocuğu, silinmiş taraf…) null → 404.</summary>
    Task<ParentChildSummaryDto?> GetChildSummaryAsync(int parentUserId, int studentId, CancellationToken ct = default);
}
