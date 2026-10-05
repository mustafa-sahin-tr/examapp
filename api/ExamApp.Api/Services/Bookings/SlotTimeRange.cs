using System;
using System.Linq.Expressions;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// Müsaitlik slotu / randevunun UTC zaman aralığı — slot saatlerinin TEK yorum noktası (issue #300).
/// <para>
/// Slot <c>Date</c> + <c>StartTime</c> + <c>EndTime</c> olarak saklanır (saat dilimsiz duvar saati, UTC kabul edilir).
/// Kural: <c>EndTime &lt; StartTime</c> ise bitiş ERTESİ GÜNDÜR (gün aşan slot, ör. 23:30–00:30). Ayrı <c>EndDate</c>
/// kolonu yoktur (PO kararı, 2026-09-30).
/// </para>
/// <para>
/// Sıfır süre (<c>EndTime == StartTime</c>) oluşturmada reddedilir (<see cref="IsZeroLength"/>); 24 saatlik slot
/// anlamına GELMEZ. Veritabanında yine de böyle bir satır varsa <see cref="Duration"/> 0'dır, hiçbir aralıkla
/// çakışmaz, katılım penceresi hiç açılmaz ve "bitmiş" sayılır (<see cref="IsValid"/>, security review L3).
/// </para>
/// Aralık yarı açıktır: <c>[StartUtc, EndUtc)</c> — bitişik iki slot (10:00 bitiş / 10:00 başlangıç) çakışmaz.
/// </summary>
public readonly record struct SlotTimeRange(DateTime StartUtc, DateTime EndUtc)
{
    public TimeSpan Duration => EndUtc - StartUtc;

    /// <summary>Pozitif süreli mi? Sıfır süreli (hatalı) satır geçersizdir.</summary>
    public bool IsValid => EndUtc > StartUtc;

    /// <summary>Slot saatlerini UTC aralığa çevirir; <c>end &lt; start</c> ise bitiş ertesi gün, eşitse süre 0.</summary>
    public static SlotTimeRange From(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var startUtc = ToUtc(date, start);
        var endUtc = ToUtc(CrossesMidnight(start, end) ? date.AddDays(1) : date, end);
        return new SlotTimeRange(startUtc, endUtc);
    }

    /// <summary>Bitiş ertesi güne mi düşüyor? (<c>end &lt; start</c>; eşitlik gün aşımı DEĞİLDİR.)</summary>
    public static bool CrossesMidnight(TimeOnly start, TimeOnly end) => end < start;

    /// <summary>Bitiş = başlangıç: oluşturma uçlarında reddedilir (<c>booking.slot.zeroLength</c>).</summary>
    public static bool IsZeroLength(TimeOnly start, TimeOnly end) => end == start;

    /// <summary>
    /// Saat dakika hassasiyetinde mi (saniye/alt-saniye yok)? 14:00:00 ile 14:00:01 ayrı satır olup unique index'i anlamsız
    /// kılmasın ve takvim kararlı kalsın (tekrarlayan kural #178; tekil slot #323).
    /// </summary>
    public static bool IsMinutePrecision(TimeOnly time) => time.Ticks % TimeSpan.TicksPerMinute == 0;

    /// <summary>Tarihten bağımsız süre (gün aşan slotta ertesi güne kadar olan kısım dahil); sıfır süre → 0.</summary>
    public static TimeSpan DurationOf(TimeOnly start, TimeOnly end)
        => CrossesMidnight(start, end) ? TimeSpan.FromDays(1) - (start - end) : end - start;

    /// <summary>
    /// issue #323 (security O1): <see cref="IsZeroLength"/> + <see cref="DurationOf"/> &lt;= 4 saat kuralının PostgreSQL
    /// karşılığı — <c>TeacherAvailabilitySlots</c> ve <c>RecurringAvailabilityRules</c> CHECK constraint'i (yalnız Npgsql
    /// modelinde; SQLite test DB'si <c>time</c> aritmetiği bilmez). Kolonlar <c>time without time zone</c>:
    /// <c>time - time</c> negatif olabilen <c>interval</c> verir; gün aşan slotta (<c>EndTime &lt; StartTime</c>) +24 saat.
    /// Üst sınır <c>BookingService.MaxSlotDurationHours</c> ile aynı (değişirse yeni migration gerekir).
    /// </summary>
    internal const string DurationCheckSql =
        "\"EndTime\" <> \"StartTime\" AND (CASE WHEN \"EndTime\" > \"StartTime\" THEN \"EndTime\" - \"StartTime\" "
        + "ELSE \"EndTime\" - \"StartTime\" + interval '24 hours' END) <= interval '4 hours'";

    internal const string SlotDurationCheckName = "CK_TeacherAvailabilitySlots_Duration";

    internal const string RuleDurationCheckName = "CK_RecurringAvailabilityRules_Duration";

    /// <summary>Tarih + saat → UTC <see cref="DateTime"/> (duvar saati UTC kabul edilir).</summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time)
        => DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);

    /// <summary>
    /// Yarı açık aralık kesişimi: <c>[a, b)</c> ile <c>[c, d)</c> → <c>a &lt; d &amp;&amp; c &lt; b</c>. Sıfır süreli
    /// (geçersiz) aralık hiçbir şeyle çakışmaz.
    /// </summary>
    public bool Overlaps(SlotTimeRange other)
        => IsValid && other.IsValid && StartUtc < other.EndUtc && other.StartUtc < EndUtc;

    /// <summary>
    /// Verilen tarihteki bir slotla çakışabilecek adayların tarih aralığı: önceki günün gün aşan slotu bu güne,
    /// bu günün gün aşan slotu da ertesi güne taşabilir. Sorgu bu aralıkla daraltılır, kesin karar bellekte
    /// <see cref="Overlaps"/> ile verilir.
    /// </summary>
    public static (DateOnly From, DateOnly To) CandidateDates(DateOnly date) => (date.AddDays(-1), date.AddDays(1));

    /// <summary>
    /// SQL'e çevrilebilir "randevunun slotu <paramref name="nowUtc"/> anında henüz BİTMEDİ" (bitiş &gt; şimdi; devam eden
    /// ders dahil) koşulu — <see cref="From"/> ile aynı kural (gün aşımı <c>EndTime &lt; StartTime</c>):
    /// <list type="bullet">
    /// <item>sıfır süreli satır (<c>EndTime == StartTime</c>) hiçbir zaman "bitmemiş" değildir;</item>
    /// <item>yarın ve sonrası başlayan her slot;</item>
    /// <item>bugün başlayan: bitişi bugün ve şimdiden sonra, ya da gün aşıyor (bitişi yarın);</item>
    /// <item>dün başlayıp gün aşan ve bitişi bugün şimdiden sonra olan slot.</item>
    /// </list>
    /// </summary>
    public static Expression<Func<Booking, bool>> BookingNotEndedAt(DateTime nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        var yesterday = today.AddDays(-1);
        var timeNow = TimeOnly.FromDateTime(nowUtc);
        return b => b.AvailabilitySlot.EndTime != b.AvailabilitySlot.StartTime
            && (b.AvailabilitySlot.Date > today
                || (b.AvailabilitySlot.Date == today
                    && (b.AvailabilitySlot.EndTime > timeNow || b.AvailabilitySlot.EndTime < b.AvailabilitySlot.StartTime))
                || (b.AvailabilitySlot.Date == yesterday
                    && b.AvailabilitySlot.EndTime < b.AvailabilitySlot.StartTime
                    && b.AvailabilitySlot.EndTime > timeNow));
    }
}
