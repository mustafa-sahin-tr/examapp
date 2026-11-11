using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Plan sınırı uygulanan özelliklerin anahtarları (#1019). Yeni özellik = buraya sabit + plan sınırı satırı (seed / #1027) +
/// kullanım anında <see cref="IEntitlementService.RecordUsageAsync"/>, karar anında <see cref="IEntitlementService.CheckAsync"/>.
/// </summary>
public static class EntitlementFeatures
{
    /// <summary>Kitaptan aktarım (epic #1017): kurulumu onaylanan iş bir kullanım sayılır (#1021 kaydeder).</summary>
    public const string BookImport = "book-import";

    /// <summary>
    /// Soru sınıflandırma (AI): öğretmenin kaydettiği yeni soru başına bir kullanım (#1031). Sınır doluysa soru kaydedilir ama
    /// sınıflandırıcıya gönderilmez. Kitap ve hızlı ekleme taslakları sayılmaz: onların maliyeti kendi özelliğine yazılır.
    /// </summary>
    public const string AiClassification = "ai-classification";

    /// <summary>Deneme sınavı çözme. Free'de 0: herkese açık tek deneme sınavı sınırdan muaftır (#1031).</summary>
    public const string MockExam = "mock-exam";

    /// <summary>Hızlı soru ekleme (#1048, #1052): yüklenen her fotoğraf bir kullanım sayılır (yüklemede kaydedilir).</summary>
    public const string QuickAdd = "quick-add";

    // Issue #1093 (epic #1085): paket kademeleri. Kapı kontrolleri sonraki alt issue'larda (#1087-#1089) bağlanır.
    public const string ManualLabeling = "manual-labeling";
    public const string AutoDetect = "auto-detect";

    /// <summary>İşaretleme tuvalinde Claude: her çağrı bir kullanım.</summary>
    public const string CanvasAi = "canvas-ai";

    public const string QuestionGeneration = "question-generation";
    public const string ActivityEditor = "activity-editor";
    public const string FlashcardSets = "flashcard-sets";
    public const string CoachPanel = "coach-panel";
    public const string PrintTest = "print-test";

    /// <summary>Öğretmen ve öğrencide ortak: iki tarafta da plan izin vermeli (#1088).</summary>
    public const string Messaging = "messaging";

    /// <summary>Özel ders profili, müsaitlik ve randevu; iki tarafta da plan izin vermeli.</summary>
    public const string Tutoring = "tutoring";

    /// <summary>Canlı ders: ders (randevu) başına bir kullanım; saat ölçümü yok.</summary>
    public const string LiveLesson = "live-lesson";

    public const string MixedMini = "mixed-mini";

    /// <summary>Öğrencinin konu seçip kendi başlattığı test.</summary>
    public const string Practice = "practice";

    /// <summary>Öğrencinin kendi seçtiği etkinlik.</summary>
    public const string SelfActivity = "self-activity";

    /// <summary>Çalışma sayfaları, yanlışlarım, kaydedilenler, bilgi kartları, program takibi.</summary>
    public const string StudyTools = "study-tools";

    /// <summary>Konu performansı, haftalık gelişim, kazanım seviyesi.</summary>
    public const string Analytics = "analytics";

    /// <summary>Issue #1112: velisine haftalık rapor (panel, bildirim, e-posta).</summary>
    public const string WeeklyReport = "weekly-report";

    /// <summary>
    /// Issue #1111: öğretmene soru sor — cevaplanan soru başına bir kullanım (cevapta kaydedilir). Gönderimde cevap bekleyen sorular da
    /// kalan haktan düşülür; süresi dolan ya da geri çekilen soru sayılmaz.
    /// </summary>
    public const string AskTeacher = "ask-teacher";

    /// <summary>Issue #1113: yanlış ya da boş bırakılan soruda adım adım ipucu — ilk kez açılan adım başına bir kullanım.</summary>
    public const string StepHints = "step-hints";

    /// <summary>
    /// Bütün özellikler, plan kartlarındaki sırayla. Admin plan düzenleyicisi (#1086) yalnız bu anahtarları kabul eder; arayüz
    /// adları ve birimleri buradan alır.
    /// </summary>
    public static readonly IReadOnlyList<EntitlementFeatureInfo> Registry =
    [
        new(ManualLabeling, EntitlementAudience.Teacher, EntitlementFeatureKind.Toggle),
        new(AutoDetect, EntitlementAudience.Teacher, EntitlementFeatureKind.Toggle),
        new(AiClassification, EntitlementAudience.Teacher, EntitlementFeatureKind.Metered, "question"),
        new(CanvasAi, EntitlementAudience.Teacher, EntitlementFeatureKind.Metered, "call"),
        new(QuickAdd, EntitlementAudience.Teacher, EntitlementFeatureKind.Metered, "photo"),
        new(QuestionGeneration, EntitlementAudience.Teacher, EntitlementFeatureKind.Metered, "question"),
        new(BookImport, EntitlementAudience.Teacher, EntitlementFeatureKind.Metered, "book"),
        new(ActivityEditor, EntitlementAudience.Teacher, EntitlementFeatureKind.Toggle),
        new(FlashcardSets, EntitlementAudience.Teacher, EntitlementFeatureKind.Toggle),
        new(CoachPanel, EntitlementAudience.Teacher, EntitlementFeatureKind.Toggle),
        new(PrintTest, EntitlementAudience.Teacher, EntitlementFeatureKind.Toggle),
        new(MockExam, EntitlementAudience.Student, EntitlementFeatureKind.Metered, "exam"),
        new(MixedMini, EntitlementAudience.Student, EntitlementFeatureKind.Metered, "exam"),
        new(Practice, EntitlementAudience.Student, EntitlementFeatureKind.Metered, "test"),
        new(SelfActivity, EntitlementAudience.Student, EntitlementFeatureKind.Metered, "activity"),
        new(StudyTools, EntitlementAudience.Student, EntitlementFeatureKind.Toggle),
        new(Analytics, EntitlementAudience.Student, EntitlementFeatureKind.Toggle),
        new(WeeklyReport, EntitlementAudience.Student, EntitlementFeatureKind.Toggle),
        new(AskTeacher, EntitlementAudience.Student, EntitlementFeatureKind.Metered, "question"),
        new(StepHints, EntitlementAudience.Student, EntitlementFeatureKind.Metered, "hint"),
        new(Messaging, EntitlementAudience.Both, EntitlementFeatureKind.Toggle),
        new(Tutoring, EntitlementAudience.Both, EntitlementFeatureKind.Toggle),
        new(LiveLesson, EntitlementAudience.Both, EntitlementFeatureKind.Metered, "lesson"),
    ];

    private static readonly Dictionary<string, EntitlementFeatureInfo> ByKey = Registry.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static EntitlementFeatureInfo? Find(string featureKey) => ByKey.GetValueOrDefault(featureKey);

    /// <summary>Kullanıcının plan özetinde (#1027) her zaman listelenen özellikler.</summary>
    public static readonly IReadOnlyList<string> All = Registry.Select(f => f.Key).ToList();

    /// <summary>Hedef kitleye görünen özellikler (plan özeti çağıranın rolüne göre süzülür).</summary>
    public static IEnumerable<EntitlementFeatureInfo> For(IReadOnlySet<SubscriptionPlanAudience> audiences) =>
        Registry.Where(f => f.Audience == EntitlementAudience.Both
            || (f.Audience == EntitlementAudience.Teacher && audiences.Contains(SubscriptionPlanAudience.Teacher))
            || (f.Audience == EntitlementAudience.Student && audiences.Contains(SubscriptionPlanAudience.Student)));
}

public enum EntitlementAudience
{
    Teacher,
    Student,
    Both,
}

/// <summary><see cref="Toggle"/>: planda var / yok (satır sınırı null). <see cref="Metered"/>: aylık sayılır.</summary>
public enum EntitlementFeatureKind
{
    Toggle,
    Metered,
}

/// <param name="Unit">Sayılan özelliğin birimi (arayüz çeviri anahtarı); açık / kapalı özellikte null.</param>
public sealed record EntitlementFeatureInfo(string Key, EntitlementAudience Audience, EntitlementFeatureKind Kind, string? Unit = null);

/// <summary>Hak çözümlemesinde bakılan roller. Admin her özellikte sınırsız; diğer muafiyetler özellik bazında.</summary>
public static class EntitlementRoles
{
    public const string Admin = "Admin";
    public const string QuestionTrainer = "QuestionTrainer";

    /// <summary>Plandan bağımsız sınırsız roller (Admin dışında). QuestionTrainer epic #1017'nin pilot içerik ekibidir.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> UnlimitedByFeature = new Dictionary<string, string[]>
    {
        [EntitlementFeatures.BookImport] = [QuestionTrainer],
        [EntitlementFeatures.AiClassification] = [QuestionTrainer],
        // #1087: soru girişi plana bağlandı; içerik ekibi bütün soru girişi kademelerinde sınırsız kalır.
        [EntitlementFeatures.ManualLabeling] = [QuestionTrainer],
        [EntitlementFeatures.AutoDetect] = [QuestionTrainer],
        [EntitlementFeatures.CanvasAi] = [QuestionTrainer],
        [EntitlementFeatures.QuickAdd] = [QuestionTrainer],
        [EntitlementFeatures.QuestionGeneration] = [QuestionTrainer],
    };

    internal static readonly string[] All = [Admin, QuestionTrainer];
}

/// <summary>Hakkı sorulan kullanıcı: exam kullanıcı id'si ve hak çözümlemesini etkileyen rolleri.</summary>
public sealed record EntitlementSubject(int UserId, IReadOnlySet<string> Roles)
{
    public bool IsAdmin => Roles.Contains(EntitlementRoles.Admin);

    public static EntitlementSubject Create(int userId, params string[] roles) => new(userId, roles.ToHashSet(StringComparer.Ordinal));

    /// <summary>Token rollerinden yalnız hakla ilgili olanları alır.</summary>
    public static EntitlementSubject FromPrincipal(int userId, ClaimsPrincipal principal) =>
        Create(userId, EntitlementRoles.All.Where(principal.IsInRole).ToArray());
}

/// <param name="Allowed">Kullanıcı özelliği şimdi kullanabilir mi.</param>
/// <param name="Limit">Aylık sınır; null = sınırsız.</param>
/// <param name="Used">Bu takvim ayındaki (UTC) kullanım.</param>
/// <param name="ResetsAtUtc">Sayacın sıfırlanacağı an (sınırsızda null).</param>
/// <param name="PlanKey">Kullanıcının çözülen planı (rol muafiyetinde de plan bilgisi döner; plan yoksa null).</param>
public sealed record EntitlementCheck(bool Allowed, int? Limit, int Used, DateTime? ResetsAtUtc, string? PlanKey = null)
{
    public int? Remaining => Limit is { } limit ? Math.Max(0, limit - Used) : null;
}

/// <summary>İşlem plan sınırına takıldı; denetleyici <see cref="EntitlementLimitResponse"/> döner.</summary>
public sealed class EntitlementLimitException(string featureKey, EntitlementCheck check) : Exception(featureKey)
{
    public string FeatureKey { get; } = featureKey;
    public EntitlementCheck Check { get; } = check;
}

/// <summary>
/// Kullanıcının o anki planı; <paramref name="ValidToUtc"/> atamanın (ya da lisansın) bitişi (Free / süresiz atamada null).
/// <paramref name="FromSchoolLicense"/>: plan okulun lisansından geliyor (#1092).
/// <paramref name="ScopeSubjectId"/>: tek ders planının dersi (#1108); null = bütün dersler (okul lisansı da).
/// <paramref name="FromStudentSponsorship"/>: öğretmenin planı ücretli planı olan öğrencilerinden geliyor (#1115); grace'teyse
/// <paramref name="ValidToUtc"/> grace'in bitişi.
/// </summary>
public sealed record ResolvedPlan(int? PlanId, string? Key, string? Name, DateTime? ValidToUtc = null, bool FromSchoolLicense = false,
    int? ScopeSubjectId = null, bool FromStudentSponsorship = false);

public enum AssignPlanError
{
    None,
    UnknownPlan,
    InvalidRange,
}

/// <summary>Kullanıcının kendi başlattığı plan değişikliğinin sonucu (#1028).</summary>
public enum SelfServicePlanError
{
    None,
    UnknownPlan,

    /// <summary>Plan çağıranın rolüne açık değil ya da bu yolla alınamaz (ör. Free / deneme ödeme ile alınmaz).</summary>
    NotAvailable,
    TrialAlreadyUsed,

    /// <summary>Issue #1108: seçilen kapsam / süre bu plan ve sınıf için satılmıyor ya da ders öğrencinin sınıfında yok.</summary>
    InvalidOption,
}

/// <summary>
/// Issue #1019: plan / hak kontrolünün tek giriş noktası. Ataması olmayan kullanıcı Free'ye çözülür; Admin her özellikte, rol
/// muafiyeti olan kullanıcı ilgili özellikte sınırsızdır. Sayaç UTC takvim ayıdır.
/// </summary>
public interface IEntitlementService
{
    Task<ResolvedPlan> ResolvePlanAsync(int userId, CancellationToken ct = default);

    Task<EntitlementCheck> CheckAsync(EntitlementSubject subject, string featureKey, CancellationToken ct = default);

    /// <summary>Bir kullanımı kaydeder. <paramref name="referenceKey"/> özellik içinde tekildir; zaten kayıtlıysa false.</summary>
    Task<bool> RecordUsageAsync(int userId, string featureKey, string referenceKey, CancellationToken ct = default);

    /// <summary>Kullanıcıya plan atar; açık atamayı yeni atamanın başında kapatır (hesap başına tek plan).</summary>
    Task<AssignPlanError> AssignPlanAsync(int userId, string planKey, DateTime? validFromUtc, DateTime? validToUtc,
        int assignedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Issue #1030: aynı planı birden çok kullanıcıya tek kayıtla atar (okula toplu atama; <paramref name="schoolId"/> audit'e
    /// yazılır). Her kullanıcı için bir audit satırı (eski → yeni).
    /// </summary>
    Task<AssignPlanError> AssignPlanToUsersAsync(IReadOnlyCollection<int> userIds, string planKey, DateTime? validFromUtc,
        DateTime? validToUtc, int assignedByUserId, int? schoolId, CancellationToken ct = default);

    /// <summary>Issue #1030: açık atamayı şimdi bitirir, ileride başlayacakları iptal eder (Free'ye döner); atama yoksa false.</summary>
    Task<bool> EndPlanAsync(int userId, int endedByUserId, CancellationToken ct = default);

    /// <summary>Kullanıcı daha önce (silinmiş atamalar dahil) deneme planı aldıysa true.</summary>
    Task<bool> HasUsedTrialAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// #1028: deneme planını şimdi başlatır, <c>TrialDays</c> sonra biter. Hesap başına bir kez; deneme <paramref name="audiences"/>
    /// içinde olmalı.
    /// </summary>
    Task<SelfServicePlanError> StartTrialAsync(int userId, IReadOnlySet<SubscriptionPlanAudience> audiences,
        CancellationToken ct = default);

    /// <summary>
    /// #1028 sahte ödeme: ücretli planı (fiyatı > 0, deneme değil) şimdiden başlatır. Gerçek ödeme yoktur.
    /// #1091: <paramref name="purchasedByUserId"/> ödeyen (veli çocuğuna alır); boşsa kullanıcının kendisi. Kayıtta aktör odur.
    /// #1108: <paramref name="purchase"/> kapsam (tek ders) ve süre; boşsa aylık, bütün dersler. Süre bitişi belirler, fiyat
    /// <see cref="StudentPlanPricing"/>'ten gelir ve atamaya yazılır; satılmayan seçenek <see cref="SelfServicePlanError.InvalidOption"/>.
    /// </summary>
    Task<SelfServicePlanError> CheckoutAsync(int userId, string planKey, IReadOnlySet<SubscriptionPlanAudience> audiences,
        int? purchasedByUserId = null, PlanPurchase? purchase = null, CancellationToken ct = default);

    /// <summary>
    /// Issue #1116: süren satın alınmış planın durumu (iptal edilebilir mi, cayma süresi); satın alma yoksa (Free, deneme, admin
    /// ataması, okul lisansı) null.
    /// </summary>
    Task<PlanPurchaseStatus?> PurchaseStatusAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Issue #1116: süren satın almayı iptal eder (<see cref="PurchaseTermsPolicy"/>). Aylık plan ve cayma süresi geçmiş uzun plan
    /// yenilenmez, erişim dönem sonuna kadar sürer; 6 / 12 ay / yaz planı ilk 14 günde iptal edilirse plan hemen biter ve ödenen
    /// tutarın tamamı iade kaydına yazılır.
    /// </summary>
    Task<PlanCancellationResult> CancelPlanAsync(int userId, int cancelledByUserId, CancellationToken ct = default);
}

/// <summary>Issue #1116: süren satın alma. <see cref="FullRefundUntilUtc"/> aylık planda null.</summary>
public sealed record PlanPurchaseStatus(PlanDuration Duration, decimal PricePaid, string Currency, DateTime? ValidToUtc,
    DateTime? CancelledAtUtc, DateTime? FullRefundUntilUtc);

public enum CancelPlanError
{
    None,

    /// <summary>Süren satın alma yok (Free, deneme, admin ataması, okul lisansı).</summary>
    NothingToCancel,
    AlreadyCancelled,
}

/// <summary>Issue #1116: iptal sonucu. <see cref="AccessUntilUtc"/> erişimin bittiği an; <see cref="RefundAmount"/> cayma iadesi.</summary>
public sealed record PlanCancellationResult(CancelPlanError Error, DateTime? AccessUntilUtc = null, decimal? RefundAmount = null,
    string? Currency = null);

/// <summary>
/// Issue #1116: satın alma koşulları. Aylık plan her ay yenilenir, istenince iptal edilir, erişim ödenen ayın sonuna kadar sürer.
/// 6 / 12 aylık (ve yaz) planda ilk <see cref="FullRefundDays"/> gün koşulsuz tam iade; sonrasında kalan süre için kısmi iade
/// talep üzerine (destek). Gerçek tahsilat ve iade ödeme sağlayıcısı kararını bekliyor (canlıya geçiş). Fiyat artışı en erken
/// <see cref="PriceIncreaseNoticeDays"/> gün sonra geçerli olur ve ödeyene bildirilir; ödenmiş dönemin fiyatı değişmez.
/// </summary>
public static class PurchaseTermsPolicy
{
    public const int FullRefundDays = 14;
    public const int PriceIncreaseNoticeDays = 30;

    /// <summary>
    /// Admin'in yeni fiyatı: artış (bekleyen artıştan farklıysa) <paramref name="nowUtc"/> + <see cref="PriceIncreaseNoticeDays"/>'da
    /// geçerli olmak üzere bekler, bekleyen artışın aynısı olduğu gibi kalır; indirim ya da aynı fiyat hemen geçerlidir ve bekleyeni siler.
    /// </summary>
    public static (decimal Price, decimal? PendingPrice, DateTime? PendingFromUtc) Apply(
        decimal price, decimal? pendingPrice, DateTime? pendingFromUtc, decimal requested, DateTime nowUtc) =>
        requested <= price ? (requested, null, null)
        : requested == pendingPrice ? (price, pendingPrice, pendingFromUtc)
        : (price, requested, nowUtc.AddDays(PriceIncreaseNoticeDays));

    /// <summary>Tam iade süresinin sonu; aylık planda null (iade yok, dönem sonuna kadar erişim).</summary>
    public static DateTime? FullRefundUntil(PlanDuration duration, DateTime validFromUtc) =>
        duration == PlanDuration.Monthly ? null : validFromUtc.AddDays(FullRefundDays);
}

/// <summary>Issue #1108: satın alınan seçenek. <see cref="SubjectId"/> null = bütün dersler.</summary>
public sealed record PlanPurchase(int? SubjectId = null, PlanDuration Duration = PlanDuration.Monthly);

/// <summary>Issue #1108: atamaya yazılan satın alma koşulları (indirim sonrası fiyat).</summary>
public sealed record PurchaseTerms(int? SubjectId, PlanDuration Duration, decimal Price, int DiscountPercent);
