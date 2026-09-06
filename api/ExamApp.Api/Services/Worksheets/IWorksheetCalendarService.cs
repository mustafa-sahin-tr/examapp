using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Worksheets;

public interface IWorksheetCalendarService
{
    /// <summary>
    /// Öğrencinin [fromUtc, toUtc) aralığındaki takvim etkinliklerini döner (toUtc hariç / exclusive):
    /// planlanmış hatırlatmalar + atama son teslim tarihleri.
    /// </summary>
    Task<StudentCalendarResponseDto> GetMyCalendarAsync(
        int studentId, int? gradeId, int? schoolId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
