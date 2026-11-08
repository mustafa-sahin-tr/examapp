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

    public List<FeatureUsageDto> Features { get; set; } = new();
}

/// <summary>Bir özelliğin bu ayki (UTC takvim ayı) hakkı; <see cref="Limit"/> null = sınırsız.</summary>
public class FeatureUsageDto
{
    public string FeatureKey { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public int? Limit { get; set; }
    public int Used { get; set; }
    public int? Remaining { get; set; }
    public DateTime? ResetsAtUtc { get; set; }
}
