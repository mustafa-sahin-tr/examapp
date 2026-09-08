using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.Dashboard;

/// <summary>Admin dashboard aggregate sayıları.</summary>
public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>Bugün dahil son <paramref name="days"/> gün için günlük trend serileri.</summary>
    Task<DashboardTrendsDto> GetTrendsAsync(int days, CancellationToken ct = default);
}
