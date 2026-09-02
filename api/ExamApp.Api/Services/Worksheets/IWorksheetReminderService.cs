using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Worksheets;

public interface IWorksheetReminderService
{
    Task<WorksheetReminderDto?> GetAsync(int worksheetId, int studentId, CancellationToken ct);

    Task<WorksheetReminderDto> UpsertAsync(int worksheetId, int studentId, string? studentKeycloakId, DateTime scheduledForUtc, int remindBeforeMinutes, CancellationToken ct);

    Task DeleteAsync(int worksheetId, int studentId, CancellationToken ct);
}
