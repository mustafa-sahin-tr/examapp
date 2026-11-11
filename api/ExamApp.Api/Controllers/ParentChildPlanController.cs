using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1091: öğrencinin planını velisi alır. Yalnız Active bağlantının velisi (her veli, yalnız birincil değil); başka velinin
/// çocuğu ve Pending / Revoked bağlantı 404 (<see cref="IParentChildAccess"/>). Ödeme #1028 gibi sahtedir: plan seçilen süreyle atanır (#1108),
/// plan kaydında aktör velidir.
/// </summary>
[ApiController]
[Route("api/parent/children/{studentId:int}/plan")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentChildPlanController : BaseController
{
    private static readonly SubscriptionPlanAudience[] StudentAudiences = [SubscriptionPlanAudience.Any, SubscriptionPlanAudience.Student];

    private readonly AppDbContext _db;
    private readonly IParentChildAccess _access;
    private readonly IParentAccessAuditLog _audit;
    private readonly IEntitlementService _entitlements;
    private readonly IStringLocalizer<Messages> _localizer;
    private readonly TimeProvider _clock;

    public ParentChildPlanController(AppDbContext db, IParentChildAccess access, IParentAccessAuditLog audit,
        IEntitlementService entitlements, IStringLocalizer<Messages>? localizer = null, TimeProvider? clock = null)
    {
        _db = db;
        _access = access;
        _audit = audit;
        _entitlements = entitlements;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Çocuğun planı, bitişi ve velinin alabileceği öğrenci planları.</summary>
    [HttpGet]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> Get(int studentId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var grant = await _access.EnsureActiveChildAsync(user.Id, studentId, ct);
        if (grant is null)
            return ChildNotFound();
        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildPlan, ct: ct);

        return Ok(await ChildPlanAsync(grant.StudentUserId, user.Id, ct));
    }

    /// <summary>Sahte ödeme: seçilen öğrenci planını çocuğa seçilen süre ve kapsamla atar (#1108; varsayılan aylık, bütün dersler).</summary>
    [HttpPost("checkout")]
    [EnableRateLimiting(ParentLinkRateLimiting.ManagePolicy)]
    public async Task<IActionResult> Checkout(int studentId, [FromBody] SubscriptionCheckoutDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var grant = await _access.EnsureActiveChildAsync(user.Id, studentId, ct);
        if (grant is null)
            return ChildNotFound();

        if (StudentPlanPricing.PurchaseOf(request) is not { } purchase)
            return SubscriptionPlanErrorCodes.ToResult(this, SelfServicePlanError.InvalidOption);
        var error = await _entitlements.CheckoutAsync(grant.StudentUserId, request.PlanKey, StudentAudiences.ToHashSet(),
            purchasedByUserId: user.Id, purchase, ct);
        if (error != SelfServicePlanError.None)
            return SubscriptionPlanErrorCodes.ToResult(this, error);

        var plan = await ChildPlanAsync(grant.StudentUserId, user.Id, ct);
        var planId = await _db.SubscriptionPlans.AsNoTracking().Where(p => p.Key == request.PlanKey).Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildPlanCheckout, planId, ct);
        return Ok(plan);
    }

    /// <summary>
    /// Issue #1116: çocuğun satın alınmış planını iptal eder (her Active veli). Koşullar <see cref="PurchaseTermsPolicy"/>; satın alma
    /// yoksa 409 <c>subscription.nothingToCancel</c>, ikinci iptal 409 <c>subscription.alreadyCancelled</c>.
    /// </summary>
    [HttpPost("cancel")]
    [EnableRateLimiting(ParentLinkRateLimiting.ManagePolicy)]
    public async Task<IActionResult> Cancel(int studentId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var grant = await _access.EnsureActiveChildAsync(user.Id, studentId, ct);
        if (grant is null)
            return ChildNotFound();

        var planId = (await _entitlements.ResolvePlanAsync(grant.StudentUserId, ct)).PlanId;
        var result = await _entitlements.CancelPlanAsync(grant.StudentUserId, user.Id, ct);
        if (result.Error != CancelPlanError.None)
            return SubscriptionPlanErrorCodes.ToResult(this, result);

        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildPlanCancel, planId, ct);
        return Ok(new PlanCancellationDto { AccessUntilUtc = result.AccessUntilUtc, RefundAmount = result.RefundAmount, Currency = result.Currency });
    }

    private async Task<ChildSubscriptionPlanDto> ChildPlanAsync(int studentUserId, int parentUserId, CancellationToken ct)
    {
        var resolved = await _entitlements.ResolvePlanAsync(studentUserId, ct);
        var current = resolved.PlanId is { } planId
            ? await _db.SubscriptionPlans.AsNoTracking().Where(p => p.Id == planId)
                .Select(p => new { p.DisplayNameKey, IsTrial = p.TrialDays != null }).FirstOrDefaultAsync(ct)
            : null;

        var now = _clock.GetUtcNow().UtcDateTime;
        var offer = await StudentPlanPricing.OfferAsync(_db, studentUserId, parentUserId, now, ct);
        var plans = await SubscriptionPlanCatalog.ListAsync(_db,
            p => p.IsActive && StudentAudiences.Contains(p.Audience) && p.MonthlyPrice > 0 && p.TrialDays == null, ct);
        // #1108: kart çocuğun bandındaki aylık, bütün dersler fiyatını gösterir (band yoksa planın aylık fiyatı).
        foreach (var plan in plans)
        {
            var monthly = offer.Prices.FirstOrDefault(p => p.PlanKey == plan.Key
                && p.Scope == nameof(PlanScope.AllSubjects) && p.Duration == nameof(PlanDuration.Monthly));
            if (monthly is not null)
                plan.MonthlyPrice = monthly.Amount;
        }
        // Okul lisansından gelen plan kapsamsızdır; kişisel atamada tek ders varsa adıyla.
        var scope = resolved.FromSchoolLicense || resolved.PlanId is null ? null : await _db.UserSubscriptionPlans.AsNoTracking()
            .Where(a => a.UserId == studentUserId && a.PlanId == resolved.PlanId && a.ValidFromUtc <= now
                && (a.ValidToUtc == null || a.ValidToUtc > now))
            .OrderByDescending(a => a.ValidFromUtc).ThenByDescending(a => a.Id)
            .Select(a => new { a.ScopeSubjectId, Name = _db.Subjects.Where(s => s.Id == a.ScopeSubjectId).Select(s => s.Name).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);

        return new ChildSubscriptionPlanDto
        {
            PlanKey = resolved.Key ?? SubscriptionPlan.FreeKey,
            DisplayNameKey = current?.DisplayNameKey,
            ValidToUtc = resolved.ValidToUtc,
            IsTrial = current?.IsTrial ?? false,
            SchoolLicense = resolved.FromSchoolLicense,
            Plans = plans,
            Features = EntitlementFeatures.For(StudentAudiences.ToHashSet()).Select(EntitlementFeatureDto.From).ToList(),
            Offer = offer,
            ScopeSubjectId = scope?.ScopeSubjectId,
            ScopeSubjectName = scope?.Name,
            Purchase = PlanPurchaseDto.From(await _entitlements.PurchaseStatusAsync(studentUserId, ct)),
        };
    }

    private NotFoundObjectResult ChildNotFound() => NotFound(new
    {
        message = _localizer["parentLinks.errors.childNotFound"].Value,
        errorCode = ParentLinkErrorCodes.NotFound
    });
}
