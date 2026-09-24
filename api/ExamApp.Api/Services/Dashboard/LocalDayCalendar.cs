using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Dashboard;

/// <summary>
/// <c>Dashboard</c> ayarları (issue #265). Dashboard pencereleri ve gün kovaları UTC değil bu saat diliminin yerel takvim
/// günüdür (ürün kararı: Türkiye günü — TR 00:00-03:00 arası artık bir önceki güne sayılmaz).
/// </summary>
public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>IANA saat dilimi kimliği (Linux/Windows'ta .NET 6+ ile çözülür). Geçersizse uygulama başlarken düşer.</summary>
    [Required]
    public string TimeZone { get; set; } = "Europe/Istanbul";

    /// <summary>
    /// Öğretmen aktivite toplamasının (own/students-activity-summary ortak hesabı) süreç içi önbellek süresi (saniye).
    /// 0 → önbellek kapalı (eşzamanlı istekler yine tek hesabı paylaşır).
    /// </summary>
    [Range(0, 3600)]
    public int TeacherActivityCacheSeconds { get; set; } = 60;
}

/// <summary>
/// Bugün (yerel) dahil son N yerel takvim günü. <see cref="StartUtc"/> = ilk günün yerel 00:00'ı, <see cref="EndUtc"/> =
/// son günden sonraki günün yerel 00:00'ı (hariç); ikisi de <see cref="DateTimeKind.Utc"/> (Npgsql timestamptz uyumlu).
/// </summary>
public sealed record LocalDayWindow(
    DateOnly FirstDay, DateOnly LastDay, DateTime StartUtc, DateTime EndUtc, IReadOnlyList<LocalDaySegment> Segments)
{
    public int Days => LastDay.DayNumber - FirstDay.DayNumber + 1;
}

/// <summary>
/// SQL gün gruplaması için pencere parçası. <see cref="Offset"/> parça boyunca sabittir: <c>utc + Offset</c>'in tarih
/// kısmı yerel günü verir. <see cref="SingleDay"/> doluysa parça, yaz saati geçişi olan (23/25 saatlik) tek bir gündür —
/// ofset gün içinde değiştiği için tarih kısmına güvenilmez; parçadaki tüm satırlar o güne sayılır.
/// Sabit ofsetli bölgelerde (Europe/Istanbul, 2016'dan beri UTC+3) pencere tek parçadır.
/// </summary>
public readonly record struct LocalDaySegment(DateTime StartUtc, DateTime EndUtc, TimeSpan Offset, DateOnly? SingleDay);

/// <summary>Dashboard'ların "yerel gün" kaynağı (issue #265).</summary>
public interface ILocalDayCalendar
{
    TimeZoneInfo TimeZone { get; }

    /// <summary>Yerel bugün.</summary>
    DateOnly Today { get; }

    /// <summary>Bugün (yerel) dahil son <paramref name="days"/> yerel takvim günü (days ≥ 1).</summary>
    LocalDayWindow LastDays(int days);

    /// <summary>Yerel gün <paramref name="day"/>'in başlangıç anı (UTC).</summary>
    DateTime StartOfDayUtc(DateOnly day);
}

/// <summary>
/// <see cref="ILocalDayCalendar"/> — saat dilimi <see cref="DashboardOptions.TimeZone"/>'dan, "şimdi"
/// <see cref="TimeProvider"/>'dan (testte sabitlenebilir). Singleton, durumsuz.
/// </summary>
public sealed class LocalDayCalendar : ILocalDayCalendar
{
    public const string DefaultTimeZoneId = "Europe/Istanbul";

    private readonly TimeProvider _clock;

    public LocalDayCalendar(IOptions<DashboardOptions> options, TimeProvider clock)
        : this(options.Value.TimeZone, clock)
    {
    }

    public LocalDayCalendar(string timeZoneId, TimeProvider? clock = null)
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>DI dışı (birim test / varsayılan) kurulum: Europe/Istanbul + sistem saati.</summary>
    public static LocalDayCalendar Default { get; } = new(DefaultTimeZoneId);

    public TimeZoneInfo TimeZone { get; }

    /// <summary>Ayar doğrulaması: kimlik bu makinede çözülebiliyor mu (tzdata/ICU eksikse de false).</summary>
    public static bool IsKnownTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public DateOnly Today
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.GetUtcNow().UtcDateTime, TimeZone));

    public DateTime StartOfDayUtc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // Yerel gece yarısı yaz saati geçişiyle atlanıyorsa (bazı bölgeler) günün ilk geçerli anı alınır.
        var guard = 0;
        while (TimeZone.IsInvalidTime(local) && guard++ < 24 * 4)
            local = local.AddMinutes(15);

        // Belirsiz (iki kez yaşanan) gece yarısı: en erken UTC anı = en büyük ofset.
        var offset = TimeZone.IsAmbiguousTime(local)
            ? MaxOffset(TimeZone.GetAmbiguousTimeOffsets(local))
            : TimeZone.GetUtcOffset(local);

        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

    public LocalDayWindow LastDays(int days)
    {
        if (days < 1)
            throw new ArgumentOutOfRangeException(nameof(days), days, "days must be >= 1");

        var lastDay = Today;
        var firstDay = lastDay.AddDays(-(days - 1));

        // Gün sınırları (UTC): boundaries[i] = firstDay+i'nin başlangıcı; boundaries[days] = pencere sonu (hariç).
        var boundaries = new DateTime[days + 1];
        for (var i = 0; i <= days; i++)
            boundaries[i] = StartOfDayUtc(firstDay.AddDays(i));

        // Ardışık 24 saatlik günler tek parçada birleşir; geçiş günü (≠ 24 saat) kendi başına parça olur.
        var segments = new List<LocalDaySegment>();
        var runStart = -1;
        for (var i = 0; i < days; i++)
        {
            var dayOffset = OffsetAt(boundaries[i], firstDay.AddDays(i));
            var isRegular = boundaries[i + 1] - boundaries[i] == TimeSpan.FromDays(1);

            if (!isRegular)
            {
                FlushRun(i);
                segments.Add(new LocalDaySegment(boundaries[i], boundaries[i + 1], dayOffset, firstDay.AddDays(i)));
                continue;
            }

            // Bitişik 24 saatlik günlerde kaydırma zaten aynıdır; ayrıca kontrol gerekmez.
            if (runStart < 0)
                runStart = i;
        }
        FlushRun(days);

        return new LocalDayWindow(firstDay, lastDay, boundaries[0], boundaries[days], segments);

        void FlushRun(int endExclusive)
        {
            if (runStart < 0)
                return;
            segments.Add(new LocalDaySegment(
                boundaries[runStart], boundaries[endExclusive],
                OffsetAt(boundaries[runStart], firstDay.AddDays(runStart)), null));
            runStart = -1;
        }
    }

    /// <summary>
    /// Günün başlangıç anı + bu kaydırma = o günün yerel 00:00'ı. 24 saatlik ardışık günlerde sabittir (sınırlar bitişik),
    /// bu yüzden bir parça boyunca <c>(utc + kaydırma).Date</c> yerel günü verir.
    /// </summary>
    private static TimeSpan OffsetAt(DateTime startUtc, DateOnly day)
        => day.ToDateTime(TimeOnly.MinValue) - DateTime.SpecifyKind(startUtc, DateTimeKind.Unspecified);

    private static TimeSpan MaxOffset(TimeSpan[] offsets)
    {
        var max = offsets[0];
        foreach (var o in offsets)
            if (o > max)
                max = o;
        return max;
    }
}
