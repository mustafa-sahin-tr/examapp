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

    /// <summary>
    /// The live taxonomy JSON (same payload the Gemini cache is built from) + its hash. Read by the Claude
    /// classifier in BadgeService (issue #469), which caches it with Anthropic prompt caching instead.
    /// Issue #565: a positive <paramref name="gradeId"/> restricts it to that grade's subjects and topics
    /// (same shape, hash of the filtered JSON); null or &lt;= 0 = the whole taxonomy.
    /// Issue #577: a positive <paramref name="topicId"/> further restricts it to that single topic (and its subject) — the
    /// book-TOC hybrid mode classifies only the subtopic within a known topic. An unknown topic (or one outside the grade)
    /// yields an empty taxonomy.
    /// </summary>
    Task<ClassifierTaxonomyDto> GetTaxonomyAsync(int? gradeId = null, int? topicId = null, CancellationToken ct = default);

    /// <summary>Rebuild the Gemini cached content from the live taxonomy and persist the new pointer.</summary>
    Task<ClassifierCacheRefreshResultDto> RefreshAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Rebuild only if the cache is stale vs. the live taxonomy. Entry point for the
    /// debounced Hangfire job scheduled on every taxonomy change (and the hourly
    /// safety-net reconciler), so a burst of edits results in a single rebuild.
    /// </summary>
    Task RefreshIfStaleAsync(int userId);
}
