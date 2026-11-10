using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Plan kataloğu (#1019 admin, #1027 kullanıcı). Fiyat yer tutucudur; gerçek ödeme yok.</summary>
public class SubscriptionPlanDto
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayNameKey { get; set; } = string.Empty;

    /// <summary><c>Any</c>, <c>Teacher</c> veya <c>Student</c>.</summary>
    public string Audience { get; set; } = string.Empty;

    public decimal MonthlyPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int? TrialDays { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Özellik anahtarı → aylık sınır (null = sınırsız).</summary>
    public Dictionary<string, int?> MonthlyLimits { get; set; } = new();
}

/// <summary>Issue #1093: özellik listesinden bir satır. <see cref="Kind"/> <c>Toggle</c> ya da <c>Metered</c>.</summary>
public class EntitlementFeatureDto
{
    public string Key { get; set; } = string.Empty;

    /// <summary><c>Teacher</c>, <c>Student</c> veya <c>Both</c>.</summary>
    public string Audience { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    /// <summary>Sayılan özelliğin birimi (ör. <c>book</c>); açık / kapalı özellikte null.</summary>
    public string? Unit { get; set; }

    public static EntitlementFeatureDto From(ExamApp.Api.Services.Entitlements.EntitlementFeatureInfo info) => new()
    {
        Key = info.Key,
        Audience = info.Audience.ToString(),
        Kind = info.Kind.ToString(),
        Unit = info.Unit,
    };
}

/// <summary>Kullanıcıya plan atama isteği; tarih verilmezse şimdi başlar, süresiz.</summary>
public class AssignSubscriptionPlanDto
{
    public string PlanKey { get; set; } = string.Empty;
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
}

/// <summary>Kullanıcının o anki planı.</summary>
public class UserSubscriptionPlanDto
{
    public int UserId { get; set; }
    public string? PlanKey { get; set; }
    public string? PlanName { get; set; }
}

/// <summary>Issue #1027: oturumdaki kullanıcının planı ve bu ayki kullanımı.</summary>
public class CurrentSubscriptionPlanDto
{
    public string? PlanKey { get; set; }
    public string? PlanName { get; set; }
    public string? DisplayNameKey { get; set; }

    /// <summary>Atamanın bitişi (deneme / süreli plan); Free ve süresiz atamada null.</summary>
    public DateTime? ValidToUtc { get; set; }

    /// <summary>#1028: deneme planı çağırana açık ve hesap daha önce deneme almadı.</summary>
    public bool TrialAvailable { get; set; }

    public List<FeatureUsageDto> Features { get; set; } = new();
}

/// <summary>Bir özelliğin bu ayki (UTC takvim ayı) hakkı; <see cref="Limit"/> null = sınırsız.</summary>
public class FeatureUsageDto
{
    public string FeatureKey { get; set; } = string.Empty;

    /// <summary>#1093: <c>Toggle</c> ya da <c>Metered</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? Unit { get; set; }
    public bool Allowed { get; set; }
    public int? Limit { get; set; }
    public int Used { get; set; }
    public int? Remaining { get; set; }
    public DateTime? ResetsAtUtc { get; set; }
}

/// <summary>Issue #1029: plan sınırına takılan isteğin ortak 429 gövdesi (<c>EntitlementLimitResponse</c>).</summary>
public class EntitlementLimitReachedDto
{
    public const string LimitReachedCode = "entitlement.limitReached";

    /// <summary>Issue #1087: özellik planda yok (403).</summary>
    public const string NotInPlanCode = "entitlement.notInPlan";

    /// <summary>Issue #1088: paylaşılan özellik karşı tarafın planında yok (403); <see cref="Message"/> kimin yükselteceğini söyler.</summary>
    public const string CounterpartNotInPlanCode = "entitlement.counterpartNotInPlan";

    public string Code { get; set; } = LimitReachedCode;

    /// <summary>Sınırına ulaşılan özellik anahtarı (ör. <c>book-import</c>).</summary>
    public string Feature { get; set; } = string.Empty;

    public int? Limit { get; set; }
    public int? Used { get; set; }
    public DateTime? ResetsAtUtc { get; set; }

    /// <summary>Kullanıcının çözülen planı (pencerede "mevcut planın" işareti için).</summary>
    public string? PlanKey { get; set; }

    public string? Message { get; set; }
}

/// <summary>Issue #1028: sahte ödeme isteği (gerçek ödeme yok).</summary>
public class SubscriptionCheckoutDto
{
    public string PlanKey { get; set; } = string.Empty;
}
