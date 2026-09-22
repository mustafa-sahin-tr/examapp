using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Assigning worksheets to students/grades and reading assignment progress.
/// Split out of the former god-class <c>ExamService</c>.
/// </summary>
public interface IWorksheetAssignmentService
{
    Task<ResponseBaseDto> AssignWorksheetAsync(WorksheetAssignmentRequestDto request, int userId, bool isAdmin = false);

    Task<List<AssignedWorksheetDto>> GetActiveAssignmentsForStudentAsync(StudentProfileDto student);

    /// <summary>issue #190: requester.UserId atamaları yapan öğretmen; öğrenci listesi requester okuluyla sınırlı.</summary>
    Task<TeacherWorksheetAssignmentsDto> GetWorksheetAssignmentsForTeacherAsync(int worksheetId, SchoolScope requester);
}
