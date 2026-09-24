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
/// issue #262: detay erişimi ve rate limit reddi (429) de aynı tabloya yazılır; saklama süresi
/// <see cref="AdminDataAccessLogRetentionJob"/> ile sınırlıdır.
/// </summary>
public class AdminDataAccessAuditService : IAdminDataAccessAuditService
{
    private readonly AppDbContext _context;

    public AdminDataAccessAuditService(AppDbContext context)
    {
        _context = context;
    }

    public Task RecordListAccessAsync(AdminListAccessRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return AddAsync(new AdminDataAccessLog
        {
            ActorKeycloakId = RequireActor(record.ActorKeycloakId),
            Resource = record.Resource,
            SchoolIdFilter = record.SchoolIdFilter,
            UnassignedFilter = record.UnassignedFilter,
            Page = record.Page,
            PageSize = record.PageSize,
            ReturnedCount = record.ReturnedCount,
            TotalCount = record.TotalCount,
            StatusFilter = record.StatusFilter,
            Outcome = AdminDataAccessOutcome.Served
        });
    }

    public Task RecordDetailAccessAsync(AdminDetailAccessRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Outcome is not (AdminDataAccessOutcome.Served or AdminDataAccessOutcome.NotFound))
            throw new ArgumentException("Detail access outcome must be Served or NotFound.", nameof(record));

        var found = record.Outcome == AdminDataAccessOutcome.Served ? 1 : 0;
        return AddAsync(new AdminDataAccessLog
        {
            ActorKeycloakId = RequireActor(record.ActorKeycloakId),
            Resource = record.Resource,
            TargetId = record.TargetId,
            TargetStatus = record.Outcome == AdminDataAccessOutcome.Served ? record.TargetStatus : null,
            Page = 1,
            PageSize = 1,
            ReturnedCount = found,
            TotalCount = found,
            Outcome = record.Outcome
        });
    }

    public Task RecordRateLimitedAsync(AdminRateLimitedAccessRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return AddAsync(new AdminDataAccessLog
        {
            ActorKeycloakId = RequireActor(record.ActorKeycloakId),
            Resource = record.Resource,
            SchoolIdFilter = record.SchoolIdFilter,
            UnassignedFilter = record.UnassignedFilter,
            TargetId = record.TargetId,
            StatusFilter = record.StatusFilter,
            Outcome = AdminDataAccessOutcome.RateLimited
        });
    }

    private static string RequireActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new InvalidOperationException("Admin data access audit requires the actor's Keycloak subject.");
        return actor.Trim();
    }

    private async Task AddAsync(AdminDataAccessLog row)
    {
        row.OccurredAtUtc = DateTime.UtcNow;
        _context.AdminDataAccessLogs.Add(row);

        // İstemci bağlantıyı kesse bile kayıt tamamlansın: veri sorgusu zaten yapıldı, iz kaybolmamalı.
        await _context.SaveChangesAsync(CancellationToken.None);
    }
}
