using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Request body for PUT api/worksheet/{id}/visibility (issue #10).
/// Updates both visibility axes together.
/// </summary>
public class UpdateWorksheetVisibilityDto
{
    [Required]
    [EnumDataType(typeof(WorksheetTeacherSharing))]
    public WorksheetTeacherSharing TeacherSharing { get; set; }

    [Required]
    [EnumDataType(typeof(WorksheetStudentVisibility))]
    public WorksheetStudentVisibility StudentVisibility { get; set; }
}
