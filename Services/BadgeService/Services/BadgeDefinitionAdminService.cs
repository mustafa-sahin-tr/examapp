using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Entities;
using BadgeService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

/// <summary>Paged result for GET (list) — added by the #148 security review (M2, contract change).</summary>
public sealed class BadgeDefinitionAdminPage
{
    public required IReadOnlyList<BadgeDefinitionAdminDto> Items { get; init; }
    public required int TotalCount { get; init; }
}

/// <summary>
/// Issue #148: CRUD for <see cref="BadgeDefinition"/> behind the admin-only controller. No hard delete —
/// badges already awarded (<see cref="BadgeEarned"/>) must remain valid, so definitions are only ever
/// created, edited, or deactivated/reactivated (<see cref="BadgeDefinition.IsActive"/>).
/// </summary>
public class BadgeDefinitionAdminService
{
    // Security review follow-up (M1) — mirrored by BadgeDbContext's HasMaxLength (DB-level backstop) and
    // reported to the UI team so the admin form enforces the exact same limits client-side.
    public const int MaxCodeLength = 64;
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxCategoryLength = 100;
    public const int MaxPathKeyLength = 100;
    public const int MaxPathNameLength = 100;
    public const int MinPathOrder = 1;
    public const int MaxPathOrder = 1000;

    /// <summary>Lowercase letters/digits, single hyphens between segments — matches every existing seeder Code.</summary>
    public static readonly Regex CodePattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    // Security review follow-up (M2) — arbitrary but deliberately generous cap so a runaway admin script
    // (or bug) can't make BadgeEvaluator scan an unbounded table on every answer submission.
    public const int MaxActiveDefinitions = 500;

    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    private readonly BadgeDbContext _context;
    private readonly ILogger<BadgeDefinitionAdminService> _logger;

    public BadgeDefinitionAdminService(BadgeDbContext context, ILogger<BadgeDefinitionAdminService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Security review follow-up (M2, contract change): now paged (<paramref name="skip"/>/<paramref name="take"/>,
    /// <paramref name="take"/> clamped to 1..<see cref="MaxPageSize"/>, defaults to <see cref="DefaultPageSize"/>)
    /// instead of returning every row.
    /// </summary>
    public async Task<BadgeDefinitionAdminPage> ListAsync(bool includeInactive, int skip, int take, CancellationToken cancellationToken)
    {
        skip = Math.Max(0, skip);
        take = take <= 0 ? DefaultPageSize : Math.Min(take, MaxPageSize);

        var query = _context.BadgeDefinitions.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Code review follow-up (#148, SHOULD-FIX): Category/Name aren't unique — without a final
        // tie-breaker, Skip/Take pages aren't guaranteed stable across requests (the DB is free to order
        // ties differently each time), which can duplicate or skip rows between pages. Code is unique.
        var rows = await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new BadgeDefinitionAdminPage { Items = rows.Select(ToDto).ToList(), TotalCount = totalCount };
    }

    public async Task<BadgeDefinitionAdminDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _context.BadgeDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<BadgeDefinitionAdminResult> CreateAsync(
        CreateBadgeDefinitionRequest request, string actorId, string? actorName, CancellationToken cancellationToken)
    {
        var errors = new List<RuleValidationError>();

        // Code review follow-up (#148, NIT): trim before validating length/format, not just before
        // storing — otherwise "  " + 100 significant chars can pass length validation but fail once
        // trimmed at save (or vice versa: pure whitespace already caught by the required-field checks).
        var iconUrl = Normalize(request.IconUrl);
        var pathKey = Normalize(request.PathKey);
        var pathName = Normalize(request.PathName);

        ValidateCode(request.Code, errors);
        ValidateCommonFields(request.Name, request.Description, iconUrl, request.Category, pathKey, pathName, request.PathOrder, errors);

        var ruleValid = BadgeRuleTypeCatalog.TryValidateAndNormalize(
            request.RuleType, request.RuleConfigJson, out var normalizedConfig, out var ruleErrors, _logger);
        errors.AddRange(ruleErrors);

        if (errors.Count > 0)
        {
            return BadgeDefinitionAdminResult.Invalid(errors);
        }

        var trimmedCode = request.Code.Trim();
        var codeExists = await _context.BadgeDefinitions
            .AsNoTracking()
            .AnyAsync(x => x.Code.ToLower() == trimmedCode.ToLower(), cancellationToken);
        if (codeExists)
        {
            return BadgeDefinitionAdminResult.Conflict("code", $"'{trimmedCode}' koduna sahip bir rozet zaten var.");
        }

        var activeCapResult = await CheckActiveCapAsync(cancellationToken);
        if (activeCapResult is not null)
        {
            return activeCapResult;
        }

        var now = DateTime.UtcNow;
        var entity = new BadgeDefinition
        {
            Id = Guid.NewGuid(),
            Code = trimmedCode,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            IconUrl = iconUrl,
            Category = request.Category.Trim(),
            RuleType = ResolveCanonicalRuleType(request.RuleType),
            RuleConfigJson = normalizedConfig,
            PathKey = pathKey,
            PathName = pathName,
            PathOrder = request.PathOrder,
            IsActive = true,
            CreatedBy = actorId,
            CreatedByName = actorName,
            CreatedAtUtc = now,
        };

        _context.BadgeDefinitions.Add(entity);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Security review follow-up (L3): the pre-check above can lose a race to a concurrent create
            // with the same Code — detect that here (provider-agnostic: re-query rather than branch on
            // Npgsql/Sqlite exception types) and report 409, not a raw 500.
            _context.Entry(entity).State = EntityState.Detached;
            var stillExists = await _context.BadgeDefinitions
                .AsNoTracking()
                .AnyAsync(x => x.Code.ToLower() == trimmedCode.ToLower(), cancellationToken);
            if (stillExists)
            {
                _logger.LogInformation(ex, "BadgeDefinition oluşturma, eşzamanlı Code çakışması nedeniyle 409'a düştü: {Code}", trimmedCode);
                return BadgeDefinitionAdminResult.Conflict("code", $"'{trimmedCode}' koduna sahip bir rozet zaten var.");
            }

            throw;
        }

        return BadgeDefinitionAdminResult.Ok(ToDto(entity));
    }

    public async Task<BadgeDefinitionAdminResult> UpdateAsync(
        Guid id, UpdateBadgeDefinitionRequest request, string actorId, string? actorName, CancellationToken cancellationToken)
    {
        var entity = await _context.BadgeDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return BadgeDefinitionAdminResult.NotFound();
        }

        var errors = new List<RuleValidationError>();

        var iconUrl = Normalize(request.IconUrl);
        var pathKey = Normalize(request.PathKey);
        var pathName = Normalize(request.PathName);

        ValidateCommonFields(request.Name, request.Description, iconUrl, request.Category, pathKey, pathName, request.PathOrder, errors);

        var ruleValid = BadgeRuleTypeCatalog.TryValidateAndNormalize(
            request.RuleType, request.RuleConfigJson, out var normalizedConfig, out var ruleErrors, _logger);
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
        entity.IconUrl = iconUrl;
        entity.Category = request.Category.Trim();
        entity.RuleType = ResolveCanonicalRuleType(request.RuleType);
        entity.RuleConfigJson = normalizedConfig;
        entity.PathKey = pathKey;
        entity.PathName = pathName;
        entity.PathOrder = request.PathOrder;
        entity.UpdatedBy = actorId;
        entity.UpdatedByName = actorName;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return BadgeDefinitionAdminResult.Ok(ToDto(entity));
    }

    /// <summary>
    /// Idempotent: setting IsActive to a value it already has is a no-op (still returns the DTO). Going
    /// false→true is subject to <see cref="MaxActiveDefinitions"/> (M2) — going true→false never is.
    /// </summary>
    public async Task<BadgeDefinitionAdminResult> SetActiveAsync(Guid id, bool isActive, string actorId, string? actorName, CancellationToken cancellationToken)
    {
        var entity = await _context.BadgeDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return BadgeDefinitionAdminResult.NotFound();
        }

        if (entity.IsActive == isActive)
        {
            return BadgeDefinitionAdminResult.Ok(ToDto(entity));
        }

        if (isActive)
        {
            var activeCapResult = await CheckActiveCapAsync(cancellationToken);
            if (activeCapResult is not null)
            {
                return activeCapResult;
            }
        }

        entity.IsActive = isActive;
        entity.UpdatedBy = actorId;
        entity.UpdatedByName = actorName;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return BadgeDefinitionAdminResult.Ok(ToDto(entity));
    }

    private async Task<BadgeDefinitionAdminResult?> CheckActiveCapAsync(CancellationToken cancellationToken)
    {
        var activeCount = await _context.BadgeDefinitions.CountAsync(x => x.IsActive, cancellationToken);
        if (activeCount >= MaxActiveDefinitions)
        {
            return BadgeDefinitionAdminResult.Conflict(
                "isActive", $"Aktif rozet sayısı üst sınıra ulaştı (en fazla {MaxActiveDefinitions}). Önce başka bir rozeti pasifleştirin.");
        }

        return null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateCode(string code, List<RuleValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            errors.Add(new RuleValidationError("code", "code zorunludur."));
            return;
        }

        var trimmed = code.Trim();
        if (trimmed.Length > MaxCodeLength)
        {
            errors.Add(new RuleValidationError("code", $"code en fazla {MaxCodeLength} karakter olabilir."));
        }

        if (!CodePattern.IsMatch(trimmed))
        {
            errors.Add(new RuleValidationError(
                "code", "code yalnızca küçük harf, rakam ve segmentleri ayıran tek tireden oluşabilir (örn. 'subject-matematik-mastery')."));
        }
    }

    private static void ValidateCommonFields(
        string name, string? description, string? iconUrl, string category, string? pathKey, string? pathName, int? pathOrder, List<RuleValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new RuleValidationError("name", "name zorunludur."));
        }
        else if (name.Trim().Length > MaxNameLength)
        {
            errors.Add(new RuleValidationError("name", $"name en fazla {MaxNameLength} karakter olabilir."));
        }

        if ((description ?? string.Empty).Trim().Length > MaxDescriptionLength)
        {
            errors.Add(new RuleValidationError("description", $"description en fazla {MaxDescriptionLength} karakter olabilir."));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            errors.Add(new RuleValidationError("category", "category zorunludur."));
        }
        else if (category.Trim().Length > MaxCategoryLength)
        {
            errors.Add(new RuleValidationError("category", $"category en fazla {MaxCategoryLength} karakter olabilir."));
        }

        if (!BadgeIconValidator.IsValid(iconUrl))
        {
            errors.Add(new RuleValidationError("iconUrl", "iconUrl 'achievements/<dosya>.svg' biçiminde olmalıdır."));
        }

        if (pathKey is { Length: > 0 } && pathKey.Length > MaxPathKeyLength)
        {
            errors.Add(new RuleValidationError("pathKey", $"pathKey en fazla {MaxPathKeyLength} karakter olabilir."));
        }

        if (pathName is { Length: > 0 } && pathName.Length > MaxPathNameLength)
        {
            errors.Add(new RuleValidationError("pathName", $"pathName en fazla {MaxPathNameLength} karakter olabilir."));
        }

        if (pathOrder.HasValue && (pathOrder.Value < MinPathOrder || pathOrder.Value > MaxPathOrder))
        {
            errors.Add(new RuleValidationError("pathOrder", $"pathOrder {MinPathOrder} ile {MaxPathOrder} arasında olmalıdır."));
        }
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
        CreatedByName = entity.CreatedByName,
        CreatedAtUtc = entity.CreatedAtUtc,
        UpdatedBy = entity.UpdatedBy,
        UpdatedByName = entity.UpdatedByName,
        UpdatedAtUtc = entity.UpdatedAtUtc,
    };
}
