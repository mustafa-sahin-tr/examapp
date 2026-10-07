using System;
using System.Collections.Generic;
using System.Linq;
using ExamApp.Foundation.Badges;

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
    // Issue #422: liste ExamApp.Foundation.Badges.BadgeIconAllowlist'e taşındı (exam API consumer'ı da aynı kuralı uygular);
    // bu sınıf BadgeService içindeki mevcut çağıranlar için ince bir sarmalayıcıdır.
    public const string Achievement = BadgeIconAllowlist.Achievement;
    public const string Learning = BadgeIconAllowlist.Learning;
    public const string Time = BadgeIconAllowlist.Time;
    public const string Streak = BadgeIconAllowlist.Streak;
    public const string Other = BadgeIconAllowlist.Other;

    /// <summary>Category keys in display order (for the admin picker's chips).</summary>
    public static IReadOnlyList<string> Categories => BadgeIconAllowlist.Categories;

    private static readonly BadgeIconEntry[] Entries =
        BadgeIconAllowlist.Entries.Select(e => new BadgeIconEntry(e.Name, e.Category)).ToArray();

    public static IReadOnlyList<BadgeIconEntry> All => Entries;

    /// <summary>Exact (case-sensitive) membership — names are always lower-case snake_case.</summary>
    public static bool Contains(string? name) => BadgeIconAllowlist.Contains(name);
}

/// <summary>One allowlisted icon. Serialized as <c>{ "name": "...", "category": "..." }</c>.</summary>
public sealed record BadgeIconEntry(string Name, string Category);
