namespace BadgeService.Consumers;

/// <summary>
/// Randevu bildirimlerindeki zaman metni: <c>dd.MM.yyyy HH:mm-HH:mm</c>. issue #300: slot gece yarısını (UTC) geçiyorsa
/// (<c>EndTime &lt; StartTime</c>; exam API'deki <c>SlotTimeRange</c> kuralıyla aynı) bitiş ertesi gündür ve bitiş saatine
/// <c>" (+1)"</c> eklenir — ör. <c>16.06.2026 23:30-00:30 (+1)</c>. Event contract'ı değişmedi; hesap mevcut
/// <c>Date</c>/<c>StartTime</c>/<c>EndTime</c> alanlarından yapılır.
/// </summary>
internal static class BookingWhenText
{
    public static string Format(DateOnly date, TimeOnly start, TimeOnly end)
        => $"{date:dd.MM.yyyy} {start:HH:mm}-{end:HH:mm}{(end < start ? " (+1)" : string.Empty)}";
}
