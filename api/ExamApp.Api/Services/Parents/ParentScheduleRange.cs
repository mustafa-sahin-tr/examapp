using System;
using System.Globalization;
using ExamApp.Api.Models.Dtos.ParentDashboard;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Issue #422: veli programı <c>?from=&amp;to=</c> doğrulaması — veri okumaz, kapıdan önce çalışabilir. Kurallar:
/// <list type="bullet">
/// <item>İkisi de boş → <paramref name="defaultRange"/> (bu hafta).</item>
/// <item>İkisi birlikte, "yyyy-MM-dd" biçiminde (yerel takvim günü).</item>
/// <item><c>from &lt;= to</c>, en fazla <see cref="ParentChildScheduleDto.MaxDays"/> gün (iki uç dahil).</item>
/// <item><c>from &gt;= bugün − 1 yıl</c>, <c>to &lt;= bugün + 1 yıl</c> (review: uç yıllarda tarih aritmetiği taşmasın — 500 yerine 400).</item>
/// </list>
/// </summary>
public static class ParentScheduleRange
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Bugünden geriye / ileriye izin verilen en uzak yıl.</summary>
    public const int MaxYearsFromToday = 1;

    public readonly record struct Range(DateOnly From, DateOnly To);

    /// <summary>Bu hafta: <paramref name="today"/>'in Pazartesi'si – Pazar.</summary>
    public static Range CurrentWeek(DateOnly today)
    {
        var monday = ParentDashboardService.StartOfWeek(today);
        return new Range(monday, monday.AddDays(6));
    }

    public static bool TryResolve(string? from, string? to, DateOnly today, out Range range)
    {
        range = default;
        var hasFrom = !string.IsNullOrWhiteSpace(from);
        var hasTo = !string.IsNullOrWhiteSpace(to);
        if (!hasFrom && !hasTo)
        {
            range = CurrentWeek(today);
            return true;
        }

        if (!hasFrom || !hasTo
            || !DateOnly.TryParseExact(from!.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDay)
            || !DateOnly.TryParseExact(to!.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDay))
            return false;

        if (!IsValid(fromDay, toDay, today))
            return false;

        range = new Range(fromDay, toDay);
        return true;
    }

    /// <summary>Gün sayısı ve bugüne göre sınırlar (servis de savunma olarak çağırır).</summary>
    public static bool IsValid(DateOnly from, DateOnly to, DateOnly today)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        return days >= 1 && days <= ParentChildScheduleDto.MaxDays
            && from >= today.AddYears(-MaxYearsFromToday)
            && to <= today.AddYears(MaxYearsFromToday);
    }
}
