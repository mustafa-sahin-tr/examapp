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
}
