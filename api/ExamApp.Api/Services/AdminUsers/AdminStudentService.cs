using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.AdminUsers;

public class AdminStudentService : IAdminStudentService
{
    private readonly AppDbContext _context;
    private readonly IAdminUserDirectory _userDirectory;

    public AdminStudentService(AppDbContext context, IAdminUserDirectory userDirectory)
    {
        _context = context;
        _userDirectory = userDirectory;
    }

    public async Task<Paged<AdminStudentListItemDto>> ListAsync(int page, int pageSize, int? schoolId, bool unassigned, CancellationToken ct = default)
    {
        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);

        // Soft-deleted öğrenciler global query filter ile hariç.
        var query = _context.Students.AsNoTracking();
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
                    Email = user?.Email ?? string.Empty,
                    StudentNumber = r.StudentNumber ?? string.Empty,
                    SchoolId = r.SchoolId,
                    SchoolName = r.SchoolName,
                    GradeId = r.GradeId,
                    GradeName = r.GradeName,
                    IsEnabled = user?.Enabled
                };
            }).ToList()
        };
    }
}
