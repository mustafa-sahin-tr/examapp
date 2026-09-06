using System;
using System.Collections.Generic;
using System.Linq;
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
/// Öğrenci takvimi read-model'i: planlanmış hatırlatmalar + atama son teslim tarihleri.
/// Salt okuma; hiçbir yan etki yok. Aralık [fromUtc, toUtc) — üst sınır exclusive.
/// </summary>
public class WorksheetCalendarService : IWorksheetCalendarService
{
    private const string KindReminder = "reminder";
    private const string KindAssignmentDeadline = "assignment-deadline";

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
        int studentId, int? gradeId, int? schoolId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (fromUtc.Kind != DateTimeKind.Utc)
            fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        if (toUtc.Kind != DateTimeKind.Utc)
            toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var events = new List<CalendarEventDto>();
        events.AddRange(await BuildReminderEventsAsync(studentId, fromUtc, toUtc, ct));
        events.AddRange(await BuildAssignmentDeadlineEventsAsync(studentId, gradeId, schoolId, fromUtc, toUtc, ct));

        return new StudentCalendarResponseDto
        {
            Events = events.OrderBy(e => e.Date).ToList()
        };
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
