using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1027: kullanıcıya açık plan kataloğu ve oturumdaki kullanıcının planı + bu ayki kullanımı. #1028: deneme başlatma ve
/// sahte ödeme (kayıt sihirbazının plan adımı, sonra "Planım"). Uygulama (sınırda engel) #1031'de.
/// </summary>
[ApiController]
[Route("api/subscription-plans")]
[Authorize]
[AllowWithoutPlan] // issue #1090: kilitli kullanıcı planını burada seçer
public class SubscriptionPlansController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IEntitlementService _entitlements;
    private readonly IPlanLockService? _locks;

    public SubscriptionPlansController(AppDbContext db, IEntitlementService entitlements, IPlanLockService? locks = null)
    {
        _db = db;
        _entitlements = entitlements;
        _locks = locks;
    }

    /// <summary>Çağıranın rolüne uygun aktif planlar (sıralı). #1090: plan kilidi açıkken Free seçilebilir bir plan değildir.</summary>
    [HttpGet]
    public async Task<ActionResult<List<SubscriptionPlanDto>>> List(CancellationToken ct)
    {
        var audiences = SubscriptionPlanCatalog.AudiencesFor(User);
        var withoutFree = _locks?.Enabled == true;
        return await SubscriptionPlanCatalog.ListAsync(_db,
            p => p.IsActive && audiences.Contains(p.Audience) && !(withoutFree && p.Key == SubscriptionPlan.FreeKey), ct);
    }

    /// <summary>
    /// #1093: çağıranın rolüne uygun özellik listesi (sıra, tür, birim). Plan kartları ve sınır penceresi adları buradan dizer.
    /// </summary>
    [HttpGet("features")]
    public ActionResult<List<EntitlementFeatureDto>> Features() =>
        EntitlementFeatures.For(SubscriptionPlanCatalog.AudiencesFor(User)).Select(EntitlementFeatureDto.From).ToList();

    /// <summary>Planı (ataması yoksa Free) ve planın sınırladığı / bilinen her özellik için bu ayki hakkı.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CurrentSubscriptionPlanDto>> Current(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        return await CurrentAsync(user.Id, ct);
    }

    /// <summary>#1028: deneme planını başlatır (hesap başına bir kez; ikinci deneme 409 <c>subscription.trialUsed</c>).</summary>
    [HttpPost("me/trial")]
    [Authorize(Roles = "Teacher,Student,Admin")] // veliye plan satılmaz (sihirbaz veliye plan adımı göstermez)
    public async Task<ActionResult<CurrentSubscriptionPlanDto>> StartTrial(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        var error = await _entitlements.StartTrialAsync(user.Id, SubscriptionPlanCatalog.AudiencesFor(User), ct);
        return error == SelfServicePlanError.None ? await CurrentAsync(user.Id, ct) : PlanError(error);
    }

    /// <summary>
    /// #1028 sahte ödeme: ücretli planı bir aylık atar. Gerçek ödeme alınmaz; arayüz ekranı "demo" diye işaretler. #1091: öğrencinin
    /// planını velisi alır (<c>api/parent/children/{id}/plan</c>); öğrenci kendine alamaz (403 <c>subscription.parentPays</c>).
    /// </summary>
    [HttpPost("me/checkout")]
    [Authorize(Roles = "Teacher,Student,Admin")] // veliye kendi adına plan satılmaz
    public async Task<ActionResult<CurrentSubscriptionPlanDto>> Checkout([FromBody] SubscriptionCheckoutDto request, CancellationToken ct)
    {
        if (User.IsInRole("Student") && !User.IsInRole("Teacher") && !User.IsInRole(EntitlementRoles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = SubscriptionPlanErrorCodes.ParentPays });

        var user = await GetAuthenticatedUserAsync(ct);
        if (StudentPlanPricing.PurchaseOf(request) is not { } purchase)
            return SubscriptionPlanErrorCodes.ToResult(this, SelfServicePlanError.InvalidOption);
        var error = await _entitlements.CheckoutAsync(user.Id, request.PlanKey, SubscriptionPlanCatalog.AudiencesFor(User),
            purchase: purchase, ct: ct);
        return error == SelfServicePlanError.None ? await CurrentAsync(user.Id, ct) : PlanError(error);
    }

    /// <summary>
    /// Issue #1116: satın alınmış planı iptal eder (öğretmen; öğrencinin planını velisi iptal eder, 403 <c>subscription.parentPays</c>).
    /// Aylık / cayma süresi geçmiş plan dönem sonuna kadar sürer; 14 gün içindeki uzun plan hemen biter, tam iade kaydı yazılır.
    /// Satın alma yoksa 409 <c>subscription.nothingToCancel</c>, ikinci iptal 409 <c>subscription.alreadyCancelled</c>.
    /// </summary>
    [HttpPost("me/cancel")]
    [Authorize(Roles = "Teacher,Student,Admin")]
    public async Task<ActionResult<PlanCancellationDto>> Cancel(CancellationToken ct)
    {
        if (User.IsInRole("Student") && !User.IsInRole("Teacher") && !User.IsInRole(EntitlementRoles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden, new { code = SubscriptionPlanErrorCodes.ParentPays });

        var user = await GetAuthenticatedUserAsync(ct);
        var result = await _entitlements.CancelPlanAsync(user.Id, user.Id, ct);
        return result.Error == CancelPlanError.None
            ? new PlanCancellationDto { AccessUntilUtc = result.AccessUntilUtc, RefundAmount = result.RefundAmount, Currency = result.Currency }
            : SubscriptionPlanErrorCodes.ToResult(this, result);
    }

    private ActionResult PlanError(SelfServicePlanError error) => SubscriptionPlanErrorCodes.ToResult(this, error);

    private async Task<CurrentSubscriptionPlanDto> CurrentAsync(int userId, CancellationToken ct)
    {
        // #1090: önce kilit (denemesi hiç başlamamış kullanıcının denemesi burada başlar, plan aşağıda deneme olarak okunur).
        var locked = _locks is not null && await _locks.IsLockedAsync(userId, User, ct);
        var subject = EntitlementSubject.FromPrincipal(userId, User);
        var plan = await _entitlements.ResolvePlanAsync(userId, ct);
        var displayNameKey = plan.PlanId is { } planId
            ? await _db.SubscriptionPlans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.DisplayNameKey).FirstOrDefaultAsync(ct)
            : null;

        // #1093: yalnız çağıranın rolüne ait özellikler (deneme planı iki kitlenin de satırlarını taşır).
        var features = new List<FeatureUsageDto>();
        foreach (var feature in EntitlementFeatures.For(SubscriptionPlanCatalog.AudiencesFor(User)))
        {
            var check = await _entitlements.CheckAsync(subject, feature.Key, ct);
            features.Add(new FeatureUsageDto
            {
                FeatureKey = feature.Key,
                Kind = feature.Kind.ToString(),
                Unit = feature.Unit,
                Allowed = check.Allowed,
                Limit = check.Limit,
                Used = check.Used,
                Remaining = check.Remaining,
                ResetsAtUtc = check.ResetsAtUtc,
            });
        }

        return new CurrentSubscriptionPlanDto
        {
            PlanKey = plan.Key,
            PlanName = plan.Name,
            DisplayNameKey = displayNameKey,
            ValidToUtc = plan.ValidToUtc,
            TrialAvailable = await IsTrialAvailableAsync(userId, ct),
            Locked = locked,
            SchoolLicense = plan.FromSchoolLicense,
            StudentSponsored = plan.FromStudentSponsorship,
            Purchase = PlanPurchaseDto.From(await _entitlements.PurchaseStatusAsync(userId, ct)),
            Features = features,
        };
    }

    private async Task<bool> IsTrialAvailableAsync(int userId, CancellationToken ct)
    {
        var audiences = SubscriptionPlanCatalog.AudiencesFor(User);
        var offered = await _db.SubscriptionPlans.AsNoTracking()
            .AnyAsync(p => p.Key == SubscriptionPlan.TrialKey && p.IsActive && p.TrialDays != null && audiences.Contains(p.Audience), ct);
        return offered && !await _entitlements.HasUsedTrialAsync(userId, ct);
    }
}
