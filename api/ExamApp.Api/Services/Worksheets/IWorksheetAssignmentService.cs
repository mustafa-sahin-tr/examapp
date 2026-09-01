using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Assigning worksheets to students/grades and reading assignment progress.
/// Split out of the former god-class <c>ExamService</c>.
/// </summary>
public interface IWorksheetAssignmentService
{
    Task<ResponseBaseDto> AssignWorksheetAsync(WorksheetAssignmentRequestDto request, int userId);

    Task<List<AssignedWorksheetDto>> GetActiveAssignmentsForStudentAsync(StudentProfileDto student);

    Task<TeacherWorksheetAssignmentsDto> GetWorksheetAssignmentsForTeacherAsync(int worksheetId, int teacherUserId);
}
