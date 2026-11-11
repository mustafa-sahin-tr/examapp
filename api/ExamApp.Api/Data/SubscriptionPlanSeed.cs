using System;
using System.Collections.Generic;
using System.Linq;
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
    public const int TeacherBasicPlanId = 5;
    public const int TeacherEssentialPlanId = 6;
    public const int StudentBasicPlanId = 7;
    public const int StudentEssentialPlanId = 8;
    public const int StudentProPlanId = 9;
    public const int DefaultTrialDays = 14;

    private static readonly DateTime SeedTime = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    // Issue #1093 (epic #1085): her kitleye Basic / Essential / Pro. Fiyat ve sınırlar sahibin yer tutucularıdır (#1082, #1084);
    // admin plan düzenleyicisi (#1086) değiştirir. Açık / kapalı özellik = null (sınırsız) satır, satır yok = planda yok.
    private static readonly (int PlanId, string Feature, int? Limit)[] TierLimits =
    [
        (TeacherBasicPlanId, EntitlementFeatures.ManualLabeling, null),

        (TeacherEssentialPlanId, EntitlementFeatures.ManualLabeling, null),
        (TeacherEssentialPlanId, EntitlementFeatures.AutoDetect, null),
        (TeacherEssentialPlanId, EntitlementFeatures.AiClassification, 500),
        (TeacherEssentialPlanId, EntitlementFeatures.CanvasAi, 300),
        (TeacherEssentialPlanId, EntitlementFeatures.QuickAdd, 100),
        (TeacherEssentialPlanId, EntitlementFeatures.ActivityEditor, null),
        (TeacherEssentialPlanId, EntitlementFeatures.FlashcardSets, null),
        (TeacherEssentialPlanId, EntitlementFeatures.CoachPanel, null),
        (TeacherEssentialPlanId, EntitlementFeatures.PrintTest, null),
        (TeacherEssentialPlanId, EntitlementFeatures.Messaging, null),
        (TeacherEssentialPlanId, EntitlementFeatures.Tutoring, null),
        (TeacherEssentialPlanId, EntitlementFeatures.LiveLesson, 10),

        // Pro'nun book-import (10), ai-classification (1000) ve quick-add (200) satırları #1019 / #1052'den kalır.
        (TeacherProPlanId, EntitlementFeatures.ManualLabeling, null),
        (TeacherProPlanId, EntitlementFeatures.AutoDetect, null),
        (TeacherProPlanId, EntitlementFeatures.CanvasAi, 1000),
        (TeacherProPlanId, EntitlementFeatures.QuestionGeneration, 200),
        (TeacherProPlanId, EntitlementFeatures.ActivityEditor, null),
        (TeacherProPlanId, EntitlementFeatures.FlashcardSets, null),
        (TeacherProPlanId, EntitlementFeatures.CoachPanel, null),
        (TeacherProPlanId, EntitlementFeatures.PrintTest, null),
        (TeacherProPlanId, EntitlementFeatures.Messaging, null),
        (TeacherProPlanId, EntitlementFeatures.Tutoring, null),
        (TeacherProPlanId, EntitlementFeatures.LiveLesson, 40),

        (StudentBasicPlanId, EntitlementFeatures.MockExam, 3),
        (StudentBasicPlanId, EntitlementFeatures.MixedMini, 3),
        (StudentBasicPlanId, EntitlementFeatures.Practice, 10),
        (StudentBasicPlanId, EntitlementFeatures.SelfActivity, 10),

        (StudentEssentialPlanId, EntitlementFeatures.MockExam, 6),
        (StudentEssentialPlanId, EntitlementFeatures.MixedMini, 6),
        (StudentEssentialPlanId, EntitlementFeatures.Practice, 50),
        (StudentEssentialPlanId, EntitlementFeatures.SelfActivity, 50),
        (StudentEssentialPlanId, EntitlementFeatures.StudyTools, null),
        (StudentEssentialPlanId, EntitlementFeatures.Messaging, null),
        (StudentEssentialPlanId, EntitlementFeatures.Tutoring, null),
        (StudentEssentialPlanId, EntitlementFeatures.LiveLesson, null),

        (StudentProPlanId, EntitlementFeatures.MockExam, null),
        (StudentProPlanId, EntitlementFeatures.MixedMini, null),
        (StudentProPlanId, EntitlementFeatures.Practice, null),
        (StudentProPlanId, EntitlementFeatures.SelfActivity, null),
        (StudentProPlanId, EntitlementFeatures.StudyTools, null),
        (StudentProPlanId, EntitlementFeatures.Analytics, null),
        (StudentProPlanId, EntitlementFeatures.Messaging, null),
        (StudentProPlanId, EntitlementFeatures.Tutoring, null),
        (StudentProPlanId, EntitlementFeatures.LiveLesson, null),

        // Deneme iki kitleye de açık ve Pro gibi davranır; sayılan özelliklerde deneme sınırı. book-import (1), ai-classification
        // (50), mock-exam (2), quick-add (20) satırları önceki seed'den kalır.
        (TrialPlanId, EntitlementFeatures.ManualLabeling, null),
        (TrialPlanId, EntitlementFeatures.AutoDetect, null),
        (TrialPlanId, EntitlementFeatures.CanvasAi, 30),
        (TrialPlanId, EntitlementFeatures.QuestionGeneration, 20),
        (TrialPlanId, EntitlementFeatures.ActivityEditor, null),
        (TrialPlanId, EntitlementFeatures.FlashcardSets, null),
        (TrialPlanId, EntitlementFeatures.CoachPanel, null),
        (TrialPlanId, EntitlementFeatures.PrintTest, null),
        (TrialPlanId, EntitlementFeatures.Messaging, null),
        (TrialPlanId, EntitlementFeatures.Tutoring, null),
        (TrialPlanId, EntitlementFeatures.LiveLesson, 2),
        (TrialPlanId, EntitlementFeatures.MixedMini, 2),
        (TrialPlanId, EntitlementFeatures.Practice, 10),
        (TrialPlanId, EntitlementFeatures.SelfActivity, 10),
        (TrialPlanId, EntitlementFeatures.StudyTools, null),
        (TrialPlanId, EntitlementFeatures.Analytics, null),

        // #1087: soru girişi plana bağlandı; Free bugünkü öğretmen yeteneklerini (elle soru, otomatik tespit) kilitleme işine (#1090)
        // kadar korur. Sona eklenir: önceki satırların id'leri değişmez.
        (FreePlanId, EntitlementFeatures.ManualLabeling, null),
        (FreePlanId, EntitlementFeatures.AutoDetect, null),

        // #1088: öğretmen araçları ve iki taraflı özellikler plana bağlandı; Free bugünkü davranışı #1090'a kadar korur.
        (FreePlanId, EntitlementFeatures.ActivityEditor, null),
        (FreePlanId, EntitlementFeatures.FlashcardSets, null),
        (FreePlanId, EntitlementFeatures.CoachPanel, null),
        (FreePlanId, EntitlementFeatures.PrintTest, null),
        (FreePlanId, EntitlementFeatures.Messaging, null),
        (FreePlanId, EntitlementFeatures.Tutoring, null),
        (FreePlanId, EntitlementFeatures.LiveLesson, null),

        // #1089: öğrencinin kendi çalışması plana bağlandı; Free bugünkü davranışı #1090'a kadar korur (deneme sınavı #1031'deki gibi 0).
        (FreePlanId, EntitlementFeatures.MixedMini, null),
        (FreePlanId, EntitlementFeatures.Practice, null),
        (FreePlanId, EntitlementFeatures.SelfActivity, null),
        (FreePlanId, EntitlementFeatures.StudyTools, null),
        (FreePlanId, EntitlementFeatures.Analytics, null),

        // #1112: haftalık veli raporu Essential ve Pro'da (Basic yalnız panel); deneme Pro gibi.
        (StudentEssentialPlanId, EntitlementFeatures.WeeklyReport, null),
        (StudentProPlanId, EntitlementFeatures.WeeklyReport, null),
        (TrialPlanId, EntitlementFeatures.WeeklyReport, null),

        // #1111: öğretmene soru sor — Basic'te yok, Essential 10, Pro 40 (ayda); deneme Pro gibi.
        (StudentEssentialPlanId, EntitlementFeatures.AskTeacher, 10),
        (StudentProPlanId, EntitlementFeatures.AskTeacher, 40),
        (TrialPlanId, EntitlementFeatures.AskTeacher, 40),
    ];

    private const int FirstTierLimitId = 13;

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubscriptionPlan>().HasData(Plans());
        modelBuilder.Entity<SubscriptionPlanFeatureLimit>().HasData(Limits());
        modelBuilder.Entity<SubscriptionPlanPrice>().HasData(Prices());
    }

    // Issue #1108 (epic #1118): öğrenci planının aylık, bütün dersler fiyatı band başına (sahibin onayladığı yer tutucular). Diğer
    // seçenekler bundan türetilir: tek ders %60 (yalnız Basic / Essential), 6 ay 5.4×, 12 ay 9×, yaz 0.5×; tam liraya yuvarlanır.
    private static readonly (StudentGradeBand Band, decimal Basic, decimal Essential, decimal Pro)[] BandMonthlyPrices =
    [
        (StudentGradeBand.Primary, 149m, 249m, 399m),
        (StudentGradeBand.Middle, 199m, 349m, 549m),
        (StudentGradeBand.Lgs, 249m, 449m, 699m),
        (StudentGradeBand.HighSchool, 249m, 449m, 699m),
        (StudentGradeBand.Yks, 299m, 549m, 899m),
    ];

    private static readonly (PlanDuration Duration, decimal Factor)[] DurationFactors =
    [
        (PlanDuration.Monthly, 1m),
        (PlanDuration.SixMonths, 5.4m),
        (PlanDuration.TwelveMonths, 9m),
        (PlanDuration.Summer, 0.5m),
    ];

    public const decimal SingleSubjectFactor = 0.6m;

    /// <inheritdoc cref="Plans"/>
    public static SubscriptionPlanPrice[] Prices()
    {
        var rows = new List<SubscriptionPlanPrice>();
        foreach (var (band, basic, essential, pro) in BandMonthlyPrices)
        {
            foreach (var (planId, monthly, single) in new[] { (StudentBasicPlanId, basic, true), (StudentEssentialPlanId, essential, true), (StudentProPlanId, pro, false) })
            {
                foreach (var (scope, scopeFactor) in single
                             ? new[] { (PlanScope.AllSubjects, 1m), (PlanScope.SingleSubject, SingleSubjectFactor) }
                             : new[] { (PlanScope.AllSubjects, 1m) })
                {
                    foreach (var (duration, factor) in DurationFactors)
                    {
                        rows.Add(new SubscriptionPlanPrice
                        {
                            Id = rows.Count + 1,
                            PlanId = planId,
                            Band = band,
                            Scope = scope,
                            Duration = duration,
                            Price = decimal.Round(monthly * scopeFactor * factor, 0, MidpointRounding.AwayFromZero),
                            CreateTime = SeedTime,
                        });
                    }
                }
            }
        }
        return rows.ToArray();
    }

    /// <summary>Katalog satırları (seed). Entegrasyon testleri veritabanını boşalttıktan sonra kataloğu bunlarla geri yükler.</summary>
    public static SubscriptionPlan[] Plans() =>
        [
            Plan(FreePlanId, SubscriptionPlan.FreeKey, "Free", "free", SubscriptionPlanAudience.Any, 0m, null, 0),
            Plan(TrialPlanId, SubscriptionPlan.TrialKey, "Trial", "trial", SubscriptionPlanAudience.Any, 0m, DefaultTrialDays, 1),
            Plan(TeacherBasicPlanId, SubscriptionPlan.TeacherBasicKey, "Teacher Basic", "teacherBasic", SubscriptionPlanAudience.Teacher, 149m, null, 2),
            Plan(TeacherEssentialPlanId, SubscriptionPlan.TeacherEssentialKey, "Teacher Essential", "teacherEssential", SubscriptionPlanAudience.Teacher, 299m, null, 3),
            Plan(TeacherProPlanId, SubscriptionPlan.TeacherProKey, "Teacher Pro", "teacherPro", SubscriptionPlanAudience.Teacher, 599m, null, 4),
            Plan(StudentBasicPlanId, SubscriptionPlan.StudentBasicKey, "Student Basic", "studentBasic", SubscriptionPlanAudience.Student, 99m, null, 5),
            Plan(StudentEssentialPlanId, SubscriptionPlan.StudentEssentialKey, "Student Essential", "studentEssential", SubscriptionPlanAudience.Student, 199m, null, 6),
            Plan(StudentProPlanId, SubscriptionPlan.StudentProKey, "Student Pro", "studentPro", SubscriptionPlanAudience.Student, 349m, null, 7),
            Plan(StudentPlusPlanId, SubscriptionPlan.StudentPlusKey, "Student Plus", "studentPlus", SubscriptionPlanAudience.Student, 99m, null, 8, isActive: false),
        ];

    /// <inheritdoc cref="Plans"/>
    public static SubscriptionPlanFeatureLimit[] Limits() =>
            new[]
            {
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
                Limit(12, TeacherProPlanId, EntitlementFeatures.QuickAdd, 200),
            }.Concat(TierLimits.Select((l, i) => Limit(FirstTierLimitId + i, l.PlanId, l.Feature, l.Limit))).ToArray();

    private static SubscriptionPlan Plan(int id, string key, string name, string displayKey, SubscriptionPlanAudience audience,
        decimal price, int? trialDays, int sortOrder, bool isActive = true) => new()
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
        IsActive = isActive,
        CreateTime = SeedTime,
    };

    private static SubscriptionPlanFeatureLimit Limit(int id, int planId, string featureKey, int? monthlyLimit) => new()
    {
        Id = id, PlanId = planId, FeatureKey = featureKey, MonthlyLimit = monthlyLimit, CreateTime = SeedTime,
    };
}
