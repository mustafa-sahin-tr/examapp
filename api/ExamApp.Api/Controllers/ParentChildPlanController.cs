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
/// çocuğu ve Pending / Revoked bağlantı 404 (<see cref="IParentChildAccess"/>). Ödeme #1028 gibi sahtedir: plan bir ay atanır,
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

    public ParentChildPlanController(AppDbContext db, IParentChildAccess access, IParentAccessAuditLog audit,
        IEntitlementService entitlements, IStringLocalizer<Messages>? localizer = null)
    {
        _db = db;
        _access = access;
        _audit = audit;
        _entitlements = entitlements;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
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

        return Ok(await ChildPlanAsync(grant.StudentUserId, ct));
    }

    /// <summary>Sahte ödeme: seçilen öğrenci planını çocuğa bir aylık atar.</summary>
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

        var error = await _entitlements.CheckoutAsync(grant.StudentUserId, request.PlanKey, StudentAudiences.ToHashSet(),
            purchasedByUserId: user.Id, ct: ct);
        if (error != SelfServicePlanError.None)
            return SubscriptionPlanErrorCodes.ToResult(this, error);

        var plan = await ChildPlanAsync(grant.StudentUserId, ct);
        var planId = await _db.SubscriptionPlans.AsNoTracking().Where(p => p.Key == request.PlanKey).Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildPlanCheckout, planId, ct);
        return Ok(plan);
    }

    private async Task<ChildSubscriptionPlanDto> ChildPlanAsync(int studentUserId, CancellationToken ct)
    {
        var resolved = await _entitlements.ResolvePlanAsync(studentUserId, ct);
        var current = resolved.PlanId is { } planId
            ? await _db.SubscriptionPlans.AsNoTracking().Where(p => p.Id == planId)
                .Select(p => new { p.DisplayNameKey, IsTrial = p.TrialDays != null }).FirstOrDefaultAsync(ct)
            : null;

        return new ChildSubscriptionPlanDto
        {
            PlanKey = resolved.Key ?? SubscriptionPlan.FreeKey,
            DisplayNameKey = current?.DisplayNameKey,
            ValidToUtc = resolved.ValidToUtc,
            IsTrial = current?.IsTrial ?? false,
            SchoolLicense = resolved.FromSchoolLicense,
            Plans = await SubscriptionPlanCatalog.ListAsync(_db,
                p => p.IsActive && StudentAudiences.Contains(p.Audience) && p.MonthlyPrice > 0 && p.TrialDays == null, ct),
            Features = EntitlementFeatures.For(StudentAudiences.ToHashSet()).Select(EntitlementFeatureDto.From).ToList(),
        };
    }

    private NotFoundObjectResult ChildNotFound() => NotFound(new
    {
        message = _localizer["parentLinks.errors.childNotFound"].Value,
        errorCode = ParentLinkErrorCodes.NotFound
    });
}
