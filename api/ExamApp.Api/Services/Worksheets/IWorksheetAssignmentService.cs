using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Assigning worksheets to students/grades and reading assignment progress.
/// Split out of the former god-class <c>ExamService</c>.
/// </summary>
public interface IWorksheetAssignmentService
{
    /// <summary>
    /// issue #222: istek sahibinin sunucu tarafında çözülmüş tenant bağlamıyla (<c>GetSchoolScopeAsync</c>) atama.
    /// Unrestricted = admin; bağımsız (okulsuz) öğretmen yalnızca öğrenci bazlı (Approved Booking) atayabilir.
    /// Admin olmayan istek sahibinin okulu öğretmen kaydından doğrulanır (kayıt yok/uyuşmazlık → red).
    /// </summary>
    Task<ResponseBaseDto> AssignWorksheetAsync(
        WorksheetAssignmentRequestDto request, SchoolScope requester, CancellationToken ct = default);

    Task<List<AssignedWorksheetDto>> GetActiveAssignmentsForStudentAsync(StudentProfileDto student);

    /// <summary>issue #190: requester.UserId atamaları yapan öğretmen; öğrenci listesi requester okuluyla sınırlı.</summary>
    Task<TeacherWorksheetAssignmentsDto> GetWorksheetAssignmentsForTeacherAsync(int worksheetId, SchoolScope requester);
}
