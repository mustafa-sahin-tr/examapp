using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Entities;
using BadgeService.Models;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Services;

/// <summary>Result of a create/update attempt: either the persisted DTO or a set of field errors.</summary>
public sealed class BadgeDefinitionAdminResult
{
    public BadgeDefinitionAdminDto? Value { get; private init; }
    public IReadOnlyList<RuleValidationError> Errors { get; private init; } = Array.Empty<RuleValidationError>();
    public bool IsConflict { get; private init; }
    public bool IsNotFound { get; private init; }

    public bool Succeeded => Value is not null;

    public static BadgeDefinitionAdminResult Ok(BadgeDefinitionAdminDto value) => new() { Value = value };
    public static BadgeDefinitionAdminResult Invalid(IReadOnlyList<RuleValidationError> errors) => new() { Errors = errors };
    public static BadgeDefinitionAdminResult Conflict(string field, string message) => new()
    {
        IsConflict = true,
        Errors = new[] { new RuleValidationError(field, message) },
    };
    public static BadgeDefinitionAdminResult NotFound() => new() { IsNotFound = true };
}

/// <summary>
/// Issue #148: CRUD for <see cref="BadgeDefinition"/> behind the admin-only controller. No hard delete —
/// badges already awarded (<see cref="BadgeEarned"/>) must remain valid, so definitions are only ever
/// created, edited, or deactivated/reactivated (<see cref="BadgeDefinition.IsActive"/>).
/// </summary>
public class BadgeDefinitionAdminService
{
    private readonly BadgeDbContext _context;

    public BadgeDefinitionAdminService(BadgeDbContext context)
    {
        _context = context;
    }

    public async Task<List<BadgeDefinitionAdminDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = _context.BadgeDefinitions.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        var rows = await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    public async Task<BadgeDefinitionAdminDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _context.BadgeDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<BadgeDefinitionAdminResult> CreateAsync(CreateBadgeDefinitionRequest request, string actor, CancellationToken cancellationToken)
    {
        var errors = new List<RuleValidationError>();

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors.Add(new RuleValidationError("code", "code zorunludur."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new RuleValidationError("name", "name zorunludur."));
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            errors.Add(new RuleValidationError("category", "category zorunludur."));
        }

        if (!BadgeIconValidator.IsValid(request.IconUrl))
        {
            errors.Add(new RuleValidationError("iconUrl", "iconUrl 'achievements/<dosya>.svg' biçiminde olmalıdır."));
        }

        var ruleValid = BadgeRuleTypeCatalog.TryValidateAndNormalize(
            request.RuleType, request.RuleConfigJson, out var normalizedConfig, out var ruleErrors);
        errors.AddRange(ruleErrors);

        if (errors.Count > 0)
        {
            return BadgeDefinitionAdminResult.Invalid(errors);
        }

        var codeExists = await _context.BadgeDefinitions
            .AsNoTracking()
            .AnyAsync(x => x.Code.ToLower() == request.Code.Trim().ToLower(), cancellationToken);
        if (codeExists)
        {
            return BadgeDefinitionAdminResult.Conflict("code", $"'{request.Code}' koduna sahip bir rozet zaten var.");
        }

        var now = DateTime.UtcNow;
        var entity = new BadgeDefinition
        {
            Id = Guid.NewGuid(),
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            IconUrl = string.IsNullOrWhiteSpace(request.IconUrl) ? null : request.IconUrl.Trim(),
            Category = request.Category.Trim(),
            RuleType = ResolveCanonicalRuleType(request.RuleType),
            RuleConfigJson = normalizedConfig,
            PathKey = request.PathKey,
            PathName = request.PathName,
            PathOrder = request.PathOrder,
            IsActive = true,
            CreatedBy = actor,
            CreatedAtUtc = now,
        };

        _context.BadgeDefinitions.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return BadgeDefinitionAdminResult.Ok(ToDto(entity));
    }

    public async Task<BadgeDefinitionAdminResult> UpdateAsync(Guid id, UpdateBadgeDefinitionRequest request, string actor, CancellationToken cancellationToken)
    {
        var entity = await _context.BadgeDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return BadgeDefinitionAdminResult.NotFound();
        }

        var errors = new List<RuleValidationError>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new RuleValidationError("name", "name zorunludur."));
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            errors.Add(new RuleValidationError("category", "category zorunludur."));
        }

        if (!BadgeIconValidator.IsValid(request.IconUrl))
        {
            errors.Add(new RuleValidationError("iconUrl", "iconUrl 'achievements/<dosya>.svg' biçiminde olmalıdır."));
        }

        var ruleValid = BadgeRuleTypeCatalog.TryValidateAndNormalize(
            request.RuleType, request.RuleConfigJson, out var normalizedConfig, out var ruleErrors);
        errors.AddRange(ruleErrors);

        if (errors.Count > 0)
        {
            return BadgeDefinitionAdminResult.Invalid(errors);
        }

        // Issue #148 semantics: editing a live badge's rule/threshold never touches already-awarded
        // BadgeEarned rows (untouched here) — BadgeEvaluator re-reads RuleConfigJson from now on
        // (AsNoTracking, no caching), so the new rule/threshold applies to future evaluations only.
        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim() ?? string.Empty;
        entity.IconUrl = string.IsNullOrWhiteSpace(request.IconUrl) ? null : request.IconUrl.Trim();
        entity.Category = request.Category.Trim();
        entity.RuleType = ResolveCanonicalRuleType(request.RuleType);
        entity.RuleConfigJson = normalizedConfig;
        entity.PathKey = request.PathKey;
        entity.PathName = request.PathName;
        entity.PathOrder = request.PathOrder;
        entity.UpdatedBy = actor;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return BadgeDefinitionAdminResult.Ok(ToDto(entity));
    }

    /// <summary>Idempotent: setting IsActive to a value it already has is a no-op (still returns the DTO).</summary>
    public async Task<BadgeDefinitionAdminResult> SetActiveAsync(Guid id, bool isActive, string actor, CancellationToken cancellationToken)
    {
        var entity = await _context.BadgeDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return BadgeDefinitionAdminResult.NotFound();
        }

        if (entity.IsActive != isActive)
        {
            entity.IsActive = isActive;
            entity.UpdatedBy = actor;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return BadgeDefinitionAdminResult.Ok(ToDto(entity));
    }

    private static string ResolveCanonicalRuleType(string ruleType) =>
        BadgeRuleTypeCatalog.TryGetSchema(ruleType, out var schema) ? schema.RuleType : ruleType;

    private static BadgeDefinitionAdminDto ToDto(BadgeDefinition entity) => new()
    {
        Id = entity.Id,
        Code = entity.Code,
        Name = entity.Name,
        Description = entity.Description,
        IconUrl = entity.IconUrl,
        Category = entity.Category,
        RuleType = entity.RuleType,
        RuleConfigJson = entity.RuleConfigJson,
        PathKey = entity.PathKey,
        PathName = entity.PathName,
        PathOrder = entity.PathOrder,
        IsActive = entity.IsActive,
        CreatedBy = entity.CreatedBy,
        CreatedAtUtc = entity.CreatedAtUtc,
        UpdatedBy = entity.UpdatedBy,
        UpdatedAtUtc = entity.UpdatedAtUtc,
    };
}
