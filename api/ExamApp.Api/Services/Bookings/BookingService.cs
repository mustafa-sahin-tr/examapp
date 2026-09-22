using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Models.Dtos.Video;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
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

    // issue #190: okula bağlı öğretmenin takvimi yalnızca kendi okulundan görünür.
    private readonly ISchoolAccessPolicy _schoolAccessPolicy;

    /// <summary>
    /// Şimdilik yalnızca görüşme katılım penceresi (issue #97) bu saat kaynağını kullanır;
    /// diğer metotlardaki <c>DateTime.UtcNow</c> çağrıları issue #96'dan olduğu gibi bırakıldı.
    /// </summary>
    private readonly TimeProvider _timeProvider;

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
        ISchoolAccessPolicy schoolAccessPolicy,
        IStringLocalizer<Messages>? localizer = null)
    {
        _schoolAccessPolicy = schoolAccessPolicy;
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
        if (dto.EndTime <= dto.StartTime)
            return SlotFail(_localizer["booking.slot.endBeforeStart"]);

        if (ToUtc(dto.Date, dto.StartTime) <= DateTime.UtcNow)
            return SlotFail(_localizer["booking.slot.inPast"]);

        // Üst sınırlar sunucu tarafında zorunlu (istemci doğrulaması güvenlik sınırı değildir).
        var maxDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(MaxAdvanceDays);
        if (dto.Date > maxDate)
            return SlotFail(_localizer["booking.slot.tooFarAhead", MaxAdvanceDays]);

        if (dto.EndTime - dto.StartTime > MaxSlotDuration)
            return SlotFail(_localizer["booking.slot.tooLong", MaxSlotDurationHours]);

        var teacher = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => new { t.Id, t.ApprovalStatus })
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

        // Aynı gün içinde kesişen bir aralık varsa ikinci slot açılmaz — aksi halde öğretmen
        // aynı saate iki ayrı randevu alabilirdi.
        var overlaps = await _context.TeacherAvailabilitySlots
            .AsNoTracking()
            .AnyAsync(s => s.TeacherId == teacher.Id
                && s.Date == dto.Date
                && s.StartTime < dto.EndTime
                && s.EndTime > dto.StartTime, ct);

        if (overlaps)
            return new AvailabilitySlotResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["booking.slot.overlapping"]
            };

        _context.SetCurrentUser(teacherUserId);

        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = teacher.Id,
            Date = dto.Date,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            CreatedAt = DateTime.UtcNow
        };

        _context.TeacherAvailabilitySlots.Add(slot);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Filtreli unique index (TeacherId, Date, StartTime, EndTime) — eşzamanlı istek yarışı.
            _logger.LogWarning(ex, "Müsaitlik aralığı eklenemedi (çakışma). TeacherId={TeacherId}", teacher.Id);
            return new AvailabilitySlotResultDto
            {
                Success = false,
                Conflict = true,
                Message = _localizer["booking.slot.duplicate"]
            };
        }

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
        var teacherId = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (teacherId == null)
            return new AvailabilitySlotListResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        // Tekrarlayan kuralların 90 günlük penceresi burada lazy ileri kaydırılır (issue #178):
        // arka plan job yok; kuralı olmayan öğretmen için maliyet tek indeksli sorgudur.
        await _recurringAvailability.TopUpAsync(teacherId.Value, teacherUserId, ct);

        var rows = await _context.TeacherAvailabilitySlots
            .AsNoTracking()
            .Where(s => s.TeacherId == teacherId.Value)
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

    public async Task<ResponseBaseDto> DeleteSlotAsync(int teacherUserId, int slotId, CancellationToken ct = default)
    {
        var teacherId = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (teacherId == null)
            return new ResponseBaseDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        var slot = await _context.TeacherAvailabilitySlots
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);

        if (slot == null)
            return new ResponseBaseDto { Success = false, NotFound = true, Message = _localizer["booking.slot.notFound"] };

        // Başkasının slotu: 403 (worksheet sahiplik deseniyle tutarlı).
        if (slot.TeacherId != teacherId.Value)
            return new ResponseBaseDto { Success = false, Forbidden = true, Message = _localizer["booking.slot.notOwned"] };

        var hasActiveBooking = await _context.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.AvailabilitySlotId == slotId && ActiveStatuses.Contains(b.Status), ct);

        if (hasActiveBooking)
            return new ResponseBaseDto
            {
                Success = false,
                Message = _localizer["booking.slot.hasActiveBooking"]
            };

        _context.SetCurrentUser(teacherUserId);
        _context.TeacherAvailabilitySlots.Remove(slot); // BaseEntity → soft delete
        await _context.SaveChangesAsync(ct);

        return new ResponseBaseDto { Success = true, ObjectId = slotId, Message = _localizer["booking.slot.deleted"] };
    }

    // ------------------------------------------------------------------
    // Müsaitlik slotları (öğrenci görünümü)
    // ------------------------------------------------------------------

    public async Task<AvailabilitySlotListResultDto> GetTeacherOpenSlotsAsync(
        int teacherId, SchoolScope requester, int skip, int take, CancellationToken ct = default)
    {
        var teacher = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.Id == teacherId && t.ApprovalStatus == TeacherApprovalStatus.Approved)
            .Select(t => new { t.IsIndependentTutor, t.SchoolId })
            .FirstOrDefaultAsync(ct);

        // Onaysız/olmayan öğretmen ayrımı sızdırılmaz (tutor public-profile ile aynı desen).
        // issue #190: tutor pazar yeri istisnası yalnızca bağımsız tutor içindir; okula bağlı
        // öğretmenin takvimi yalnızca aynı okuldan (veya admin/servis) görünür — aksi halde aynı 404.
        if (teacher == null || (!teacher.IsIndependentTutor && !_schoolAccessPolicy.CanAccess(requester, teacher.SchoolId)))
            return new AvailabilitySlotListResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherNotFound"] };

        var now = DateTime.UtcNow;
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
                TeacherUserId = s.Teacher.UserId
            })
            .FirstOrDefaultAsync(ct);

        // Onaysız öğretmenin slotu öğrenciye hiç görünmez → var/yok ayrımı da sızdırılmaz.
        if (slot == null || slot.TeacherApproval != TeacherApprovalStatus.Approved)
            return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.slot.notFound"] };

        if (ToUtc(slot.Date, slot.StartTime) <= DateTime.UtcNow)
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
            CreatedAt = DateTime.UtcNow
        };

        // Booking satırı + outbox mesajı tek transaction'da (WorksheetAccessRequestService ile aynı
        // desen — retry-on-failure execution strategy içinde). booking.Id identity DB'den üretildiği
        // için iki SaveChanges tek transaction ile atomik kılınır; conflict catch tüm bloğu sarar.
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync(ct);

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
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });
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

        var names = await ResolveUserNamesAsync(new[] { slot.TeacherUserId, studentUserId }, ct);

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
                StartUtc = ToUtc(slot.Date, slot.StartTime),
                EndUtc = ToUtc(slot.Date, slot.EndTime),
                Status = booking.Status.ToString(),
                CreatedAt = booking.CreatedAt
            }
        };
    }

    public async Task<BookingListResultDto> GetTeacherBookingsAsync(
        int teacherUserId, int skip, int take, CancellationToken ct = default)
    {
        var teacherId = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (teacherId == null)
            return new BookingListResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        return await QueryBookingsAsync(b => b.TeacherId == teacherId.Value, skip, take, ct);
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
        var teacherId = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (teacherId == null)
            return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.teacherRecordNotFound"] };

        var booking = await _context.Bookings
            .Include(b => b.AvailabilitySlot)
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking == null)
            return new BookingResultDto { Success = false, NotFound = true, Message = _localizer["booking.request.notFound"] };

        if (booking.TeacherId != teacherId.Value)
            return new BookingResultDto { Success = false, Forbidden = true, Message = _localizer["booking.request.notOwned"] };

        if (booking.Status != BookingStatus.Pending)
            return new BookingResultDto
            {
                Success = false,
                Message = _localizer["booking.request.alreadyDecided", booking.Status]
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

        booking.Status = newStatus;
        booking.DecisionAt = DateTime.UtcNow;
        booking.RejectionReason = newStatus == BookingStatus.Rejected && !string.IsNullOrWhiteSpace(rejectionReason)
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
            RejectionReason = booking.RejectionReason,
            Date = booking.AvailabilitySlot.Date,
            StartTime = booking.AvailabilitySlot.StartTime,
            EndTime = booking.AvailabilitySlot.EndTime,
            DecidedAt = booking.DecisionAt.Value
        };
        _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<BookingDecisionEvent>(),
            Content = JsonSerializer.Serialize(decisionEvent),
            CreatedAt = DateTime.UtcNow
        });

        // Tek SaveChanges — status + outbox aynı transaction'da (WorksheetAccessRequestService
        // Approve/Reject ile aynı desen).
        await _context.SaveChangesAsync(ct);

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
    // Görüşme odası (issue #97)
    // ------------------------------------------------------------------

    public async Task<VideoSessionResultDto> GetVideoSessionAsync(
        int callerUserId, int bookingId, CancellationToken ct = default)
    {
        var row = await _context.Bookings
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
                b.AvailabilitySlot.EndTime
            })
            .FirstOrDefaultAsync(ct);

        if (row == null)
            return VideoFail(notFound: true, message: _localizer["booking.video.bookingNotFound"]);

        var isTeacher = row.TeacherUserId == callerUserId;
        var isStudent = row.StudentUserId == callerUserId;

        // Sadece randevunun iki tarafı odaya girebilir — rol attribute'u tek başına yetmez.
        if (!isTeacher && !isStudent)
            return VideoFail(forbidden: true, message: _localizer["booking.video.notParticipant"]);

        if (row.Status != BookingStatus.Approved)
            return VideoFail(conflict: true, message: _localizer["booking.video.notApproved"]);

        var options = _videoOptions.Value;
        var startUtc = ToUtc(row.Date, row.StartTime);
        var endUtc = ToUtc(row.Date, row.EndTime);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var windowOpensAt = startUtc.AddMinutes(-options.JoinWindowBeforeMinutes);
        var windowClosesAt = endUtc.AddMinutes(options.JoinWindowAfterMinutes);

        if (now < windowOpensAt)
            return VideoFail(conflict: true, message:
                _localizer["booking.video.windowNotOpen", options.JoinWindowBeforeMinutes]);

        if (now > windowClosesAt)
            return VideoFail(conflict: true, message:
                _localizer["booking.video.windowClosed", options.JoinWindowAfterMinutes]);

        var participantUserId = isTeacher ? row.TeacherUserId : row.StudentUserId;
        var names = await ResolveUserNamesAsync(new[] { participantUserId }, ct);
        var displayName = names.TryGetValue(participantUserId, out var resolved) && !string.IsNullOrWhiteSpace(resolved)
            ? resolved
            : _localizer[isTeacher ? "booking.video.participantTeacher" : "booking.video.participantStudent"];

        VideoSessionDto session;
        try
        {
            session = await _videoSessionProvider.CreateOrJoinSessionAsync(
                new VideoSessionRequest(
                    BookingId: row.Id,
                    ParticipantUserId: participantUserId,
                    ParticipantDisplayName: displayName,
                    ParticipantRole: isTeacher ? VideoParticipantRoles.Teacher : VideoParticipantRoles.Student,
                    StartUtc: startUtc,
                    EndUtc: endUtc,
                    WindowClosesAtUtc: windowClosesAt),
                ct);
        }
        catch (InvalidOperationException ex)
        {
            // Sağlayıcı yapılandırması eksik/hatalı (secret yok, prod'da dev secret vb.).
            // İstemciye stack trace sızdırmak yerine anlaşılır bir çakışma mesajı döneriz.
            _logger.LogError(ex,
                "Video sağlayıcısı yapılandırılmamış; görüşme odası üretilemedi. BookingId={BookingId}", row.Id);
            return VideoFail(conflict: true, message:
                _localizer["booking.video.providerUnavailable"]);
        }

        return new VideoSessionResultDto
        {
            Success = true,
            ObjectId = row.Id,
            Session = session
        };
    }

    private static VideoSessionResultDto VideoFail(
        string message, bool notFound = false, bool forbidden = false, bool conflict = false)
        => new()
        {
            Success = false,
            NotFound = notFound,
            Forbidden = forbidden,
            Conflict = conflict,
            Message = message
        };

    // ------------------------------------------------------------------
    // Ortak yardımcılar
    // ------------------------------------------------------------------

    private async Task<BookingListResultDto> QueryBookingsAsync(
        System.Linq.Expressions.Expression<Func<Booking, bool>> predicate, int skip, int take, CancellationToken ct)
    {
        var rows = await _context.Bookings
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
            Items = rows.Select(r => new BookingDto
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
                StartUtc = ToUtc(r.Date, r.StartTime),
                EndUtc = ToUtc(r.Date, r.EndTime),
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt,
                DecisionAt = r.DecisionAt,
                RejectionReason = r.RejectionReason
            }).ToList()
        };
    }

    /// <summary>Slot/booking tarih-saatini UTC DateTime'a çevirir (duvar saati UTC kabul edilir).</summary>
    internal static DateTime ToUtc(DateOnly date, TimeOnly time)
        => DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);

    private static int Normalize(int value, bool isTake = false)
    {
        if (!isTake)
            return Math.Max(value, 0);

        return value <= 0 ? DefaultTake : Math.Min(value, MaxTake);
    }

    private static AvailabilitySlotDto MapSlot(
        int id, int teacherId, DateOnly date, TimeOnly start, TimeOnly end, DateTime createdAt,
        int? bookingId, BookingStatus? bookingStatus, string? studentName, int? recurringRuleId) => new()
        {
            Id = id,
            TeacherId = teacherId,
            Date = date,
            StartTime = start,
            EndTime = end,
            CreatedAt = createdAt,
            StartUtc = ToUtc(date, start),
            EndUtc = ToUtc(date, end),
            IsBooked = bookingId.HasValue,
            BookingId = bookingId,
            BookingStatus = bookingStatus?.ToString(),
            StudentName = studentName,
            RecurringAvailabilityRuleId = recurringRuleId
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
