using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.AdminUsers;

/// <inheritdoc cref="IAdminParentAccessAuditService"/>
public sealed class AdminParentAccessAuditService : IAdminParentAccessAuditService
{
    private readonly AppDbContext _context;

    public AdminParentAccessAuditService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AdminParentAccessAuditListResult> ListAsync(
        AdminParentAccessAuditQuery query, int page, int pageSize, CancellationToken ct = default)
    {
        var from = UtcDateTimes.AsUtc(query.From);
        var to = UtcDateTimes.AsUtc(query.To);
        if (from.HasValue && to.HasValue)
        {
            if (from.Value >= to.Value)
                return new(AdminParentAccessAuditListStatus.InvalidRange, null);
            if (to.Value - from.Value > TimeSpan.FromDays(AdminParentAccessAuditQuery.MaxRangeDays))
                return new(AdminParentAccessAuditListStatus.RangeTooLong, null);
        }

        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);

        // Index'ler: (ParentId, At), (StudentId, At), (At) — her filtre bileşimi birine oturur.
        var rows = _context.ParentAccessAudits.AsNoTracking();
        if (query.ParentId.HasValue)
            rows = rows.Where(a => a.ParentId == query.ParentId.Value);
        if (query.StudentId.HasValue)
            rows = rows.Where(a => a.StudentId == query.StudentId.Value);
        if (from.HasValue)
            rows = rows.Where(a => a.At >= from.Value);
        if (to.HasValue)
            rows = rows.Where(a => a.At < to.Value);

        var totalCount = await rows.CountAsync(ct);
        if (!AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset))
        {
            return new(AdminParentAccessAuditListStatus.Ok,
                AdminListPaging.EmptyPage<AdminParentAccessAuditItemDto>(page, pageSize, totalCount));
        }

        var items = await rows
            .OrderByDescending(a => a.At)
            .ThenByDescending(a => a.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(a => new AdminParentAccessAuditItemDto
            {
                Id = a.Id,
                ParentId = a.ParentId,
                StudentId = a.StudentId,
                Endpoint = a.Endpoint,
                ResourceId = a.ResourceId,
                At = a.At
            })
            .ToListAsync(ct);

        return new(AdminParentAccessAuditListStatus.Ok, new Paged<AdminParentAccessAuditItemDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = items
        });
    }
}
