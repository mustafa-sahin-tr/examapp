using ExamApp.Api.Data;
using ExamApp.Api.Services.Parents;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Issue #1089: veli paneli çocuğun planına bakar (velinin değil). Bağlantı kapısı (<see cref="IParentChildAccess"/>) önce gelir:
/// başka velinin çocuğu için plan sorulmaz, uç noktanın kendi 404'ü döner.
/// </summary>
public static class ChildEntitlements
{
    /// <summary>Çocuğun planında özellik yoksa 403 <c>entitlement.notInPlan</c>; varsa ya da bağlantı yoksa null.</summary>
    public static async Task<ObjectResult?> DenyUnlessChildHasAsync(IParentChildAccess access, IEntitlementService entitlements,
        int parentUserId, int studentId, string featureKey, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant is null)
            return null;

        var check = await entitlements.CheckAsync(EntitlementSubject.Create(grant.StudentUserId), featureKey, ct);
        return check.Limit == 0 ? EntitlementLimitResponse.NotInPlan(featureKey, check) : null;
    }

    /// <summary>Veli panelinin gösterdiği öğrenci özellikleri: plan anahtarı ve özellik başına "planda var mı".</summary>
    public static async Task<ChildPlanFeaturesDto?> FeaturesAsync(IParentChildAccess access, IParentAccessAuditLog audit,
        IEntitlementService entitlements, int parentUserId, int studentId, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant is null)
            return null;
        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildPlanFeatures, ct: ct);

        var subject = EntitlementSubject.Create(grant.StudentUserId);
        var features = new Dictionary<string, bool>(StringComparer.Ordinal);
        string? planKey = null;
        foreach (var feature in EntitlementFeatures.Registry.Where(f => f.Audience == EntitlementAudience.Student))
        {
            var check = await entitlements.CheckAsync(subject, feature.Key, ct);
            planKey ??= check.PlanKey;
            features[feature.Key] = check.Limit != 0;
        }
        return new ChildPlanFeaturesDto(planKey, features);
    }
}

public sealed record ChildPlanFeaturesDto(string? PlanKey, IReadOnlyDictionary<string, bool> Features);
