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

    [HttpGet]
    public async Task<ActionResult<object>> ListAsync([FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var items = await _service.ListAsync(includeInactive, cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
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
        var result = await _service.CreateAsync(request, GetActor(), cancellationToken);
        if (result.IsConflict)
        {
            return Conflict(ToErrorBody(result));
        }

        if (!result.Succeeded)
        {
            return ValidationProblemFrom(result);
        }

        return CreatedAtAction(nameof(GetAsync), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BadgeDefinitionAdminDto>> UpdateAsync(
        Guid id,
        [FromBody] UpdateBadgeDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, GetActor(), cancellationToken);
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
        var result = await _service.SetActiveAsync(id, isActive: false, GetActor(), cancellationToken);
        return result.IsNotFound ? NotFound() : Ok(result.Value);
    }

    /// <summary>Idempotent — activating an already-active badge just returns 200.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<BadgeDefinitionAdminDto>> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.SetActiveAsync(id, isActive: true, GetActor(), cancellationToken);
        return result.IsNotFound ? NotFound() : Ok(result.Value);
    }

    private string GetActor() =>
        User.FindFirstValue("preferred_username")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown-admin";

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
