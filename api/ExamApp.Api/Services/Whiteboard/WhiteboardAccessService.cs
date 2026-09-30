using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Whiteboard;

/// <summary>Tahtaya katılım kararı. Başarılıysa <see cref="ErrorCode"/> null'dır.</summary>
public sealed record WhiteboardAccessResult(
    string? ErrorCode,
    int BookingId,
    int UserId,
    string Role,
    DateTime WindowClosesAtUtc)
{
    public bool Allowed => ErrorCode is null;

    public static WhiteboardAccessResult Deny(string code, int bookingId) => new(code, bookingId, 0, string.Empty, default);
}

/// <summary>Tahta katılımcı rolleri (<c>PointerUpdated(userRole, ...)</c> değeri).</summary>
public static class WhiteboardRoles
{
    public const string Teacher = "teacher";
    public const string Student = "student";
}

public interface IWhiteboardAccessService
{
    /// <summary>
    /// Çağıran (JWT principal) bu booking'in tahtasına katılabilir mi? Kural: booking'in öğretmeni ya da öğrencisi,
    /// booking Approved, şimdi katılım penceresi içinde (<see cref="IBookingService.GetLiveSessionAccessAsync"/>) VE
    /// çağıran booking'in öğretmeniyse ya da Teacher rolündeyse hesabı onaylı/askıda değil (#287/#289).
    /// </summary>
    Task<WhiteboardAccessResult> AuthorizeAsync(ClaimsPrincipal user, int bookingId, CancellationToken ct = default);

    /// <summary>
    /// Açık tahtalar için toplu geçerlilik kontrolü (temizlik servisi). Artık geçerli OLMAYAN booking id'leri ve kapanış
    /// nedenini döner (<see cref="WhiteboardCloseReasons"/>). Pencere kontrolü burada yapılmaz (store'daki kapanış anı).
    /// </summary>
    Task<IReadOnlyDictionary<int, string>> FindInvalidBoardsAsync(IReadOnlyCollection<int> bookingIds, CancellationToken ct = default);
}

/// <summary>Scoped: <see cref="IBookingService"/>, <see cref="IApprovedTeacherGuard"/> ve DbContext'e bağlı.</summary>
public sealed class WhiteboardAccessService : IWhiteboardAccessService
{
    private readonly IBookingService _bookings;
    private readonly IApprovedTeacherGuard _teacherGuard;
    private readonly IUserProfileProvider _profiles;
    private readonly AppDbContext _context;

    public WhiteboardAccessService(IBookingService bookings, IApprovedTeacherGuard teacherGuard,
        IUserProfileProvider profiles, AppDbContext context)
    {
        _bookings = bookings;
        _teacherGuard = teacherGuard;
        _profiles = profiles;
        _context = context;
    }

    public async Task<WhiteboardAccessResult> AuthorizeAsync(ClaimsPrincipal user, int bookingId, CancellationToken ct = default)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
            return WhiteboardAccessResult.Deny(WhiteboardErrorCodes.UserNotResolved, bookingId);

        // Fail-closed: provider hatası (Redis/auth-api) yukarı fırlar; hub genel hata döner, katılıma izin verilmez.
        var profile = await _profiles.GetAsync(sub, ct);
        if (profile is not { Id: > 0 })
            return WhiteboardAccessResult.Deny(WhiteboardErrorCodes.UserNotResolved, bookingId);

        var access = await _bookings.GetLiveSessionAccessAsync(profile.Id, bookingId, ct);
        var denial = access.Denial switch
        {
            BookingLiveSessionDenial.NotFound => WhiteboardErrorCodes.BookingNotFound,
            BookingLiveSessionDenial.NotParticipant => WhiteboardErrorCodes.NotParticipant,
            BookingLiveSessionDenial.NotApproved => WhiteboardErrorCodes.BookingNotApproved,
            BookingLiveSessionDenial.WindowNotOpen => WhiteboardErrorCodes.WindowNotOpen,
            BookingLiveSessionDenial.WindowClosed => WhiteboardErrorCodes.WindowClosed,
            _ => null
        };
        if (denial is not null)
            return WhiteboardAccessResult.Deny(denial, bookingId);

        // #287/#289: booking'in öğretmeni (rolünden bağımsız) ve Teacher rollü her çağıran onaylı + askıda olmayan
        // öğretmen hesabına sahip olmalı. Policy'den (Admin muafiyeti) daha sıkı: tahtada admin istisnası yok.
        if (access.IsTeacher || user.IsInRole(ApprovedTeacherRequirementRole))
        {
            var check = await _teacherGuard.CheckAsync(profile.Id, ct);
            if (check != TeacherApprovalCheck.Approved)
                return WhiteboardAccessResult.Deny(WhiteboardErrorCodes.TeacherNotApproved, bookingId);
        }

        return new WhiteboardAccessResult(
            null,
            access.BookingId,
            profile.Id,
            access.IsTeacher ? WhiteboardRoles.Teacher : WhiteboardRoles.Student,
            access.Window.ClosesAtUtc);
    }

    public async Task<IReadOnlyDictionary<int, string>> FindInvalidBoardsAsync(
        IReadOnlyCollection<int> bookingIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, string>();
        if (bookingIds.Count == 0)
            return result;

        var ids = bookingIds.ToList();
        // Global soft-delete filtresi silinmiş booking'i zaten dışarıda bırakır → "bulunamadı" = iptal.
        var rows = await _context.Bookings
            .AsNoTracking()
            .Where(b => ids.Contains(b.Id))
            .Select(b => new
            {
                b.Id,
                b.Status,
                b.Teacher.AccountApprovedAt,
                b.Teacher.AccountSuspendedAt
            })
            .ToListAsync(ct);

        var byId = rows.ToDictionary(r => r.Id);
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var row) || row.Status != BookingStatus.Approved)
                result[id] = WhiteboardCloseReasons.BookingCancelled;
            // ApprovedTeacherGuard ile aynı karar: askı önce, sonra onay.
            else if (row.AccountSuspendedAt is not null || row.AccountApprovedAt is null)
                result[id] = WhiteboardCloseReasons.TeacherNotApproved;
        }

        return result;
    }

    private const string ApprovedTeacherRequirementRole = Teachers.Authorization.ApprovedTeacherRequirement.TeacherRole;
}
