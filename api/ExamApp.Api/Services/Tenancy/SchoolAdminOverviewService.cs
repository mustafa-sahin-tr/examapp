using System.Net.Http;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.Tenancy;

/// <summary>
/// issue #1278 (#1071 S3): okul yöneticisi paneli — kendi okulunun sayıları, öğretmenleri ve öğrencileri (salt okunur).
/// Kapsam çağıranın verdiği <paramref name="schoolId"/> ile sabittir; controller onu <see cref="ISchoolAdminResolver"/>'dan
/// (DB) çözer, istemciden almaz. Onay / ret mevcut uçlardadır (öğretmen başvurusu: school-admin, öğrenci üyeliği:
/// student-school-requests).
/// </summary>
public interface ISchoolAdminOverviewService
{
    Task<SchoolAdminSummaryDto> GetSummaryAsync(int schoolId, int actorUserId, int pendingTeacherApplications,
        CancellationToken ct = default);

    Task<Paged<SchoolAdminTeacherDto>> ListTeachersAsync(int schoolId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="sectionId"/> verilirse yalnız o şubenin aktif üyeleri, <paramref name="unassigned"/> ise şubesizler
    /// (#1279 atama diyaloğu).
    /// </summary>
    Task<Paged<SchoolAdminStudentDto>> ListStudentsAsync(int schoolId, int? gradeId, int page, int pageSize,
        CancellationToken ct = default, int? sectionId = null, bool unassigned = false);
}

public class SchoolAdminOverviewService : ISchoolAdminOverviewService
{
    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApi;
    private readonly ILogger<SchoolAdminOverviewService> _logger;

    public SchoolAdminOverviewService(AppDbContext context, IAuthApiClient authApi,
        ILogger<SchoolAdminOverviewService>? logger = null)
    {
        _context = context;
        _authApi = authApi;
        _logger = logger ?? NullLogger<SchoolAdminOverviewService>.Instance;
    }

    public async Task<SchoolAdminSummaryDto> GetSummaryAsync(int schoolId, int actorUserId, int pendingTeacherApplications,
        CancellationToken ct = default)
    {
        var schoolName = await _context.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        return new SchoolAdminSummaryDto
        {
            SchoolId = schoolId,
            SchoolName = schoolName ?? string.Empty,
            TeacherCount = await Teachers(schoolId).CountAsync(ct),
            StudentCount = await Students(schoolId).CountAsync(ct),
            PendingTeacherApplications = pendingTeacherApplications,
            // StudentSchoolMembershipService.PendingQuery ile aynı kural (onaylayıcının kendi kaydı hariç).
            PendingStudentRequests = await _context.Students.AsNoTracking()
                .CountAsync(s => s.SchoolId == schoolId && s.SchoolVerifiedAt == null && s.UserId != actorUserId, ct)
        };
    }

    public async Task<Paged<SchoolAdminTeacherDto>> ListTeachersAsync(int schoolId, int page, int pageSize,
        CancellationToken ct = default)
    {
        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);
        var query = Teachers(schoolId);
        var totalCount = await query.CountAsync(ct);
        if (!AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset))
            return AdminListPaging.EmptyPage<SchoolAdminTeacherDto>(page, pageSize, totalCount);

        // Yöneticiler önce, sonra onay sırası (ad auth-api'de; DB'de sıralanamaz).
        var rows = await query
            .OrderByDescending(t => t.IsSchoolAdmin)
            .ThenBy(t => t.AccountApprovedAt)
            .ThenBy(t => t.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.UserId,
                t.IsSchoolAdmin,
                Suspended = t.AccountSuspendedAt != null,
                t.AccountApprovedAt,
                Subjects = t.TeacherSubjects.OrderBy(ts => ts.Subject.Name).Select(ts => ts.Subject.Name).ToList()
            })
            .ToListAsync(ct);

        var names = await ResolveNamesAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);
        return new Paged<SchoolAdminTeacherDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(r => new SchoolAdminTeacherDto
            {
                TeacherId = r.Id,
                FullName = names.GetValueOrDefault(r.UserId) ?? string.Empty,
                IsSchoolAdmin = r.IsSchoolAdmin,
                IsSuspended = r.Suspended,
                ApprovedAt = r.AccountApprovedAt,
                Subjects = r.Subjects
            }).ToList()
        };
    }

    public async Task<Paged<SchoolAdminStudentDto>> ListStudentsAsync(int schoolId, int? gradeId, int page, int pageSize,
        CancellationToken ct = default, int? sectionId = null, bool unassigned = false)
    {
        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);
        var query = Students(schoolId);
        if (gradeId is int grade)
            query = query.Where(s => s.GradeId == grade);
        var memberships = _context.SchoolSectionMemberships.Where(m => m.EndedAt == null && m.Section.SchoolId == schoolId);
        if (sectionId is int section)
            query = query.Where(s => memberships.Any(m => m.StudentId == s.Id && m.SectionId == section));
        else if (unassigned)
            query = query.Where(s => !memberships.Any(m => m.StudentId == s.Id));
        var totalCount = await query.CountAsync(ct);
        if (!AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset))
            return AdminListPaging.EmptyPage<SchoolAdminStudentDto>(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(s => s.GradeId)
            .ThenBy(s => s.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.UserId,
                s.StudentNumber,
                s.GradeId,
                GradeName = s.Grade != null ? s.Grade.Name : null,
                s.SchoolVerifiedAt,
                Section = memberships.Where(m => m.StudentId == s.Id)
                    .Select(m => new { m.SectionId, m.Section.Name }).FirstOrDefault()
            })
            .ToListAsync(ct);

        var names = await ResolveNamesAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);
        return new Paged<SchoolAdminStudentDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(r => new SchoolAdminStudentDto
            {
                StudentId = r.Id,
                FullName = names.GetValueOrDefault(r.UserId) ?? string.Empty,
                // #262: liste görünümünde numara kısmi.
                StudentNumber = StudentNumberMask.Apply(r.StudentNumber),
                GradeId = r.GradeId,
                GradeName = r.GradeName,
                VerifiedAt = r.SchoolVerifiedAt,
                SectionId = r.Section?.SectionId,
                SectionName = r.Section?.Name
            }).ToList()
        };
    }

    /// <summary>Okulun öğretmenleri: okula bağlı, hesabı onaylı, bağımsız olmayan (askıdakiler görünür, işaretli).</summary>
    private IQueryable<Teacher> Teachers(int schoolId) =>
        _context.Teachers.AsNoTracking()
            .Where(t => t.SchoolId == schoolId && t.AccountApprovedAt != null && !t.IsIndependentTutor);

    /// <summary>Okulun öğrencileri: üyeliği doğrulanmış (bekleyenler ayrı kuyrukta).</summary>
    private IQueryable<Student> Students(int schoolId) =>
        _context.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.SchoolVerifiedAt != null);

    /// <summary>Ad çözümü tek batch (StudentSchoolMembershipService deseni); auth-api erişilemezse liste adsız döner.</summary>
    private async Task<Dictionary<int, string>> ResolveNamesAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<int, string>();
        try
        {
            var users = await _authApi.GetUsersByIdsAsync(userIds, ct);
            return users.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().FullName ?? string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "[SchoolAdmin] Ad çözümü başarısız ({Count} kullanıcı); liste adsız döner", userIds.Count);
            return new Dictionary<int, string>();
        }
    }
}
