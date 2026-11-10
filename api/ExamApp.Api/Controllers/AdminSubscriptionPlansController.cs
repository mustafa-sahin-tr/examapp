using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1019: plan kataloğunu okuma ve kullanıcıya elle plan atama; #1030: kullanıcı araması, geçmiş, atamayı bitirme ve okula
/// toplu atama (yalnız Admin / SuperAdmin). Kullanıcı id'si exam kullanıcı id'sidir. Her değişiklik
/// <c>SubscriptionPlanAssignmentAudits</c>'e yazılır (eski → yeni, kim, ne zaman). Arama ve ayrıntı kişisel veri döndürdüğü için
/// <c>AdminDataAccessLogs</c>'a yazılır ve admin liste hız sınırındadır (#246, #262); arama metni kayda girmez.
/// </summary>
[ApiController]
[Route("api/admin/subscription-plans")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class AdminSubscriptionPlansController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IEntitlementService _entitlements;
    private readonly IAdminSubscriptionService _admin;
    private readonly IAdminDataAccessAuditService _dataAccessAudit;

    public AdminSubscriptionPlansController(AppDbContext db, IEntitlementService entitlements, IAdminSubscriptionService admin,
        IAdminDataAccessAuditService dataAccessAudit)
    {
        _db = db;
        _entitlements = entitlements;
        _admin = admin;
        _dataAccessAudit = dataAccessAudit;
    }

    [HttpGet]
    public async Task<ActionResult<List<SubscriptionPlanDto>>> List(CancellationToken ct) =>
        await SubscriptionPlanCatalog.ListAsync(_db, _ => true, ct, includePrices: true);

    // PUT {planKey} — #1086: ad, fiyat, deneme süresi, etkinlik ve bütün sınırlar; listede olmayan özellik planda yoktur.
    [HttpPut("{planKey}")]
    public async Task<ActionResult<SubscriptionPlanDto>> UpdatePlan(string planKey, [FromBody] UpdateSubscriptionPlanDto request,
        CancellationToken ct)
    {
        var admin = await GetAuthenticatedUserAsync(ct);
        var error = await _admin.UpdatePlanAsync(planKey, request, admin.Id, ct);
        if (error == AdminPlanUpdateError.PlanNotFound)
            return NotFound(new { code = "subscription.unknownPlan" });
        if (error != AdminPlanUpdateError.None)
            return BadRequest(new { code = PlanUpdateErrorCodes[error] });
        return (await SubscriptionPlanCatalog.ListAsync(_db, p => p.Key == planKey, ct, includePrices: true)).Single();
    }

    private static readonly Dictionary<AdminPlanUpdateError, string> PlanUpdateErrorCodes = new()
    {
        [AdminPlanUpdateError.InvalidName] = "subscription.invalidName",
        [AdminPlanUpdateError.InvalidPrice] = "subscription.invalidPrice",
        [AdminPlanUpdateError.InvalidTrialDays] = "subscription.invalidTrialDays",
        [AdminPlanUpdateError.UnknownFeature] = "subscription.unknownFeature",
        [AdminPlanUpdateError.AudienceMismatch] = "subscription.featureAudienceMismatch",
        [AdminPlanUpdateError.InvalidLimit] = "subscription.invalidLimit",
        [AdminPlanUpdateError.PlanRequired] = "subscription.planRequired",
        [AdminPlanUpdateError.InvalidPriceRow] = "subscription.invalidPriceRow",
    };

    // GET users?q=&role= — ad ya da e-postada geçen metin (en az 2 karakter), rol Student | Teacher | Parent; en fazla 20 sonuç.
    [HttpGet("users")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(AdminUserListRateLimiting.Policy)]
    [AdminDataAccess(AdminDataAccessResource.SubscriptionUserSearch)]
    public async Task<ActionResult<List<AdminSubscriptionUserDto>>> SearchUsers(
        [FromQuery] string? q, [FromQuery] string? role, CancellationToken ct)
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length < MinQueryLength)
            return BadRequest(new { code = "subscription.queryTooShort" });
        if (!string.IsNullOrWhiteSpace(role) && !SearchRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { code = "subscription.unknownRole" });

        var users = await _admin.SearchAsync(term, string.IsNullOrWhiteSpace(role) ? null : role, ct);
        await _dataAccessAudit.RecordListAccessAsync(new AdminListAccessRecord(
            KeyCloakId ?? string.Empty, AdminDataAccessResource.SubscriptionUserSearch, null, false, 1, users.Count, users.Count, users.Count), ct);
        return users;
    }

    [HttpGet("users/{userId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(AdminUserListRateLimiting.Policy)]
    [AdminDataAccess(AdminDataAccessResource.SubscriptionUserDetail)]
    public async Task<ActionResult<AdminSubscriptionUserDetailDto>> GetUserPlan(int userId, CancellationToken ct)
    {
        var detail = await _admin.GetDetailAsync(userId, ct);
        await _dataAccessAudit.RecordDetailAccessAsync(new AdminDetailAccessRecord(
            KeyCloakId ?? string.Empty, AdminDataAccessResource.SubscriptionUserDetail, userId,
            detail is null ? AdminDataAccessOutcome.NotFound : AdminDataAccessOutcome.Served), ct);
        return detail is null ? NotFound(new { code = "subscription.userNotFound" }) : detail;
    }

    [HttpPut("users/{userId:int}")]
    public async Task<IActionResult> AssignUserPlan(int userId, [FromBody] AssignSubscriptionPlanDto request, CancellationToken ct)
    {
        if (userId <= 0)
            return BadRequest(new { code = "subscription.invalidUser" });

        var admin = await GetAuthenticatedUserAsync(ct);
        var error = await _entitlements.AssignPlanAsync(userId, request.PlanKey, request.ValidFromUtc, request.ValidToUtc, admin.Id, ct);
        return error switch
        {
            AssignPlanError.None => Ok(await UserPlanAsync(userId, ct)),
            AssignPlanError.UnknownPlan => BadRequest(new { code = "subscription.unknownPlan" }),
            _ => BadRequest(new { code = "subscription.invalidRange" }),
        };
    }

    // POST users/{id}/end — açık atamayı şimdi bitirir, ileride başlayacakları iptal eder; kullanıcı Free'ye döner.
    [HttpPost("users/{userId:int}/end")]
    public async Task<IActionResult> EndUserPlan(int userId, CancellationToken ct)
    {
        var admin = await GetAuthenticatedUserAsync(ct);
        return await _entitlements.EndPlanAsync(userId, admin.Id, ct)
            ? Ok(await UserPlanAsync(userId, ct))
            : NotFound(new { code = "subscription.noAssignment" });
    }

    // GET schools/{id}/members — toplu atamadan önce kaç öğretmen / öğrenciye gideceği.
    [HttpGet("schools/{schoolId:int}/members")]
    public async Task<ActionResult<SchoolSubscriptionMembersDto>> GetSchoolMembers(int schoolId, CancellationToken ct) =>
        await _admin.GetSchoolMembersAsync(schoolId, ct) is { } members ? members : NotFound(new { code = "subscription.schoolNotFound" });

    // PUT schools/{id} — okulun öğretmenlerine / öğrencilerine (ya da hepsine) aynı tarihlerle aynı planı atar.
    [HttpPut("schools/{schoolId:int}")]
    public async Task<ActionResult<AssignSchoolSubscriptionPlanResultDto>> AssignSchoolPlan(
        int schoolId, [FromBody] AssignSchoolSubscriptionPlanDto request, CancellationToken ct)
    {
        var admin = await GetAuthenticatedUserAsync(ct);
        var (error, assigned) = await _admin.AssignSchoolAsync(schoolId, request, admin.Id, ct);
        return error switch
        {
            AdminSchoolAssignError.None => new AssignSchoolSubscriptionPlanResultDto { Assigned = assigned },
            AdminSchoolAssignError.SchoolNotFound => NotFound(new { code = "subscription.schoolNotFound" }),
            AdminSchoolAssignError.UnknownPlan => BadRequest(new { code = "subscription.unknownPlan" }),
            AdminSchoolAssignError.AudienceMismatch => BadRequest(new { code = "subscription.audienceMismatch" }),
            AdminSchoolAssignError.NoMembers => BadRequest(new { code = "subscription.noMembers" }),
            AdminSchoolAssignError.TooManyMembers => BadRequest(new { code = "subscription.tooManyMembers" }),
            _ => BadRequest(new { code = "subscription.invalidRange" }),
        };
    }

    private const int MinQueryLength = 2;
    private static readonly string[] SearchRoles = ["Student", "Teacher", "Parent"];

    private async Task<UserSubscriptionPlanDto> UserPlanAsync(int userId, CancellationToken ct)
    {
        var plan = await _entitlements.ResolvePlanAsync(userId, ct);
        return new UserSubscriptionPlanDto { UserId = userId, PlanKey = plan.Key, PlanName = plan.Name };
    }
}
