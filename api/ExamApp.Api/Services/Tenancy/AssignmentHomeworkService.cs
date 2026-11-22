using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.Tenancy;

/// <summary>
/// issue #1283 (#1071 S8): ödev olarak verilen test / etkinlik atamaları (<see cref="WorksheetAssignment.IsHomework"/>). Durum
/// kaydedilmez, oturumdan çözülür: öğretmen tarafında atama ilerleme ekranıyla AYNI sayılar
/// (<see cref="IWorksheetAssignmentService.GetWorksheetAssignmentsForTeacherAsync"/>), öğrenci tarafında veli paneliyle AYNI kova
/// (<see cref="ParentAssignmentScope.Classify"/>: tamamlandı / bekliyor / gecikmiş). Veli gecikme bildirimi mevcut
/// <see cref="ParentHomeworkOverdueSweepJob"/>'dan gelir (teslim tarihli her atama).
/// </summary>
public interface IAssignmentHomeworkService
{
    /// <summary>Öğretmenin verdiği ödev atamaları: teslimi son <see cref="AssignmentHomeworkService.HistoryDays"/> gün içinde ya da ileride.</summary>
    Task<IReadOnlyList<AssignmentHomeworkDto>> ListForTeacherAsync(SchoolScope requester, CancellationToken ct = default);

    /// <summary>Öğrencinin gördüğü (başlamış) ödev atamaları; öğrenci değilse null.</summary>
    Task<IReadOnlyList<StudentAssignmentHomeworkDto>?> ListForStudentAsync(int studentUserId, CancellationToken ct = default);
}

public sealed class AssignmentHomeworkService : IAssignmentHomeworkService
{
    /// <summary>Teslimi bu kadar gün önce geçmiş ödevler listede kalır (öğrenci ve veli panelinin 30 gün penceresiyle aynı).</summary>
    public const int HistoryDays = ParentAssignmentScope.WindowDays;

    /// <summary>Öğretmen listesinde en fazla bu kadar atama (her biri için ilerleme okunur).</summary>
    public const int MaxTeacherRows = 40;

    private readonly AppDbContext _context;
    private readonly IWorksheetAssignmentService _assignments;
    private readonly IAuthApiClient? _authApi;
    private readonly ILogger<AssignmentHomeworkService> _logger;
    private readonly TimeProvider _time;

    public AssignmentHomeworkService(
        AppDbContext context,
        IWorksheetAssignmentService assignments,
        IAuthApiClient? authApi = null,
        ILogger<AssignmentHomeworkService>? logger = null,
        TimeProvider? time = null)
    {
        _context = context;
        _assignments = assignments;
        _authApi = authApi;
        _logger = logger ?? NullLogger<AssignmentHomeworkService>.Instance;
        _time = time ?? TimeProvider.System;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<AssignmentHomeworkDto>> ListForTeacherAsync(SchoolScope requester, CancellationToken ct = default)
    {
        var from = UtcNow.AddDays(-HistoryDays);
        var own = await _context.WorksheetAssignments.AsNoTracking()
            .Where(a => a.IsHomework && a.CreateUserId == requester.UserId && !a.IsDeleted && !a.Worksheet.IsDeleted
                && a.EndAt != null && a.EndAt >= from)
            .OrderBy(a => a.EndAt).ThenBy(a => a.Id)
            .Take(MaxTeacherRows)
            .Select(a => new { a.Id, a.WorksheetId })
            .ToListAsync(ct);
        if (own.Count == 0)
            return [];

        var ids = own.Select(a => a.Id).ToHashSet();
        var rows = new Dictionary<int, AssignmentHomeworkDto>();
        // İlerleme ekranıyla aynı sayılar (öğrenci kapsamı istek sahibinin okulu, #190); worksheet başına bir okuma.
        foreach (var worksheetId in own.Select(a => a.WorksheetId).Distinct())
        {
            var progress = await _assignments.GetWorksheetAssignmentsForTeacherAsync(worksheetId, requester);
            foreach (var a in progress.Assignments.Where(a => ids.Contains(a.AssignmentId)))
            {
                rows[a.AssignmentId] = new AssignmentHomeworkDto
                {
                    AssignmentId = a.AssignmentId,
                    WorksheetId = worksheetId,
                    WorksheetName = progress.WorksheetName,
                    WorksheetKind = progress.WorksheetKind.ToString(),
                    TargetType = a.TargetType,
                    TargetName = a.TargetName,
                    StartAt = a.StartAt,
                    DueAt = a.EndAt!.Value,
                    IsPastDue = a.EndAt <= UtcNow,
                    StudentCount = a.StudentCount,
                    CompletedCount = a.CompletedCount,
                    InProgressCount = a.InProgressCount,
                    NotStartedCount = a.NotStartedCount + a.ScheduledCount,
                    OverdueCount = a.ExpiredCount
                };
            }
        }

        return own.Where(a => rows.ContainsKey(a.Id)).Select(a => rows[a.Id]).ToList();
    }

    public async Task<IReadOnlyList<StudentAssignmentHomeworkDto>?> ListForStudentAsync(int studentUserId, CancellationToken ct = default)
    {
        var student = await _context.Students.AsNoTracking()
            .Where(s => s.UserId == studentUserId)
            .Select(s => new { s.Id, s.GradeId, VerifiedSchoolId = s.SchoolVerifiedAt != null ? s.SchoolId : null })
            .FirstOrDefaultAsync(ct);
        if (student == null)
            return null;

        var now = UtcNow;
        var from = now.AddDays(-HistoryDays);
        var assignments = await _context.WorksheetAssignments.AsNoTracking()
            .Where(WorksheetStudentAccess.AssignmentVisibleTo(student.Id, student.GradeId, student.VerifiedSchoolId))
            .Where(a => a.IsHomework && !a.IsDeleted && !a.Worksheet.IsDeleted && a.StartAt <= now
                && a.EndAt != null && a.EndAt >= from)
            .OrderBy(a => a.EndAt).ThenBy(a => a.Id)
            .Take(ParentAssignmentScope.MaxRows)
            .Select(a => new
            {
                Row = new ParentAssignmentScope.AssignmentRow(a.Id, a.WorksheetId, a.StartAt, a.EndAt, a.CreateUserId),
                WorksheetName = a.Worksheet.Name,
                a.Worksheet.Kind,
                SubjectName = a.Worksheet.Subject != null ? a.Worksheet.Subject.Name : null
            })
            .ToListAsync(ct);
        if (assignments.Count == 0)
            return [];

        var worksheetIds = assignments.Select(a => a.Row.WorksheetId).Distinct().ToList();
        var instances = await _context.TestInstances.AsNoTracking()
            .Where(ti => ti.StudentId == student.Id && worksheetIds.Contains(ti.WorksheetId))
            // issue #609: yeniden çözülen etkinlik tamamlanmış sayılır (veli paneli ve öğretmen takibiyle aynı).
            .Select(ti => new ParentAssignmentScope.InstanceRow(ti.Id, ti.WorksheetId, ti.StartTime,
                ti.Status == WorksheetInstanceStatus.Started && ti.FirstCompletedAt != null ? WorksheetInstanceStatus.Completed : ti.Status,
                ti.EndTime,
                ti.Status == WorksheetInstanceStatus.Started && ti.FirstCompletedAt != null))
            .ToListAsync(ct);

        // Worksheet başına tek satır (aynı test iki kez ödev verilmiş olabilir): veli paneliyle aynı temsilci seçimi.
        var classified = ParentAssignmentScope.Classify(assignments.Select(a => a.Row).ToList(), instances, now).ToList();
        var meta = assignments.ToDictionary(a => a.Row.AssignmentId);
        var teachers = await ParentNotificationSupport.LookupAsync(_authApi,
            classified.Where(c => c.Assignment.CreateUserId.HasValue).Select(c => c.Assignment.CreateUserId!.Value), _logger, ct);

        return classified
            .OrderBy(c => c.Assignment.EndAt).ThenBy(c => c.Assignment.AssignmentId)
            .Select(c =>
            {
                var m = meta[c.Assignment.AssignmentId];
                return new StudentAssignmentHomeworkDto
                {
                    AssignmentId = c.Assignment.AssignmentId,
                    WorksheetId = c.Assignment.WorksheetId,
                    WorksheetName = m.WorksheetName,
                    WorksheetKind = m.Kind.ToString(),
                    SubjectName = m.SubjectName,
                    TeacherName = c.Assignment.CreateUserId is { } uid ? ParentNotificationSupport.Of(teachers, uid).DisplayName : string.Empty,
                    DueAt = c.Assignment.EndAt!.Value,
                    Status = c.Bucket.ToString(),
                    InProgress = c.Bucket == ParentAssignmentBucket.Pending && c.Instance is { Status: WorksheetInstanceStatus.Started },
                    CompletedAt = c.Bucket == ParentAssignmentBucket.Completed ? c.Instance?.EndTime : null
                };
            })
            .ToList();
    }
}
