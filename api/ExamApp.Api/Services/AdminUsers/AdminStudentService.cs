using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Parents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.AdminUsers;

public class AdminStudentService : IAdminStudentService
{
    private readonly AppDbContext _context;
    private readonly IAdminUserDirectory _userDirectory;
    private readonly IOptionsMonitor<ParentRequirementOptions>? _parentRequirement;
    private readonly TimeProvider _time;

    public AdminStudentService(AppDbContext context, IAdminUserDirectory userDirectory,
        IOptionsMonitor<ParentRequirementOptions>? parentRequirement = null, TimeProvider? time = null)
    {
        _context = context;
        _userDirectory = userDirectory;
        _parentRequirement = parentRequirement;
        _time = time ?? TimeProvider.System;
    }

    public async Task<Paged<AdminStudentListItemDto>> ListAsync(int page, int pageSize, int? schoolId, bool unassigned,
        AdminStudentParentFilter? parentStatus, CancellationToken ct = default)
    {
        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);

        // Soft-deleted öğrenciler global query filter ile hariç.
        var query = _context.Students.AsNoTracking();
        if (parentStatus.HasValue)
            query = ParentStatusQuery(parentStatus.Value);
        if (schoolId.HasValue)
            query = query.Where(s => s.SchoolId == schoolId.Value);
        else if (unassigned)
            query = query.Where(s => s.SchoolId == null);

        var totalCount = await query.CountAsync(ct);

        // Toplamı aşan sayfa boş döner (taşma-güvenli; DB'ye ikinci sorgu ve auth-api çağrısı yok).
        if (!AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset))
            return AdminListPaging.EmptyPage<AdminStudentListItemDto>(page, pageSize, totalCount);

        // Filtre + sayfalama SQL'de; ad/e-posta/hesap durumu yalnızca bu sayfanın kullanıcıları için tek çağrıyla çözülür.
        var rows = await query
            .OrderBy(s => s.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.UserId,
                s.StudentNumber,
                s.SchoolId,
                SchoolPending = s.SchoolId != null && s.SchoolVerifiedAt == null, // issue #361
                SchoolName = s.School != null ? s.School.Name : s.SchoolName,
                s.GradeId,
                GradeName = s.Grade != null ? s.Grade.Name : null
            })
            .ToListAsync(ct);

        IReadOnlyDictionary<int, UserLookupResultDto> users = rows.Count == 0
            ? new Dictionary<int, UserLookupResultDto>()
            : await _userDirectory.ResolveWithAccountStatusAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);

        return new Paged<AdminStudentListItemDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(r =>
            {
                users.TryGetValue(r.UserId, out var user);
                return new AdminStudentListItemDto
                {
                    Id = r.Id,
                    FullName = user?.FullName ?? string.Empty,
                    // issue #246: liste görünümünde tam e-posta dönülmez (KVKK veri minimizasyonu).
                    Email = EmailMask.Apply(user?.Email),
                    // issue #262: öğrenci numarası kısmi (yalnızca son 4 karakter) — tam numara listede dönülmez.
                    StudentNumber = StudentNumberMask.Apply(r.StudentNumber),
                    SchoolId = r.SchoolId,
                    SchoolName = r.SchoolName,
                    SchoolPending = r.SchoolPending,
                    GradeId = r.GradeId,
                    GradeName = r.GradeName,
                    IsEnabled = user?.Enabled
                };
            }).ToList()
        };
    }

    /// <summary>
    /// #917: panodaki veli durumu sayaçlarıyla AYNI küme (<see cref="StudentParentStatusService.QueryByMode"/>);
    /// "velisi olmayan" = süre içinde + süresi dolmuş.
    /// </summary>
    private IQueryable<Student> ParentStatusQuery(AdminStudentParentFilter filter)
    {
        var modes = StudentParentStatusService.QueryByMode(_context, _time.GetUtcNow().UtcDateTime,
            _parentRequirement?.CurrentValue ?? new ParentRequirementOptions());
        return filter switch
        {
            AdminStudentParentFilter.InGrace => modes.Grace,
            AdminStudentParentFilter.GraceExpired => modes.Expired,
            AdminStudentParentFilter.AwaitingActivation => modes.AwaitingActivation,
            _ => modes.Grace.Concat(modes.Expired),
        };
    }
}
