using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Plan sınırı uygulanan özelliklerin anahtarları (#1019). Yeni özellik = buraya sabit + plan sınırı satırı (seed / #1027) +
/// kullanım anında <see cref="IEntitlementService.RecordUsageAsync"/>, karar anında <see cref="IEntitlementService.CheckAsync"/>.
/// </summary>
public static class EntitlementFeatures
{
    /// <summary>Kitaptan aktarım (epic #1017): kurulumu onaylanan iş bir kullanım sayılır (#1021 kaydeder).</summary>
    public const string BookImport = "book-import";

    /// <summary>Soru sınıflandırma (AI); sayım #1031'de.</summary>
    public const string AiClassification = "ai-classification";

    /// <summary>Deneme sınavı çözme. Free'de 0: herkese açık tek deneme sınavı sınırdan muaftır (#1031).</summary>
    public const string MockExam = "mock-exam";

    /// <summary>Kullanıcının plan özetinde (#1027) her zaman listelenen özellikler.</summary>
    public static readonly IReadOnlyList<string> All = [BookImport, AiClassification, MockExam];
}

/// <summary>Hak çözümlemesinde bakılan roller. Admin her özellikte sınırsız; diğer muafiyetler özellik bazında.</summary>
public static class EntitlementRoles
{
    public const string Admin = "Admin";
    public const string QuestionTrainer = "QuestionTrainer";

    /// <summary>Plandan bağımsız sınırsız roller (Admin dışında). QuestionTrainer epic #1017'nin pilot içerik ekibidir.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> UnlimitedByFeature = new Dictionary<string, string[]>
    {
        [EntitlementFeatures.BookImport] = [QuestionTrainer],
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

/// <summary>Kullanıcının o anki planı; <paramref name="ValidToUtc"/> atamanın bitişi (Free / süresiz atamada null).</summary>
public sealed record ResolvedPlan(int? PlanId, string? Key, string? Name, DateTime? ValidToUtc = null);

public enum AssignPlanError
{
    None,
    UnknownPlan,
    InvalidRange,
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
}
