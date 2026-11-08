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
/// Issue #1027: kullanıcıya açık plan kataloğu ve oturumdaki kullanıcının planı + bu ayki kullanımı. Plan seçimi / ödeme
/// #1028'de, uygulama (sınırda engel) #1031'de.
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

    /// <summary>Planı (ataması yoksa Free) ve planın sınırladığı / bilinen her özellik için bu ayki hakkı.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CurrentSubscriptionPlanDto>> Current(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        var subject = EntitlementSubject.FromPrincipal(user.Id, User);
        var plan = await _entitlements.ResolvePlanAsync(user.Id, ct);
        var planInfo = plan.PlanId is { } planId
            ? await _db.SubscriptionPlans.AsNoTracking()
                .Where(p => p.Id == planId)
                .Select(p => new { p.DisplayNameKey, FeatureKeys = p.FeatureLimits.Select(l => l.FeatureKey).ToList() })
                .FirstOrDefaultAsync(ct)
            : null;

        var features = new List<FeatureUsageDto>();
        foreach (var featureKey in EntitlementFeatures.All.Union(planInfo?.FeatureKeys ?? []))
        {
            var check = await _entitlements.CheckAsync(subject, featureKey, ct);
            features.Add(new FeatureUsageDto
            {
                FeatureKey = featureKey,
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
            DisplayNameKey = planInfo?.DisplayNameKey,
            ValidToUtc = plan.ValidToUtc,
            Features = features,
        };
    }
}
