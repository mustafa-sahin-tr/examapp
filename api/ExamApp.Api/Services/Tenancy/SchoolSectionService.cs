using System.Globalization;
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

public enum SchoolSectionStatus
{
    Success,
    /// <summary>Şube / öğrenci bu okulda yok (başka okul dahil; var/yok oracle'ı kapalı).</summary>
    NotFound,
    /// <summary>Ad boş / uzun, sınıf yok, öğrenci listesi boş / çok uzun.</summary>
    Invalid,
    /// <summary>Okul + sınıfta aynı adlı şube var.</summary>
    Duplicate,
    /// <summary>Öğrencinin sınıfı şubenin sınıfından farklı (ya da boş).</summary>
    GradeMismatch,
    /// <summary>Eşzamanlı değişiklik (tek aktif üyelik indeksi).</summary>
    Conflict
}

public sealed record SchoolSectionResult(
    SchoolSectionStatus Status,
    SchoolSectionDto? Section = null,
    SchoolSectionAssignResponseDto? Assignment = null,
    IReadOnlyList<int>? StudentIds = null);

/// <summary>
/// issue #1279 (#1071 S4): okul şubeleri ve öğrenci üyelikleri. Okul kimliği her zaman çağıran controller'ın DB'den çözdüğü
/// okuldur (okul yöneticisi: <see cref="ISchoolAdminResolver"/>, okul öğretmeni: <see cref="ResolveTeacherSchoolIdAsync"/>).
/// Öğrenci yalnız kendi okulunun doğrulanmış üyesiyse ve sınıfı şubenin sınıfıysa atanır; bir öğrencinin en çok bir aktif
/// üyeliği vardır, taşıma eskisini <see cref="SchoolSectionEndReason.Moved"/> ile kapatır. Üyelik satırları silinmez (geçmiş).
/// </summary>
public interface ISchoolSectionService
{
    /// <summary>Okulun onaylı, askısız, bağımsız olmayan öğretmeninin okulu; değilse null (okul yöneticisi de buradan geçer).</summary>
    Task<int?> ResolveTeacherSchoolIdAsync(int userId, CancellationToken ct = default);

    Task<IReadOnlyList<SchoolSectionDto>> ListAsync(int schoolId, int? gradeId, CancellationToken ct = default);

    /// <summary>Şubenin aktif öğrencileri; şube bu okulda değilse null.</summary>
    Task<Paged<SchoolSectionStudentDto>?> ListStudentsAsync(int schoolId, int sectionId, int page, int pageSize,
        CancellationToken ct = default);

    Task<SchoolSectionResult> CreateAsync(int schoolId, int actorUserId, int? gradeId, string? name, CancellationToken ct = default);

    Task<SchoolSectionResult> RenameAsync(int schoolId, int actorUserId, int sectionId, string? name, CancellationToken ct = default);

    Task<SchoolSectionResult> DeleteAsync(int schoolId, int actorUserId, int sectionId, CancellationToken ct = default);

    Task<SchoolSectionResult> AssignAsync(int schoolId, int actorUserId, int sectionId, IReadOnlyCollection<int>? studentIds,
        CancellationToken ct = default);

    Task<SchoolSectionResult> RemoveStudentAsync(int schoolId, int actorUserId, int sectionId, int studentId,
        CancellationToken ct = default);
}

public class SchoolSectionService : ISchoolSectionService
{
    public const int MaxAssignBatch = 100;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApi;
    private readonly ILogger<SchoolSectionService> _logger;

    public SchoolSectionService(AppDbContext context, IAuthApiClient authApi, ILogger<SchoolSectionService>? logger = null)
    {
        _context = context;
        _authApi = authApi;
        _logger = logger ?? NullLogger<SchoolSectionService>.Instance;
    }

    public async Task<int?> ResolveTeacherSchoolIdAsync(int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            return null;
        // StudentSchoolMembershipService.ResolveApproverScopeAsync ile aynı: tek canlı satır, okullu, onaylı, askısız.
        var rows = await _context.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.SchoolId, t.AccountApprovedAt, t.AccountSuspendedAt, t.IsIndependentTutor })
            .Take(2)
            .ToListAsync(ct);
        if (rows.Count != 1)
            return null;
        var row = rows[0];
        return row.SchoolId is int id && row.AccountApprovedAt != null && row.AccountSuspendedAt == null && !row.IsIndependentTutor
            ? id
            : null;
    }

    public async Task<IReadOnlyList<SchoolSectionDto>> ListAsync(int schoolId, int? gradeId, CancellationToken ct = default)
    {
        var query = _context.SchoolSections.AsNoTracking().Where(s => s.SchoolId == schoolId);
        if (gradeId is int g)
            query = query.Where(s => s.GradeId == g);
        return await query
            .OrderBy(s => s.GradeId)
            .ThenBy(s => s.NormalizedName)
            .Select(s => new SchoolSectionDto
            {
                Id = s.Id,
                GradeId = s.GradeId,
                GradeName = s.Grade.Name,
                Name = s.Name,
                StudentCount = _context.SchoolSectionMemberships.Count(m => m.SectionId == s.Id && m.EndedAt == null)
            })
            .ToListAsync(ct);
    }

    public async Task<Paged<SchoolSectionStudentDto>?> ListStudentsAsync(int schoolId, int sectionId, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!await _context.SchoolSections.AnyAsync(s => s.Id == sectionId && s.SchoolId == schoolId, ct))
            return null;

        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);
        var query = _context.SchoolSectionMemberships.AsNoTracking()
            .Where(m => m.SectionId == sectionId && m.EndedAt == null);
        var totalCount = await query.CountAsync(ct);
        if (!AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset))
            return AdminListPaging.EmptyPage<SchoolSectionStudentDto>(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(m => m.Student.StudentNumber)
            .ThenBy(m => m.StudentId)
            .Skip(offset)
            .Take(pageSize)
            .Select(m => new { m.StudentId, m.Student.UserId, m.Student.StudentNumber, m.StartedAt })
            .ToListAsync(ct);
        var names = await ResolveNamesAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);
        return new Paged<SchoolSectionStudentDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(r => new SchoolSectionStudentDto
            {
                StudentId = r.StudentId,
                FullName = names.GetValueOrDefault(r.UserId) ?? string.Empty,
                StudentNumber = StudentNumberMask.Apply(r.StudentNumber),
                Since = r.StartedAt
            }).ToList()
        };
    }

    public async Task<SchoolSectionResult> CreateAsync(int schoolId, int actorUserId, int? gradeId, string? name,
        CancellationToken ct = default)
    {
        if (!TryNormalize(name, out var trimmed, out var normalized) || gradeId is not int grade
            || !await _context.Grades.AnyAsync(g => g.Id == grade, ct))
            return new(SchoolSectionStatus.Invalid);
        if (await NameTakenAsync(schoolId, grade, normalized, null, ct))
            return new(SchoolSectionStatus.Duplicate);

        var section = new SchoolSection { SchoolId = schoolId, GradeId = grade, Name = trimmed, NormalizedName = normalized };
        _context.SchoolSections.Add(section);
        if (!await TrySaveAsync(ct))
            return new(SchoolSectionStatus.Duplicate);

        _logger.LogInformation("[SchoolSection] Şube oluşturuldu: Section#{SectionId} School#{SchoolId} Grade#{GradeId} actor={Actor}",
            section.Id, schoolId, grade, actorUserId);
        return new(SchoolSectionStatus.Success, await GetDtoAsync(section.Id, ct));
    }

    public async Task<SchoolSectionResult> RenameAsync(int schoolId, int actorUserId, int sectionId, string? name,
        CancellationToken ct = default)
    {
        var section = await _context.SchoolSections.FirstOrDefaultAsync(s => s.Id == sectionId && s.SchoolId == schoolId, ct);
        if (section is null)
            return new(SchoolSectionStatus.NotFound);
        if (!TryNormalize(name, out var trimmed, out var normalized))
            return new(SchoolSectionStatus.Invalid);
        if (await NameTakenAsync(schoolId, section.GradeId, normalized, sectionId, ct))
            return new(SchoolSectionStatus.Duplicate);

        section.Name = trimmed;
        section.NormalizedName = normalized;
        if (!await TrySaveAsync(ct))
            return new(SchoolSectionStatus.Duplicate);
        return new(SchoolSectionStatus.Success, await GetDtoAsync(section.Id, ct));
    }

    public async Task<SchoolSectionResult> DeleteAsync(int schoolId, int actorUserId, int sectionId, CancellationToken ct = default)
    {
        var section = await _context.SchoolSections.FirstOrDefaultAsync(s => s.Id == sectionId && s.SchoolId == schoolId, ct);
        if (section is null)
            return new(SchoolSectionStatus.NotFound);

        var now = DateTime.UtcNow;
        var strategy = _context.Database.CreateExecutionStrategy();
        var ended = 0;
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);
            ended = await EndActive(Memberships().Where(m => m.SectionId == sectionId), actorUserId, now,
                SchoolSectionEndReason.SectionDeleted, CancellationToken.None);
            _context.SchoolSections.Remove(section); // soft delete (SaveChanges hook)
            await _context.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        });
        _logger.LogInformation("[SchoolSection] Şube silindi: Section#{SectionId} School#{SchoolId} ({Ended} üyelik kapandı) actor={Actor}",
            sectionId, schoolId, ended, actorUserId);
        return new(SchoolSectionStatus.Success);
    }

    public async Task<SchoolSectionResult> AssignAsync(int schoolId, int actorUserId, int sectionId,
        IReadOnlyCollection<int>? studentIds, CancellationToken ct = default)
    {
        var ids = studentIds?.Where(id => id > 0).Distinct().ToList() ?? [];
        if (ids.Count == 0 || ids.Count > MaxAssignBatch)
            return new(SchoolSectionStatus.Invalid);

        var section = await _context.SchoolSections.AsNoTracking()
            .Where(s => s.Id == sectionId && s.SchoolId == schoolId)
            .Select(s => new { s.Id, s.GradeId })
            .FirstOrDefaultAsync(ct);
        if (section is null)
            return new(SchoolSectionStatus.NotFound);

        // Okulun doğrulanmış üyesi olmayan (başka okul, bekleyen, yok) öğrenci → hepsi reddedilir (404, oracle kapalı).
        var students = await _context.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.SchoolId == schoolId && s.SchoolVerifiedAt != null)
            .Select(s => new { s.Id, s.GradeId })
            .ToListAsync(ct);
        if (students.Count != ids.Count)
            return new(SchoolSectionStatus.NotFound);
        var wrongGrade = students.Where(s => s.GradeId != section.GradeId).Select(s => s.Id).ToList();
        if (wrongGrade.Count > 0)
            return new(SchoolSectionStatus.GradeMismatch, StudentIds: wrongGrade);

        var already = await _context.SchoolSectionMemberships.AsNoTracking()
            .Where(m => ids.Contains(m.StudentId) && m.SectionId == sectionId && m.EndedAt == null)
            .Select(m => m.StudentId)
            .ToListAsync(ct);
        var toAssign = ids.Except(already).ToList();
        if (toAssign.Count == 0)
            return new(SchoolSectionStatus.Success, Assignment: new() { Assigned = 0, Unchanged = already.Count });

        var now = DateTime.UtcNow;
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);
                // Başka şubedeki aktif üyelik (silinmiş şubeninki dahil — filtre dışı) taşıma olarak kapanır.
                await EndActive(Memberships().Where(m => toAssign.Contains(m.StudentId) && m.SectionId != sectionId),
                    actorUserId, now, SchoolSectionEndReason.Moved, CancellationToken.None);
                _context.SchoolSectionMemberships.AddRange(toAssign.Select(id => new SchoolSectionMembership
                {
                    SectionId = sectionId, StudentId = id, StartedAt = now, StartedByUserId = actorUserId
                }));
                await _context.SaveChangesAsync(CancellationToken.None);
                await tx.CommitAsync(CancellationToken.None);
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "[SchoolSection] Eşzamanlı atama: Section#{SectionId}", sectionId);
            _context.ChangeTracker.Clear();
            return new(SchoolSectionStatus.Conflict);
        }

        _logger.LogInformation("[SchoolSection] {Count} öğrenci Section#{SectionId}'e atandı actor={Actor}", toAssign.Count, sectionId, actorUserId);
        return new(SchoolSectionStatus.Success, Assignment: new() { Assigned = toAssign.Count, Unchanged = already.Count });
    }

    public async Task<SchoolSectionResult> RemoveStudentAsync(int schoolId, int actorUserId, int sectionId, int studentId,
        CancellationToken ct = default)
    {
        var membershipId = await _context.SchoolSectionMemberships.AsNoTracking()
            .Where(m => m.SectionId == sectionId && m.StudentId == studentId && m.EndedAt == null && m.Section.SchoolId == schoolId)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (membershipId is not int id)
            return new(SchoolSectionStatus.NotFound);
        var ended = await EndActive(Memberships().Where(m => m.Id == id), actorUserId, DateTime.UtcNow,
            SchoolSectionEndReason.Removed, ct);
        return new(ended == 0 ? SchoolSectionStatus.NotFound : SchoolSectionStatus.Success);
    }

    /// <summary>
    /// issue #1279: öğrencinin okulu değişince (admin okul ataması / okulsuz yapma) aktif şube üyeliği kapanır; okul yazımıyla
    /// aynı transaction'da çağrılır. Silinmiş şubedeki aktif satır da kapanır (filtre dışı).
    /// </summary>
    public static Task<int> EndForSchoolChangeAsync(AppDbContext context, int studentId, int? actorUserId, DateTime now,
        CancellationToken ct) =>
        EndActive(context.SchoolSectionMemberships.IgnoreQueryFilters().Where(m => m.StudentId == studentId),
            actorUserId, now, SchoolSectionEndReason.SchoolChanged, ct);

    /// <summary>Yazma sorguları için filtresiz küme: global filtre join'i UPDATE'e taşınmasın (SQLite join'li UPDATE desteklemez).</summary>
    private IQueryable<SchoolSectionMembership> Memberships() => _context.SchoolSectionMemberships.IgnoreQueryFilters();

    private static Task<int> EndActive(IQueryable<SchoolSectionMembership> query, int? actorUserId, DateTime now,
        SchoolSectionEndReason reason, CancellationToken ct) =>
        query.Where(m => m.EndedAt == null).ExecuteUpdateAsync(set => set
            .SetProperty(m => m.EndedAt, now)
            .SetProperty(m => m.EndedByUserId, actorUserId)
            .SetProperty(m => m.EndReason, reason), ct);

    private static bool TryNormalize(string? name, out string trimmed, out string normalized)
    {
        trimmed = (name ?? string.Empty).Trim();
        normalized = trimmed.ToUpper(Turkish);
        return trimmed.Length is > 0 and <= SchoolSection.NameMaxLength;
    }

    private Task<bool> NameTakenAsync(int schoolId, int gradeId, string normalized, int? exceptId, CancellationToken ct) =>
        _context.SchoolSections.AnyAsync(s => s.SchoolId == schoolId && s.GradeId == gradeId
            && s.NormalizedName == normalized && s.Id != exceptId, ct);

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı aynı ad: tekil indeks.
            _context.ChangeTracker.Clear();
            return false;
        }
    }

    private Task<SchoolSectionDto?> GetDtoAsync(int sectionId, CancellationToken ct) =>
        _context.SchoolSections.AsNoTracking()
            .Where(s => s.Id == sectionId)
            .Select(s => new SchoolSectionDto
            {
                Id = s.Id,
                GradeId = s.GradeId,
                GradeName = s.Grade.Name,
                Name = s.Name,
                StudentCount = _context.SchoolSectionMemberships.Count(m => m.SectionId == s.Id && m.EndedAt == null)
            })
            .FirstOrDefaultAsync(ct);

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
            _logger.LogWarning(ex, "[SchoolSection] Ad çözümü başarısız ({Count} kullanıcı); liste adsız döner", userIds.Count);
            return new Dictionary<int, string>();
        }
    }
}
