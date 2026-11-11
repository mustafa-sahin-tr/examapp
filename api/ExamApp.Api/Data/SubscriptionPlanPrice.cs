namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1108 (epic #1118): öğrenci planının fiyatı sınıf bandına, kapsama ve süreye göre. Kademe (Basic / Essential / Pro) ve
/// sınırlar <see cref="SubscriptionPlan"/>'da kalır; bu tablo yalnız fiyattır. Satır yoksa o seçenek satılmaz (ör. Pro tek ders).
/// <see cref="GradeLevel"/> doluysa bandın içinde yalnız o sınıfın fiyatıdır ve band satırının önüne geçer.
/// </summary>
public class SubscriptionPlanPrice : BaseEntity
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;
    public StudentGradeBand Band { get; set; }

    /// <summary>Bandın içinde tek sınıfa özel fiyat (1–12); null = bandın tamamı.</summary>
    public int? GradeLevel { get; set; }

    public PlanScope Scope { get; set; }
    public PlanDuration Duration { get; set; }

    public decimal Price { get; set; }

    /// <summary>
    /// Issue #1116: planlanmış fiyat artışı; <see cref="PendingFromUtc"/>'da <see cref="Price"/>'a geçer (<c>PlanPriceChangeJob</c>).
    /// Ödenmiş dönemin fiyatı değişmez; yenileme (yeni satın alma) o tarihten sonra yeni fiyatla olur.
    /// </summary>
    public decimal? PendingPrice { get; set; }

    public DateTime? PendingFromUtc { get; set; }
}

/// <summary>Sınıf bandı; öğrencinin sınıfından çözülür (<c>StudentPlanPricing.BandOf</c>). Kalıcı değer string'dir.</summary>
public enum StudentGradeBand
{
    Primary,
    Middle,
    Lgs,
    HighSchool,
    Yks,
}

/// <summary>Planın kapsamı: bütün dersler ya da öğrencinin sınıfından tek ders (ders atamada <c>ScopeSubjectId</c>).</summary>
public enum PlanScope
{
    AllSubjects,
    SingleSubject,
}

/// <summary>Satın alma süresi. <see cref="Summer"/> Haziran–Ağustos arasında satılır ve 1 Eylül'de biter.</summary>
public enum PlanDuration
{
    Monthly,
    SixMonths,
    TwelveMonths,
    Summer,
}
