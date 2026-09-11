using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Worksheets;

public interface IWorksheetCalendarService
{
    /// <summary>
    /// Öğrencinin [fromUtc, toUtc) aralığındaki takvim etkinliklerini döner (toUtc hariç / exclusive):
    /// planlanmış hatırlatmalar + atama son teslim tarihleri + aktif çalışma programı sayfa planları.
    /// <paramref name="keycloakUserId"/> program planları için gerekir (UserProgram.UserId Keycloak sub tutar).
    /// </summary>
    Task<StudentCalendarResponseDto> GetMyCalendarAsync(
        int studentId, string keycloakUserId, int? gradeId, int? schoolId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>
    /// Öğretmenin [fromUtc, toUtc) aralığındaki takvim etkinlikleri (issue #96): şu an yalnızca
    /// onaylanmış (Approved) randevular. <paramref name="teacherUserId"/> auth/exam user id'sidir;
    /// Teacher kaydı servis içinde çözülür — öğretmen kaydı yoksa boş liste döner.
    /// </summary>
    Task<StudentCalendarResponseDto> GetTeacherCalendarAsync(
        int teacherUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
