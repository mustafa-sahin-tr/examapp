using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #1019: plan kataloğu (admin).</summary>
public class SubscriptionPlanDto
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
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
