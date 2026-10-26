using System.Collections.Generic;

namespace BadgeService.Models;

/// <summary>Issue #572: öğrenci ekranı için günlük etkinlik özeti + en yakın etkinlik rozetleri.</summary>
public class ActivitySummaryDto
{
    public ActivityTodayDto Today { get; set; } = new();
    public List<ActivityBadgeProgressDto> Badges { get; set; } = new();
}

/// <summary>"Bugün" = Europe/Istanbul yerel günü.</summary>
public class ActivityTodayDto
{
    /// <summary>Bugün tamamlanan etkinlik denemesi sayısı.</summary>
    public int ActivityCount { get; set; }

    /// <summary>Bugün çalışılan etkin süre (dakika, aşağı yuvarlanmış).</summary>
    public int ActiveMinutes { get; set; }

    /// <summary>Güncel ardışık etkinlik günü (bugün henüz yoksa dünden geriye).</summary>
    public int StreakDays { get; set; }
}

public class ActivityBadgeProgressDto
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public int Current { get; set; }
    public int Target { get; set; }
    public bool Earned { get; set; }
}
