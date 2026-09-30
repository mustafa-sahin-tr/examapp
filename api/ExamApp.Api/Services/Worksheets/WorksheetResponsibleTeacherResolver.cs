using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Worksheets;

/// <inheritdoc cref="IWorksheetResponsibleTeacherResolver"/>
public class WorksheetResponsibleTeacherResolver : IWorksheetResponsibleTeacherResolver
{
    private readonly AppDbContext _context;

    public WorksheetResponsibleTeacherResolver(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Karar için gereken atama alanları (tam entity yerine projeksiyon).</summary>
    private sealed record AssignmentRow(int Id, int? StudentId, int? GradeId, int? SchoolId, bool IsPlatformWide,
        DateTime StartAt, int? CreateUserId, bool? CommentsEnabledOverride);

    public async Task<ResponsibleTeacher?> ResolveResponsibleTeacherAsync(int worksheetId, int studentUserId, CancellationToken ct = default)
    {
        var result = await ResolveResponsibleTeachersAsync(worksheetId, new[] { studentUserId }, ct);
        return result.TryGetValue(studentUserId, out var teacher) ? teacher : null;
    }

    public async Task<ResponsibleTeacher?> ResolveResponsibleTeacherAsync(ResponsibleTeacherWorksheet worksheet, int studentUserId, CancellationToken ct = default)
    {
        var result = await ResolveResponsibleTeachersAsync(worksheet, new[] { studentUserId }, ct);
        return result.TryGetValue(studentUserId, out var teacher) ? teacher : null;
    }

    public async Task<IReadOnlyDictionary<int, ResponsibleTeacher?>> ResolveResponsibleTeachersAsync(
        int worksheetId, IReadOnlyCollection<int> studentUserIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(studentUserIds);

        // Retire (soft-delete) edilmiş worksheet'in de ilgili öğretmeni çözülür: thread görünür kalır.
        var worksheet = await _context.Worksheets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(w => w.Id == worksheetId)
            .Select(w => new ResponsibleTeacherWorksheet(w.Id, w.CreateUserId, w.SourceWorksheetId))
            .FirstOrDefaultAsync(ct);

        if (worksheet == null)
            return studentUserIds.Distinct().ToDictionary(id => id, _ => (ResponsibleTeacher?)null);

        return await ResolveResponsibleTeachersAsync(worksheet, studentUserIds, ct);
    }

    public async Task<IReadOnlyDictionary<int, ResponsibleTeacher?>> ResolveResponsibleTeachersAsync(
        ResponsibleTeacherWorksheet worksheet, IReadOnlyCollection<int> studentUserIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(worksheet);
        ArgumentNullException.ThrowIfNull(studentUserIds);

        var result = new Dictionary<int, ResponsibleTeacher?>();
        var userIds = studentUserIds.Distinct().ToList();
        if (userIds.Count == 0)
            return result;

        // (2)/(3): aktif atama yoksa worksheet'in kendi sahibi — kopyada kopyalayan, değilse orijinal yaratıcı.
        // Kaynak worksheet'in sahibine hiçbir zaman gidilmez. Legacy (0/null) sahip → öğretmen yok.
        ResponsibleTeacher? fallback = IsRealUser(worksheet.CreateUserId)
            ? new ResponsibleTeacher(worksheet.CreateUserId!.Value,
                worksheet.SourceWorksheetId.HasValue ? ResponsibleTeacherSource.CopyOwner : ResponsibleTeacherSource.Owner,
                null)
            : null;

        var students = await _context.Students
            .AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .Select(s => new { s.Id, s.UserId, s.GradeId, s.SchoolId })
            .ToListAsync(ct);

        // Students.UserId unique değil (legacy); deterministik olsun diye en küçük Id.
        var studentByUserId = students
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).First());

        var activeAssignments = studentByUserId.Count == 0
            ? new List<AssignmentRow>()
            : await ActiveAssignmentsQuery(worksheet.Id).ToListAsync(ct);

        foreach (var userId in userIds)
        {
            var teacher = fallback;
            if (studentByUserId.TryGetValue(userId, out var student) && activeAssignments.Count > 0)
            {
                // Hedef/okul koşulu: WorksheetStudentAccess.AssignmentVisibleTo'nun bellek içi eşdeğeri.
                var relevant = PickRelevant(activeAssignments.Where(a => WorksheetStudentAccess.IsAssignmentVisibleTo(
                    a.StudentId, a.GradeId, a.SchoolId, a.IsPlatformWide, student.Id, student.GradeId, student.SchoolId)));
                if (relevant != null && IsRealUser(relevant.CreateUserId))
                    teacher = new ResponsibleTeacher(relevant.CreateUserId!.Value, ResponsibleTeacherSource.Assignment, relevant.Id);
            }

            result[userId] = teacher;
        }

        return result;
    }

    public async Task<RelevantAssignment?> FindRelevantActiveAssignmentAsync(
        int worksheetId, int studentId, int? gradeId, int? schoolId, CancellationToken ct = default)
    {
        var candidates = await _context.ActiveAssignmentsFor(studentId, gradeId, schoolId, DateTime.UtcNow)
            .AsNoTracking()
            .Where(a => a.WorksheetId == worksheetId)
            .Select(a => new AssignmentRow(a.Id, a.StudentId, a.GradeId, a.SchoolId, a.IsPlatformWide, a.StartAt,
                a.CreateUserId, a.CommentsEnabledOverride))
            .ToListAsync(ct);

        var relevant = PickRelevant(candidates);
        return relevant == null ? null : new RelevantAssignment(relevant.Id, relevant.CreateUserId, relevant.CommentsEnabledOverride);
    }

    private IQueryable<AssignmentRow> ActiveAssignmentsQuery(int worksheetId) =>
        _context.WorksheetAssignments
            .AsNoTracking()
            .Where(a => a.WorksheetId == worksheetId)
            .Where(WorksheetAccess.ActiveAt(DateTime.UtcNow))
            .Select(a => new AssignmentRow(a.Id, a.StudentId, a.GradeId, a.SchoolId, a.IsPlatformWide, a.StartAt,
                a.CreateUserId, a.CommentsEnabledOverride));

    /// <summary>
    /// Birden fazla aktif atama varsa: öğrenci hedefli (daha özel) sınıf hedefliden önce, sonra en yeni StartAt, sonra Id.
    /// Tek yer — hem öğretmen tespiti hem etkin yorum ayarı aynı atamayı görür.
    /// </summary>
    private static AssignmentRow? PickRelevant(IEnumerable<AssignmentRow> activeAssignments) =>
        activeAssignments
            .OrderByDescending(a => a.StudentId.HasValue)
            .ThenByDescending(a => a.StartAt)
            .ThenByDescending(a => a.Id)
            .FirstOrDefault();

    private static bool IsRealUser(int? userId) => userId.HasValue && userId.Value > 0;
}
