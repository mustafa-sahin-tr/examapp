using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1019: plan kataloğunu okuma ve kullanıcıya elle plan atama (yalnız Admin; arayüzü #1030). Kullanıcı id'si exam
/// kullanıcı id'sidir.
/// </summary>
[ApiController]
[Route("api/admin/subscription-plans")]
[Authorize(Roles = "Admin")]
public class AdminSubscriptionPlansController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IEntitlementService _entitlements;

    public AdminSubscriptionPlansController(AppDbContext db, IEntitlementService entitlements)
    {
        _db = db;
        _entitlements = entitlements;
    }

    [HttpGet]
    public async Task<ActionResult<List<SubscriptionPlanDto>>> List(CancellationToken ct) =>
        await SubscriptionPlanCatalog.ListAsync(_db, _ => true, ct);

    [HttpGet("users/{userId:int}")]
    public async Task<ActionResult<UserSubscriptionPlanDto>> GetUserPlan(int userId, CancellationToken ct) =>
        await UserPlanAsync(userId, ct);

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

    private async Task<UserSubscriptionPlanDto> UserPlanAsync(int userId, CancellationToken ct)
    {
        var plan = await _entitlements.ResolvePlanAsync(userId, ct);
        return new UserSubscriptionPlanDto { UserId = userId, PlanKey = plan.Key, PlanName = plan.Name };
    }
}
