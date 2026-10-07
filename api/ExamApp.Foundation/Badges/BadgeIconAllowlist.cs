using System.Text.RegularExpressions;

namespace ExamApp.Foundation.Badges;

/// <summary>
/// Rozet ikonlarının TEK izin listesi (issue #149'da BadgeService'te doğdu; issue #422 security review ile Foundation'a taşındı ki
/// exam API'deki <c>StudentBadgeEarnedConsumer</c> da event'ten gelen ikonu aynı kurala göre süzsün). Değerler Material Symbols
/// adları. BadgeService <c>BadgeIconCatalog</c>/<c>BadgeIconValidator</c> bu listeye devreder.
/// </summary>
public static partial class BadgeIconAllowlist
{
    public const string Achievement = "achievement";
    public const string Learning = "learning";
    public const string Time = "time";
    public const string Streak = "streak";
    public const string Other = "other";

    /// <summary>Kolon uzunluğu — regex üst sınırıyla aynı (1 + 63).</summary>
    public const int MaxIconLength = 64;

    /// <summary>Kategori anahtarları (görüntüleme sırası).</summary>
    public static IReadOnlyList<string> Categories { get; } = new[] { Achievement, Learning, Time, Streak, Other };

    /// <summary>(Ad, kategori) sırası korunur; ilk 20'si BadgeSeeder ikonlarıdır.</summary>
    public static IReadOnlyList<(string Name, string Category)> Entries { get; } = new (string, string)[]
    {
        // --- Seed icons (BadgeSeeder, issue #149 icon table) ---
        ("flag", Achievement),
        ("done_all", Achievement),
        ("gps_fixed", Achievement),
        ("task_alt", Achievement),
        ("rocket_launch", Achievement),
        ("theater_comedy", Other),
        ("psychology", Learning),
        ("schedule", Time),
        ("visibility", Other),
        ("travel_explore", Time),
        ("school", Learning),
        ("workspace_premium", Achievement),
        ("menu_book", Learning),
        ("verified", Achievement),
        ("timer", Time),
        ("calculate", Learning),
        ("science", Learning),
        ("public", Learning),
        ("local_fire_department", Streak),
        ("event_available", Streak),

        // --- Additional badge-themed icons for admin-created badges ---
        ("emoji_events", Achievement),
        ("military_tech", Achievement),
        ("star", Achievement),
        ("diamond", Achievement),
        ("celebration", Achievement),
        ("trending_up", Achievement),
        ("bolt", Achievement),
        ("auto_stories", Learning),
        ("edit_note", Learning),
        ("lightbulb", Learning),
        ("quiz", Learning),
        ("functions", Learning),
        ("translate", Learning),
        ("history_edu", Learning),
        ("palette", Learning),
        ("hourglass_top", Time),
        ("alarm", Time),
        ("update", Time),
        ("av_timer", Time),
        ("whatshot", Streak),
        ("event_repeat", Streak),
        ("calendar_month", Streak),
        ("today", Streak),
        ("directions_run", Streak),
        ("favorite", Other),
        ("extension", Other),
        ("explore", Other),
        ("pets", Other),
    };

    private static readonly HashSet<string> Names = new(Entries.Select(e => e.Name), StringComparer.Ordinal);

    [GeneratedRegex(@"^[a-z][a-z0-9_]{1,63}$")]
    private static partial Regex IconNamePattern();

    /// <summary>Tam (büyük/küçük harf duyarlı) üyelik.</summary>
    public static bool Contains(string? name) => name is not null && Names.Contains(name);

    /// <summary>Boş olmayan, biçimi doğru ve listede olan ad — istemciye ikon göndermeden / saklamadan önce kullanın.</summary>
    public static bool IsAllowed(string? icon) =>
        !string.IsNullOrEmpty(icon) && IconNamePattern().IsMatch(icon) && Names.Contains(icon);
}
