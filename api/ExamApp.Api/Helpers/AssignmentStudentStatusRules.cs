using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Tek atama penceresi için öğrencinin atama durumu (<see cref="AssignmentStudentStatuses"/>) — öğretmenin atama ilerleme
/// ekranı (<c>WorksheetAssignmentService</c>) ve veli özeti (issue #420, <c>ParentDashboardService</c>) aynı kuralı okur.
/// İlgili instance <see cref="AssignmentInstanceWindow.SelectRelevant"/> ile seçilmiş olmalıdır.
/// <list type="bullet">
/// <item>Instance yok: pencere başlamamışsa Scheduled, <c>EndAt</c> geçmişse Expired, aksi halde NotStarted.</item>
/// <item>Completed → Completed; Expired (süre sınırı/sıfırlama, #396) → Expired; Started + EndTime dolu → Completed
/// (legacy); Started → InProgress.</item>
/// </list>
/// </summary>
public static class AssignmentStudentStatusRules
{
    public static string Resolve(
        DateTime assignmentStartAt,
        DateTime? assignmentEndAt,
        WorksheetInstanceStatus? instanceStatus,
        DateTime? instanceEndTime,
        DateTime now)
    {
        if (instanceStatus == null)
        {
            if (assignmentStartAt > now)
                return AssignmentStudentStatuses.Scheduled;

            if (assignmentEndAt.HasValue && assignmentEndAt.Value < now)
                return AssignmentStudentStatuses.Expired;

            return AssignmentStudentStatuses.NotStarted;
        }

        return instanceStatus.Value switch
        {
            WorksheetInstanceStatus.Completed => AssignmentStudentStatuses.Completed,
            WorksheetInstanceStatus.Expired => AssignmentStudentStatuses.Expired,
            WorksheetInstanceStatus.Started => instanceEndTime.HasValue
                ? AssignmentStudentStatuses.Completed
                : AssignmentStudentStatuses.InProgress,
            _ => AssignmentStudentStatuses.NotStarted
        };
    }
}
