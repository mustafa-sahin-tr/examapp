using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Helpers;
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

        // issue #300: EndTime < StartTime → occurrence'ın bitişi ertesi gün (SlotTimeRange). Yalnız sıfır süre reddedilir.
        if (SlotTimeRange.IsZeroLength(dto.StartTime, dto.EndTime))
            return Fail(_localizer["booking.slot.zeroLength"]);

        var duration = SlotTimeRange.DurationOf(dto.StartTime, dto.EndTime);
        if (duration > BookingService.MaxSlotDuration)
            return Fail(_localizer[BookingService.TooLongMessageKey(dto.StartTime, dto.EndTime), BookingService.MaxSlotDurationHours]);

        if (duration < MinRuleDuration)
            return Fail(_localizer["booking.recurringRule.tooShort", MinRuleDurationMinutes]);

        // Dakika hassasiyeti: saniye/mikrosaniye taşıyan saatler unique index'i anlamsız
        // (14:00:00 vs 14:00:01 iki ayrı kural) ve UI'ı kararsız yapar.
        if (!SlotTimeRange.IsMinutePrecision(dto.StartTime) || !SlotTimeRange.IsMinutePrecision(dto.EndTime))
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
            .Select(t => new { t.Id, t.ApprovalStatus, t.IsIndependentTutor, t.SchoolId })
            .FirstOrDefaultAsync(ct);

        if (teacher == null)
            return new RecurringAvailabilityRuleResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        if (teacher.ApprovalStatus != TeacherApprovalStatus.Approved)
            return new RecurringAvailabilityRuleResultDto { Success = false, Forbidden = true, Message = _localizer["booking.teacherNotApproved"] };

        // issue #418: randevu bağımsız öğretmen özelliği — okula bağlı (hibrit dahil) öğretmen tekrarlayan kural tanımlayamaz.
        // Sıra BookingService.CreateSlotAsync ile aynı: önce onay, sonra bağımsızlık.
        if (!TeacherIndependence.IsIndependent(teacher.IsIndependentTutor, teacher.SchoolId))
            return new RecurringAvailabilityRuleResultDto { Success = false, Forbidden = true, Message = _localizer[BookingService.TeacherNotIndependentKey] };

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

        _context.SetCurrentUser(teacherUserId);

        // issue #323 (security L2): kural/slot çakışma kontrolleri + INSERT öğretmen bazlı advisory lock altında, tek
        // transaction'da (tekil slot oluşturma ve top-up ile AYNI kilit) — paralel istekler kesişen kural/slot açamaz.
        // Retry-on-failure nedeniyle transaction execution strategy İÇİNDE açılır; her deneme planı sıfırdan kurar.
        // Bilinen sınır (code review Uyarı-2): COMMIT veritabanına ulaşıp onayı kaybolursa strateji yeniden dener ve kendi
        // az önce yazdığı kuralı "kesişen kural" görüp 409 döner (veri doğrudur, yalnız yanıt yanlıştır; liste yenilenince
        // kural görünür). Tekil slotta bu durum CreatedAt eşleşmesiyle başarı sayılır; kuralda CreateTime audit hook'unda
        // DateTime.UtcNow ile yazıldığı ve yanıt üretilen slot listesini de taşıdığı için ucuz/güvenli bir eşleşme yok.
        RecurringAvailabilityRuleResultDto? rejection = null;
        var plan = new MaterializePlan(new List<TeacherAvailabilitySlot>(), new List<DateOnly>());
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                DetachForRetry(rule, plan.ToAdd);
                rejection = null;

                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                await _context.Database.AcquireTeacherAvailabilityLockAsync(teacher.Id, ct);

                // Öğretmenin aynı gündeki aktif kuralları tek sorguda: sayı sınırı + kesişme kontrolü.
                var activeRules = await _context.RecurringAvailabilityRules
                    .AsNoTracking()
                    .Where(r => r.TeacherId == teacher.Id && r.IsActive)
                    .Select(r => new { r.DayOfWeek, r.StartTime, r.EndTime, r.EffectiveFrom, r.EffectiveUntil })
                    .ToListAsync(ct);

                if (activeRules.Count >= MaxActiveRulesPerTeacher)
                {
                    rejection = Fail(_localizer["booking.recurringRule.tooMany", MaxActiveRulesPerTeacher]);
                    return; // commit yok → dispose'da rollback
                }

                // Haftalık zaman çizgisinde aralık kesişiyor (yarı açık) + geçerlilik tarihleri kesişiyor → 409.
                // Birebir aynı kural da bu daldan yakalanır; bitişik aralıklar (15:00 bitiş / 15:00 başlangıç) kabul.
                // issue #300: gün aşan kural (Pzt 23:30–00:30) ertesi günün kuralıyla (Sal 00:00–00:45) da çakışır; bu yüzden
                // karşılaştırma gün+saat üzerinden haftalık yapılır. Gün aşan kuralın son occurrence'ı EffectiveUntil'in ertesi
                // gününe taşabildiğinden tarih kesişimi o durumda bir gün toleranslıdır (kenar durumda muhafazakâr 409).
                var overlapping = activeRules.Any(r =>
                    WeeklyOverlaps(r.DayOfWeek, r.StartTime, r.EndTime, dto.DayOfWeek, dto.StartTime, dto.EndTime)
                    && EffectiveRangesOverlap(r.EffectiveFrom, r.EffectiveUntil, r.StartTime, r.EndTime,
                        dto.EffectiveFrom, dto.EffectiveUntil, dto.StartTime, dto.EndTime));

                if (overlapping)
                {
                    rejection = new RecurringAvailabilityRuleResultDto { Success = false, Conflict = true, Message = _localizer["booking.slot.overlapping"] };
                    return;
                }

                var horizon = today.AddDays(BookingService.MaxAdvanceDays);
                var existing = await LoadExistingSlotsAsync(teacher.Id, today, horizon, ct);
                plan = Plan(rule, ruleId: null, existing, today, now, horizon);

                _context.RecurringAvailabilityRules.Add(rule);
                foreach (var slot in plan.ToAdd)
                {
                    slot.RecurringAvailabilityRule = rule; // FK, kural insert edildikten sonra EF tarafından bağlanır
                    _context.TeacherAvailabilitySlots.Add(slot);
                }

                // Kural + üretilen slotlar aynı transaction'da (yarım seri kalmaz).
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Kilit dışından gelen (ör. doğrudan SQL) yazıcı: sorgular geçti ama unique index'lerden biri son sözü söyledi.
            DetachForRetry(rule, plan.ToAdd);
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
        catch (TeacherAvailabilityLockTimeoutException ex)
        {
            DetachForRetry(rule, plan.ToAdd);
            _logger.LogWarning(ex, "Tekrarlayan kural: müsaitlik kilidi zaman aşımı. TeacherId={TeacherId}", teacher.Id);
            return new RecurringAvailabilityRuleResultDto { Success = false, Conflict = true, Message = _localizer["booking.slot.busy"] };
        }

        if (rejection != null)
            return rejection;

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
        var teacher = await ResolveTeacherAsync(teacherUserId, ct);
        if (teacher == null)
            return new RecurringAvailabilityRuleListResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        // issue #418: okula bağlı öğretmen → 403 (BookingService öğretmen uçlarıyla aynı).
        if (!teacher.Value.IsIndependent)
            return new RecurringAvailabilityRuleListResultDto { Success = false, Forbidden = true, Message = _localizer[BookingService.TeacherNotIndependentKey] };

        var teacherId = teacher.Value.Id;

        var rules = await _context.RecurringAvailabilityRules
            .AsNoTracking()
            .Where(r => r.TeacherId == teacherId && r.IsActive)
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
        var teacher = await ResolveTeacherAsync(teacherUserId, ct);
        if (teacher == null)
            return new RecurringAvailabilityRuleDeleteResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        // issue #418: okula bağlı öğretmen → 403 (BookingService öğretmen uçlarıyla aynı).
        if (!teacher.Value.IsIndependent)
            return new RecurringAvailabilityRuleDeleteResultDto { Success = false, Forbidden = true, Message = _localizer[BookingService.TeacherNotIndependentKey] };

        var teacherId = teacher.Value.Id;

        // Hızlı yol (kilitsiz, salt okunur): 404/403 ayrımı. Karar kilit altında yeniden okunan satırla verilir.
        var owner = await _context.RecurringAvailabilityRules
            .AsNoTracking()
            .Where(r => r.Id == ruleId)
            .Select(r => (int?)r.TeacherId)
            .FirstOrDefaultAsync(ct);

        if (owner == null)
            return new RecurringAvailabilityRuleDeleteResultDto { Success = false, NotFound = true, Message = _localizer["booking.recurringRule.notFound"] };

        // Başkasının kuralı: 403 (DeleteSlotAsync ile tutarlı; bkz. sınıf yorumu — bilinçli karar).
        if (owner.Value != teacherId)
            return new RecurringAvailabilityRuleDeleteResultDto { Success = false, Forbidden = true, Message = _localizer["booking.recurringRule.notOwned"] };

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        _context.SetCurrentUser(teacherUserId);

        // issue #323 (code/security review): seri silme top-up ile AYNI öğretmen kilidi altında, tek transaction'da —
        // aksi halde kilitsiz okuyan bir top-up silinmekte olan kurala yeni occurrence üretebilirdi. Kural + gelecek slot
        // sorgusu + SaveChanges kilidin altında. Bilinen sınır: COMMIT onayı kaybolup strateji yeniden denerse kural artık
        // silinmiş göründüğünden 404 döner (veri doğru, yanıt yanlış).
        RecurringAvailabilityRuleDeleteResultDto? result = null;
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                // Retry: önceki denemenin (geri alınmış) değişiklikleri tekrar uygulanmasın; her deneme sıfırdan okur.
                _context.ChangeTracker.Clear();
                result = null;

                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                await _context.Database.AcquireTeacherAvailabilityLockAsync(teacherId, ct);

                var rule = await _context.RecurringAvailabilityRules
                    .FirstOrDefaultAsync(r => r.Id == ruleId && r.TeacherId == teacherId, ct);
                if (rule == null)
                {
                    // Kilidi beklerken eşzamanlı bir silme kazandı.
                    result = new RecurringAvailabilityRuleDeleteResultDto { Success = false, NotFound = true, Message = _localizer["booking.recurringRule.notFound"] };
                    return;
                }

                // Bugün dahil gelecekteki occurrence'lar; aktif randevusu olanlar tek sorguda işaretlenir (N+1 yok).
                var future = await _context.TeacherAvailabilitySlots
                    .Where(s => s.RecurringAvailabilityRuleId == ruleId && s.Date >= today)
                    .Select(s => new
                    {
                        Slot = s,
                        HasActiveBooking = s.Bookings.Any(b => BookingService.ActiveStatuses.Contains(b.Status))
                    })
                    .ToListAsync(ct);

                rule.IsActive = false;

                // Seri bugün kapanır; henüz başlamamış kuralda EffectiveUntil >= EffectiveFrom invariant'ı korunur.
                var closeAt = rule.EffectiveFrom > today ? rule.EffectiveFrom : today;
                if (rule.EffectiveUntil == null || rule.EffectiveUntil > closeAt)
                    rule.EffectiveUntil = closeAt;

                _context.RecurringAvailabilityRules.Remove(rule); // BaseEntity → soft delete (IsActive/EffectiveUntil de yazılır)

                var attemptResult = new RecurringAvailabilityRuleDeleteResultDto
                {
                    Success = true,
                    ObjectId = ruleId,
                    Message = _localizer["booking.recurringRule.deleted"]
                };

                foreach (var row in future)
                {
                    if (row.HasActiveBooking)
                    {
                        attemptResult.PreservedSlotIds.Add(row.Slot.Id);
                        continue;
                    }

                    _context.TeacherAvailabilitySlots.Remove(row.Slot);
                    attemptResult.DeletedSlotIds.Add(row.Slot.Id);
                }

                attemptResult.PreservedBookedCount = attemptResult.PreservedSlotIds.Count;

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                result = attemptResult;
            });
        }
        catch (TeacherAvailabilityLockTimeoutException ex)
        {
            _context.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Tekrarlayan kural silme: müsaitlik kilidi zaman aşımı. TeacherId={TeacherId}", teacherId);
            return new RecurringAvailabilityRuleDeleteResultDto { Success = false, Conflict = true, Message = _localizer["booking.slot.busy"] };
        }

        return result!;
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
        var rules = await LoadTopUpRulesAsync(teacherId, today, ct);

        if (rules.Count == 0)
            return 0;

        // Ucuz yol: kilitsiz ön plan — eklenecek occurrence yoksa (çoğu /mine çağrısı) yazma ve kilit yok.
        var preview = await PlanTopUpAsync(rules, teacherId, today, now, horizon, logSkipped: false, ct);
        if (preview.Count == 0)
            return 0;

        // issue #323 (security L2): eklenecekler kilit altında yeniden planlanır — tekil slot / kural oluşturma ile AYNI
        // öğretmen kilidi; arada açılan kesişen slot görülür ve o hafta atlanır.
        var toAdd = new List<TeacherAvailabilitySlot>();
        _context.SetCurrentUser(teacherUserId);
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                DetachForRetry(rule: null, toAdd);

                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                await _context.Database.AcquireTeacherAvailabilityLockAsync(teacherId, ct);

                // Kurallar da kilit altında YENİDEN okunur (code/security review): ön okumadan sonra silinen/durdurulan
                // kurala occurrence üretilmez (seri silme aynı kilidi tutar).
                var lockedRules = await LoadTopUpRulesAsync(teacherId, today, ct);
                toAdd = await PlanTopUpAsync(lockedRules, teacherId, today, now, horizon, logSkipped: true, ct);
                if (toAdd.Count == 0)
                    return;

                _context.TeacherAvailabilitySlots.AddRange(toAdd);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Kilit dışından gelen eşzamanlı yazıcı aynı occurrence'ı üretti; unique index kazananı seçti.
            // Okuma yolu kırılmasın: transaction geri alındı, bir sonraki çağrı eksikleri tamamlar.
            // Diğer DbUpdateException'lar (FK, bağlantı vb.) bilinçli olarak yukarı fırlar.
            _logger.LogWarning(ex, "Tekrarlayan kural top-up unique çakışması; atlanıyor. TeacherId={TeacherId}", teacherId);
            DetachForRetry(rule: null, toAdd);
            return 0;
        }
        catch (TeacherAvailabilityLockTimeoutException ex)
        {
            // Okuma yolu (GET /slots/mine) kırılmasın: kilidi tutan yazıcı bitince bir sonraki çağrı eksikleri tamamlar.
            _logger.LogWarning(ex, "Tekrarlayan kural top-up: müsaitlik kilidi zaman aşımı; atlanıyor. TeacherId={TeacherId}", teacherId);
            DetachForRetry(rule: null, toAdd);
            return 0;
        }

        return toAdd.Count;
    }

    // ------------------------------------------------------------------
    // Ortak yardımcılar
    // ------------------------------------------------------------------

    /// <summary>
    /// Top-up'ın işleyeceği kurallar: aktif ve bitişi bugün ya da sonrası. Hem kilitsiz ön plan hem kilit altındaki
    /// yeniden okuma bu tek sorguyu kullanır.
    /// </summary>
    private Task<List<RecurringAvailabilityRule>> LoadTopUpRulesAsync(int teacherId, DateOnly today, CancellationToken ct)
        => _context.RecurringAvailabilityRules
            .AsNoTracking()
            .Where(r => r.TeacherId == teacherId
                && r.IsActive
                && (r.EffectiveUntil == null || r.EffectiveUntil >= today))
            .OrderBy(r => r.Id) // deterministik: aynı güne düşen iki kuraldan hep eski olan önce
            .ToListAsync(ct);

    /// <summary>
    /// Top-up planı: her aktif kural için ufka kadar eksik occurrence'lar (çakışanlar atlanır). Aynı sweep içinde iki kural
    /// aynı güne düşerse ikincisi ilkini görür.
    /// </summary>
    private async Task<List<TeacherAvailabilitySlot>> PlanTopUpAsync(
        List<RecurringAvailabilityRule> rules, int teacherId, DateOnly today, DateTime now, DateOnly horizon,
        bool logSkipped, CancellationToken ct)
    {
        var existing = await LoadExistingSlotsAsync(teacherId, today, horizon, ct);

        var toAdd = new List<TeacherAvailabilitySlot>();
        foreach (var rule in rules)
        {
            var plan = Plan(rule, rule.Id, existing, today, now, horizon);
            toAdd.AddRange(plan.ToAdd);

            if (logSkipped && plan.Skipped.Count > 0)
                _logger.LogInformation(
                    "Top-up: RuleId={RuleId} için {Count} occurrence çakışma nedeniyle atlandı: {Dates}",
                    rule.Id, plan.Skipped.Count, string.Join(",", plan.Skipped));

            // Aynı sweep içinde iki kural aynı güne düşerse ikincisi ilkini görsün.
            foreach (var s in plan.ToAdd)
                existing.Add(new ExistingSlot(s.Date, s.StartTime, s.EndTime, rule.Id, IsDeleted: false));
        }

        return toAdd;
    }

    /// <summary>
    /// issue #323: execution strategy retry'ı ya da yakalanan çakışma sonrası, önceki denemenin (geri alınmış) kural/slot
    /// nesnelerini change tracker'dan ayırır — sonraki deneme planı ve satırları sıfırdan kurar.
    /// </summary>
    private void DetachForRetry(RecurringAvailabilityRule? rule, IEnumerable<TeacherAvailabilitySlot> slots)
    {
        foreach (var slot in slots)
        {
            if (_context.Entry(slot).State != EntityState.Detached)
                _context.Entry(slot).State = EntityState.Detached;
        }

        if (rule != null)
        {
            if (_context.Entry(rule).State != EntityState.Detached)
                _context.Entry(rule).State = EntityState.Detached;
            rule.Id = 0;
            rule.GeneratedSlots.Clear();
        }
    }

    /// <summary>
    /// Ufuk içindeki mevcut satırlar, tarihe göre indeksli — <b>soft-delete edilmişler dahil</b>
    /// (sweep doğruluğu için). Çakışma kontrolü yalnızca silinmemişlere bakar.
    /// issue #300: aralık her iki yönde bir gün genişletilir — ilk occurrence önceki günün gün aşan slotuyla,
    /// son occurrence'ın gün aşan kısmı ertesi günün slotuyla çakışabilir.
    /// </summary>
    private async Task<ExistingSlotIndex> LoadExistingSlotsAsync(int teacherId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (candidateFrom, _) = SlotTimeRange.CandidateDates(from);
        var (_, candidateTo) = SlotTimeRange.CandidateDates(to);
        var rows = await _context.TeacherAvailabilitySlots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TeacherId == teacherId && s.Date >= candidateFrom && s.Date <= candidateTo)
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

            // (Silinmemiş) başka bir aralıkla kesişiyor → bu hafta atlanır. issue #300: adaylar önceki/aynı/sonraki
            // gün (gün aşan slotlar), karşılaştırma UTC [Start, End) aralığıyla.
            var range = SlotTimeRange.From(date, rule.StartTime, rule.EndTime);
            var (fromDate, toDate) = SlotTimeRange.CandidateDates(date);
            if (existing.Between(fromDate, toDate).Any(e =>
                    !e.IsDeleted && SlotTimeRange.From(e.Date, e.StartTime, e.EndTime).Overlaps(range)))
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

    private const int MinutesPerDay = 24 * 60;
    private const int MinutesPerWeek = 7 * MinutesPerDay;

    /// <summary>
    /// İki haftalık kuralın occurrence aralıkları haftalık (dairesel) zaman çizgisinde kesişiyor mu? Gün aşan kural
    /// (<see cref="SlotTimeRange.CrossesMidnight"/>) ertesi güne, Cumartesi'ninki Pazar'a (hafta başına) taşar.
    /// Yarı açık aralık: bitişik kurallar çakışmaz.
    /// </summary>
    internal static bool WeeklyOverlaps(
        DayOfWeek dayA, TimeOnly startA, TimeOnly endA, DayOfWeek dayB, TimeOnly startB, TimeOnly endB)
    {
        var a = (int)dayA * MinutesPerDay + (int)startA.ToTimeSpan().TotalMinutes;
        var aEnd = a + (int)SlotTimeRange.DurationOf(startA, endA).TotalMinutes;
        var b = (int)dayB * MinutesPerDay + (int)startB.ToTimeSpan().TotalMinutes;
        var bLength = (int)SlotTimeRange.DurationOf(startB, endB).TotalMinutes;

        // B bir hafta geri/ileri kaydırılarak hafta sınırını (Cumartesi → Pazar) aşan durumlar da yakalanır.
        for (var shift = -MinutesPerWeek; shift <= MinutesPerWeek; shift += MinutesPerWeek)
        {
            var bStart = b + shift;
            if (a < bStart + bLength && bStart < aEnd)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Geçerlilik tarih aralıkları kesişiyor mu? Gün aşan kuralın son occurrence'ı EffectiveUntil'in ertesi gününe
    /// taşabildiği için o kuralın bitişi bir gün uzatılır.
    /// </summary>
    private static bool EffectiveRangesOverlap(
        DateOnly fromA, DateOnly? untilA, TimeOnly startA, TimeOnly endA,
        DateOnly fromB, DateOnly? untilB, TimeOnly startB, TimeOnly endB)
    {
        var lastA = untilA?.AddDays(SlotTimeRange.CrossesMidnight(startA, endA) ? 1 : 0);
        var lastB = untilB?.AddDays(SlotTimeRange.CrossesMidnight(startB, endB) ? 1 : 0);
        return (lastA == null || lastA >= fromB) && (lastB == null || fromA <= lastB);
    }

    /// <summary>
    /// Yalnızca unique index ihlali yutulur; FK/bağlantı gibi diğer DbUpdateException'lar yukarı fırlar.
    /// Kural tek yerde: <see cref="ExamApp.Api.Helpers.DbUpdateExceptionClassifier"/> (issue #259).
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException ex) => ExamApp.Api.Helpers.DbUpdateExceptionClassifier.IsUniqueViolation(ex);

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

    private async Task<(int Id, bool IsIndependent)?> ResolveTeacherAsync(int teacherUserId, CancellationToken ct)
    {
        var row = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => new { t.Id, t.IsIndependentTutor, t.SchoolId })
            .FirstOrDefaultAsync(ct);
        return row == null ? null : (row.Id, TeacherIndependence.IsIndependent(row.IsIndependentTutor, row.SchoolId));
    }

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

        /// <summary>[from, to] (iki uç dahil) günlerindeki satırlar.</summary>
        public IEnumerable<ExistingSlot> Between(DateOnly from, DateOnly to)
        {
            for (var d = from; d <= to; d = d.AddDays(1))
                foreach (var slot in At(d))
                    yield return slot;
        }

        public void Add(ExistingSlot slot)
        {
            if (!_byDate.TryGetValue(slot.Date, out var list))
                _byDate[slot.Date] = list = new List<ExistingSlot>();
            list.Add(slot);
        }
    }
}
