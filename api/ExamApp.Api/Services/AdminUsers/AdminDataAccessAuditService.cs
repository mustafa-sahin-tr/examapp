using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// Admin liste erişim kaydını <see cref="AdminDataAccessLog"/> tablosuna satır olarak ekler (issue #246).
/// Yapılandırılmış log yerine DB tablosu: kayıt sorgulanabilir ("şu admin son 30 günde kaç sayfa çekti"),
/// log saklama/örnekleme politikasına bağlı değil ve mevcut <see cref="LoginEvent"/> deseniyle aynı.
/// </summary>
public class AdminDataAccessAuditService : IAdminDataAccessAuditService
{
    private readonly AppDbContext _context;

    public AdminDataAccessAuditService(AppDbContext context)
    {
        _context = context;
    }

    public async Task RecordListAccessAsync(AdminListAccessRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.ActorKeycloakId))
            throw new InvalidOperationException("Admin data access audit requires the actor's Keycloak subject.");

        _context.AdminDataAccessLogs.Add(new AdminDataAccessLog
        {
            ActorKeycloakId = record.ActorKeycloakId.Trim(),
            Resource = record.Resource,
            SchoolIdFilter = record.SchoolIdFilter,
            UnassignedFilter = record.UnassignedFilter,
            Page = record.Page,
            PageSize = record.PageSize,
            ReturnedCount = record.ReturnedCount,
            TotalCount = record.TotalCount,
            OccurredAtUtc = DateTime.UtcNow
        });

        // İstemci bağlantıyı kesse bile kayıt tamamlansın: veri sorgusu zaten yapıldı, iz kaybolmamalı.
        await _context.SaveChangesAsync(CancellationToken.None);
    }

}
