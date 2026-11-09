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
    public const int CurrencyMaxLength = 3;

    /// <summary>Ataması olmayan kullanıcının çözüldüğü plan.</summary>
    public const string FreeKey = "free";
    public const string TrialKey = "trial";
    public const string TeacherBasicKey = "teacher-basic";
    public const string TeacherEssentialKey = "teacher-essential";
    public const string TeacherProKey = "teacher-pro";
    public const string StudentBasicKey = "student-basic";
    public const string StudentEssentialKey = "student-essential";
    public const string StudentProKey = "student-pro";

    /// <summary>#1093'ten beri pasif; atamaları <see cref="StudentProKey"/>'e taşındı.</summary>
    public const string StudentPlusKey = "student-plus";
    public const string DefaultCurrency = "TRY";

    public int Id { get; set; }

    [MaxLength(KeyMaxLength)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Arayüzdeki ad için çeviri anahtarı (#1027); <see cref="Name"/> admin / kayıt içindir.</summary>
    [MaxLength(NameMaxLength)]
    public string DisplayNameKey { get; set; } = string.Empty;

    /// <summary>Planın gösterildiği rol (#1027); katalog uç noktası çağıranın rollerine göre süzer.</summary>
    public SubscriptionPlanAudience Audience { get; set; } = SubscriptionPlanAudience.Any;

    /// <summary>Aylık fiyat — şimdilik yer tutucu, gerçek ödeme yok (#1028 sahte ödeme ekranı).</summary>
    public decimal MonthlyPrice { get; set; }

    [MaxLength(CurrencyMaxLength)]
    public string Currency { get; set; } = DefaultCurrency;

    /// <summary>Deneme planında süre (gün); diğerlerinde null. Atamada <c>ValidToUtc</c> buradan hesaplanır (#1028).</summary>
    public int? TrialDays { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<SubscriptionPlanFeatureLimit> FeatureLimits { get; set; } = new List<SubscriptionPlanFeatureLimit>();
}

/// <summary>Planın gösterildiği rol. <see cref="Any"/> her role görünür.</summary>
public enum SubscriptionPlanAudience
{
    Any,
    Teacher,
    Student,
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

/// <summary>
/// Issue #1030: plan atamasındaki her değişikliğin kaydı (kim, ne zaman, eski → yeni). <see cref="BaseEntity"/>'den türemez,
/// silinmez. Plan anahtarı saklanır (plan satırı değişse de kayıt okunur kalır); <c>null</c> = atama yok (Free).
/// <see cref="ActorUserId"/> kendi başlattığı değişiklikte (deneme, ödeme) kullanıcının kendisidir.
/// </summary>
public class SubscriptionPlanAssignmentAudit
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public int ActorUserId { get; set; }
    public SubscriptionPlanAuditAction Action { get; set; }

    [MaxLength(SubscriptionPlan.KeyMaxLength)]
    public string? FromPlanKey { get; set; }

    [MaxLength(SubscriptionPlan.KeyMaxLength)]
    public string? ToPlanKey { get; set; }

    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }

    /// <summary>Okula toplu atamada okul (Schools.Id); tekil atamada null.</summary>
    public int? SchoolId { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>Kalıcı değer string'dir; yeniden adlandırma geçmiş kayıtları bozar.</summary>
public enum SubscriptionPlanAuditAction
{
    Assigned = 1,
    Ended = 2,
}
