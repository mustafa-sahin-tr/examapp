using System.Text.RegularExpressions;

namespace BadgeService.Services;

/// <summary>
/// Issue #148: minimal format validation for <c>BadgeDefinition.IconUrl</c> until the real icon picker
/// (#149, separate issue) lands. Keeps the existing seeded convention (relative path under
/// <c>achievements/</c>, <c>.svg</c>) and rejects absolute URLs / path traversal — it does not check that
/// the file actually exists in storage.
/// </summary>
public static partial class BadgeIconValidator
{
    [GeneratedRegex(@"^achievements/[A-Za-z0-9._-]+\.svg$")]
    private static partial Regex AllowedIconPattern();

    public static bool IsValid(string? iconUrl)
    {
        // Null/empty is allowed — a badge without an icon is valid (falls back to a default in the UI).
        if (string.IsNullOrWhiteSpace(iconUrl))
        {
            return true;
        }

        return AllowedIconPattern().IsMatch(iconUrl);
    }
}
