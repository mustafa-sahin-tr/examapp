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
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// Ders planlama / randevu iş kuralları (issue #96).
/// <para>
/// Slot tarih-saatleri saat dilimsiz duvar saatidir; geçmiş kontrolü ve takvim dönüşümü bunları
/// UTC kabul eder (<see cref="TeacherAvailabilitySlot"/> notuna bakın).
/// </para>
/// Kapsam dışı: iptal/erteleme, no-show, recurring slot, ödeme.
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
    private const int MaxAdvanceDays = 90;

    /// <summary>Tek bir müsaitlik aralığının azami süresi (saat).</summary>
    private const int MaxSlotDurationHours = 4;

    private static readonly TimeSpan MaxSlotDuration = TimeSpan.FromHours(MaxSlotDurationHours);

    private static readonly BookingStatus[] ActiveStatuses =
        { BookingStatus.Pending, BookingStatus.Approved };

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;
    private readonly ILogger<BookingService> _logger;

    public BookingService(AppDbContext context, IAuthApiClient authApiClient, ILogger<BookingService> logger)
    {
        _context = context;
        _authApiClient = authApiClient;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Müsaitlik slotları (öğretmen)
    // ------------------------------------------------------------------

    public async Task<AvailabilitySlotResultDto> CreateSlotAsync(
        int teacherUserId, CreateAvailabilitySlotDto dto, CancellationToken ct = default)
    {
        if (dto.EndTime <= dto.StartTime)
            return SlotFail("Bitiş saati başlangıç saatinden sonra olmalıdır.");

        if (ToUtc(dto.Date, dto.StartTime) <= DateTime.UtcNow)
            return SlotFail("Geçmiş bir zaman aralığı tanımlanamaz.");

        // Üst sınırlar sunucu tarafında zorunlu (istemci doğrulaması güvenlik sınırı değildir).
        var maxDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(MaxAdvanceDays);
        if (dto.Date > maxDate)
            return SlotFail($"Müsaitlik aralığı en fazla {MaxAdvanceDays} gün sonrası için tanımlanabilir.");

        if (dto.EndTime - dto.StartTime > MaxSlotDuration)
            return SlotFail($"Bir müsaitlik aralığı en fazla {MaxSlotDurationHours} saat sürebilir.");

        var teacher = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => new { t.Id, t.ApprovalStatus })
            .FirstOrDefaultAsync(ct);

        if (teacher == null)
            return new AvailabilitySlotResultDto { Success = false, NotFound = true, Message = "Öğretmen kaydı bulunamadı." };

        // Sadece onaylı öğretmenler randevu alabilir (tutor-search ile aynı kısıt).
        if (teacher.ApprovalStatus != TeacherApprovalStatus.Approved)
            return new AvailabilitySlotResultDto
            {
                Success = false,
                Forbidden = true,
                Message = "Müsaitlik tanımlayabilmek için öğretmen hesabınızın onaylanmış olması gerekir."
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
                Message = "Bu zaman aralığı mevcut bir müsaitlik aralığıyla çakışıyor."
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
                Message = "Bu zaman aralığı zaten tanımlı."
            };
        }

        return new AvailabilitySlotResultDto
        {
            Success = true,
            ObjectId = slot.Id,
            Message = "Müsaitlik aralığı eklendi.",
            Slot = MapSlot(slot.Id, teacher.Id, slot.Date, slot.StartTime, slot.EndTime, slot.CreatedAt, null, null, null)
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
            return new AvailabilitySlotListResultDto { Success = false, NotFound = true, Message = "Öğretmen kaydı bulunamadı." };

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
                r.StudentUserId.HasValue && names.TryGetValue(r.StudentUserId.Value, out var n) ? n : null)).ToList()
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
            return new ResponseBaseDto { Success = false, NotFound = true, Message = "Öğretmen kaydı bulunamadı." };

        var slot = await _context.TeacherAvailabilitySlots
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);

        if (slot == null)
            return new ResponseBaseDto { Success = false, NotFound = true, Message = "Müsaitlik aralığı bulunamadı." };

        // Başkasının slotu: 403 (worksheet sahiplik deseniyle tutarlı).
        if (slot.TeacherId != teacherId.Value)
            return new ResponseBaseDto { Success = false, Forbidden = true, Message = "Bu müsaitlik aralığı size ait değil." };

        var hasActiveBooking = await _context.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.AvailabilitySlotId == slotId && ActiveStatuses.Contains(b.Status), ct);

        if (hasActiveBooking)
            return new ResponseBaseDto
            {
                Success = false,
                Message = "Bekleyen veya onaylanmış randevusu olan bir müsaitlik aralığı silinemez."
            };

        _context.SetCurrentUser(teacherUserId);
        _context.TeacherAvailabilitySlots.Remove(slot); // BaseEntity → soft delete
        await _context.SaveChangesAsync(ct);

        return new ResponseBaseDto { Success = true, ObjectId = slotId, Message = "Müsaitlik aralığı silindi." };
    }

    // ------------------------------------------------------------------
    // Müsaitlik slotları (öğrenci görünümü)
    // ------------------------------------------------------------------

    public async Task<AvailabilitySlotListResultDto> GetTeacherOpenSlotsAsync(
        int teacherId, int skip, int take, CancellationToken ct = default)
    {
        var isApproved = await _context.Teachers
            .AsNoTracking()
            .AnyAsync(t => t.Id == teacherId && t.ApprovalStatus == TeacherApprovalStatus.Approved, ct);

        // Onaysız/olmayan öğretmen ayrımı sızdırılmaz (tutor public-profile ile aynı desen).
        if (!isApproved)
            return new AvailabilitySlotListResultDto { Success = false, NotFound = true, Message = "Öğretmen bulunamadı." };

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

        return new AvailabilitySlotListResultDto
        {
            Success = true,
            Items = rows
                .Select(r => MapSlot(r.Id, r.TeacherId, r.Date, r.StartTime, r.EndTime, r.CreatedAt, null, null, null))
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
            return new BookingResultDto { Success = false, NotFound = true, Message = "Öğrenci kaydı bulunamadı." };

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
            return new BookingResultDto { Success = false, NotFound = true, Message = "Müsaitlik aralığı bulunamadı." };

        if (ToUtc(slot.Date, slot.StartTime) <= DateTime.UtcNow)
            return new BookingResultDto { Success = false, Message = "Geçmiş bir zaman aralığı için randevu oluşturulamaz." };

        var alreadyBooked = await _context.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.AvailabilitySlotId == slot.Id && ActiveStatuses.Contains(b.Status), ct);

        if (alreadyBooked)
            return new BookingResultDto
            {
                Success = false,
                Conflict = true,
                Message = "Bu zaman aralığı için zaten bir randevu talebi var."
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
                Message = "Bu zaman aralığı için zaten bir randevu talebi var."
            };
        }

        var names = await ResolveUserNamesAsync(new[] { slot.TeacherUserId, studentUserId }, ct);

        return new BookingResultDto
        {
            Success = true,
            ObjectId = booking.Id,
            Message = "Randevu talebi oluşturuldu.",
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
            return new BookingListResultDto { Success = false, NotFound = true, Message = "Öğretmen kaydı bulunamadı." };

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
            return new BookingListResultDto { Success = false, NotFound = true, Message = "Öğrenci kaydı bulunamadı." };

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
            return new BookingResultDto { Success = false, NotFound = true, Message = "Öğretmen kaydı bulunamadı." };

        var booking = await _context.Bookings
            .Include(b => b.AvailabilitySlot)
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking == null)
            return new BookingResultDto { Success = false, NotFound = true, Message = "Randevu talebi bulunamadı." };

        if (booking.TeacherId != teacherId.Value)
            return new BookingResultDto { Success = false, Forbidden = true, Message = "Bu randevu talebi size ait değil." };

        if (booking.Status != BookingStatus.Pending)
            return new BookingResultDto
            {
                Success = false,
                Message = $"Bu talep zaten sonuçlandırılmış ({booking.Status})."
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
            Message = newStatus == BookingStatus.Approved ? "Randevu onaylandı." : "Randevu reddedildi.",
            Booking = result.Items.FirstOrDefault()
        };
    }

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
        int? bookingId, BookingStatus? bookingStatus, string? studentName) => new()
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
            StudentName = studentName
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
