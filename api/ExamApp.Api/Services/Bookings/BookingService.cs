using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Models.Dtos.Teachers;
using ExamApp.Api.Models.Dtos.Video;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Services.Video;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// Ders planlama / randevu iş kuralları (issue #96).
/// <para>
/// Slot tarih-saatleri saat dilimsiz duvar saatidir; geçmiş kontrolü ve takvim dönüşümü bunları
/// UTC kabul eder (<see cref="TeacherAvailabilitySlot"/> notuna bakın).
/// </para>
/// Kapsam dışı: iptal/erteleme, no-show, ödeme. Tekrarlayan kurallar
/// <see cref="RecurringAvailabilityService"/>'te (issue #178).
/// </summary>
public class BookingService : IBookingService
{
    /// <summary>Sayfalama üst sınırı — sınırsız liste dönülmez.</summary>
    private const int MaxTake = 200;

    private const int DefaultTake = 50;

    /// <summary>
    /// Bir slot bugünden en fazla bu kadar gün ileriye tanımlanabilir. Takvim sorgusunun
    /// <c>MaxRangeDays</c> (180 gün) üst sınırıyla aynı büyüklük mertebesinde tutulur; amaç
    /// 9999 gibi uçuk tarihlerle veri hijyenini ve takvim sorgu maliyetini bozmayı engellemek.
    /// </summary>
    internal const int MaxAdvanceDays = 90;

    /// <summary>Tek bir müsaitlik aralığının azami süresi (saat).</summary>
    internal const int MaxSlotDurationHours = 4;

    internal static readonly TimeSpan MaxSlotDuration = TimeSpan.FromHours(MaxSlotDurationHours);

    // Sabitler internal: tekrarlayan kural servisi (issue #178) aynı sınırları paylaşır.
    internal static readonly BookingStatus[] ActiveStatuses =
        { BookingStatus.Pending, BookingStatus.Approved };

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;
    private readonly IVideoSessionProvider _videoSessionProvider;
    private readonly IOptions<VideoOptions> _videoOptions;
    private readonly IRecurringAvailabilityService _recurringAvailability;

    /// <summary>
    /// Servisteki tüm "şimdi" okumaları (geçmiş slot/randevu reddi, açık slot filtresi, zaman damgaları,
    /// görüşme katılım penceresi — issue #97) bu saat kaynağından gelir; testler sabit saat verebilsin (issue #294).
    /// </summary>
    private readonly TimeProvider _timeProvider;

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private readonly ILogger<BookingService> _logger;

    // Client'a ulasan mesajlar sozlukten gelir (issue #184). Localizer opsiyoneldir: DI disinda
    // olusturulan (birim test) ornekler varsayilan dile kilitli fallback'e duser.
    private readonly IStringLocalizer<Messages> _localizer;

    public BookingService(
        AppDbContext context,
        IAuthApiClient authApiClient,
        IVideoSessionProvider videoSessionProvider,
        IOptions<VideoOptions> videoOptions,
        TimeProvider timeProvider,
        IRecurringAvailabilityService recurringAvailability,
        ILogger<BookingService> logger,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _authApiClient = authApiClient;
        _videoSessionProvider = videoSessionProvider;
        _videoOptions = videoOptions;
        _timeProvider = timeProvider;
        _recurringAvailability = recurringAvailability;
        _logger = logger;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    // ------------------------------------------------------------------
    // Müsaitlik slotları (öğretmen)
    // ------------------------------------------------------------------

    public async Task<AvailabilitySlotResultDto> CreateSlotAsync(
        int teacherUserId, CreateAvailabilitySlotDto dto, CancellationToken ct = default)
    {
        var now = UtcNow();

        // issue #300: EndTime < StartTime → bitiş ertesi gün (gün aşan slot, SlotTimeRange). Yalnız sıfır süre reddedilir.
        if (SlotTimeRange.IsZeroLength(dto.StartTime, dto.EndTime))
            return SlotFail(_localizer["booking.slot.zeroLength"]);

        var range = SlotTimeRange.From(dto.Date, dto.StartTime, dto.EndTime);

        if (range.StartUtc <= now)
            return SlotFail(_localizer["booking.slot.inPast"]);

        // Üst sınırlar sunucu tarafında zorunlu (istemci doğrulaması güvenlik sınırı değildir).
        var maxDate = DateOnly.FromDateTime(now).AddDays(MaxAdvanceDays);
        if (dto.Date > maxDate)
            return SlotFail(_localizer["booking.slot.tooFarAhead", MaxAdvanceDays]);

        if (range.Duration > MaxSlotDuration)
            return SlotFail(_localizer[TooLongMessageKey(dto.StartTime, dto.EndTime), MaxSlotDurationHours]);

        // issue #323: tekrarlayan kural ile aynı dakika hassasiyeti — 10:00:00 / 10:00:01 gibi neredeyse aynı iki satır
        // unique index'i atlatmasın.
        if (!SlotTimeRange.IsMinutePrecision(dto.StartTime) || !SlotTimeRange.IsMinutePrecision(dto.EndTime))
            return SlotFail(_localizer["booking.slot.invalidPrecision"]);

        var teacher = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => new { t.Id, t.ApprovalStatus, t.IsIndependentTutor, t.SchoolId })
            .FirstOrDefaultAsync(ct);

        if (teacher == null)
            return new AvailabilitySlotResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        // Sadece onaylı öğretmenler randevu alabilir (tutor-search ile aynı kısıt).
        if (teacher.ApprovalStatus != TeacherApprovalStatus.Approved)
            return new AvailabilitySlotResultDto
            {
                Success = false,
                Forbidden = true,
                Message = _localizer["booking.teacherNotApproved"]
            };

        // issue #418: randevu bağımsız öğretmen özelliği — okula bağlı (hibrit dahil) öğretmen müsaitlik tanımlayamaz.
        // Sıra CreateRuleAsync ile aynı: önce onay, sonra bağımsızlık.
        if (!TeacherIndependence.IsIndependent(teacher.IsIndependentTutor, teacher.SchoolId))
            return new AvailabilitySlotResultDto { Success = false, Forbidden = true, Message = _localizer[TeacherNotIndependentKey] };

        _context.SetCurrentUser(teacherUserId);

        // PostgreSQL timestamp mikro saniye hassasiyetindedir; değer retry'da "kendi satırım mı?" karşılaştırması için
        // DB'ye yazıldığı haliyle (mikro saniyeye kesilmiş) tutulur.
        var createdAt = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = teacher.Id,
            Date = dto.Date,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            CreatedAt = createdAt
        };

        // issue #323 (security L2): çakışma kontrolü + INSERT öğretmen bazlı advisory lock altında, tek transaction'da —
        // aksi halde paralel iki istek ikisi de "kesişen yok" görüp kesişen iki slot açabilirdi (unique index yalnız
        // birebir aynı aralığı yakalar). Retry-on-failure nedeniyle transaction execution strategy İÇİNDE açılır.
        var overlaps = false;
        int? committedEarlierId = null;
        var attempt = 0;
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                attempt++;
                overlaps = false;
                committedEarlierId = null;

                // Geçici hata sonrası retry (ör. commit'te kopan bağlantı): önceki denemenin satırı geri alınmış olabilir;
                // her deneme satırı sıfırdan ekler (ResetForRetry).
                ResetForRetry(slot);

                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                await _context.Database.AcquireTeacherAvailabilityLockAsync(teacher.Id, ct);

                // Kesişen bir aralık varsa ikinci slot açılmaz — aksi halde öğretmen aynı saate iki ayrı randevu alabilirdi.
                // issue #300: gün aşan slotlar yüzünden aday yalnız aynı gün değil, önceki/sonraki gündür; tarih aralığıyla
                // daraltılıp UTC [Start, End) aralıkları bellekte karşılaştırılır (en fazla birkaç satır).
                var (fromDate, toDate) = SlotTimeRange.CandidateDates(dto.Date);
                var candidates = await _context.TeacherAvailabilitySlots
                    .AsNoTracking()
                    .Where(s => s.TeacherId == teacher.Id && s.Date >= fromDate && s.Date <= toDate)
                    .Select(s => new { s.Id, s.Date, s.StartTime, s.EndTime, s.CreatedAt, s.CreateUserId })
                    .ToListAsync(ct);

                // Belirsiz commit (code review Uyarı-2): önceki deneme COMMIT etti ama onay kayboldu → strateji yeniden
                // denedi. Kesişen satır bu isteğin kendi satırıysa (aynı aralık + aynı yazan + aynı CreatedAt anı) 409
                // yerine başarı sayılır. Yalnız retry'da bakılır; ilk denemede böyle bir satır olamaz.
                if (attempt > 1)
                {
                    var own = candidates.FirstOrDefault(c => c.Date == dto.Date && c.StartTime == dto.StartTime
                        && c.EndTime == dto.EndTime && c.CreatedAt == createdAt && c.CreateUserId == teacherUserId);
                    if (own != null)
                    {
                        committedEarlierId = own.Id;
                        return;
                    }
                }

                overlaps = candidates.Any(s => SlotTimeRange.From(s.Date, s.StartTime, s.EndTime).Overlaps(range));
                if (overlaps)
                    return; // commit yok → dispose'da rollback (kilit bırakılır)

                _context.TeacherAvailabilitySlots.Add(slot);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // Filtreli unique index (TeacherId, Date, StartTime, EndTime) — kilit dışından gelen (ör. doğrudan SQL) yazıcı.
            // Diğer DbUpdateException'lar (FK, CHECK, bağlantı) bilinçli olarak yukarı fırlar.
            _logger.LogWarning(ex, "Müsaitlik aralığı eklenemedi (çakışma). TeacherId={TeacherId}", teacher.Id);
            _context.Entry(slot).State = EntityState.Detached;
            return new AvailabilitySlotResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["booking.slot.duplicate"]
            };
        }
        catch (TeacherAvailabilityLockTimeoutException ex)
        {
            _logger.LogWarning(ex, "Müsaitlik kilidi zaman aşımı. TeacherId={TeacherId}", teacher.Id);
            _context.Entry(slot).State = EntityState.Detached;
            return SlotBusy();
        }

        if (committedEarlierId.HasValue)
        {
            _logger.LogInformation("Müsaitlik aralığı önceki denemede commit edilmiş (onay kaybı); başarı sayıldı. SlotId={SlotId}",
                committedEarlierId.Value);
            slot.Id = committedEarlierId.Value;
        }

        if (overlaps)
            return new AvailabilitySlotResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["booking.slot.overlapping"]
            };

        return new AvailabilitySlotResultDto
        {
            Success = true,
            ObjectId = slot.Id,
            Message = _localizer["booking.slot.created"],
            Slot = MapSlot(slot.Id, teacher.Id, slot.Date, slot.StartTime, slot.EndTime, slot.CreatedAt, null, null, null, null)
        };
    }

    public async Task<AvailabilitySlotListResultDto> GetMySlotsAsync(
        int teacherUserId, int skip, int take, CancellationToken ct = default)
    {
        var teacher = await FindTeacherAsync(teacherUserId, ct);
        if (teacher == null)
            return new AvailabilitySlotListResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        // issue #418: okula bağlı öğretmen randevu/müsaitlik uçlarını kullanamaz (403, CreateSlotAsync ile aynı).
        if (!teacher.IsIndependent)
            return new AvailabilitySlotListResultDto { Success = false, Forbidden = true, Message = _localizer[TeacherNotIndependentKey] };

        var teacherId = teacher.Id;

        // Tekrarlayan kuralların 90 günlük penceresi burada lazy ileri kaydırılır (issue #178):
        // arka plan job yok; kuralı olmayan öğretmen için maliyet tek indeksli sorgudur.
        await _recurringAvailability.TopUpAsync(teacherId, teacherUserId, ct);

        var rows = await _context.TeacherAvailabilitySlots
            .AsNoTracking()
            .Where(s => s.TeacherId == teacherId)
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.StartTime)
            .Skip(Normalize(skip))
            .Take(Normalize(take, isTake: true))
            .Select(s => new SlotRow
            {
                Id = s.Id,
                TeacherId = s.TeacherId,
                Date = s.Date,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                CreatedAt = s.CreatedAt,
                RecurringAvailabilityRuleId = s.RecurringAvailabilityRuleId,
                BookingId = s.Bookings
                    .Where(b => ActiveStatuses.Contains(b.Status))
                    .Select(b => (int?)b.Id)
                    .FirstOrDefault(),
                BookingStatus = s.Bookings
                    .Where(b => ActiveStatuses.Contains(b.Status))
                    .Select(b => (BookingStatus?)b.Status)
                    .FirstOrDefault(),
                StudentUserId = s.Bookings
                    .Where(b => ActiveStatuses.Contains(b.Status))
                    .Select(b => (int?)b.Student.UserId)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var names = await ResolveUserNamesAsync(
            rows.Where(r => r.StudentUserId.HasValue).Select(r => r.StudentUserId!.Value), ct);

        return new AvailabilitySlotListResultDto
        {
            Success = true,
            Items = rows.Select(r => MapSlot(
                r.Id, r.TeacherId, r.Date, r.StartTime, r.EndTime, r.CreatedAt,
                r.BookingId, r.BookingStatus,
                r.StudentUserId.HasValue && names.TryGetValue(r.StudentUserId.Value, out var n) ? n : null,
                r.RecurringAvailabilityRuleId)).ToList()
        };
    }

    public async Task<AvailabilitySlotDeleteResultDto> DeleteSlotAsync(int teacherUserId, int slotId, CancellationToken ct = default)
    {
        var teacher = await FindTeacherAsync(teacherUserId, ct);
        if (teacher == null)
            return new AvailabilitySlotDeleteResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        // issue #418: okula bağlı öğretmen randevu/müsaitlik uçlarını kullanamaz (403, CreateSlotAsync ile aynı).
        if (!teacher.IsIndependent)
            return new AvailabilitySlotDeleteResultDto { Success = false, Forbidden = true, Message = _localizer[TeacherNotIndependentKey] };

        var teacherId = teacher.Id;

        // Hızlı yol (kilitsiz, salt okunur): 404/403 ayrımı. Karar kilit altında yeniden okunan satırla verilir.
        var owner = await _context.TeacherAvailabilitySlots
            .AsNoTracking()
            .Where(s => s.Id == slotId)
            .Select(s => (int?)s.TeacherId)
            .FirstOrDefaultAsync(ct);

        if (owner == null)
            return new AvailabilitySlotDeleteResultDto { Success = false, NotFound = true, Message = _localizer["booking.slot.notFound"] };

        // Başkasının slotu: 403 (worksheet sahiplik deseniyle tutarlı).
        if (owner.Value != teacherId)
            return new AvailabilitySlotDeleteResultDto { Success = false, Forbidden = true, Message = _localizer["booking.slot.notOwned"] };

        _context.SetCurrentUser(teacherUserId);

        // issue #376: "aktif randevu yok" kontrolü + soft delete öğretmen müsaitlik kilidi (#323) altında, tek transaction'da.
        // Randevu talebi (CreateBookingAsync) aynı kilidi alıp slotun hâlâ silinmemiş olduğunu kilidin altında doğrular; böylece
        // eşzamanlı "talep + silme" ikilisinden biri diğerini görür ve aktif randevulu slot hiçbir sırada silinmez.
        AvailabilitySlotDeleteResultDto? result = null;
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                result = null;

                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                await _context.Database.AcquireTeacherAvailabilityLockAsync(teacherId, ct);

                var slot = await _context.TeacherAvailabilitySlots
                    .FirstOrDefaultAsync(s => s.Id == slotId && s.TeacherId == teacherId, ct);
                if (slot == null)
                {
                    // Kilidi beklerken eşzamanlı bir silme (tekil ya da seri) kazandı.
                    result = new AvailabilitySlotDeleteResultDto { Success = false, NotFound = true, Message = _localizer["booking.slot.notFound"] };
                    return;
                }

                var hasActiveBooking = await _context.Bookings
                    .AsNoTracking()
                    .AnyAsync(b => b.AvailabilitySlotId == slotId && ActiveStatuses.Contains(b.Status), ct);
                if (hasActiveBooking)
                {
                    result = new AvailabilitySlotDeleteResultDto
                    {
                        Success = false,
                        Conflict = true,
                        ErrorCode = BookingErrorCodes.SlotHasActiveBooking,
                        Message = _localizer["booking.slot.hasActiveBooking"]
                    };
                    return; // commit yok → dispose'da rollback (kilit bırakılır)
                }

                _context.TeacherAvailabilitySlots.Remove(slot); // BaseEntity → soft delete
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                result = new AvailabilitySlotDeleteResultDto { Success = true, ObjectId = slotId, Message = _localizer["booking.slot.deleted"] };
            });
        }
        catch (TeacherAvailabilityLockTimeoutException ex)
        {
            _context.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Slot silme: müsaitlik kilidi zaman aşımı. TeacherId={TeacherId}", teacherId);
            return new AvailabilitySlotDeleteResultDto { Success = false, Conflict = true, Message = _localizer["booking.slot.busy"] };
        }

        return result!;
    }

    // ------------------------------------------------------------------
    // Müsaitlik slotları (öğrenci görünümü)
    // ------------------------------------------------------------------

    public async Task<AvailabilitySlotListResultDto> GetTeacherOpenSlotsAsync(
        int teacherId, int skip, int take, CancellationToken ct = default)
    {
        var teacher = await _context.Teachers
            .AsNoTracking()
            // issue #289: askıdaki öğretmenin takvimi öğrenciye görünmez (onaysız ile aynı 404).
            .Where(t => t.Id == teacherId && t.ApprovalStatus == TeacherApprovalStatus.Approved && t.AccountSuspendedAt == null)
            .Select(t => new { t.IsIndependentTutor, t.SchoolId })
            .FirstOrDefaultAsync(ct);

        // Onaysız/olmayan öğretmen ayrımı sızdırılmaz (tutor public-profile ile aynı desen).
        // issue #418: randevu bağımsız öğretmen özelliğidir — okula bağlı öğretmenin takvimi kimseye (aynı okul ve admin
        // dahil; #190'daki aynı-okul istisnası kaldırıldı) görünmez; aynı 404.
        if (teacher == null || !TeacherIndependence.IsIndependent(teacher.IsIndependentTutor, teacher.SchoolId))
            return new AvailabilitySlotListResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherNotFound"] };

        var now = UtcNow();
        var today = DateOnly.FromDateTime(now);
        var timeNow = TimeOnly.FromDateTime(now);

        var rows = await _context.TeacherAvailabilitySlots
            .AsNoTracking()
            .Where(s => s.TeacherId == teacherId
                && (s.Date > today || (s.Date == today && s.StartTime > timeNow))
                && !s.Bookings.Any(b => ActiveStatuses.Contains(b.Status)))
            .OrderBy(s => s.Date)
            .ThenBy(s => s.StartTime)
            .Skip(Normalize(skip))
            .Take(Normalize(take, isTake: true))
            .Select(s => new SlotRow
            {
                Id = s.Id,
                TeacherId = s.TeacherId,
                Date = s.Date,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync(ct);

        // Öğrenciye kural kimliği sızdırılmaz (öğretmenin iç planlama bilgisi) → recurringRuleId null.
        return new AvailabilitySlotListResultDto
        {
            Success = true,
            Items = rows
                .Select(r => MapSlot(r.Id, r.TeacherId, r.Date, r.StartTime, r.EndTime, r.CreatedAt, null, null, null, null))
                .ToList()
        };
    }

    // ------------------------------------------------------------------
    // Randevu talepleri
    // ------------------------------------------------------------------

    public async Task<BookingResultDto> CreateBookingAsync(
        int studentUserId, CreateBookingDto dto, CancellationToken ct = default)
    {
        var now = UtcNow();

        var studentId = await _context.Students
            .AsNoTracking()
            .Where(s => s.UserId == studentUserId)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);

        if (studentId == null)
            return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.studentRecordNotFound"] };

        var slot = await _context.TeacherAvailabilitySlots
            .AsNoTracking()
            .Where(s => s.Id == dto.AvailabilitySlotId)
            .Select(s => new
            {
                s.Id,
                s.TeacherId,
                s.Date,
                s.StartTime,
                s.EndTime,
                TeacherApproval = s.Teacher.ApprovalStatus,
                TeacherSuspended = s.Teacher.AccountSuspendedAt != null, // issue #289
                // issue #331 (security D1): "onaylı" tanımı yetkiyle (IApprovedTeacherGuard, #287) hizalı — hesap onayı da şart.
                TeacherAccountApproved = s.Teacher.AccountApprovedAt != null,
                // issue #418: randevu yalnız bağımsız öğretmene alınır (GetTeacherOpenSlotsAsync ile aynı kural).
                TeacherIndependentTutor = s.Teacher.IsIndependentTutor,
                TeacherSchoolId = s.Teacher.SchoolId,
                TeacherUserId = s.Teacher.UserId
            })
            .FirstOrDefaultAsync(ct);

        // Onaysız / okula bağlı öğretmenin slotu öğrenciye hiç görünmez → var/yok ayrımı da sızdırılmaz.
        if (slot == null || slot.TeacherApproval != TeacherApprovalStatus.Approved || slot.TeacherSuspended
            || !slot.TeacherAccountApproved
            || !TeacherIndependence.IsIndependent(slot.TeacherIndependentTutor, slot.TeacherSchoolId))
            return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.slot.notFound"] };

        if (ToUtc(slot.Date, slot.StartTime) <= now)
            return new BookingResultDto { Success = false, Message = _localizer["booking.request.slotInPast"] };

        var alreadyBooked = await _context.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.AvailabilitySlotId == slot.Id && ActiveStatuses.Contains(b.Status), ct);

        if (alreadyBooked)
            return new BookingResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["booking.request.duplicate"]
            };

        // auth-api lookup TEK yazım işleminden önce, best-effort (WorksheetAccessRequestService ile
        // aynı desen). Patlarsa event alanları boş geçer (consumer boş sub'ı LogWarning ile tolere
        // eder) — booking + outbox yine de atomik yazılır.
        var studentName = string.Empty;
        var teacherKeycloakId = string.Empty;
        try
        {
            var lookup = await _authApiClient.GetUsersByIdsAsync(new[] { studentUserId, slot.TeacherUserId }, ct);
            studentName = lookup.FirstOrDefault(u => u.Id == studentUserId)?.FullName ?? string.Empty;
            teacherKeycloakId = lookup.FirstOrDefault(u => u.Id == slot.TeacherUserId)?.KeycloakId ?? string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex,
                "Randevu talebi için auth-api lookup başarısız; event alanları boş geçilecek. SlotId={SlotId}",
                slot.Id);
        }

        _context.SetCurrentUser(studentUserId);

        var booking = new Booking
        {
            TeacherId = slot.TeacherId,
            StudentId = studentId.Value,
            AvailabilitySlotId = slot.Id,
            Status = BookingStatus.Pending,
            CreatedAt = now
        };

        // Booking satırı + outbox mesajı tek transaction'da (WorksheetAccessRequestService ile aynı
        // desen — retry-on-failure execution strategy içinde). booking.Id identity DB'den üretildiği
        // için iki SaveChanges tek transaction ile atomik kılınır; conflict catch tüm bloğu sarar.
        var teacherUnavailable = false;
        var slotGone = false;
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                // Retry'da önceki denemenin (commit edilmemiş) booking/outbox satırları tekrar eklenmesin.
                _context.ChangeTracker.Clear();
                booking.Id = 0;
                teacherUnavailable = false;
                slotGone = false;

                await using var tx = await _context.Database.BeginTransactionAsync(ct);

                // issue #376: slot silme yollarıyla (tekil + seri, #323 öğretmen müsaitlik kilidi) serileşir. Ön okumadan sonra
                // slot silindiyse talep oluşmaz; aksi halde silinmiş slota bağlı aktif randevu doğabilirdi (silme yolları
                // "aktif randevu yok" kontrolünü aynı kilidin altında yapar).
                await _context.Database.AcquireTeacherAvailabilityLockAsync(slot.TeacherId, ct);
                if (!await _context.TeacherAvailabilitySlots.AsNoTracking().AnyAsync(s => s.Id == slot.Id, ct))
                {
                    slotGone = true;
                    return; // commit yok → dispose'da rollback
                }

                // issue #331: yukarıdaki askı kontrolü kilitsiz bir ön okumadır. Askı transaction'ı arada commit olursa
                // askıdaki öğretmende Pending talep kalırdı. Öğretmen satırı burada FOR SHARE ile kilitlenip koşul AYNI
                // transaction'da yeniden doğrulanır; askı da aynı satırı UPDATE ettiği için ikisi serileşir.
                if (!await LockBookableTeacherAsync(slot.TeacherId, ct))
                {
                    teacherUnavailable = true;
                    return; // commit yok → dispose'da rollback
                }

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync(ct);

                var @event = new BookingRequestCreatedEvent
                {
                    BookingId = booking.Id,
                    TeacherId = slot.TeacherId,
                    TeacherUserId = slot.TeacherUserId,
                    TargetKeycloakId = teacherKeycloakId,
                    StudentId = studentId.Value,
                    StudentName = studentName,
                    AvailabilitySlotId = slot.Id,
                    Date = slot.Date,
                    StartTime = slot.StartTime,
                    EndTime = slot.EndTime,
                    RequestedAt = booking.CreatedAt
                };
                _context.OutboxMessages.Add(new OutboxMessage
                {
                    Type = OutboxEventRegistry.NameFor<BookingRequestCreatedEvent>(),
                    Content = JsonSerializer.Serialize(@event),
                    CreatedAt = now
                });
                await _context.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });
        }
        catch (TeacherAvailabilityLockTimeoutException ex)
        {
            // issue #331 (review U1/D3) + #376: öğretmen müsaitlik advisory kilidi ya da öğretmen satırı kilidi (FOR SHARE)
            // lock_timeout içinde alınamadı → retry'sız 409 (#323 deseni).
            _logger.LogWarning(ex, "Randevu talebi: öğretmen kilidi (müsaitlik advisory / öğretmen satırı) zaman aşımı. TeacherId={TeacherId}",
                slot.TeacherId);
            return new BookingResultDto { Success = false, Conflict = true, Message = _localizer["booking.slot.busy"] };
        }
        catch (DbUpdateException ex)
        {
            // Filtreli unique index: iki öğrenci aynı anda aynı slotu talep etti.
            _logger.LogWarning(ex, "Randevu oluşturulamadı (çakışma). SlotId={SlotId}", slot.Id);
            return new BookingResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["booking.request.duplicate"]
            };
        }

        if (slotGone)
        {
            _logger.LogInformation("Randevu talebi reddedildi: slot talep sırasında silindi. SlotId={SlotId}", slot.Id);
            return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.slot.notFound"] };
        }

        if (teacherUnavailable)
        {
            _logger.LogInformation(
                "Randevu talebi reddedildi: öğretmen talep sırasında askıya alındı/onayı kalktı. SlotId={SlotId} TeacherId={TeacherId}",
                slot.Id, slot.TeacherId);
            return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.slot.notFound"] };
        }

        var names = await ResolveUserNamesAsync(new[] { slot.TeacherUserId, studentUserId }, ct);
        var slotRange = SlotTimeRange.From(slot.Date, slot.StartTime, slot.EndTime);

        return new BookingResultDto
        {
            Success = true,
            ObjectId = booking.Id,
            Message = _localizer["booking.request.created"],
            Booking = new BookingDto
            {
                Id = booking.Id,
                TeacherId = booking.TeacherId,
                TeacherName = names.TryGetValue(slot.TeacherUserId, out var tn) ? tn : null,
                StudentId = booking.StudentId,
                StudentName = names.TryGetValue(studentUserId, out var sn) ? sn : null,
                AvailabilitySlotId = slot.Id,
                Date = slot.Date,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                StartUtc = slotRange.StartUtc,
                EndUtc = slotRange.EndUtc,
                Status = booking.Status.ToString(),
                CreatedAt = booking.CreatedAt
            }
        };
    }

    /// <summary>
    /// <see cref="LockBookableTeacherAsync"/>'ın Postgres sorgusu. {0} = Teachers.Id, {1} = Approved. Koşul ön okumayla ve
    /// öğretmen yetkisiyle (<c>IApprovedTeacherGuard</c>) aynı: başvuru onaylı, hesap onaylı, askıda değil, silinmemiş;
    /// issue #418: ve bağımsız (<see cref="TeacherIndependence.SqlCondition"/>) — admin aradaki okul atamasını commit ederse
    /// talep oluşmaz.
    /// </summary>
    internal const string BookableTeacherLockSql = $$"""
        SELECT "Id" AS "Value" FROM "Teachers"
        WHERE "Id" = {0} AND "ApprovalStatus" = {1}
          AND "AccountApprovedAt" IS NOT NULL AND "AccountSuspendedAt" IS NULL AND NOT "IsDeleted"
          AND {{TeacherIndependence.SqlCondition}}
        FOR SHARE
        """;

    /// <summary>
    /// issue #331 (security L4): talep insert'iyle AYNI transaction'da öğretmenin hâlâ talep alabilir olduğunu (onaylı,
    /// askıda değil, silinmemiş) doğrular. PostgreSQL'de satır <c>SELECT ... FOR SHARE</c> ile kilitlenir:
    /// <list type="bullet">
    /// <item>Askı (<c>AdminTeacherSuspensionService</c>, aynı satıra koşullu UPDATE → satır kilidi) önce gelmişse bu ifade
    /// askı commit'ini bekler; READ COMMITTED satırı askının yazdığı yeni sürümle yeniden değerlendirir → satır dönmez →
    /// talep oluşmaz.</item>
    /// <item>Bu ifade önce gelmişse askının UPDATE'i talep transaction'ının commit'ini bekler; askı ardından Pending talepleri
    /// okurken yeni talebi görür ve aynı transaction'da otomatik reddeder (#298).</item>
    /// </list>
    /// Booking'in Teachers FK kontrolü yalnızca <c>FOR KEY SHARE</c> alır ve askının UPDATE'iyle çakışmaz — yarışın kaynağı
    /// buydu. <c>FOR SHARE</c> eşzamanlı taleplerin birbirini beklemesine yol açmaz (paylaşımlı kilit). Ayrı bir advisory
    /// kilit yerine satır kilidi: askı yolunda değişiklik gerektirmez, öğretmen satırını değiştiren her yazıcıyla (onay
    /// akışları dahil) kendiliğinden serileşir. Postgres dışı sağlayıcılarda (SQLite birim testleri) kilitsiz aynı koşul.
    /// </summary>
    private async Task<bool> LockBookableTeacherAsync(int teacherId, CancellationToken ct)
    {
        if (!_context.Database.IsNpgsql())
        {
            return await _context.Teachers
                .AsNoTracking()
                .Where(TeacherIndependence.Holds)
                .AnyAsync(t => t.Id == teacherId
                    && t.ApprovalStatus == TeacherApprovalStatus.Approved
                    && t.AccountApprovedAt != null
                    && t.AccountSuspendedAt == null, ct);
        }

        // Review U1/D3: bekleme üst sınırı (#323 ile aynı 5 sn). SET LOCAL transaction sonunda kendiliğinden geri döner.
        // 55P03 Npgsql'de geçici sayılır; execution strategy yeniden denemesin diye kalıcı istisnaya çevrilir → 409.
        await _context.Database.ExecuteSqlRawAsync(TeacherAvailabilityLock.SetLockTimeoutSql, ct);
        try
        {
            var rows = await _context.Database
                .SqlQueryRaw<int>(BookableTeacherLockSql, teacherId, (int)TeacherApprovalStatus.Approved)
                .ToListAsync(ct);
            return rows.Count > 0;
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.LockNotAvailable)
        {
            throw new TeacherAvailabilityLockTimeoutException(teacherId, ex);
        }
    }

    public async Task<BookingListResultDto> GetTeacherBookingsAsync(
        int teacherUserId, int skip, int take, CancellationToken ct = default)
    {
        var teacher = await FindTeacherAsync(teacherUserId, ct);
        if (teacher == null)
            return new BookingListResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        // issue #418: okula bağlı öğretmen randevu/müsaitlik uçlarını kullanamaz (403, CreateSlotAsync ile aynı).
        if (!teacher.IsIndependent)
            return new BookingListResultDto { Success = false, Forbidden = true, Message = _localizer[TeacherNotIndependentKey] };

        var teacherId = teacher.Id;

        return await QueryBookingsAsync(b => b.TeacherId == teacherId, skip, take, ct);
    }

    public async Task<BookingListResultDto> GetStudentBookingsAsync(
        int studentUserId, int skip, int take, CancellationToken ct = default)
    {
        var studentId = await _context.Students
            .AsNoTracking()
            .Where(s => s.UserId == studentUserId)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);

        if (studentId == null)
            return new BookingListResultDto { Success = false, NotFound = true, Message = _localizer["booking.studentRecordNotFound"] };

        return await QueryBookingsAsync(b => b.StudentId == studentId.Value, skip, take, ct);
    }

    public Task<BookingResultDto> ApproveBookingAsync(int teacherUserId, int bookingId, CancellationToken ct = default)
        => DecideAsync(teacherUserId, bookingId, BookingStatus.Approved, null, ct);

    public Task<BookingResultDto> RejectBookingAsync(
        int teacherUserId, int bookingId, string? rejectionReason, CancellationToken ct = default)
        => DecideAsync(teacherUserId, bookingId, BookingStatus.Rejected, rejectionReason, ct);

    private async Task<BookingResultDto> DecideAsync(
        int teacherUserId, int bookingId, BookingStatus newStatus, string? rejectionReason, CancellationToken ct)
    {
        var teacher = await FindTeacherAsync(teacherUserId, ct);
        if (teacher == null)
            return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        // issue #418: okula bağlı öğretmen randevu/müsaitlik uçlarını kullanamaz (403, CreateSlotAsync ile aynı).
        if (!teacher.IsIndependent)
            return new BookingResultDto { Success = false, Forbidden = true, Message = _localizer[TeacherNotIndependentKey] };

        var teacherId = teacher.Id;

        // issue #376: slotu soft-delete edilmiş (eski veri) talep de karara bağlanabilmeli → yalnız slot filtresi kapatılır.
        var booking = await _context.Bookings
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Include(b => b.AvailabilitySlot)
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking == null)
            return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.request.notFound"] };

        if (booking.TeacherId != teacherId)
            return new BookingResultDto { Success = false, Forbidden = true, Message = _localizer["booking.request.notOwned"] };

        if (booking.Status != BookingStatus.Pending)
            return new BookingResultDto
            {
                Success = false,
                Message = _localizer["booking.request.alreadyDecided", booking.Status]
            };

        // issue #376: slotu soft-delete edilmiş (eski veri) Pending talep yalnız reddedilebilir; onay silinmiş slota yeni
        // ders bağlardı. Yarış yok: aktif (Pending) talebi olan slot artık silinemez (DeleteSlotAsync, kilit altında).
        if (newStatus == BookingStatus.Approved && booking.AvailabilitySlot.IsDeleted)
            return new BookingDecisionResultDto
            {
                Success = false,
                Conflict = true,
                ErrorCode = BookingErrorCodes.RequestSlotDeleted,
                Message = _localizer["booking.request.slotDeleted"]
            };

        // auth-api lookup best-effort, karar yazılmadan önce (WorksheetAccessRequestService'in
        // AddDecisionOutboxAsync'i ile aynı desen). Patlarsa event alanları boş geçer.
        var teacherName = string.Empty;
        var studentKeycloakId = string.Empty;
        try
        {
            var lookup = await _authApiClient.GetUsersByIdsAsync(new[] { teacherUserId, booking.Student.UserId }, ct);
            teacherName = lookup.FirstOrDefault(u => u.Id == teacherUserId)?.FullName ?? string.Empty;
            studentKeycloakId = lookup.FirstOrDefault(u => u.Id == booking.Student.UserId)?.KeycloakId ?? string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex,
                "Randevu kararı için auth-api lookup başarısız; event alanları boş geçilecek. BookingId={BookingId}",
                bookingId);
        }

        _context.SetCurrentUser(teacherUserId);

        var decidedAt = UtcNow();
        var storedReason = newStatus == BookingStatus.Rejected && !string.IsNullOrWhiteSpace(rejectionReason)
            ? rejectionReason.Trim()
            : null;

        var decisionEvent = new BookingDecisionEvent
        {
            BookingId = booking.Id,
            TeacherId = booking.TeacherId,
            TeacherName = teacherName,
            StudentId = booking.StudentId,
            StudentUserId = booking.Student.UserId,
            TargetKeycloakId = studentKeycloakId,
            Approved = newStatus == BookingStatus.Approved,
            RejectionReason = storedReason,
            Date = booking.AvailabilitySlot.Date,
            StartTime = booking.AvailabilitySlot.StartTime,
            EndTime = booking.AvailabilitySlot.EndTime,
            DecidedAt = decidedAt
        };

        // Code review O1 (#298): okuma ile yazma arasında talep başka bir yoldan karara bağlanabilir (öğretmen askıya
        // alınınca otomatik ret, ya da öğretmenin paralel ikinci isteği). Koşullu UPDATE (Status == Pending) + outbox aynı
        // transaction'da; 0 satır → "zaten karara bağlandı" ve event YAZILMAZ (öğrenciye çelişen iki bildirim gitmez).
        // ExecuteUpdate SaveChanges denetimini atladığı için UpdateTime/UpdateUserId açıkça yazılır.
        var affected = 0;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            affected = await _context.Bookings
                .Where(b => b.Id == bookingId && b.Status == BookingStatus.Pending)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.Status, newStatus)
                    .SetProperty(b => b.DecisionAt, decidedAt)
                    .SetProperty(b => b.RejectionReason, storedReason)
                    .SetProperty(b => b.UpdateTime, decidedAt)
                    .SetProperty(b => b.UpdateUserId, teacherUserId), ct);

            if (affected == 0)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            _context.OutboxMessages.Add(new OutboxMessage
            {
                Type = OutboxEventRegistry.NameFor<BookingDecisionEvent>(),
                Content = JsonSerializer.Serialize(decisionEvent),
                CreatedAt = decidedAt
            });
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        if (affected == 0)
        {
            var current = await _context.Bookings.AsNoTracking()
                .Where(b => b.Id == bookingId)
                .Select(b => (BookingStatus?)b.Status)
                .FirstOrDefaultAsync(ct);
            if (current == null)
                return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.request.notFound"] };

            return new BookingResultDto
            {
                Success = false,
                Message = _localizer["booking.request.alreadyDecided", current.Value]
            };
        }

        var result = await QueryBookingsAsync(b => b.Id == bookingId, 0, 1, ct);

        return new BookingResultDto
        {
            Success = true,
            ObjectId = bookingId,
            Message = newStatus == BookingStatus.Approved ? _localizer["booking.request.approved"] : _localizer["booking.request.rejected"],
            Booking = result.Items.FirstOrDefault()
        };
    }

    // ------------------------------------------------------------------
    // Görüşme odası (issue #97) + ortak katılım kuralı (çizim tahtası, issue #98)
    // ------------------------------------------------------------------

    public async Task<BookingLiveSessionAccess> GetLiveSessionAccessAsync(
        int callerUserId, int bookingId, CancellationToken ct = default)
    {
        // issue #376: onaylı randevu slotu silinse de geçerlidir — slotun saati silinmiş slottan da okunur (yalnız slot
        // filtresi kapanır; silinmiş booking/öğretmen/öğrenci yine "bulunamadı"dır).
        var row = await _context.Bookings
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new
            {
                b.Id,
                b.Status,
                TeacherUserId = b.Teacher.UserId,
                StudentUserId = b.Student.UserId,
                b.AvailabilitySlot.Date,
                b.AvailabilitySlot.StartTime,
                b.AvailabilitySlot.EndTime,
                // issue #298: ApprovedTeacherGuard ile aynı karar (askı önce, sonra hesap onayı).
                TeacherAvailable = b.Teacher.AccountSuspendedAt == null && b.Teacher.AccountApprovedAt != null
            })
            .FirstOrDefaultAsync(ct);

        if (row == null)
            return BookingLiveSessionAccess.Denied(BookingLiveSessionDenial.NotFound, bookingId);

        var isTeacher = row.TeacherUserId == callerUserId;
        var isStudent = row.StudentUserId == callerUserId;

        // Sadece randevunun iki tarafı katılabilir — rol attribute'u tek başına yetmez.
        if (!isTeacher && !isStudent)
            return BookingLiveSessionAccess.Denied(BookingLiveSessionDenial.NotParticipant, bookingId);

        var window = BookingSessionWindow.For(row.Date, row.StartTime, row.EndTime, _videoOptions.Value);
        // Sıra: taraf → randevu durumu → öğretmen hesabı (issue #298) → pencere.
        var denial = row.Status != BookingStatus.Approved
            ? BookingLiveSessionDenial.NotApproved
            : !row.TeacherAvailable
                ? BookingLiveSessionDenial.TeacherUnavailable
            : window.StateAt(UtcNow()) switch
            {
                BookingWindowState.NotOpen => BookingLiveSessionDenial.WindowNotOpen,
                BookingWindowState.Closed => BookingLiveSessionDenial.WindowClosed,
                _ => BookingLiveSessionDenial.None
            };

        return new BookingLiveSessionAccess(denial, row.Id, isTeacher, row.TeacherUserId, row.StudentUserId, window);
    }

    public async Task<VideoSessionResultDto> GetVideoSessionAsync(
        int callerUserId, int bookingId, CancellationToken ct = default)
    {
        // Katılım kuralı (taraf + Approved + pencere) çizim tahtasıyla (#98) ortak: GetLiveSessionAccessAsync.
        var access = await GetLiveSessionAccessAsync(callerUserId, bookingId, ct);
        var options = _videoOptions.Value;

        switch (access.Denial)
        {
            case BookingLiveSessionDenial.NotFound:
                return VideoFail(notFound: true, message: _localizer["booking.video.bookingNotFound"]);
            case BookingLiveSessionDenial.NotParticipant:
                return VideoFail(forbidden: true, message: _localizer["booking.video.notParticipant"]);
            case BookingLiveSessionDenial.NotApproved:
                return VideoFail(conflict: true, message: _localizer["booking.video.notApproved"]);
            case BookingLiveSessionDenial.TeacherUnavailable:
                // issue #298: askıdaki öğretmenin randevusunda iki taraf da oda token'ı alamaz. 409 + makine okunur kod.
                return VideoFail(conflict: true, message: _localizer["booking.video.teacherUnavailable"],
                    errorCode: TeacherAccessErrorCodes.TeacherUnavailable);
            case BookingLiveSessionDenial.WindowNotOpen:
                return VideoFail(conflict: true, message:
                    _localizer["booking.video.windowNotOpen", options.JoinWindowBeforeMinutes]);
            case BookingLiveSessionDenial.WindowClosed:
                return VideoFail(conflict: true, message:
                    _localizer["booking.video.windowClosed", options.JoinWindowAfterMinutes]);
        }

        var isTeacher = access.IsTeacher;
        var participantUserId = isTeacher ? access.TeacherUserId : access.StudentUserId;
        var names = await ResolveUserNamesAsync(new[] { participantUserId }, ct);
        var displayName = names.TryGetValue(participantUserId, out var resolved) && !string.IsNullOrWhiteSpace(resolved)
            ? resolved
            : _localizer[isTeacher ? "booking.video.participantTeacher" : "booking.video.participantStudent"];

        VideoSessionDto session;
        try
        {
            session = await _videoSessionProvider.CreateOrJoinSessionAsync(
                new VideoSessionRequest(
                    BookingId: access.BookingId,
                    ParticipantUserId: participantUserId,
                    ParticipantDisplayName: displayName,
                    ParticipantRole: isTeacher ? VideoParticipantRoles.Teacher : VideoParticipantRoles.Student,
                    StartUtc: access.Window.StartUtc,
                    EndUtc: access.Window.EndUtc,
                    WindowClosesAtUtc: access.Window.ClosesAtUtc),
                ct);
        }
        catch (InvalidOperationException ex)
        {
            // Sağlayıcı yapılandırması eksik/hatalı (secret yok, prod'da dev secret vb.).
            // İstemciye stack trace sızdırmak yerine anlaşılır bir çakışma mesajı döneriz.
            _logger.LogError(ex,
                "Video sağlayıcısı yapılandırılmamış; görüşme odası üretilemedi. BookingId={BookingId}", access.BookingId);
            return VideoFail(conflict: true, message:
                _localizer["booking.video.providerUnavailable"]);
        }

        return new VideoSessionResultDto
        {
            Success = true,
            ObjectId = access.BookingId,
            Session = session
        };
    }

    private static VideoSessionResultDto VideoFail(
        string message, bool notFound = false, bool forbidden = false, bool conflict = false, string? errorCode = null)
        => new()
        {
            Success = false,
            NotFound = notFound,
            Forbidden = forbidden,
            Conflict = conflict,
            Message = message,
            ErrorCode = errorCode
        };

    // ------------------------------------------------------------------
    // Ortak yardımcılar
    // ------------------------------------------------------------------

    /// <summary>issue #418: okula bağlı öğretmen randevu/müsaitlik uçlarında 403 mesajı (tekrarlayan kural servisiyle ortak).</summary>
    internal const string TeacherNotIndependentKey = "booking.teacherNotIndependent";

    private sealed record BookingTeacher(int Id, bool IsIndependent);

    /// <summary>Öğretmen-tarafı uçların kayıt çözümü: kimlik + bağımsızlık (issue #418 kapısı, <see cref="TeacherIndependence"/>).</summary>
    private async Task<BookingTeacher?> FindTeacherAsync(int teacherUserId, CancellationToken ct)
    {
        var row = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => new { t.Id, t.IsIndependentTutor, t.SchoolId })
            .FirstOrDefaultAsync(ct);
        return row == null ? null : new BookingTeacher(row.Id, TeacherIndependence.IsIndependent(row.IsIndependentTutor, row.SchoolId));
    }

    private async Task<BookingListResultDto> QueryBookingsAsync(
        System.Linq.Expressions.Expression<Func<Booking, bool>> predicate, int skip, int take, CancellationToken ct)
    {
        // issue #376: slotu silinmiş randevu listeden düşmez (öğretmen "Randevu Talepleri", öğrenci "Randevularım").
        var rows = await _context.Bookings
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Where(predicate)
            .OrderByDescending(b => b.AvailabilitySlot.Date)
            .ThenByDescending(b => b.AvailabilitySlot.StartTime)
            .Skip(Normalize(skip))
            .Take(Normalize(take, isTake: true))
            .Select(b => new BookingRow
            {
                Id = b.Id,
                TeacherId = b.TeacherId,
                TeacherUserId = b.Teacher.UserId,
                StudentId = b.StudentId,
                StudentUserId = b.Student.UserId,
                AvailabilitySlotId = b.AvailabilitySlotId,
                Date = b.AvailabilitySlot.Date,
                StartTime = b.AvailabilitySlot.StartTime,
                EndTime = b.AvailabilitySlot.EndTime,
                Status = b.Status,
                CreatedAt = b.CreatedAt,
                DecisionAt = b.DecisionAt,
                RejectionReason = b.RejectionReason
            })
            .ToListAsync(ct);

        var names = await ResolveUserNamesAsync(
            rows.SelectMany(r => new[] { r.TeacherUserId, r.StudentUserId }), ct);

        return new BookingListResultDto
        {
            Success = true,
            Items = rows.Select(r =>
            {
                var range = SlotTimeRange.From(r.Date, r.StartTime, r.EndTime);
                return new BookingDto
                {
                    Id = r.Id,
                    TeacherId = r.TeacherId,
                    TeacherName = names.TryGetValue(r.TeacherUserId, out var tn) ? tn : null,
                    StudentId = r.StudentId,
                    StudentName = names.TryGetValue(r.StudentUserId, out var sn) ? sn : null,
                    AvailabilitySlotId = r.AvailabilitySlotId,
                    Date = r.Date,
                    StartTime = r.StartTime,
                    EndTime = r.EndTime,
                    StartUtc = range.StartUtc,
                    EndUtc = range.EndUtc,
                    Status = r.Status.ToString(),
                    CreatedAt = r.CreatedAt,
                    DecisionAt = r.DecisionAt,
                    RejectionReason = r.RejectionReason
                };
            }).ToList()
        };
    }

    /// <summary>
    /// Slot/booking tarih-saatini UTC DateTime'a çevirir (duvar saati UTC kabul edilir). Yalnız BAŞLANGIÇ için;
    /// bitiş gün aşabilir → aralık için <see cref="SlotTimeRange.From"/> (issue #300).
    /// </summary>
    internal static DateTime ToUtc(DateOnly date, TimeOnly time) => SlotTimeRange.ToUtc(date, time);

    private static int Normalize(int value, bool isTake = false)
    {
        if (!isTake)
            return Math.Max(value, 0);

        return value <= 0 ? DefaultTake : Math.Min(value, MaxTake);
    }

    private static AvailabilitySlotDto MapSlot(
        int id, int teacherId, DateOnly date, TimeOnly start, TimeOnly end, DateTime createdAt,
        int? bookingId, BookingStatus? bookingStatus, string? studentName, int? recurringRuleId)
    {
        var range = SlotTimeRange.From(date, start, end);
        return new AvailabilitySlotDto
        {
            Id = id,
            TeacherId = teacherId,
            Date = date,
            StartTime = start,
            EndTime = end,
            CreatedAt = createdAt,
            StartUtc = range.StartUtc,
            EndUtc = range.EndUtc,
            IsBooked = bookingId.HasValue,
            BookingId = bookingId,
            BookingStatus = bookingStatus?.ToString(),
            StudentName = studentName,
            RecurringAvailabilityRuleId = recurringRuleId
        };
    }

    /// <summary>
    /// Süre aşımı mesajı: bitiş başlangıçtan önceyse ertesi gün sayıldığı açıkça söylenir (kullanıcı 15:00–14:00'ı
    /// "1 saat" sanmış olabilir; code review D4).
    /// </summary>
    internal static string TooLongMessageKey(TimeOnly start, TimeOnly end)
        => SlotTimeRange.CrossesMidnight(start, end) ? "booking.slot.tooLongNextDay" : "booking.slot.tooLong";

    /// <summary>
    /// issue #323: execution strategy retry'ında önceki denemenin (commit edilmemiş, geri alınmış) slot satırını
    /// change tracker'dan ayırır ve DB'nin verdiği kimliği sıfırlar — sonraki deneme aynı nesneyi yeniden INSERT eder.
    /// </summary>
    private void ResetForRetry(TeacherAvailabilitySlot slot)
    {
        if (_context.Entry(slot).State != EntityState.Detached)
            _context.Entry(slot).State = EntityState.Detached;
        slot.Id = 0;
    }

    /// <summary>issue #323: öğretmen kilidi zaman aşımı → 409 + "tekrar deneyin" (yeniden denenebilir çakışma).</summary>
    private AvailabilitySlotResultDto SlotBusy() => new()
    {
        Success = false,
        Conflict = true,
        Message = _localizer["booking.slot.busy"]
    };

    private static AvailabilitySlotResultDto SlotFail(string message)
        => new() { Success = false, Message = message };

    /// <summary>
    /// UserId → FullName çözümü (WorksheetCalendarService ile aynı best-effort desen).
    /// auth-api erişilemezse boş sözlük döner; isim alanları null kalır ama liste yine döner.
    /// </summary>
    private async Task<Dictionary<int, string>> ResolveUserNamesAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, string>();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(ids, ct);
            return users
                .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Randevu listesi için auth-api isim çözümü başarısız; isimler boş dönecek.");
            return new Dictionary<int, string>();
        }
    }

    private sealed class SlotRow
    {
        public int Id { get; init; }
        public int TeacherId { get; init; }
        public DateOnly Date { get; init; }
        public TimeOnly StartTime { get; init; }
        public TimeOnly EndTime { get; init; }
        public DateTime CreatedAt { get; init; }
        public int? RecurringAvailabilityRuleId { get; init; }
        public int? BookingId { get; init; }
        public BookingStatus? BookingStatus { get; init; }
        public int? StudentUserId { get; init; }
    }

    private sealed class BookingRow
    {
        public int Id { get; init; }
        public int TeacherId { get; init; }
        public int TeacherUserId { get; init; }
        public int StudentId { get; init; }
        public int StudentUserId { get; init; }
        public int AvailabilitySlotId { get; init; }
        public DateOnly Date { get; init; }
        public TimeOnly StartTime { get; init; }
        public TimeOnly EndTime { get; init; }
        public BookingStatus Status { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? DecisionAt { get; init; }
        public string? RejectionReason { get; init; }
    }
}
