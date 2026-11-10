using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.TargetGoals;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Issue #1108 (epic #1118): öğrenci planının fiyatı. Band öğrencinin sınıfından çözülür; fiyat önce o sınıfa özel satırdan,
/// yoksa band satırından gelir. Sınıfı olmayan öğrencide band yoktur: plan eskisi gibi aylık ve bütün dersler,
/// <see cref="SubscriptionPlan.MonthlyPrice"/> ile satılır.
/// </summary>
public static class StudentPlanPricing
{
    /// <summary>Aynı velinin ikinci (ve sonraki) çocuğuna indirim.</summary>
    public const int SiblingDiscountPercent = 20;

    /// <summary>
    /// Tek ders olarak satılan dersler (Subjects.Code): sınav dersleri. Sınıfın diğer dersleri (seçmeli, çalgı, beden eğitimi…)
    /// tek ders planı olarak satılmaz; ders yine sınıfın dersi olmalıdır.
    /// </summary>
    public static readonly string[] SingleSubjectCodes =
    [
        "matematik", "fen-bilimleri", "turkce", "ingilizce", "sosyal-bilgiler", "t-c-inkilap-tarihi-ve-ataturkculuk",
        "fizik", "kimya", "biyoloji", "turk-dili-ve-edebiyati", "tarih", "cografya",
    ];

    /// <summary>Öğrencinin sınıfında tek ders olarak satılan dersler.</summary>
    public static IQueryable<GradeSubject> SingleSubjectsOf(AppDbContext db, int? gradeId) =>
        db.GradeSubjects.AsNoTracking().Where(gs => gs.GradeId == gradeId && gs.Subject.Code != null && SingleSubjectCodes.Contains(gs.Subject.Code));

    /// <summary>Yaz planı Haziran–Ağustos arasında satılır.</summary>
    public static bool SummerOnSale(DateTime nowUtc) => nowUtc.Month is >= 6 and <= 8;

    /// <summary>İstekteki seçenek; süre adı tanınmıyorsa (sayı dahil) null.</summary>
    public static PlanPurchase? PurchaseOf(SubscriptionCheckoutDto request)
    {
        var duration = PlanDuration.Monthly;
        if (!string.IsNullOrEmpty(request.Duration)
            && (!Enum.TryParse(request.Duration, ignoreCase: true, out duration) || !Enum.IsDefined(duration)
                || char.IsDigit(request.Duration[0])))
            return null;
        return new PlanPurchase(request.SubjectId, duration);
    }

    public static StudentGradeBand? BandOf(int? gradeLevel) => gradeLevel switch
    {
        >= 1 and <= 4 => StudentGradeBand.Primary,
        >= 5 and <= 7 => StudentGradeBand.Middle,
        8 => StudentGradeBand.Lgs,
        >= 9 and <= 11 => StudentGradeBand.HighSchool,
        12 => StudentGradeBand.Yks,
        _ => null,
    };

    /// <summary>Atamanın bitişi (hariç). Yaz planı satın alındığı yılın 1 Eylül'ünde biter.</summary>
    public static DateTime ValidTo(DateTime nowUtc, PlanDuration duration) => duration switch
    {
        PlanDuration.SixMonths => nowUtc.AddMonths(6),
        PlanDuration.TwelveMonths => nowUtc.AddMonths(12),
        PlanDuration.Summer => new DateTime(nowUtc.Year, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        _ => nowUtc.AddMonths(1),
    };

    public static decimal Discounted(decimal price, int discountPercent) =>
        discountPercent <= 0 ? price : decimal.Round(price * (100 - discountPercent) / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Öğrencinin sınıf seviyesi (1–12) ve sınıfı; profilde sınıf yoksa ikisi de null.</summary>
    public static async Task<(int? GradeId, int? GradeLevel)> GradeOfAsync(AppDbContext db, int studentUserId, CancellationToken ct)
    {
        var grade = await db.Students.AsNoTracking()
            .Where(s => s.UserId == studentUserId && s.GradeId != null)
            .Select(s => new { s.GradeId, s.Grade!.Code, s.Grade.Name })
            .FirstOrDefaultAsync(ct);
        return grade is null
            ? (null, null)
            : (grade.GradeId, TargetGoalService.GradeLevelOf(grade.Code) ?? TargetGoalService.GradeLevelOf(grade.Name));
    }

    /// <summary>Bandın (ve varsa sınıfın) satışta olan fiyatları; sınıfa özel satır aynı seçeneğin band satırının yerine geçer.</summary>
    public static async Task<List<StudentPlanPriceDto>> PricesAsync(AppDbContext db, StudentGradeBand band, int? gradeLevel,
        CancellationToken ct)
    {
        var rows = await db.SubscriptionPlanPrices.AsNoTracking()
            .Where(p => p.Band == band && (p.GradeLevel == null || p.GradeLevel == gradeLevel)
                && p.Plan.IsActive && p.Plan.Audience == SubscriptionPlanAudience.Student && p.Plan.TrialDays == null)
            .Select(p => new { p.Plan.Key, p.GradeLevel, p.Scope, p.Duration, p.Price })
            .ToListAsync(ct);

        return rows
            .GroupBy(p => (p.Key, p.Scope, p.Duration))
            .Select(g => g.OrderByDescending(p => p.GradeLevel.HasValue).First())
            .OrderBy(p => p.Key, StringComparer.Ordinal).ThenBy(p => p.Scope).ThenBy(p => p.Duration)
            .Select(p => new StudentPlanPriceDto
            {
                PlanKey = p.Key,
                Scope = p.Scope.ToString(),
                Duration = p.Duration.ToString(),
                Amount = p.Price,
            })
            .ToList();
    }

    /// <summary>
    /// Velinin bu çocuğa alırken kardeş indirimi alıp almadığı: Active bağlı başka bir çocuğunun şu an ücretli bir öğrenci planı
    /// (kişisel atama, deneme değil) varsa evet. Okul lisansı sayılmaz.
    /// </summary>
    public static async Task<int> SiblingDiscountAsync(AppDbContext db, int parentUserId, int studentUserId, DateTime nowUtc,
        CancellationToken ct)
    {
        var siblingUserIds = db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Parent.UserId == parentUserId && l.Status == ParentStudentLinkStatus.Active && l.Student.UserId != studentUserId)
            .Select(l => l.Student.UserId);
        var hasPaidSibling = await db.UserSubscriptionPlans.AsNoTracking()
            .AnyAsync(a => siblingUserIds.Contains(a.UserId) && a.ValidFromUtc <= nowUtc && (a.ValidToUtc == null || a.ValidToUtc > nowUtc)
                && a.Plan.Audience == SubscriptionPlanAudience.Student && a.Plan.TrialDays == null && a.Plan.MonthlyPrice > 0, ct);
        return hasPaidSibling ? SiblingDiscountPercent : 0;
    }

    /// <summary>Velinin plan ekranındaki seçenekler: band, tek ders için dersler, fiyatlar, kardeş indirimi.</summary>
    public static async Task<StudentPlanOfferDto> OfferAsync(AppDbContext db, int studentUserId, int? parentUserId, DateTime nowUtc,
        CancellationToken ct)
    {
        var (gradeId, gradeLevel) = await GradeOfAsync(db, studentUserId, ct);
        var band = BandOf(gradeLevel);
        var offer = new StudentPlanOfferDto
        {
            Band = band?.ToString(),
            GradeLevel = gradeLevel,
            SummerOnSale = SummerOnSale(nowUtc),
            SiblingDiscountPercent = parentUserId is { } parentId ? await SiblingDiscountAsync(db, parentId, studentUserId, nowUtc, ct) : 0,
        };
        if (band is null)
            return offer;

        offer.Prices = await PricesAsync(db, band.Value, gradeLevel, ct);
        offer.Subjects = (await SingleSubjectsOf(db, gradeId)
                .Select(gs => new { gs.SubjectId, gs.Subject.Name })
                .Distinct()
                .ToListAsync(ct))
            .OrderBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(s => new StudentPlanSubjectDto { Id = s.SubjectId, Name = s.Name })
            .ToList();
        return offer;
    }
}
