using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Veli "Program" okuma modeli (issue #422, epic #407 V4) — salt okunur. Sıra: <b>kapı → audit → veri</b>.
/// </summary>
public interface IParentScheduleService
{
    /// <summary>
    /// Çocuğun [<paramref name="from"/>, <paramref name="to"/>] (yerel günler, iki uç dahil) planları ve ders randevuları.
    /// Aralık çağıran tarafından <see cref="TryResolveRange"/> ile doğrulanmış olmalıdır. Erişim yoksa null → 404.
    /// </summary>
    Task<ParentChildScheduleDto?> GetScheduleAsync(int parentUserId, int studentId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary><c>?from=&amp;to=</c> → aralık, yerel "bugün"e göre (<see cref="ParentScheduleRange"/>); geçersizse false (400).</summary>
    bool TryResolveRange(string? from, string? to, out ParentScheduleRange.Range range);
}

/// <inheritdoc cref="IParentScheduleService"/>
/// <remarks>
/// <list type="bullet">
/// <item>Planlar: öğrencinin "Planla &amp; Hatırlat" kayıtları (<see cref="WorksheetReminder"/>, "Planım" takvimiyle aynı kaynak);
/// iptal edilenler hariç. Yalnızca worksheet adı, ders ve planlanan GÜN — saat ve hatırlatma ayarı dönmez.</item>
/// <item>Dersler: öğrencinin bekleyen ve onaylanan randevuları (reddedilenler veliye gösterilmez — review), slot saatleri
/// <see cref="SlotTimeRange"/> ile (#300; aralıkla kesişen randevu dahil, gösterim günü <c>StartsOn</c>). Görüşme bağlantısı,
/// ücret, ret gerekçesi ya da not hiçbir zaman okunmaz.</item>
/// </list>
/// </remarks>
public sealed class ParentScheduleService : IParentScheduleService
{
    /// <summary>Tür başına dönülen en fazla satır (31 günlük aralıkta gerçekçi bir öğrencide ulaşılmaz).</summary>
    internal const int MaxRows = 200;

    private readonly AppDbContext _context;
    private readonly IParentChildAccess _access;
    private readonly IParentAccessAuditLog _audit;
    private readonly IAuthApiClient _authApiClient;
    private readonly ILocalDayCalendar _calendar;
    private readonly ILogger<ParentScheduleService>? _logger;

    public ParentScheduleService(
        AppDbContext context,
        IParentChildAccess access,
        IParentAccessAuditLog audit,
        IAuthApiClient authApiClient,
        ILocalDayCalendar? calendar = null,
        ILogger<ParentScheduleService>? logger = null)
    {
        _context = context;
        _access = access;
        _audit = audit;
        _authApiClient = authApiClient;
        _calendar = calendar ?? LocalDayCalendar.Default;
        _logger = logger;
    }

    /// <summary>Öğretmen adı zenginleştirmesinin süre tavanı (auth-api yavaşsa liste yine döner, ad null).</summary>
    internal TimeSpan NameLookupTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public bool TryResolveRange(string? from, string? to, out ParentScheduleRange.Range range)
        => ParentScheduleRange.TryResolve(from, to, _calendar.Today, out range);

    public async Task<ParentChildScheduleDto?> GetScheduleAsync(
        int parentUserId, int studentId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!ParentScheduleRange.IsValid(from, to, _calendar.Today))
            throw new ArgumentOutOfRangeException(nameof(to), "Invalid schedule range (validate with TryResolveRange).");

        // 1) Kapı.
        var grant = await _access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant == null)
            return null;

        // 2) Audit (veri okunmadan önce).
        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildSchedule, ct: ct);

        // 3) Veri — yerel günler → [fromUtc, toUtc) anları.
        var fromUtc = _calendar.StartOfDayUtc(from);
        var toUtc = _calendar.StartOfDayUtc(to.AddDays(1));

        var result = new ParentChildScheduleDto { StudentId = grant.StudentId, From = from, To = to };
        result.Plans.AddRange(await LoadPlansAsync(grant.StudentId, fromUtc, toUtc, ct));
        result.Lessons.AddRange(await LoadLessonsAsync(grant.StudentId, from, fromUtc, toUtc, ct));
        return result;
    }

    private async Task<List<ParentPlanItemDto>> LoadPlansAsync(int studentId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Yalnız gösterilen alanlar projekte edilir (hatırlatma süresi, Hangfire/Keycloak kimliği okunmaz).
        var rows = await _context.WorksheetReminders.AsNoTracking()
            .Where(r => r.StudentId == studentId
                && r.Status != WorksheetReminderStatus.Cancelled
                && r.ScheduledFor >= fromUtc && r.ScheduledFor < toUtc)
            .OrderBy(r => r.ScheduledFor)
            .ThenBy(r => r.Id)
            .Take(MaxRows)
            .Select(r => new
            {
                r.ScheduledFor,
                Title = r.Worksheet.Name,
                Subject = r.Worksheet.Subject != null ? r.Worksheet.Subject.Name : null
            })
            .ToListAsync(ct);

        return rows.Select(r => new ParentPlanItemDto
        {
            Title = r.Title,
            Subject = r.Subject,
            PlannedOn = LocalDay(r.ScheduledFor)
        }).ToList();
    }

    private async Task<List<ParentLessonItemDto>> LoadLessonsAsync(
        int studentId, DateOnly from, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Slot tarih/saati SQL'de birleştirilemez (WorksheetCalendarService ile aynı): gün bazında kaba daraltma, kesin kesişim
        // bellekte. Gün aşan randevu aralığa taşabilsin diye bir gün geriden başlanır. Silinmiş slotun randevusu da
        // görünür (#376, öğrencinin kendi listesiyle aynı).
        var fromDate = DateOnly.FromDateTime(fromUtc).AddDays(-1);
        var toDate = DateOnly.FromDateTime(toUtc);
        var rows = await _context.Bookings
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Where(b => b.StudentId == studentId
                && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Approved)
                && b.AvailabilitySlot.Date >= fromDate
                && b.AvailabilitySlot.Date <= toDate)
            .OrderBy(b => b.AvailabilitySlot.Date)
            .ThenBy(b => b.AvailabilitySlot.StartTime)
            .ThenBy(b => b.Id)
            .Take(MaxRows)
            .Select(b => new
            {
                TeacherUserId = b.Teacher.UserId,
                b.Status,
                b.AvailabilitySlot.Date,
                b.AvailabilitySlot.StartTime,
                b.AvailabilitySlot.EndTime
            })
            .ToListAsync(ct);

        var inRange = rows
            .Select(r => (Row: r, Range: SlotTimeRange.From(r.Date, r.StartTime, r.EndTime)))
            .Where(x => x.Range.EndUtc > fromUtc && x.Range.StartUtc < toUtc)
            .ToList();
        if (inRange.Count == 0)
            return new List<ParentLessonItemDto>();

        var names = await LookupNamesAsync(inRange.Select(x => x.Row.TeacherUserId), ct);
        return inRange
            .OrderBy(x => x.Range.StartUtc)
            .Select(x => new ParentLessonItemDto
            {
                TeacherName = names.TryGetValue(x.Row.TeacherUserId, out var name) ? name : null,
                // Aralıktan önce başlayıp taşan randevu aralığın ilk gününde gösterilir.
                StartsOn = LocalDay(x.Range.StartUtc) < from ? from : LocalDay(x.Range.StartUtc),
                StartAt = x.Range.StartUtc,
                EndAt = x.Range.EndUtc,
                Status = ToStatus(x.Row.Status)
            })
            .ToList();
    }

    internal static string ToStatus(BookingStatus status)
        => status == BookingStatus.Approved ? ParentLessonStatuses.Approved : ParentLessonStatuses.Pending;

    private DateOnly LocalDay(DateTime utc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _calendar.TimeZone));

    /// <summary>Öğretmen adları: auth-api'ye TEK toplu çağrı; erişilemezse/süre aşılırsa boş (ad null döner).</summary>
    private async Task<IReadOnlyDictionary<int, string>> LookupNamesAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, string>();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(NameLookupTimeout);
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(ids, timeout.Token);
            return users
                .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _logger?.LogWarning(ex, "[ParentSchedule] Öğretmen adları çözülemedi ({Count} kullanıcı).", ids.Count);
            return new Dictionary<int, string>();
        }
    }
}
