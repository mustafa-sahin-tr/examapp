using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Issue #1029: plan sınırına takılan her uç noktanın tek hata yanıtı (429 + <see cref="EntitlementLimitReachedDto.LimitReachedCode"/>).
/// Arayüzde tek interceptor bu biçimi yakalayıp yükseltme penceresini açar; yeni sınırlı uç nokta da bunu döndürür.
/// </summary>
public static class EntitlementLimitResponse
{
    public static ObjectResult Create(string featureKey, EntitlementCheck? check, string? message = null) =>
        new(new EntitlementLimitReachedDto
        {
            Feature = featureKey,
            Limit = check?.Limit,
            Used = check?.Used,
            ResetsAtUtc = check?.ResetsAtUtc,
            PlanKey = check?.PlanKey,
            Message = message,
        })
        {
            StatusCode = StatusCodes.Status429TooManyRequests,
        };

    /// <summary>Issue #1087: özellik çağıranın planında yok (sınır 0) — 403, arayüz aynı yükseltme penceresini açar.</summary>
    public static ObjectResult NotInPlan(string featureKey, EntitlementCheck? check, string? message = null) =>
        new(new EntitlementLimitReachedDto
        {
            Code = EntitlementLimitReachedDto.NotInPlanCode,
            Feature = featureKey,
            Limit = 0,
            Used = check?.Used,
            PlanKey = check?.PlanKey,
            Message = message,
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };

    /// <summary>
    /// Issue #1088: karşı tarafın (öğretmen / öğrenci) planı paylaşılan özelliğe izin vermiyor — 403
    /// <c>entitlement.counterpartNotInPlan</c>. Çağıranın yükseltmesi işe yaramaz; arayüz yükseltme penceresi yerine mesajı gösterir.
    /// </summary>
    public static ObjectResult CounterpartNotInPlan(string featureKey, string message) =>
        new(new EntitlementLimitReachedDto
        {
            Code = EntitlementLimitReachedDto.CounterpartNotInPlanCode,
            Feature = featureKey,
            Message = message,
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };

    /// <summary>İki taraflı denetimin yanıtı; izin varsa null. Çağıranın engeli 403 / 429, karşı tarafınki <see cref="CounterpartNotInPlan"/>.</summary>
    public static ObjectResult? ForShared(string featureKey, SharedEntitlementDecision decision, string counterpartMessage) =>
        decision.Block switch
        {
            SharedEntitlementBlock.None => null,
            SharedEntitlementBlock.Self when decision.SelfCheck.Limit == 0 => NotInPlan(featureKey, decision.SelfCheck),
            SharedEntitlementBlock.Self => Create(featureKey, decision.SelfCheck),
            _ => CounterpartNotInPlan(featureKey, counterpartMessage),
        };
}
