using System;
using ExamApp.Api.Services.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1019: migration ile gelen başlangıç kataloğu — yalnız Free. Free öğretmen gerçek kitap aktarımı yapamaz
/// (<c>book-import</c> = 0; demo #1032). Ücretli planlar ve diğer sınırlar #1027'de eklenir.
/// </summary>
public static class SubscriptionPlanSeed
{
    public const int FreePlanId = 1;
    private static readonly DateTime SeedTime = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubscriptionPlan>().HasData(
            new SubscriptionPlan { Id = FreePlanId, Key = SubscriptionPlan.FreeKey, Name = "Free", IsActive = true, CreateTime = SeedTime });

        modelBuilder.Entity<SubscriptionPlanFeatureLimit>().HasData(
            new SubscriptionPlanFeatureLimit
            {
                Id = 1, PlanId = FreePlanId, FeatureKey = EntitlementFeatures.BookImport, MonthlyLimit = 0, CreateTime = SeedTime,
            });
    }
}
