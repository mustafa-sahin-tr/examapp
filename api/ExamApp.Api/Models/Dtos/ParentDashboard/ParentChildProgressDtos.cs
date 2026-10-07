using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.ParentDashboard;

/// <summary>
/// Issue #422 (epic #407 V4): <c>GET api/parent/children/{studentId}/progress</c> — çocuğun puanı, seviyesi, rozetleri ve
/// KENDİ sıralaması. Başka öğrencinin adı, avatarı ya da puanı YOK (sıralamada yalnızca çocuğun sırası + kapsamdaki öğrenci
/// sayısı). Alan listesi ParentProgressServiceTests ve ParentProgressEndpointsTests'te kilitli.
/// </summary>
public sealed class ParentChildProgressDto
{
    public int StudentId { get; set; }

    /// <summary>Toplam puan (<c>StudentPoints.XP</c>; öğrenci profili ve V2 özetiyle aynı).</summary>
    public int TotalXp { get; set; }

    /// <summary>Seviye — öğrenci profiliyle aynı formül (<c>StudentLevel.FromXp</c>).</summary>
    public int Level { get; set; }

    /// <summary>Bu haftanın ilk günü (Pazartesi, Europe/Istanbul), "yyyy-MM-dd".</summary>
    public DateOnly WeekStart { get; set; }

    /// <summary>
    /// Bu hafta (yerel Pazartesi–bugün) kazanılan puan — exam DB'deki günlük puan defterinden (<c>StudentDailyXps</c>,
    /// <c>StudentPointsChangedEvent</c> senkronu besler).
    /// </summary>
    public int WeeklyXp { get; set; }

    /// <summary>
    /// Kazanılmış rozetler (en yeni önce) — exam DB projeksiyonundan (<c>StudentBadgeProjections</c>,
    /// <c>StudentBadgeEarnedEvent</c> besler). Projeksiyondan önce kazanılmış rozetler yeniden kazanılana kadar yer almaz.
    /// </summary>
    public List<ParentBadgeDto> Badges { get; set; } = new();

    /// <summary>Çocuğun kendi sırası: her zaman platform geneli, doğrulanmış okulu varsa okul içi de.</summary>
    public List<ParentChildRankDto> Ranks { get; set; } = new();
}

/// <summary>Kazanılmış rozet: ad, ikon ve GÜNE kesilmiş kazanma tarihi (yerel takvim günü).</summary>
public sealed class ParentBadgeDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Material Symbols adı (BadgeService allowlist'inden geçmiş); yoksa null (UI varsayılan ikon gösterir).</summary>
    public string? Icon { get; set; }

    /// <summary>Kazanıldığı gün (Europe/Istanbul), "yyyy-MM-dd". Saat bilgisi bilinçli olarak yok.</summary>
    public DateOnly EarnedOn { get; set; }
}

/// <summary>Sıralama kapsamı değerleri.</summary>
public static class ParentRankScopes
{
    public const string Global = "global";
    public const string School = "school";
}

/// <summary>
/// Çocuğun bir kapsamdaki sırası. Liderlik tablosuyla (#193) aynı kural: XP azalan, eşitlikte öğrenci id artan. Başka
/// öğrenciye ait hiçbir alan yok.
/// </summary>
public sealed class ParentChildRankDto
{
    /// <summary><see cref="ParentRankScopes"/>.</summary>
    public string Scope { get; set; } = ParentRankScopes.Global;

    /// <summary>1 tabanlı sıra.</summary>
    public int Rank { get; set; }

    /// <summary>Kapsamdaki öğrenci sayısı.</summary>
    public int TotalCount { get; set; }
}
