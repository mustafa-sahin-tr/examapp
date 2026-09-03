using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Hangfire zamanlanmış job'ı bu metodu çağırır. Gövde outbox event yazımını yapar;
/// asıl bildirim teslimi BadgeService consumer'ında gerçekleşir. İkinci parametre
/// Hangfire'ın serialize edebildiği basit bir imza için ayrılmış, kullanılmıyor.
/// </summary>
public interface IWorksheetReminderDispatcher
{
    Task DispatchAsync(int reminderId, string? _unused, CancellationToken ct = default);
}

/// <summary>
/// Reminder tetiklenince <see cref="WorksheetReminderDueEvent"/>'i outbox'a yazar ve
/// aynı transaction'da reminder'ı <see cref="WorksheetReminderStatus.Sent"/> yapar.
/// Servisten servise senkron çağrı yok — teslim outbox → OutboxPublisher → RabbitMQ → BadgeService.
/// </summary>
public class WorksheetReminderDispatcher : IWorksheetReminderDispatcher
{
    private readonly AppDbContext _context;
    private readonly ILogger<WorksheetReminderDispatcher> _logger;

    public WorksheetReminderDispatcher(AppDbContext context, ILogger<WorksheetReminderDispatcher> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Transient DB hatalarında tekrar denenir. Status kontrolü retry'ı idempotent kılar.
    [AutomaticRetry(Attempts = 3)]
    public async Task DispatchAsync(int reminderId, string? _unused, CancellationToken ct = default)
    {
        var reminder = await _context.WorksheetReminders
            .Include(r => r.Worksheet)
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => r.Id == reminderId, ct);

        if (reminder == null)
        {
            _logger.LogWarning("WorksheetReminder {ReminderId} bulunamadı; dispatch atlanıyor.", reminderId);
            return;
        }

        if (reminder.Status != WorksheetReminderStatus.Pending)
        {
            _logger.LogInformation(
                "WorksheetReminder {ReminderId} durumu {Status}; zaten işlenmiş, no-op.",
                reminderId, reminder.Status);
            return;
        }

        var @event = new WorksheetReminderDueEvent
        {
            ReminderId = reminder.Id,
            WorksheetId = reminder.WorksheetId,
            StudentId = reminder.StudentId,
            UserId = reminder.Student?.UserId ?? 0,
            UserKeycloakId = reminder.StudentKeycloakId ?? string.Empty,
            WorksheetName = reminder.Worksheet?.Name ?? string.Empty,
            ScheduledFor = DateTime.SpecifyKind(reminder.ScheduledFor, DateTimeKind.Utc),
            RemindBeforeMinutes = reminder.RemindBeforeMinutes
        };

        _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<WorksheetReminderDueEvent>(),
            Content = JsonSerializer.Serialize(@event),
            CreatedAt = DateTime.UtcNow
        });

        reminder.Status = WorksheetReminderStatus.Sent;

        // Tek SaveChanges — outbox satırı + status aynı transaction'da.
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "WorksheetReminderDueEvent outbox'a yazıldı. ReminderId={ReminderId}, WorksheetId={WorksheetId}, UserId={UserId}",
            reminder.Id, reminder.WorksheetId, @event.UserId);
    }
}
