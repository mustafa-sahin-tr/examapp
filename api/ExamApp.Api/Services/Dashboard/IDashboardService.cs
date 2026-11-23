using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.Dashboard;

/// <summary>Admin dashboard aggregate sayıları.</summary>
public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>Bugün dahil son <paramref name="days"/> YEREL takvim günü (Dashboard:TimeZone, issue #265) için günlük trend serileri.</summary>
    Task<DashboardTrendsDto> GetTrendsAsync(int days, CancellationToken ct = default);

    /// <summary>
    /// Issue #1315: <paramref name="group"/> (<see cref="ClassificationRetryGroup"/>) içindeki soruların sınıflandırma olayını
    /// outbox'a yeniden yazar (BadgeService sınıflandırıcısı). En çok <see cref="DashboardService.ClassificationRetryBatchSize"/> soru.
    /// </summary>
    Task<ClassificationRetryResultDto> QueueClassificationRetryAsync(ClassificationRetryGroup group, CancellationToken ct = default);
}

/// <summary>Issue #1315: yeniden sınıflandırılabilen panel grupları.</summary>
public enum ClassificationRetryGroup
{
    /// <summary>Konusuz, sınıflandırıcı hiç çalışmamış (kaynak AI değil).</summary>
    NotClassified,

    /// <summary>Sınıflandırıcı çalışmış (kaynak AI) ama konu atayamamış.</summary>
    AiNoTopic,
}
