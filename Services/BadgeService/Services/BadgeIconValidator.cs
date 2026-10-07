using System.Text.RegularExpressions;
using ExamApp.Foundation.Badges;

namespace BadgeService.Services;

/// <summary>
/// Badge icon validation.
/// <list type="bullet">
/// <item><see cref="IsValidIcon"/> (issue #149): the new <c>BadgeDefinition.Icon</c> must be a Material
/// Symbols name from <see cref="BadgeIconCatalog"/> — format <c>^[a-z][a-z0-9_]{1,63}$</c> AND allowlisted.</item>
/// <item><see cref="IsValid"/> (issue #148, legacy): format-only check for the fallback <c>IconUrl</c>
/// (relative path under <c>achievements/</c>, <c>.svg</c>; rejects absolute URLs / path traversal). Kept
/// for the transition; removed together with <c>IconUrl</c> one release later.</item>
/// </list>
/// </summary>
public static partial class BadgeIconValidator
{
    /// <summary>Column length (BadgeDbContext) — equals the regex upper bound (1 + 63).</summary>
    public const int MaxIconLength = BadgeIconAllowlist.MaxIconLength;

    [GeneratedRegex(@"^achievements/[A-Za-z0-9._-]+\.svg$")]
    private static partial Regex AllowedIconPattern();

    /// <summary>Legacy <c>IconUrl</c> rule (issue #148). Null/empty is allowed.</summary>
    public static bool IsValid(string? iconUrl)
    {
        // Null/empty is allowed — a badge without an icon is valid (falls back to a default in the UI).
        if (string.IsNullOrWhiteSpace(iconUrl))
        {
            return true;
        }

        return AllowedIconPattern().IsMatch(iconUrl);
    }

    /// <summary>
    /// Issue #149: null/empty is allowed (UI falls back to <c>iconUrl</c>, then <c>military_tech</c>);
    /// otherwise the name must match the format and be in <see cref="BadgeIconCatalog"/>.
    /// </summary>
    public static bool IsValidIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return true;
        }

        return IsAllowedIcon(icon);
    }

    /// <summary>True only for a non-empty, well-formed, allowlisted name — use before emitting an icon to clients.</summary>
    public static bool IsAllowedIcon(string? icon) => BadgeIconAllowlist.IsAllowed(icon); // issue #422: tek kural Foundation'da
}
