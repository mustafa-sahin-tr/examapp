using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Issue #1109: tek ders planı öğrencinin <b>kendi</b> çalışmasını o derse daraltır (pratik, deneme, bilgi kartı, çalışma sayfası,
/// soru havuzu). Öğretmenin atadığı işler, veli paneli ve raporlar daraltılmaz. Kapsam dışı istek 403
/// <c>entitlement.outOfScope</c> (<see cref="EntitlementLimitResponse.OutOfScope"/>).
/// </summary>
public sealed record StudyScope(int? SubjectId, string? PlanKey)
{
    /// <summary>Kapsam yanıtlarının özellik anahtarı (yükseltme penceresi bunu "ders ekle" diye gösterir).</summary>
    public const string Feature = "subject-scope";

    public static readonly StudyScope All = new(null, null);

    /// <summary>Ders kapsamda mı; kapsam yoksa (bütün dersler) ya da ders bilinmiyorsa (null) evet.</summary>
    public bool Covers(int? subjectId) => SubjectId is null || subjectId is null || subjectId == SubjectId;

    public static async Task<StudyScope> ForAsync(IEntitlementService entitlements, int userId, CancellationToken ct = default)
    {
        var plan = await entitlements.ResolvePlanAsync(userId, ct);
        return new StudyScope(plan.ScopeSubjectId, plan.Key);
    }
}

/// <summary>Issue #1109: servis katmanında kapsam dışı istek; denetleyici 403 <c>entitlement.outOfScope</c>'a çevirir.</summary>
public sealed class StudyScopeException(int scopeSubjectId) : Exception("The request is outside the plan's subject.")
{
    public int ScopeSubjectId { get; } = scopeSubjectId;
}
