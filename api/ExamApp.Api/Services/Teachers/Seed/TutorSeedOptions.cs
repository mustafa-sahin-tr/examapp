using System.Collections.Generic;
using ExamApp.Api.Services.Schools.Seed;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// <c>seed-tutors</c> aracının parametreleri (issue #218). Taban: seçilen illerin <c>IsSeedData</c> okullarındaki
/// seed öğretmenler (il başına okul limiti <c>seed-teachers</c> ile aynı sıralamayla uygulanır, böylece aynı
/// limitle koşulan iki komut tutarlı sayılar verir).
/// </summary>
public sealed record TutorSeedOptions
{
    public IReadOnlyList<string> Provinces { get; init; } = SchoolSeedOptions.DefaultProvinces;

    /// <summary>İl başına en fazla kaç seed okulun öğretmeni tabana sayılır (ada göre sıralı ilk N). null = tümü.</summary>
    public int? LimitSchoolsPerProvince { get; init; }

    /// <summary>true ise auth-api çağrılmaz, hiçbir şey yazılmaz; rapor planı gösterir.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// 0..1: her il+branş grubunda tutor'ların bu oranı (<c>floor</c>) <c>ApprovalStatus=Pending</c> kalır — admin onay
    /// akışını denemek için. null = ortama göre varsayılan: Development 0 (hepsi Approved: aramada görünsün diye
    /// bilinçli sapma), Staging 1 (hepsi Pending: paylaşılan ortamda onaysız tutor aramada çıkmasın).
    /// </summary>
    public double? PendingRatio { get; init; }

    public static double DefaultPendingRatioFor(Microsoft.Extensions.Hosting.IHostEnvironment environment)
        => environment.IsStaging() ? 1d : 0d;

    public bool EmitEvents { get; init; } = true;

    public string KeycloakMode { get; init; } = TeacherSeedOptions.KeycloakModeAdminApi;

    public int BatchSize { get; init; } = TeacherSeedOptions.DefaultBatchSize;
}
