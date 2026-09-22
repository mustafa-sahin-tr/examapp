using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// Tekrarlayan haftalık müsaitlik kuralları (issue #178).
/// <para>
/// Strateji: kural oluşturulunca EffectiveFrom'dan <see cref="BookingService.MaxAdvanceDays"/> ufkuna
/// kadar somut <see cref="TeacherAvailabilitySlot"/> satırları üretilir (materialize). Arka plan job yok;
/// pencere <see cref="TopUpAsync"/> ile öğretmen kendi slotlarını listelerken lazy olarak ileri kaydırılır.
/// </para>
/// <para>
/// İdempotenlik: bir kural+tarih için satır üretilip üretilmediği <b>soft-delete edilmiş satırlar dahil</b>
/// kontrol edilir (<c>IgnoreQueryFilters</c>); aksi halde öğretmenin "sadece bu hafta" sildiği occurrence
/// bir sonraki sweep'te yeniden doğardı. Çakışan tekil slot varsa o hafta atlanır (hata değil).
/// </para>
/// <para>
/// 404/403 ayrımı: <see cref="DeleteRuleAsync"/> var olmayan kural için 404, başkasının kuralı için 403
/// döner. Bu, kural id'lerinin varlığını sızdırır (existence oracle) ama mevcut
/// <c>BookingService.DeleteSlotAsync</c> ve worksheet sahiplik deseniyle bilinçli olarak tutarlı tutuldu
/// (issue #178 inceleme notu); id'ler tahmin edilebilir olsa da içerik sızmaz.
/// </para>
/// </summary>
public class RecurringAvailabilityService : IRecurringAvailabilityService
{
    private const int MaxTake = 200;
    private const int DefaultTake = 50;

    /// <summary>Öğretmen başına aktif kural üst sınırı — kaynak tüketimi (her kural ~13 slot/90 gün).</summary>
    public const int MaxActiveRulesPerTeacher = 50;

    /// <summary>Bir kuralın azami süresi (dakika). Tekil slotta böyle bir alt sınır yok; yalnız kural için.</summary>
    public const int MinRuleDurationMinutes = 30;

    /// <summary>EffectiveUntil en fazla EffectiveFrom + bu kadar yıl olabilir.</summary>
    public const int MaxRuleSpanYears = 1;

    private static readonly TimeSpan MinRuleDuration = TimeSpan.FromMinutes(MinRuleDurationMinutes);

    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecurringAvailabilityService> _logger;

    // Client'a ulasan mesajlar sozlukten gelir (issue #184); DI disinda fallback'e duser.
    private readonly IStringLocalizer<Messages> _localizer;

    public RecurringAvailabilityService(
        AppDbContext context,
        TimeProvider timeProvider,
        ILogger<RecurringAvailabilityService> logger,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    // ------------------------------------------------------------------
    // Kural oluşturma
    // ------------------------------------------------------------------

    public async Task<RecurringAvailabilityRuleResultDto> CreateRuleAsync(
        int teacherUserId, CreateRecurringAvailabilityRuleDto dto, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        // Tekil slot (BookingService.CreateSlotAsync) ile aynı sabitler ve mesaj anahtarları.
        if (!Enum.IsDefined(dto.DayOfWeek))
            return Fail(_localizer["booking.recurringRule.invalidDayOfWeek"]);

        if (dto.EndTime <= dto.StartTime)
            return Fail(_localizer["booking.slot.endBeforeStart"]);

        var duration = dto.EndTime - dto.StartTime;
        if (duration > BookingService.MaxSlotDuration)
            return Fail(_localizer["booking.slot.tooLong", BookingService.MaxSlotDurationHours]);

        if (duration < MinRuleDuration)
            return Fail(_localizer["booking.recurringRule.tooShort", MinRuleDurationMinutes]);

        // Dakika hassasiyeti: saniye/mikrosaniye taşıyan saatler unique index'i anlamsız
        // (14:00:00 vs 14:00:01 iki ayrı kural) ve UI'ı kararsız yapar.
        if (!IsMinutePrecision(dto.StartTime) || !IsMinutePrecision(dto.EndTime))
            return Fail(_localizer["booking.recurringRule.invalidPrecision"]);

        if (dto.EffectiveFrom < today)
            return Fail(_localizer["booking.recurringRule.effectiveFromInPast"]);

        if (dto.EffectiveFrom > today.AddDays(BookingService.MaxAdvanceDays))
            return Fail(_localizer["booking.slot.tooFarAhead", BookingService.MaxAdvanceDays]);

        if (dto.EffectiveUntil.HasValue && dto.EffectiveUntil.Value <= dto.EffectiveFrom)
            return Fail(_localizer["booking.recurringRule.effectiveUntilBeforeFrom"]);

        if (dto.EffectiveUntil.HasValue && dto.EffectiveUntil.Value > dto.EffectiveFrom.AddYears(MaxRuleSpanYears))
            return Fail(_localizer["booking.recurringRule.untilTooFar", MaxRuleSpanYears]);

        var teacher = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => new { t.Id, t.ApprovalStatus })
            .FirstOrDefaultAsync(ct);

        if (teacher == null)
            return new RecurringAvailabilityRuleResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        if (teacher.ApprovalStatus != TeacherApprovalStatus.Approved)
            return new RecurringAvailabilityRuleResultDto { Success = false, Forbidden = true, Message = _localizer["booking.teacherNotApproved"] };

        // Öğretmenin aynı gündeki aktif kuralları tek sorguda: sayı sınırı + kesişme kontrolü.
        var activeRules = await _context.RecurringAvailabilityRules
            .AsNoTracking()
            .Where(r => r.TeacherId == teacher.Id && r.IsActive)
            .Select(r => new { r.DayOfWeek, r.StartTime, r.EndTime, r.EffectiveFrom, r.EffectiveUntil })
            .ToListAsync(ct);

        if (activeRules.Count >= MaxActiveRulesPerTeacher)
            return Fail(_localizer["booking.recurringRule.tooMany", MaxActiveRulesPerTeacher]);

        // Aynı gün + saat aralığı kesişiyor (yarı açık) + geçerlilik tarihleri kesişiyor → 409.
        // Birebir aynı kural da bu daldan yakalanır; bitişik aralıklar (15:00 bitiş / 15:00 başlangıç) kabul.
        var overlapping = activeRules.Any(r =>
            r.DayOfWeek == dto.DayOfWeek
            && r.StartTime < dto.EndTime && r.EndTime > dto.StartTime
            && (r.EffectiveUntil == null || r.EffectiveUntil >= dto.EffectiveFrom)
            && (dto.EffectiveUntil == null || r.EffectiveFrom <= dto.EffectiveUntil));

        if (overlapping)
            return new RecurringAvailabilityRuleResultDto { Success = false, Conflict = true, Message = _localizer["booking.slot.overlapping"] };

        var rule = new RecurringAvailabilityRule
        {
            TeacherId = teacher.Id,
            DayOfWeek = dto.DayOfWeek,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            EffectiveFrom = dto.EffectiveFrom,
            EffectiveUntil = dto.EffectiveUntil,
            IsActive = true
        };

        var horizon = today.AddDays(BookingService.MaxAdvanceDays);
        var existing = await LoadExistingSlotsAsync(teacher.Id, today, horizon, ct);
        var plan = Plan(rule, ruleId: null, existing, today, now, horizon);

        _context.SetCurrentUser(teacherUserId);
        _context.RecurringAvailabilityRules.Add(rule);
        foreach (var slot in plan.ToAdd)
        {
            slot.RecurringAvailabilityRule = rule; // FK, kural insert edildikten sonra EF tarafından bağlanır
            _context.TeacherAvailabilitySlots.Add(slot);
        }

        try
        {
            // Kural + üretilen slotlar tek SaveChanges → tek transaction (yarım seri kalmaz).
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Eşzamanlı yarış: yukarıdaki sorgular geçti ama unique index'lerden biri son sözü söyledi.
            var violated = ViolatedIndex(ex);
            _logger.LogWarning(ex, "Tekrarlayan kural eklenemedi (unique çakışma: {Index}). TeacherId={TeacherId}",
                violated?.ToString() ?? "unknown", teacher.Id);

            var message = violated switch
            {
                UniqueIndexKind.Slot => _localizer["booking.slot.overlapping"],
                UniqueIndexKind.Rule => _localizer["booking.recurringRule.duplicate"],
                _ => _localizer["booking.recurringRule.conflict"]
            };
            return new RecurringAvailabilityRuleResultDto { Success = false, Conflict = true, Message = message };
        }

        return new RecurringAvailabilityRuleResultDto
        {
            Success = true,
            ObjectId = rule.Id,
            Message = _localizer["booking.recurringRule.created"],
            Rule = MapRule(rule),
            GeneratedSlotIds = plan.ToAdd.Select(s => s.Id).ToList(),
            SkippedDates = plan.Skipped
        };
    }

    // ------------------------------------------------------------------
    // Listeleme
    // ------------------------------------------------------------------

    public async Task<RecurringAvailabilityRuleListResultDto> GetMyRulesAsync(
        int teacherUserId, int skip, int take, CancellationToken ct = default)
    {
        var teacherId = await ResolveTeacherIdAsync(teacherUserId, ct);
        if (teacherId == null)
            return new RecurringAvailabilityRuleListResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        var rules = await _context.RecurringAvailabilityRules
            .AsNoTracking()
            .Where(r => r.TeacherId == teacherId.Value && r.IsActive)
            .OrderBy(r => r.DayOfWeek)
            .ThenBy(r => r.StartTime)
            .Skip(Normalize(skip))
            .Take(Normalize(take, isTake: true))
            .ToListAsync(ct);

        return new RecurringAvailabilityRuleListResultDto
        {
            Success = true,
            Items = rules.Select(MapRule).ToList()
        };
    }

    // ------------------------------------------------------------------
    // Tüm seriyi silme
    // ------------------------------------------------------------------

    public async Task<RecurringAvailabilityRuleDeleteResultDto> DeleteRuleAsync(
        int teacherUserId, int ruleId, CancellationToken ct = default)
    {
        var teacherId = await ResolveTeacherIdAsync(teacherUserId, ct);
        if (teacherId == null)
            return new RecurringAvailabilityRuleDeleteResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        var rule = await _context.RecurringAvailabilityRules
            .FirstOrDefaultAsync(r => r.Id == ruleId, ct);

        if (rule == null)
            return new RecurringAvailabilityRuleDeleteResultDto { Success = false, NotFound = true, Message = _localizer["booking.recurringRule.notFound"] };

        // Başkasının kuralı: 403 (DeleteSlotAsync ile tutarlı; bkz. sınıf yorumu — bilinçli karar).
        if (rule.TeacherId != teacherId.Value)
            return new RecurringAvailabilityRuleDeleteResultDto { Success = false, Forbidden = true, Message = _localizer["booking.recurringRule.notOwned"] };

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // Bugün dahil gelecekteki occurrence'lar; aktif randevusu olanlar tek sorguda işaretlenir (N+1 yok).
        var future = await _context.TeacherAvailabilitySlots
            .Where(s => s.RecurringAvailabilityRuleId == ruleId && s.Date >= today)
            .Select(s => new
            {
                Slot = s,
                HasActiveBooking = s.Bookings.Any(b => BookingService.ActiveStatuses.Contains(b.Status))
            })
            .ToListAsync(ct);

        _context.SetCurrentUser(teacherUserId);

        rule.IsActive = false;

        // Seri bugün kapanır; henüz başlamamış kuralda EffectiveUntil >= EffectiveFrom invariant'ı korunur.
        var closeAt = rule.EffectiveFrom > today ? rule.EffectiveFrom : today;
        if (rule.EffectiveUntil == null || rule.EffectiveUntil > closeAt)
            rule.EffectiveUntil = closeAt;

        _context.RecurringAvailabilityRules.Remove(rule); // BaseEntity → soft delete (IsActive/EffectiveUntil de yazılır)

        var result = new RecurringAvailabilityRuleDeleteResultDto
        {
            Success = true,
            ObjectId = ruleId,
            Message = _localizer["booking.recurringRule.deleted"]
        };

        foreach (var row in future)
        {
            if (row.HasActiveBooking)
            {
                result.PreservedSlotIds.Add(row.Slot.Id);
                continue;
            }

            _context.TeacherAvailabilitySlots.Remove(row.Slot);
            result.DeletedSlotIds.Add(row.Slot.Id);
        }

        result.PreservedBookedCount = result.PreservedSlotIds.Count;

        await _context.SaveChangesAsync(ct);
        return result;
    }

    // ------------------------------------------------------------------
    // Top-up (ufkun ileri kaydırılması)
    // ------------------------------------------------------------------

    public async Task<int> TopUpAsync(int teacherId, int teacherUserId, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var horizon = today.AddDays(BookingService.MaxAdvanceDays);

        // Ucuz yol: kuralı olmayan öğretmen için tek indeksli sorgu, yazma yok.
        var rules = await _context.RecurringAvailabilityRules
            .AsNoTracking()
            .Where(r => r.TeacherId == teacherId
                && r.IsActive
                && (r.EffectiveUntil == null || r.EffectiveUntil >= today))
            .OrderBy(r => r.Id) // deterministik: aynı güne düşen iki kuraldan hep eski olan önce
            .ToListAsync(ct);

        if (rules.Count == 0)
            return 0;

        var existing = await LoadExistingSlotsAsync(teacherId, today, horizon, ct);

        var toAdd = new List<TeacherAvailabilitySlot>();
        foreach (var rule in rules)
        {
            var plan = Plan(rule, rule.Id, existing, today, now, horizon);
            toAdd.AddRange(plan.ToAdd);

            if (plan.Skipped.Count > 0)
                _logger.LogInformation(
                    "Top-up: RuleId={RuleId} için {Count} occurrence çakışma nedeniyle atlandı: {Dates}",
                    rule.Id, plan.Skipped.Count, string.Join(",", plan.Skipped));

            // Aynı sweep içinde iki kural aynı güne düşerse ikincisi ilkini görsün.
            foreach (var s in plan.ToAdd)
                existing.Add(new ExistingSlot(s.Date, s.StartTime, s.EndTime, rule.Id, IsDeleted: false));
        }

        if (toAdd.Count == 0)
            return 0;

        _context.SetCurrentUser(teacherUserId);
        _context.TeacherAvailabilitySlots.AddRange(toAdd);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Eşzamanlı iki /mine çağrısı aynı occurrence'ı üretmeye çalıştı; unique index kazananı seçti.
            // Okuma yolu kırılmasın: eklenenleri bırak, bir sonraki çağrı eksikleri tamamlar.
            // Diğer DbUpdateException'lar (FK, bağlantı vb.) bilinçli olarak yukarı fırlar.
            _logger.LogWarning(ex, "Tekrarlayan kural top-up unique çakışması; atlanıyor. TeacherId={TeacherId}", teacherId);
            foreach (var slot in toAdd)
                _context.Entry(slot).State = EntityState.Detached;
            return 0;
        }

        return toAdd.Count;
    }

    // ------------------------------------------------------------------
    // Ortak yardımcılar
    // ------------------------------------------------------------------

    /// <summary>
    /// Ufuk içindeki mevcut satırlar, tarihe göre indeksli — <b>soft-delete edilmişler dahil</b>
    /// (sweep doğruluğu için). Çakışma kontrolü yalnızca silinmemişlere bakar.
    /// </summary>
    private async Task<ExistingSlotIndex> LoadExistingSlotsAsync(int teacherId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await _context.TeacherAvailabilitySlots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TeacherId == teacherId && s.Date >= from && s.Date <= to)
            .Select(s => new ExistingSlot(s.Date, s.StartTime, s.EndTime, s.RecurringAvailabilityRuleId, s.IsDeleted))
            .ToListAsync(ct);

        return new ExistingSlotIndex(rows);
    }

    /// <summary>
    /// Kuralın [max(EffectiveFrom, from), min(EffectiveUntil, horizon)] içindeki occurrence'larını hesaplar ve
    /// hangilerinin üretileceğine karar verir. Saf/deterministik — DB erişimi yok; tarih başına O(k).
    /// </summary>
    private static MaterializePlan Plan(
        RecurringAvailabilityRule rule, int? ruleId, ExistingSlotIndex existing,
        DateOnly today, DateTime now, DateOnly horizon)
    {
        var toAdd = new List<TeacherAvailabilitySlot>();
        var skipped = new List<DateOnly>();
        var timeNow = TimeOnly.FromDateTime(now);

        foreach (var date in Occurrences(rule, today, horizon))
        {
            // Bugünkü occurrence'ın saati geçtiyse tekil slot kuralı gibi üretilmez (geçmiş slot açılmaz).
            if (date == today && rule.StartTime <= timeNow)
                continue;

            var sameDay = existing.At(date);

            // Bu kural için bu tarihe zaten satır var (soft-delete edilmiş olsa bile) → idempotent.
            if (ruleId.HasValue && sameDay.Any(e => e.RuleId == ruleId.Value))
                continue;

            // Aynı güne düşen (silinmemiş) başka bir aralıkla kesişiyor → bu hafta atlanır.
            if (sameDay.Any(e => !e.IsDeleted && e.StartTime < rule.EndTime && e.EndTime > rule.StartTime))
            {
                skipped.Add(date);
                continue;
            }

            toAdd.Add(new TeacherAvailabilitySlot
            {
                TeacherId = rule.TeacherId,
                Date = date,
                StartTime = rule.StartTime,
                EndTime = rule.EndTime,
                CreatedAt = now,
                RecurringAvailabilityRuleId = ruleId
            });
        }

        return new MaterializePlan(toAdd, skipped);
    }

    /// <summary>Kuralın haftalık occurrence tarihleri (her iki uç dahil).</summary>
    internal static IEnumerable<DateOnly> Occurrences(RecurringAvailabilityRule rule, DateOnly from, DateOnly horizon)
    {
        var start = rule.EffectiveFrom > from ? rule.EffectiveFrom : from;
        var end = rule.EffectiveUntil.HasValue && rule.EffectiveUntil.Value < horizon
            ? rule.EffectiveUntil.Value
            : horizon;

        var offset = ((int)rule.DayOfWeek - (int)start.DayOfWeek + 7) % 7;
        for (var date = start.AddDays(offset); date <= end; date = date.AddDays(7))
            yield return date;
    }

    private static bool IsMinutePrecision(TimeOnly time)
        => time.Ticks % TimeSpan.TicksPerMinute == 0;

    /// <summary>
    /// Yalnızca unique index ihlali yutulur; FK/bağlantı gibi diğer DbUpdateException'lar yukarı fırlar.
    /// Postgres: SqlState 23505. SQLite (yalnız test sağlayıcısı; Api projesi paketi referanslamaz):
    /// hata kodu 19 = SQLITE_CONSTRAINT, mesajda "UNIQUE constraint failed".
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => true,
        DbException db when db.GetType().Name == "SqliteException"
            && db.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal) => true,
        _ => false
    };

    /// <summary>İhlal edilen unique index hangi tabloya ait (mesaj seçimi için); çözülemezse null.</summary>
    private static UniqueIndexKind? ViolatedIndex(DbUpdateException ex)
    {
        var text = ex.InnerException switch
        {
            PostgresException pg => pg.ConstraintName ?? string.Empty,
            DbException db => db.Message,
            _ => string.Empty
        };

        if (text.Contains(nameof(AppDbContext.TeacherAvailabilitySlots), StringComparison.Ordinal))
            return UniqueIndexKind.Slot;
        if (text.Contains(nameof(AppDbContext.RecurringAvailabilityRules), StringComparison.Ordinal))
            return UniqueIndexKind.Rule;
        return null;
    }

    private async Task<int?> ResolveTeacherIdAsync(int teacherUserId, CancellationToken ct)
        => await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

    private static RecurringAvailabilityRuleDto MapRule(RecurringAvailabilityRule rule) => new()
    {
        Id = rule.Id,
        TeacherId = rule.TeacherId,
        DayOfWeek = rule.DayOfWeek,
        StartTime = rule.StartTime,
        EndTime = rule.EndTime,
        EffectiveFrom = rule.EffectiveFrom,
        EffectiveUntil = rule.EffectiveUntil,
        IsActive = rule.IsActive,
        CreatedAt = rule.CreateTime
    };

    private static RecurringAvailabilityRuleResultDto Fail(string message)
        => new() { Success = false, Message = message };

    private static int Normalize(int value, bool isTake = false)
    {
        if (!isTake)
            return Math.Max(value, 0);

        return value <= 0 ? DefaultTake : Math.Min(value, MaxTake);
    }

    private enum UniqueIndexKind { Slot, Rule }

    private sealed record ExistingSlot(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, int? RuleId, bool IsDeleted);

    private sealed record MaterializePlan(List<TeacherAvailabilitySlot> ToAdd, List<DateOnly> Skipped);

    /// <summary>Tarih → o güne ait satırlar; Plan'daki tarama O(1) erişimli (ILookup değişmez olduğu için sözlük).</summary>
    private sealed class ExistingSlotIndex
    {
        private static readonly List<ExistingSlot> Empty = new();
        private readonly Dictionary<DateOnly, List<ExistingSlot>> _byDate = new();

        public ExistingSlotIndex(IEnumerable<ExistingSlot> rows)
        {
            foreach (var row in rows)
                Add(row);
        }

        public IReadOnlyList<ExistingSlot> At(DateOnly date)
            => _byDate.TryGetValue(date, out var list) ? list : Empty;

        public void Add(ExistingSlot slot)
        {
            if (!_byDate.TryGetValue(slot.Date, out var list))
                _byDate[slot.Date] = list = new List<ExistingSlot>();
            list.Add(slot);
        }
    }
}
