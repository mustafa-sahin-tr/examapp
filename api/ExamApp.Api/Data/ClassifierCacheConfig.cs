using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Single-row pointer to the Gemini cached content that the question classifier
/// (BadgeService) uses as its taxonomy. Rebuilt from the live Subject/Topic/SubTopic
/// tree via the admin endpoint whenever the taxonomy drifts from the cache.
///
/// Not a <see cref="BaseEntity"/> — it is configuration, not soft-deletable domain data.
/// </summary>
public class ClassifierCacheConfig
{
    public const int SingletonId = 1;

    [Key]
    public int Id { get; set; } = SingletonId;

    /// <summary>Gemini resource name, e.g. "cachedContents/abc123". Null until first refresh.</summary>
    [MaxLength(200)]
    public string? CachedContentName { get; set; }

    /// <summary>Model the cached content was created against, e.g. "models/gemini-3.5-flash-lite".</summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>SHA-256 of the taxonomy payload at last refresh — lets the UI flag "cache is stale".</summary>
    [MaxLength(64)]
    public string? TaxonomyHash { get; set; }

    /// <summary>Number of subtopics included in the last successful build (quick sanity signal).</summary>
    public int SubTopicCount { get; set; }

    public DateTime? RefreshedAt { get; set; }

    public int? RefreshedByUserId { get; set; }
}
