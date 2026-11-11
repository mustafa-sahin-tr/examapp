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

    /// <summary>Issue #1108: öğrenci planının band / kapsam / süre fiyatları (yalnız admin kataloğunda dolu).</summary>
    public List<SubscriptionPlanPriceRowDto>? Prices { get; set; }

    /// <summary>Issue #1116: planlanmış aylık fiyat artışı ve yürürlük tarihi (yalnız admin kataloğunda dolu).</summary>
    public decimal? PendingMonthlyPrice { get; set; }

    public DateTime? PendingMonthlyPriceFromUtc { get; set; }
}

/// <summary>Issue #1108: admin fiyat tablosunun bir satırı. <see cref="GradeLevel"/> doluysa yalnız o sınıfa özel fiyat.</summary>
public class SubscriptionPlanPriceRowDto
{
    public string Band { get; set; } = string.Empty;
    public int? GradeLevel { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public decimal Price { get; set; }

    /// <summary>Issue #1116: planlanmış artış ve yürürlük tarihi; yalnız çıktıda (istekte yok sayılır).</summary>
    public decimal? PendingPrice { get; set; }

    public DateTime? PendingFromUtc { get; set; }
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

    /// <summary>#1090: planı yok, uygulama yalnız plan sayfasını açar (<c>Subscriptions:RequirePlan</c> kapalıyken hep false).</summary>
    public bool Locked { get; set; }

    /// <summary>#1092: plan okulun lisansından geliyor; <see cref="ValidToUtc"/> lisansın bitişidir.</summary>
    public bool SchoolLicense { get; set; }

    /// <summary>Issue #1115: öğretmenin planı ücretli planı olan öğrencilerinden geliyor; grace'teyse <c>ValidToUtc</c> bitişi.</summary>
    public bool StudentSponsored { get; set; }

    /// <summary>Issue #1116: süren satın alma (iptal koşuluyla); Free, deneme, admin ataması ve okul lisansında null.</summary>
    public PlanPurchaseDto? Purchase { get; set; }

    public List<FeatureUsageDto> Features { get; set; } = new();
}

/// <summary>
/// Issue #1116: süren satın alma ve iptal koşulu. Aylık planda <see cref="FullRefundUntilUtc"/> null (iade yok, dönem sonuna kadar
/// erişim); 6 / 12 ay / yaz planında tam iade bu ana kadar.
/// </summary>
public class PlanPurchaseDto
{
    public string Duration { get; set; } = string.Empty;
    public decimal PricePaid { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? FullRefundUntilUtc { get; set; }

    public static PlanPurchaseDto? From(ExamApp.Api.Services.Entitlements.PlanPurchaseStatus? status) => status is null ? null : new PlanPurchaseDto
    {
        Duration = status.Duration.ToString(),
        PricePaid = status.PricePaid,
        Currency = status.Currency,
        CancelledAtUtc = status.CancelledAtUtc,
        FullRefundUntilUtc = status.FullRefundUntilUtc,
    };
}

/// <summary>Issue #1116: iptalin sonucu. <see cref="RefundAmount"/> yalnız cayma süresindeki iptalde (plan hemen biter).</summary>
public class PlanCancellationDto
{
    public DateTime? AccessUntilUtc { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? Currency { get; set; }
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

    /// <summary>Issue #1109: tek ders planında başka dersin içeriği (403); <see cref="ScopeSubjectId"/> planın dersi.</summary>
    public const string OutOfScopeCode = "entitlement.outOfScope";

    public string Code { get; set; } = LimitReachedCode;

    /// <summary>Issue #1109: <see cref="OutOfScopeCode"/> yanıtında planın dersi.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? ScopeSubjectId { get; set; }

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

    /// <summary>Issue #1108: tek ders planında ders (öğrencinin sınıfının dersi); null = bütün dersler.</summary>
    public int? SubjectId { get; set; }

    /// <summary>Issue #1108: <c>Monthly</c> (boşken de), <c>SixMonths</c>, <c>TwelveMonths</c> ya da <c>Summer</c>.</summary>
    public string? Duration { get; set; }
}

/// <summary>Issue #1108: öğrenci planı fiyatı (bir kademe × kapsam × süre).</summary>
public class StudentPlanPriceDto
{
    public string PlanKey { get; set; } = string.Empty;

    /// <summary><c>AllSubjects</c> ya da <c>SingleSubject</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    public string Duration { get; set; } = string.Empty;

    /// <summary>Süre boyunca toplam (indirimsiz) fiyat. Adı <c>price</c> değil: veli yanıtlarında <c>price</c> yasak alan (ders ücreti).</summary>
    public decimal Amount { get; set; }
}

public class StudentPlanSubjectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Issue #1108: velinin çocuğa plan seçerken gördüğü seçenekler. <see cref="Band"/> null ise çocuğun sınıfı yok: plan aylık ve
/// bütün dersler, kartın aylık fiyatıyla satılır.
/// </summary>
public class StudentPlanOfferDto
{
    public string? Band { get; set; }
    public int? GradeLevel { get; set; }
    public List<StudentPlanPriceDto> Prices { get; set; } = new();
    public List<StudentPlanSubjectDto> Subjects { get; set; } = new();
    public bool SummerOnSale { get; set; }
    public int SiblingDiscountPercent { get; set; }
}

/// <summary>Issue #1091: veli panelinde çocuğun planı ve velinin alabileceği öğrenci planları.</summary>
public class ChildSubscriptionPlanDto
{
    /// <summary>Çocuğun şu anki planı; ataması yoksa <c>free</c>.</summary>
    public string? PlanKey { get; set; }
    public string? DisplayNameKey { get; set; }

    /// <summary>Planın / denemenin bitişi; süresizse null.</summary>
    public DateTime? ValidToUtc { get; set; }

    public bool IsTrial { get; set; }

    /// <summary>#1092: plan çocuğun okulunun lisansından geliyor.</summary>
    public bool SchoolLicense { get; set; }

    /// <summary>Velinin alabileceği ücretli öğrenci planları (sıralı).</summary>
    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    /// <summary>Plan kartlarının öğrenci özellikleri (veli kendi rolüyle <c>features</c> ucundan bunları alamaz).</summary>
    public List<EntitlementFeatureDto> Features { get; set; } = new();

    /// <summary>Issue #1108: band, kapsam, süre ve fiyat seçenekleri.</summary>
    public StudentPlanOfferDto Offer { get; set; } = new();

    /// <summary>Issue #1108: çocuğun şu anki planının tek dersi (Subjects.Id); bütün derslerde null.</summary>
    public int? ScopeSubjectId { get; set; }

    public string? ScopeSubjectName { get; set; }

    /// <summary>Issue #1116: süren satın alma (veli iptal edebilir); satın alma yoksa null.</summary>
    public PlanPurchaseDto? Purchase { get; set; }
}

/// <summary>Issue #1092: okulun lisansı ve son değişiklikleri; lisans hiç yoksa <see cref="Exists"/> false.</summary>
public class SchoolLicenseDto
{
    public int SchoolId { get; set; }
    public bool Exists { get; set; }

    /// <summary>Lisans şu an geçerli (başlamış, bitmemiş).</summary>
    public bool Active { get; set; }

    public string? TeacherPlanKey { get; set; }
    public string? StudentPlanKey { get; set; }
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public string? Note { get; set; }
    public List<SchoolLicenseHistoryDto> History { get; set; } = new();
}

public class SchoolLicenseHistoryDto
{
    /// <summary><c>Created</c>, <c>Updated</c> ya da <c>Ended</c>.</summary>
    public string Action { get; set; } = string.Empty;
    public int ActorUserId { get; set; }
    public string? TeacherPlanKey { get; set; }
    public string? StudentPlanKey { get; set; }
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public string? Note { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>Issue #1092: lisansı oluşturur ya da günceller (uzatma dahil). Boş plan = o kitlenin Pro'su, boş başlangıç = şimdi.</summary>
public class SaveSchoolLicenseDto
{
    public string? TeacherPlanKey { get; set; }
    public string? StudentPlanKey { get; set; }
    public DateTime? ValidFromUtc { get; set; }

    /// <summary>Hariç bitiş; null = süresiz.</summary>
    public DateTime? ValidToUtc { get; set; }

    public string? Note { get; set; }
}
