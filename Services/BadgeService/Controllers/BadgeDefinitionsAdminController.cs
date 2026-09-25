using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Models;
using BadgeService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BadgeService.Controllers;

/// <summary>
/// Issue #148: admin-only CRUD for <c>BadgeDefinition</c>. Previously badges could only be added/changed
/// by editing <see cref="BadgeService.Data.BadgeSeeder"/> and redeploying. No hard delete — badges already
/// awarded must stay valid; use POST .../deactivate instead (hides the badge from evaluation and the
/// student catalog, keeps history).
/// </summary>
[ApiController]
[Route("api/admin/badge-definitions")]
[Authorize(Roles = "Admin")]
public class BadgeDefinitionsAdminController : ControllerBase
{
    private readonly BadgeDefinitionAdminService _service;

    public BadgeDefinitionsAdminController(BadgeDefinitionAdminService service)
    {
        _service = service;
    }

    /// <summary>Schema per RuleType for the admin rule editor (owner decision #2).</summary>
    [HttpGet("rule-types")]
    public ActionResult<object> GetRuleTypes()
    {
        return Ok(BadgeRuleTypeCatalog.All);
    }

    /// <summary>
    /// Security review #148 (M2, contract change): paged — was a bare array, now <c>{ items, totalCount }</c>.
    /// <paramref name="take"/> is clamped to 1..200 (default 50) server-side even if the caller sends more.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<BadgeDefinitionAdminListResponse>> ListAsync(
        [FromQuery] bool includeInactive,
        [FromQuery] int skip = 0,
        [FromQuery] int take = BadgeDefinitionAdminService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var page = await _service.ListAsync(includeInactive, skip, take, cancellationToken);
        return Ok(new BadgeDefinitionAdminListResponse { Items = page.Items, TotalCount = page.TotalCount });
    }

    // Code review follow-up (#148, BLOCKER): MVC's default SuppressAsyncSuffixInActionNames convention
    // strips "Async" from the route action name, so the actual registered action name is "Get" — using
    // nameof(GetAsync) ("GetAsync") in CreatedAtAction below silently fails to resolve the route (500 on
    // every create, even though the row was already saved). Pin the action name explicitly so both sides
    // agree regardless of that convention.
    [HttpGet("{id:guid}", Name = "GetBadgeDefinitionById")]
    public async Task<ActionResult<BadgeDefinitionAdminDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await _service.GetAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<BadgeDefinitionAdminDto>> CreateAsync(
        [FromBody] CreateBadgeDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorId, out var actorName))
        {
            return Forbid();
        }

        var result = await _service.CreateAsync(request, actorId, actorName, cancellationToken);
        if (result.IsConflict)
        {
            return Conflict(ToErrorBody(result));
        }

        if (!result.Succeeded)
        {
            return ValidationProblemFrom(result);
        }

        return CreatedAtRoute("GetBadgeDefinitionById", new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BadgeDefinitionAdminDto>> UpdateAsync(
        Guid id,
        [FromBody] UpdateBadgeDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorId, out var actorName))
        {
            return Forbid();
        }

        var result = await _service.UpdateAsync(id, request, actorId, actorName, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            return ValidationProblemFrom(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Idempotent — deactivating an already-inactive badge just returns 200.</summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<BadgeDefinitionAdminDto>> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorId, out var actorName))
        {
            return Forbid();
        }

        var result = await _service.SetActiveAsync(id, isActive: false, actorId, actorName, cancellationToken);
        return result.IsNotFound ? NotFound() : Ok(result.Value);
    }

    /// <summary>Idempotent — activating an already-active badge just returns 200 unless the active cap (M2) is hit.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<BadgeDefinitionAdminDto>> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorId, out var actorName))
        {
            return Forbid();
        }

        var result = await _service.SetActiveAsync(id, isActive: true, actorId, actorName, cancellationToken);
        if (result.IsConflict)
        {
            return Conflict(ToErrorBody(result));
        }

        return result.IsNotFound ? NotFound() : Ok(result.Value);
    }

    /// <summary>
    /// Security review #148 (L1): the audit id is the Keycloak `sub` claim (immutable, unlike a display
    /// name) — mapped to <see cref="ClaimTypes.NameIdentifier"/> by the JwtBearer pipeline, same claim
    /// <c>ReportsController</c> already treats as the caller's stable identity. Requests where it's
    /// missing (malformed/service token) are rejected with 403 rather than falling back to a sentinel
    /// "unknown-admin" string that would make the audit trail useless.
    /// </summary>
    private bool TryGetActor(out string actorId, out string? actorName)
    {
        actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        actorName = User.FindFirstValue("preferred_username");
        return !string.IsNullOrWhiteSpace(actorId);
    }

    private ActionResult ValidationProblemFrom(BadgeDefinitionAdminResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Field, error.Message);
        }

        return ValidationProblem(ModelState);
    }

    private static object ToErrorBody(BadgeDefinitionAdminResult result) => new
    {
        errors = result.Errors.ToDictionary(e => e.Field, e => new[] { e.Message }),
    };
}
