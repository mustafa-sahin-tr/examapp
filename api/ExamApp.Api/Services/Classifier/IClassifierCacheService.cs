using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.Classifier;

public interface IClassifierCacheService
{
    /// <summary>Current cache pointer + whether it is stale vs. the live taxonomy.</summary>
    Task<ClassifierCacheStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <summary>What BadgeService reads: the cached-content name + model to use right now.</summary>
    Task<(string? CachedContentName, string Model)> GetActivePointerAsync(CancellationToken ct = default);

    /// <summary>Rebuild the Gemini cached content from the live taxonomy and persist the new pointer.</summary>
    Task<ClassifierCacheRefreshResultDto> RefreshAsync(int userId, CancellationToken ct = default);
}
