using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Öğrenci takvimi read-model'i: planlanmış hatırlatmalar + atama son teslim tarihleri
/// + aktif çalışma programı sayfa planları (UserProgramStudyPageSchedule).
/// Salt okuma; hiçbir yan etki yok. Aralık [fromUtc, toUtc) — üst sınır exclusive.
/// </summary>
public class WorksheetCalendarService : IWorksheetCalendarService
{
    private const string KindReminder = "reminder";
    private const string KindAssignmentDeadline = "assignment-deadline";
    private const string KindProgramStudyItem = "program-study-page";
    private const string KindBooking = "booking";

    /// <summary>Sent hatırlatmalar için bu tarihten eskiler takvimde gösterilmez.</summary>
    private const int SentReminderLookbackDays = 30;

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;

    public WorksheetCalendarService(AppDbContext context, IAuthApiClient authApiClient)
    {
        _context = context;
        _authApiClient = authApiClient;
    }

    public async Task<StudentCalendarResponseDto> GetMyCalendarAsync(
        int studentId, string keycloakUserId, int? gradeId, int? schoolId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (fromUtc.Kind != DateTimeKind.Utc)
            fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        if (toUtc.Kind != DateTimeKind.Utc)
            toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var events = new List<CalendarEventDto>();
        events.AddRange(await BuildReminderEventsAsync(studentId, fromUtc, toUtc, ct));
        events.AddRange(await BuildAssignmentDeadlineEventsAsync(studentId, gradeId, schoolId, fromUtc, toUtc, ct));
        events.AddRange(await BuildProgramStudyItemEventsAsync(keycloakUserId, fromUtc, toUtc, ct));
        events.AddRange(await BuildBookingEventsAsync(b => b.StudentId == studentId, fromUtc, toUtc, isTeacherView: false, ct));

        return new StudentCalendarResponseDto
        {
            Events = events.OrderBy(e => e.Date).ToList()
        };
    }

    /// <summary>
    /// Öğretmenin takvimi (issue #96). Worksheet/reminder/program etkinlikleri öğrenciye özgü olduğu için
    /// burada yalnızca onaylanmış randevular döner.
    /// </summary>
    public async Task<StudentCalendarResponseDto> GetTeacherCalendarAsync(
        int teacherUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (fromUtc.Kind != DateTimeKind.Utc)
            fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        if (toUtc.Kind != DateTimeKind.Utc)
            toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var teacherId = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == teacherUserId)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (teacherId == null)
            return new StudentCalendarResponseDto();

        var events = await BuildBookingEventsAsync(b => b.TeacherId == teacherId.Value, fromUtc, toUtc, isTeacherView: true, ct);

        return new StudentCalendarResponseDto
        {
            Events = events.OrderBy(e => e.Date).ToList()
        };
    }

    /// <summary>
    /// Onaylanmış (Approved) randevular → takvim etkinliği (issue #96). Slot tarih/saatleri saat dilimsiz
    /// duvar saati olduğu için UTC kabul edilerek [fromUtc, toUtc) aralığıyla karşılaştırılır.
    /// Aralık filtresi bellekte uygulanır: DateOnly+TimeOnly birleşimi SQL'e çevrilemiyor, bu yüzden
    /// önce gün bazında (Date) kabaca daraltılır.
    /// <paramref name="isTeacherView"/> başlığın kimin adıyla kurulacağını belirler: öğretmen kendi
    /// takviminde karşı tarafı (öğrenciyi), öğrenci ise öğretmeni görmeli.
    /// </summary>
    private async Task<List<CalendarEventDto>> BuildBookingEventsAsync(
        Expression<Func<Booking, bool>> ownerPredicate, DateTime fromUtc, DateTime toUtc, bool isTeacherView, CancellationToken ct)
    {
        var fromDate = DateOnly.FromDateTime(fromUtc);
        var toDate = DateOnly.FromDateTime(toUtc);

        var rows = await _context.Bookings
            .AsNoTracking()
            .Where(ownerPredicate)
            .Where(b => b.Status == BookingStatus.Approved
                && b.AvailabilitySlot.Date >= fromDate
                && b.AvailabilitySlot.Date <= toDate)
            .Select(b => new
            {
                BookingId = b.Id,
                b.TeacherId,
                b.StudentId,
                b.AvailabilitySlotId,
                TeacherUserId = b.Teacher.UserId,
                StudentUserId = b.Student.UserId,
                Date = b.AvailabilitySlot.Date,
                Start = b.AvailabilitySlot.StartTime,
                End = b.AvailabilitySlot.EndTime
            })
            .ToListAsync(ct);

        var inRange = rows
            .Select(r => new
            {
                r.BookingId,
                r.TeacherId,
                r.StudentId,
                r.AvailabilitySlotId,
                r.TeacherUserId,
                r.StudentUserId,
                StartUtc = DateTime.SpecifyKind(r.Date.ToDateTime(r.Start), DateTimeKind.Utc),
                EndUtc = DateTime.SpecifyKind(r.Date.ToDateTime(r.End), DateTimeKind.Utc)
            })
            .Where(r => r.StartUtc >= fromUtc && r.StartUtc < toUtc)
            .ToList();

        if (inRange.Count == 0)
            return new List<CalendarEventDto>();

        var names = await ResolveTeacherNamesAsync(
            inRange.SelectMany(r => new[] { r.TeacherUserId, r.StudentUserId })
                .Where(id => id > 0).Distinct().ToList(), ct);

        return inRange.Select(r =>
        {
            var teacherName = names.TryGetValue(r.TeacherUserId, out var tn) ? tn : null;
            var studentName = names.TryGetValue(r.StudentUserId, out var sn) ? sn : null;
            var counterpartName = isTeacherView ? studentName : teacherName;

            return new CalendarEventDto
            {
                Kind = KindBooking,
                Date = r.StartUtc,
                EndDate = r.EndUtc,
                BookingId = r.BookingId,
                AvailabilitySlotId = r.AvailabilitySlotId,
                TeacherId = r.TeacherId,
                StudentId = r.StudentId,
                TeacherName = teacherName,
                StudentName = studentName,
                WorksheetTitle = string.IsNullOrWhiteSpace(counterpartName)
                    ? "Ders randevusu"
                    : $"{counterpartName} ile ders"
            };
        }).ToList();
    }

    private async Task<List<CalendarEventDto>> BuildReminderEventsAsync(
        int studentId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var sentFloor = DateTime.UtcNow.AddDays(-SentReminderLookbackDays);

        var rows = await _context.WorksheetReminders
            .AsNoTracking()
            .Where(r => r.StudentId == studentId
                && r.ScheduledFor >= fromUtc
                && r.ScheduledFor < toUtc
                && (r.Status == WorksheetReminderStatus.Pending
                    || (r.Status == WorksheetReminderStatus.Sent && r.ScheduledFor >= sentFloor)))
            .Select(r => new
            {
                r.WorksheetId,
                r.ScheduledFor,
                r.RemindBeforeMinutes,
                r.Status,
                Title = r.Worksheet.Name,
                Subject = r.Worksheet.Subject != null ? r.Worksheet.Subject.Name : null,
                r.Worksheet.ImageUrl
            })
            .ToListAsync(ct);

        return rows.Select(r => new CalendarEventDto
        {
            Kind = KindReminder,
            Date = DateTime.SpecifyKind(r.ScheduledFor, DateTimeKind.Utc),
            WorksheetId = r.WorksheetId,
            WorksheetTitle = r.Title,
            Subject = r.Subject,
            ImageUrl = r.ImageUrl,
            Status = r.Status.ToString(),
            RemindBeforeMinutes = r.RemindBeforeMinutes
        }).ToList();
    }

    private async Task<List<CalendarEventDto>> BuildAssignmentDeadlineEventsAsync(
        int studentId, int? gradeId, int? schoolId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var rows = await _context.WorksheetAssignments
            .AsNoTracking()
            .Where(a => a.EndAt != null && a.EndAt >= fromUtc && a.EndAt < toUtc)
            .Where(WorksheetStudentAccess.AssignmentVisibleTo(studentId, gradeId, schoolId))
            .Select(a => new
            {
                a.WorksheetId,
                EndAt = a.EndAt!.Value,
                a.CreateUserId,
                Title = a.Worksheet.Name,
                Subject = a.Worksheet.Subject != null ? a.Worksheet.Subject.Name : null,
                a.Worksheet.ImageUrl
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new List<CalendarEventDto>();

        // Aynı worksheet hem StudentId hem GradeId ile atanmış olabilir → tek event, en erken EndAt.
        var deduped = rows
            .GroupBy(r => r.WorksheetId)
            .Select(g => g.OrderBy(r => r.EndAt).First())
            .ToList();

        var worksheetIds = deduped.Select(r => r.WorksheetId).ToList();
        var completedWorksheetIds = (await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ti.StudentId == studentId
                && ti.Status == WorksheetInstanceStatus.Completed
                && worksheetIds.Contains(ti.WorksheetId))
            .Select(ti => ti.WorksheetId)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        var teacherNames = await ResolveTeacherNamesAsync(
            deduped.Where(r => r.CreateUserId is > 0).Select(r => r.CreateUserId!.Value).Distinct().ToList(), ct);

        return deduped.Select(r => new CalendarEventDto
        {
            Kind = KindAssignmentDeadline,
            Date = DateTime.SpecifyKind(r.EndAt, DateTimeKind.Utc),
            WorksheetId = r.WorksheetId,
            WorksheetTitle = r.Title,
            Subject = r.Subject,
            ImageUrl = r.ImageUrl,
            IsCompleted = completedWorksheetIds.Contains(r.WorksheetId),
            TeacherName = r.CreateUserId is > 0 && teacherNames.TryGetValue(r.CreateUserId.Value, out var name)
                ? name
                : null
        }).ToList();
    }

    /// <summary>
    /// Öğrencinin aktif (IsActive) çalışma programlarındaki sayfa planları. Planlar çok günlü olabildiği için
    /// tek nokta değil aralık kesişimi uygulanır: StartDate &lt; toUtc &amp;&amp; EndDate &gt;= fromUtc.
    /// Legacy günlük UserProgramSchedule kapsam dışıdır. UserProgram.UserId Keycloak sub tuttuğu için
    /// filtre <paramref name="keycloakUserId"/> ile yapılır — başka öğrencinin programı sızmaz.
    /// </summary>
    private async Task<List<CalendarEventDto>> BuildProgramStudyItemEventsAsync(
        string keycloakUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keycloakUserId))
            return new List<CalendarEventDto>();

        var rows = await _context.UserProgramStudyPageSchedules
            .AsNoTracking()
            .Where(s => s.UserProgram.UserId == keycloakUserId
                && s.UserProgram.IsActive
                && s.StartDate < toUtc
                && s.EndDate >= fromUtc)
            .Select(s => new
            {
                ProgramId = s.UserProgramId,
                s.UserProgram.ProgramName,
                s.StudyItemId,
                StudyItemTitle = s.StudyItem.Title,
                s.StartDate,
                s.EndDate,
                s.IsCompleted
            })
            .ToListAsync(ct);

        return rows.Select(r => new CalendarEventDto
        {
            Kind = KindProgramStudyItem,
            Date = DateTime.SpecifyKind(r.StartDate, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(r.EndDate, DateTimeKind.Utc),
            ProgramId = r.ProgramId,
            ProgramName = r.ProgramName,
            StudyItemId = r.StudyItemId,
            StudyItemTitle = r.StudyItemTitle,
            IsCompleted = r.IsCompleted
        }).ToList();
    }

    /// <summary>
    /// CreateUserId'leri tek batch çağrıyla isme çevirir (WorksheetDetailService ile aynı desen).
    /// Auth-api erişilemezse boş sözlük döner — takvim yine de dönmeli.
    /// </summary>
    private async Task<Dictionary<int, string>> ResolveTeacherNamesAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<int, string>();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, ct);
            return users
                .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Dictionary<int, string>();
        }
    }
}
