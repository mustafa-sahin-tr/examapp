using System;

namespace BadgeService.Models;

/// <summary>Response shape for the admin badge-definition CRUD endpoints (issue #148).</summary>
public class BadgeDefinitionAdminDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string Category { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty;
    public string RuleConfigJson { get; set; } = string.Empty;
    public string? PathKey { get; set; }
    public string? PathName { get; set; }
    public int? PathOrder { get; set; }
    public bool IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>
/// POST /api/admin/badge-definitions body. <see cref="Code"/> is immutable once created — it is the
/// seeder's non-overwrite key (issue #148, owner decision #3).
/// </summary>
public class CreateBadgeDefinitionRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string Category { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty;

    /// <summary>Raw JSON object matching the RuleType's schema (see GET .../rule-types).</summary>
    public string RuleConfigJson { get; set; } = string.Empty;
    public string? PathKey { get; set; }
    public string? PathName { get; set; }
    public int? PathOrder { get; set; }
}

/// <summary>
/// PUT /api/admin/badge-definitions/{id} body. Code and IsActive are intentionally absent — Code is
/// immutable, IsActive is only changed via the dedicated activate/deactivate endpoints so that action is
/// explicit and auditable on its own.
/// </summary>
public class UpdateBadgeDefinitionRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string Category { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty;
    public string RuleConfigJson { get; set; } = string.Empty;
    public string? PathKey { get; set; }
    public string? PathName { get; set; }
    public int? PathOrder { get; set; }
}
