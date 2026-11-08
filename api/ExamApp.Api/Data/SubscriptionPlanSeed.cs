using System;
using ExamApp.Api.Services.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

/// <summary>
/// Migration ile gelen MVP kataloğu (#1019 Free, #1027 diğerleri). Değerler (sınır, fiyat, deneme süresi) veritabanından
/// değiştirilebilir; seed yalnız başlangıçtır. Free öğretmen gerçek kitap aktarımı yapamaz (<c>book-import</c> = 0; demo #1032).
/// Bir planda satırı olmayan özellik 0'dır (ör. TeacherPro'da <c>mock-exam</c>).
/// </summary>
public static class SubscriptionPlanSeed
{
    public const int FreePlanId = 1;
    public const int TrialPlanId = 2;
    public const int TeacherProPlanId = 3;
    public const int StudentPlusPlanId = 4;
    public const int DefaultTrialDays = 14;

    private static readonly DateTime SeedTime = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubscriptionPlan>().HasData(
            Plan(FreePlanId, SubscriptionPlan.FreeKey, "Free", "free", SubscriptionPlanAudience.Any, 0m, null, 0),
            Plan(TrialPlanId, SubscriptionPlan.TrialKey, "Trial", "trial", SubscriptionPlanAudience.Any, 0m, DefaultTrialDays, 1),
            Plan(TeacherProPlanId, SubscriptionPlan.TeacherProKey, "Teacher Pro", "teacherPro", SubscriptionPlanAudience.Teacher, 199m, null, 2),
            Plan(StudentPlusPlanId, SubscriptionPlan.StudentPlusKey, "Student Plus", "studentPlus", SubscriptionPlanAudience.Student, 99m, null, 3));

        modelBuilder.Entity<SubscriptionPlanFeatureLimit>().HasData(
            Limit(1, FreePlanId, EntitlementFeatures.BookImport, 0),
            Limit(2, FreePlanId, EntitlementFeatures.AiClassification, 20),
            Limit(3, FreePlanId, EntitlementFeatures.MockExam, 0),
            Limit(4, TrialPlanId, EntitlementFeatures.BookImport, 1),
            Limit(5, TrialPlanId, EntitlementFeatures.AiClassification, 50),
            Limit(6, TrialPlanId, EntitlementFeatures.MockExam, 2),
            Limit(7, TeacherProPlanId, EntitlementFeatures.BookImport, 10),
            Limit(8, TeacherProPlanId, EntitlementFeatures.AiClassification, 1000),
            Limit(9, StudentPlusPlanId, EntitlementFeatures.MockExam, null),
            // Issue #1052: hızlı ekleme fotoğraf başına sayılır; Free da birkaç fotoğrafla dener.
            Limit(10, FreePlanId, EntitlementFeatures.QuickAdd, 5),
            Limit(11, TrialPlanId, EntitlementFeatures.QuickAdd, 20),
            Limit(12, TeacherProPlanId, EntitlementFeatures.QuickAdd, 200));
    }

    private static SubscriptionPlan Plan(int id, string key, string name, string displayKey, SubscriptionPlanAudience audience,
        decimal price, int? trialDays, int sortOrder) => new()
    {
        Id = id,
        Key = key,
        Name = name,
        DisplayNameKey = $"subscription.plans.{displayKey}",
        Audience = audience,
        MonthlyPrice = price,
        Currency = SubscriptionPlan.DefaultCurrency,
        TrialDays = trialDays,
        SortOrder = sortOrder,
        IsActive = true,
        CreateTime = SeedTime,
    };

    private static SubscriptionPlanFeatureLimit Limit(int id, int planId, string featureKey, int? monthlyLimit) => new()
    {
        Id = id, PlanId = planId, FeatureKey = featureKey, MonthlyLimit = monthlyLimit, CreateTime = SeedTime,
    };
}
