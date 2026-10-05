using System;
using System.Collections.Generic;
using System.Linq;

namespace BadgeService.Services;

/// <summary>
/// Issue #149: the single source of truth for the badge icons an admin may pick. Values are Material
/// Symbols names (also present in the classic "Material Icons" font the UI currently loads). The UI never
/// copies this list — it reads it from <c>GET api/admin/badge-definitions/icons</c>. Free text is not
/// allowed because an unknown name renders as the raw ligature text instead of a glyph.
/// </summary>
/// <remarks>
/// <see cref="Category"/> values are stable keys (the UI translates them): <c>achievement</c> (başarı),
/// <c>learning</c> (ders), <c>time</c> (zaman), <c>streak</c> (seri / aktivite), <c>other</c> (diğer).
/// The first 20 entries are the icons <see cref="BadgeService.Data.BadgeSeeder"/> uses.
/// </remarks>
public static class BadgeIconCatalog
{
    public const string Achievement = "achievement";
    public const string Learning = "learning";
    public const string Time = "time";
    public const string Streak = "streak";
    public const string Other = "other";

    /// <summary>Category keys in display order (for the admin picker's chips).</summary>
    public static IReadOnlyList<string> Categories { get; } = new[] { Achievement, Learning, Time, Streak, Other };

    private static readonly BadgeIconEntry[] Entries =
    {
        // --- Seed icons (BadgeSeeder, issue #149 icon table) ---
        new("flag", Achievement),
        new("done_all", Achievement),
        new("gps_fixed", Achievement),
        new("task_alt", Achievement),
        new("rocket_launch", Achievement),
        new("theater_comedy", Other),
        new("psychology", Learning),
        new("schedule", Time),
        new("visibility", Other),
        new("travel_explore", Time),
        new("school", Learning),
        new("workspace_premium", Achievement),
        new("menu_book", Learning),
        new("verified", Achievement),
        new("timer", Time),
        new("calculate", Learning),
        new("science", Learning),
        new("public", Learning),
        new("local_fire_department", Streak),
        new("event_available", Streak),

        // --- Additional badge-themed icons for admin-created badges ---
        new("emoji_events", Achievement),
        new("military_tech", Achievement),
        new("star", Achievement),
        new("diamond", Achievement),
        new("celebration", Achievement),
        new("trending_up", Achievement),
        new("bolt", Achievement),
        new("auto_stories", Learning),
        new("edit_note", Learning),
        new("lightbulb", Learning),
        new("quiz", Learning),
        new("functions", Learning),
        new("translate", Learning),
        new("history_edu", Learning),
        new("palette", Learning),
        new("hourglass_top", Time),
        new("alarm", Time),
        new("update", Time),
        new("av_timer", Time),
        new("whatshot", Streak),
        new("event_repeat", Streak),
        new("calendar_month", Streak),
        new("today", Streak),
        new("directions_run", Streak),
        new("favorite", Other),
        new("extension", Other),
        new("explore", Other),
        new("pets", Other),
    };

    private static readonly HashSet<string> Names = new(Entries.Select(e => e.Name), StringComparer.Ordinal);

    public static IReadOnlyList<BadgeIconEntry> All => Entries;

    /// <summary>Exact (case-sensitive) membership — names are always lower-case snake_case.</summary>
    public static bool Contains(string? name) => name is not null && Names.Contains(name);
}

/// <summary>One allowlisted icon. Serialized as <c>{ "name": "...", "category": "..." }</c>.</summary>
public sealed record BadgeIconEntry(string Name, string Category);
