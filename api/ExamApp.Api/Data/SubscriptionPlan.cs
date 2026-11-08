using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1019 (epic #1017, abonelik epic #1026): abonelik planı. Katalog (#1027) yeni planları satır olarak ekler; plan
/// tablolarında özelliğe özgü mantık yoktur, sınırlar <see cref="SubscriptionPlanFeatureLimit"/>'tedir.
/// </summary>
public class SubscriptionPlan : BaseEntity
{
    public const int KeyMaxLength = 50;
    public const int NameMaxLength = 100;

    /// <summary>Ataması olmayan kullanıcının çözüldüğü plan.</summary>
    public const string FreeKey = "free";

    public int Id { get; set; }

    [MaxLength(KeyMaxLength)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<SubscriptionPlanFeatureLimit> FeatureLimits { get; set; } = new List<SubscriptionPlanFeatureLimit>();
}

/// <summary>Planın bir özellik için aylık sınırı; <see cref="MonthlyLimit"/> null = sınırsız, satır yoksa 0 (izin yok).</summary>
public class SubscriptionPlanFeatureLimit : BaseEntity
{
    public const int FeatureKeyMaxLength = 50;

    public int Id { get; set; }
    public int PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;

    [MaxLength(FeatureKeyMaxLength)]
    public string FeatureKey { get; set; } = string.Empty;

    public int? MonthlyLimit { get; set; }
}

/// <summary>
/// Kullanıcının planı. Kullanıcılar auth-api'de olduğundan <see cref="UserId"/> FK'siz. Hesap başına tek geçerli plan: yeni atama
/// açık olanı kapatır; yine de çözümleme birden fazla geçerli atamada en son başlayanı seçer (abonelik epic'i genişletebilir).
/// </summary>
public class UserSubscriptionPlan : BaseEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;
    public DateTime ValidFromUtc { get; set; }

    /// <summary>Hariç üst sınır; null = süresiz.</summary>
    public DateTime? ValidToUtc { get; set; }
}

/// <summary>
/// Sınırlı bir özelliğin bir kullanımı. Ay sayacı bu satırlardan hesaplanır (UTC takvim ayı). <see cref="ReferenceKey"/>
/// özellik içinde tekildir: aynı iş iki kez sayılmaz (ör. kitap aktarımında iş id'si).
/// </summary>
public class EntitlementUsage
{
    public const int ReferenceKeyMaxLength = 100;

    public long Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(SubscriptionPlanFeatureLimit.FeatureKeyMaxLength)]
    public string FeatureKey { get; set; } = string.Empty;

    [MaxLength(ReferenceKeyMaxLength)]
    public string ReferenceKey { get; set; } = string.Empty;

    public DateTime UsedAtUtc { get; set; }
}
