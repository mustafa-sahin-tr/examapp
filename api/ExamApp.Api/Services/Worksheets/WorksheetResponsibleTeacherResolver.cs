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

        var students = await _context.Students
            .AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .Select(s => new { s.Id, s.UserId, s.GradeId, SchoolId = s.SchoolVerifiedAt != null ? s.SchoolId : null }) // issue #361: yalnız doğrulanmış okul
            .ToListAsync(ct);

        // issue #326 (D3): #259 unique index kullanıcı başına tek canlı Students satırı garanti eder (OrderBy(Id) gereksiz).
        // Index'siz ortamda çoklu canlı satır → hangi öğrenci olduğu belirsiz → atama eşleşmesi yok (güvenli taraf).
        var studentByUserId = students
            .GroupBy(s => s.UserId)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());

        var activeAssignments = studentByUserId.Count == 0
            ? new List<AssignmentRow>()
            : await ActiveAssignmentsQuery(worksheet.Id).ToListAsync(ct);

        var relevantByUser = new Dictionary<int, RelevantAssignment>();
        foreach (var userId in userIds)
        {
            if (studentByUserId.TryGetValue(userId, out var student) && activeAssignments.Count > 0)
            {
                // Hedef/okul koşulu: WorksheetStudentAccess.AssignmentVisibleTo'nun bellek içi eşdeğeri.
                var row = PickRelevant(activeAssignments.Where(a => WorksheetStudentAccess.IsAssignmentVisibleTo(
                    a.StudentId, a.GradeId, a.SchoolId, a.IsPlatformWide, student.Id, student.GradeId, student.SchoolId)));
                if (row != null)
                    relevantByUser[userId] = new RelevantAssignment(row.Id, row.CreateUserId, row.CommentsEnabledOverride);
            }
        }

        // Okul koşulu için okullar (öğrenciler + sahip + ilgili atamaları yapanlar, #334) TEK kaynaktan, TOPLU (N+1 yok).
        // Ne sahip ne atayan varsa (legacy) karar okul gerektirmez, sorgu atılmaz.
        // Yukarıda okunan tekil Students satırları yeniden sorgulanmaz (kural aynı: öğretmen satırı varsa o esas).
        var assigners = relevantByUser.Values.Select(a => a.CreateUserId).Where(id => id is > 0).ToList();
        var schools = worksheet.CreateUserId is > 0 || assigners.Count > 0
            ? await UserSchoolResolver.ResolveManyDetailedAsync(_context,
                userIds.Select(id => (int?)id).Append(worksheet.CreateUserId).Concat(assigners), ct,
                studentByUserId.ToDictionary(kv => kv.Key, kv => kv.Value.SchoolId))
            : new Dictionary<int, UserSchool>();
        // Kural girdisi: belirsiz okul (çoklu canlı satır) okulsuz sayılmaz (WorksheetCommentPinRule.ForRule, #334).
        var ownerSchoolId = worksheet.CreateUserId is { } owner ? WorksheetCommentPinRule.ForRule(schools.GetValueOrDefault(owner)) : null;

        foreach (var userId in userIds)
        {
            var relevant = relevantByUser.GetValueOrDefault(userId);
            var assignerSchoolId = relevant?.CreateUserId is { } assigner
                ? WorksheetCommentPinRule.ForRule(schools.GetValueOrDefault(assigner))
                : null;
            result[userId] = ResponsibleTeacherRule.Decide(worksheet, relevant, ownerSchoolId,
                WorksheetCommentPinRule.ForRule(schools.GetValueOrDefault(userId)),
                assignerSchoolId);
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
}
