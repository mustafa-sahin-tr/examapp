using System;

namespace BadgeService.Entities;

public class BadgeDefinition
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable, human-assigned identity (issue #148, owner decision #3) — the seeder matches on this
    /// (not Name) so it never overwrites a row an admin has edited via the CRUD API. Immutable after
    /// creation; unique (see BadgeDbContext.OnModelCreating).
    /// </summary>
    public string Code { get; set; } = default!;

    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string? IconUrl { get; set; }
    public string Category { get; set; } = default!;
    public string RuleType { get; set; } = default!; // "AnswerCount", "CorrectStreak" vb.
    public string RuleConfigJson { get; set; } = default!;
    public string? PathKey { get; set; }
    public string? PathName { get; set; }
    public int? PathOrder { get; set; }

    /// <summary>
    /// Issue #148: deactivated badges are not evaluated/awarded (BadgeEvaluator) and hidden from the
    /// student catalog (StudentReportService), but rows already earned (BadgeEarned) remain valid —
    /// deactivation never deletes data.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
