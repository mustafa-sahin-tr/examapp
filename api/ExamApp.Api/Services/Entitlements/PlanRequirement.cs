using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents.Enforcement;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>Issue #1090: <c>Subscriptions</c> bölümü.</summary>
public sealed class SubscriptionOptions
{
    public const string Section = "Subscriptions";

    /// <summary>
    /// Planı olmayan (Free'ye çözülen) öğretmen / öğrenci yalnız plan sayfasını kullanır. Varsayılan açık; geliştirme ortamı ve
    /// demo kapatabilir (AppHost, docker-compose).
    /// </summary>
    public bool RequirePlan { get; set; } = true;
}

/// <summary>
/// Issue #1090: planı olmayan kullanıcının da çağırabileceği uç (plan kataloğu / ödeme, oturum). #441'in
/// <see cref="AllowWhenParentRequiredAttribute"/> uçları (profil, oturum, veli bağlama) de açıktır: kilitli öğrenci velisini
/// bağlayabilmeli.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowWithoutPlanAttribute : Attribute;

/// <summary>Issue #1090: plan kilidi. Admin, QuestionTrainer ve veli hiç kilitlenmez (veli öder).</summary>
public interface IPlanLockService
{
    bool Enabled { get; }

    /// <summary>
    /// Çağıran kilitli mi. Denemeyi hiç kullanmamış öğretmen / öğrenci kilitlenmez, denemesi şimdi başlar: kimse ilk gün kilitli
    /// başlamaz (kayıt sihirbazını atlayan Google girişi, velinin açtığı çocuk hesabı dahil).
    /// </summary>
    Task<bool> IsLockedAsync(int userId, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// Öğretmenin atama ekranı: verilen öğrenci kullanıcılarından kilitli olanlar. Denemesini hiç kullanmamış öğrenci ilk girişte
    /// deneme alacağı için sayılmaz.
    /// </summary>
    Task<IReadOnlySet<int>> LockedAmongAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);
}

public sealed class PlanLockService(AppDbContext db, IEntitlementService entitlements, IOptionsSnapshot<SubscriptionOptions> options,
    TimeProvider clock) : IPlanLockService
{
    private static readonly string[] ExemptRoles = [EntitlementRoles.Admin, EntitlementRoles.QuestionTrainer, "Parent"];

    public bool Enabled => options.Value.RequirePlan;

    /// <summary>Kilit yalnız öğretmen ve öğrenci rolüne uygulanır; muaf rollerden biri varsa hiç uygulanmaz.</summary>
    public static bool AppliesTo(ClaimsPrincipal principal) =>
        (principal.IsInRole("Teacher") || principal.IsInRole("Student")) && !ExemptRoles.Any(principal.IsInRole);

    public async Task<bool> IsLockedAsync(int userId, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (!Enabled || !AppliesTo(principal))
            return false;

        if (!IsFree(await entitlements.ResolvePlanAsync(userId, ct)))
            return false;

        if (await entitlements.StartTrialAsync(userId, SubscriptionPlanCatalog.AudiencesFor(principal), ct) == SelfServicePlanError.None)
            return false;

        // İlk sayfanın eş zamanlı istekleri: denemeyi başka bir istek az önce başlatmış olabilir ("deneme kullanıldı" yanıtı).
        return IsFree(await entitlements.ResolvePlanAsync(userId, ct));
    }

    private static bool IsFree(ResolvedPlan plan) => plan.Key is null or SubscriptionPlan.FreeKey;

    public async Task<IReadOnlySet<int>> LockedAmongAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
    {
        if (!Enabled || userIds.Count == 0)
            return new HashSet<int>();

        var ids = userIds.Distinct().ToList();
        var now = clock.GetUtcNow().UtcDateTime;
        var withPlan = await db.UserSubscriptionPlans.AsNoTracking()
            .Where(a => ids.Contains(a.UserId) && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now)
                && a.Plan.IsActive && a.Plan.Key != SubscriptionPlan.FreeKey)
            .Select(a => a.UserId).Distinct().ToListAsync(ct);
        var trialUsed = await db.UserSubscriptionPlans.IgnoreQueryFilters().AsNoTracking()
            .Where(a => ids.Contains(a.UserId) && a.Plan.TrialDays != null)
            .Select(a => a.UserId).Distinct().ToListAsync(ct);

        return trialUsed.Except(withPlan).ToHashSet();
    }
}

/// <summary>Issue #1090: kilitli kullanıcının 402 gövdesi. <see cref="Audience"/> arayüzün açıklamasını seçer.</summary>
public sealed class SubscriptionRequiredDto
{
    public const string RequiredCode = "subscription.required";

    public string Code { get; set; } = RequiredCode;

    /// <summary><c>student</c> (velisi plan seçer) ya da <c>teacher</c>.</summary>
    public string Audience { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Issue #1090: kilitli öğretmen / öğrenci için yetkili TÜM uçlarda 402 <c>subscription.required</c>; yalnız
/// <see cref="AllowWithoutPlanAttribute"/> ve <see cref="AllowWhenParentRequiredAttribute"/> işaretli uçlar açık. Anonim uçlar ve
/// kilide tabi olmayan çağıranlar etkilenmez, veri silinmez. Pipeline'da #441 yaptırımından sonra, rate limiter'dan önce.
/// </summary>
public sealed class PlanRequiredMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IPlanLockService locks, IUserProfileProvider profiles, IConfiguration configuration,
        ILogger<PlanRequiredMiddleware> logger)
    {
        var endpoint = context.GetEndpoint();
        var user = context.User;
        if (endpoint == null
            || !locks.Enabled
            || user.Identity?.IsAuthenticated != true
            || endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null
            || endpoint.Metadata.GetMetadata<IAuthorizeData>() == null
            || endpoint.Metadata.GetMetadata<AllowWithoutPlanAttribute>() != null
            || endpoint.Metadata.GetMetadata<AllowWhenParentRequiredAttribute>() != null
            || !PlanLockService.AppliesTo(user)
            || ServicePrincipal.IsService(user, configuration.GetSection("Keycloak:ServiceClients").Get<string[]>()))
        {
            await next(context);
            return;
        }

        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier);
        int userId;
        try
        {
            userId = string.IsNullOrEmpty(sub) ? 0 : (await profiles.GetAsync(sub, context.RequestAborted))?.Id ?? 0;
        }
        catch (Exception ex) when (StudentParentStatusResolver.IsProfileProviderFailure(ex, context.RequestAborted))
        {
            // #441 ile aynı: profil sağlayıcı hatası fail-open (uçlar profili çözemeyince zaten 503 döner).
            logger.LogWarning(ex, "[PlanRequired] Profil çözülemedi; plan kilidi bu istekte uygulanmadı. Sub={Sub}", sub);
            userId = 0;
        }

        if (userId > 0 && await locks.IsLockedAsync(userId, user, context.RequestAborted))
        {
            var isStudent = user.IsInRole("Student");
            var localizer = context.RequestServices.GetService<IStringLocalizer<Messages>>() ?? FallbackMessageLocalizer.Instance;
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new SubscriptionRequiredDto
            {
                Audience = isStudent ? "student" : "teacher",
                Message = localizer[isStudent ? "entitlements.subscriptionRequired.student" : "entitlements.subscriptionRequired.teacher"].Value,
            }, context.RequestAborted);
            return;
        }

        await next(context);
    }
}

public static class PlanRequirementServiceCollectionExtensions
{
    public static IServiceCollection AddPlanRequirement(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SubscriptionOptions>(configuration.GetSection(SubscriptionOptions.Section));
        services.AddScoped<IPlanLockService, PlanLockService>();
        return services;
    }

    public static IApplicationBuilder UsePlanRequirement(this IApplicationBuilder app) => app.UseMiddleware<PlanRequiredMiddleware>();
}
