using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.AdminUsers;

/// <inheritdoc cref="IAdminUserActionAuditService"/>
public class AdminUserActionAuditService : IAdminUserActionAuditService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AdminUserActionAuditService> _logger;

    public AdminUserActionAuditService(AppDbContext context, ILogger<AdminUserActionAuditService>? logger = null)
    {
        _context = context;
        _logger = logger ?? NullLogger<AdminUserActionAuditService>.Instance;
    }

    public async Task<long> RecordAsync(AdminUserActionRecord record, AdminUserActionOutcome outcome, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.ActorKeycloakId))
            throw new InvalidOperationException("Admin user action audit requires the actor's Keycloak subject.");

        var row = new AdminUserActionLog
        {
            ActorKeycloakId = record.ActorKeycloakId.Trim(),
            Action = record.Action,
            TargetType = record.TargetType,
            TargetId = record.TargetId,
            Outcome = outcome,
            OccurredAtUtc = DateTime.UtcNow,
            FromSchoolId = record.FromSchoolId,
            ToSchoolId = record.ToSchoolId
        };
        _context.AdminUserActionLogs.Add(row);

        // İstemci bağlantıyı kesse bile iz tamamlansın.
        await _context.SaveChangesAsync(CancellationToken.None);
        return row.Id;
    }

    public async Task UpdateOutcomeAsync(long id, AdminUserActionOutcome outcome, CancellationToken ct = default)
    {
        var updated = await _context.AdminUserActionLogs
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Outcome, outcome), CancellationToken.None);
        if (updated != 1)
            throw new InvalidOperationException($"Admin user action audit row {id} not found.");
    }

    public async Task TryRecordAsync(AdminUserActionRecord record, AdminUserActionOutcome outcome)
    {
        try
        {
            await RecordAsync(record, outcome, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AdminUserAction] Audit yazılamadı: {Action} {TargetType}#{TargetId} outcome={Outcome}",
                record.Action, record.TargetType, record.TargetId, outcome);
        }
    }

    public async Task TryUpdateOutcomeAsync(long id, AdminUserActionOutcome outcome)
    {
        try
        {
            await UpdateOutcomeAsync(id, outcome, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AdminUserAction] Audit sonucu güncellenemedi: id={AuditId} outcome={Outcome}", id, outcome);
        }
    }
}
