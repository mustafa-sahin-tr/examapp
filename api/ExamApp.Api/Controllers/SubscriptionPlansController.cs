using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
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
public class SubscriptionPlansController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IEntitlementService _entitlements;

    public SubscriptionPlansController(AppDbContext db, IEntitlementService entitlements)
    {
        _db = db;
        _entitlements = entitlements;
    }

    /// <summary>Çağıranın rolüne uygun aktif planlar (sıralı).</summary>
    [HttpGet]
    public async Task<ActionResult<List<SubscriptionPlanDto>>> List(CancellationToken ct)
    {
        var audiences = SubscriptionPlanCatalog.AudiencesFor(User);
        return await SubscriptionPlanCatalog.ListAsync(_db, p => p.IsActive && audiences.Contains(p.Audience), ct);
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

    /// <summary>#1028 sahte ödeme: ücretli planı bir aylık atar. Gerçek ödeme alınmaz; arayüz ekranı "demo" diye işaretler.</summary>
    [HttpPost("me/checkout")]
    [Authorize(Roles = "Teacher,Student,Admin")] // veliye plan satılmaz (sihirbaz veliye plan adımı göstermez)
    public async Task<ActionResult<CurrentSubscriptionPlanDto>> Checkout([FromBody] SubscriptionCheckoutDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        var error = await _entitlements.CheckoutAsync(user.Id, request.PlanKey, SubscriptionPlanCatalog.AudiencesFor(User), ct);
        return error == SelfServicePlanError.None ? await CurrentAsync(user.Id, ct) : PlanError(error);
    }

    private ActionResult PlanError(SelfServicePlanError error) => error switch
    {
        SelfServicePlanError.TrialAlreadyUsed => Conflict(new { code = "subscription.trialUsed" }),
        SelfServicePlanError.NotAvailable => BadRequest(new { code = "subscription.planNotAvailable" }),
        _ => BadRequest(new { code = "subscription.unknownPlan" }),
    };

    private async Task<CurrentSubscriptionPlanDto> CurrentAsync(int userId, CancellationToken ct)
    {
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
