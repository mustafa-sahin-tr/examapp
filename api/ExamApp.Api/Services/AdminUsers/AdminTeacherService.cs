using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.AdminUsers;

public class AdminTeacherService : IAdminTeacherService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly IAdminUserDirectory _userDirectory;

    public AdminTeacherService(AppDbContext context, IAdminUserDirectory userDirectory)
    {
        _context = context;
        _userDirectory = userDirectory;
    }

    public async Task<Paged<AdminTeacherListItemDto>> ListAsync(int page, int pageSize, int? schoolId, bool unassigned, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.Teachers.AsNoTracking();
        if (schoolId.HasValue)
            query = query.Where(t => t.SchoolId == schoolId.Value);
        else if (unassigned)
            query = query.Where(t => t.SchoolId == null);

        var totalCount = await query.CountAsync(ct);

        // (page - 1) * pageSize int'te taşabilir (page=int.MaxValue → negatif Skip → 500). long'da hesapla;
        // toplamı aşan sayfa boş döner (DB'ye ikinci sorgu ve auth-api çağrısı yok). Aksi halde offset < totalCount ≤ int.MaxValue.
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
        {
            return new Paged<AdminTeacherListItemDto>
            {
                PageNumber = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                Items = new List<AdminTeacherListItemDto>()
            };
        }

        // Filtre + sayfalama SQL'de; ad/e-posta/hesap durumu yalnızca bu sayfanın kullanıcıları için tek çağrıyla çözülür.
        var rows = await query
            .OrderBy(t => t.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.UserId,
                t.SchoolId,
                SchoolName = t.School != null ? t.School.Name : t.SchoolName,
                t.IsIndependentTutor,
                t.ApprovalStatus
            })
            .ToListAsync(ct);

        IReadOnlyDictionary<int, UserLookupResultDto> users = rows.Count == 0
            ? new Dictionary<int, UserLookupResultDto>()
            : await _userDirectory.ResolveWithAccountStatusAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);

        return new Paged<AdminTeacherListItemDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(r =>
            {
                users.TryGetValue(r.UserId, out var user);
                return new AdminTeacherListItemDto
                {
                    Id = r.Id,
                    UserId = r.UserId,
                    FullName = user?.FullName ?? string.Empty,
                    Email = user?.Email ?? string.Empty,
                    SchoolId = r.SchoolId,
                    SchoolName = r.SchoolName,
                    IsIndependentTutor = r.IsIndependentTutor,
                    ApprovalStatus = r.ApprovalStatus.ToString(),
                    IsEnabled = user?.Enabled
                };
            }).ToList()
        };
    }
}
