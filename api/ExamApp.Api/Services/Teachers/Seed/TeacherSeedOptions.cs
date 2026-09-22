using System.Collections.Generic;
using ExamApp.Api.Services.Schools.Seed;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// <c>seed-teachers</c> aracının parametreleri (issue #217). Varsayılanlar tam kapsamı verir: #216'daki
/// 10 il, il başına okul limiti yok, outbox event'leri açık, Keycloak tekil admin API.
/// </summary>
public sealed record TeacherSeedOptions
{
    public const string KeycloakModeAdminApi = "admin-api";
    public const string KeycloakModePartialImport = "partial-import";

    public const int DefaultBatchSize = 100;
    public const int MaxBatchSize = 500;

    public IReadOnlyList<string> Provinces { get; init; } = SchoolSeedOptions.DefaultProvinces;

    /// <summary>İl başına en fazla kaç seed okul (ada göre sıralı ilk N). null = sınırsız.</summary>
    public int? LimitSchoolsPerProvince { get; init; }

    /// <summary>true ise auth-api çağrılmaz, hiçbir şey yazılmaz; rapor planı gösterir.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Register akışındaki <c>UserPreferredLocaleChangedEvent</c> outbox satırı yazılsın mı (BadgeService tutarlılığı).
    /// Hacim için <c>--no-events</c> ile kapatılır.
    /// </summary>
    public bool EmitEvents { get; init; } = true;

    /// <summary><see cref="KeycloakModeAdminApi"/> ya da <see cref="KeycloakModePartialImport"/>.</summary>
    public string KeycloakMode { get; init; } = KeycloakModeAdminApi;

    /// <summary>auth-api'ye istek başına kaç hesap (1..500).</summary>
    public int BatchSize { get; init; } = DefaultBatchSize;
}
