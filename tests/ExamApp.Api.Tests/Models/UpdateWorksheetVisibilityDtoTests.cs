using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Tests.Models;

/// <summary>
/// GitHub issue #10 — PUT api/worksheet/{id}/visibility request body validation.
/// ASP.NET model binding runs these DataAnnotations before the action executes and
/// short-circuits to 400 when they fail; this exercises that contract directly since
/// the controller depends on infrastructure (Keycloak claims, DI-resolved services)
/// not otherwise unit-tested in this project.
/// </summary>
public class UpdateWorksheetVisibilityDtoTests
{
    private static IList<ValidationResult> Validate(UpdateWorksheetVisibilityDto dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Validate_ValidEnumValues_NoValidationErrors()
    {
        var dto = new UpdateWorksheetVisibilityDto
        {
            TeacherSharing = WorksheetTeacherSharing.PublicAssignable,
            StudentVisibility = WorksheetStudentVisibility.Restricted,
        };

        Validate(dto).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_OutOfRangeTeacherSharingValue_ProducesValidationError()
    {
        var dto = new UpdateWorksheetVisibilityDto
        {
            TeacherSharing = (WorksheetTeacherSharing)999,
            StudentVisibility = WorksheetStudentVisibility.Normal,
        };

        Validate(dto).ShouldContain(r => r.MemberNames.Contains(nameof(UpdateWorksheetVisibilityDto.TeacherSharing)));
    }

    [Fact]
    public void Validate_OutOfRangeStudentVisibilityValue_ProducesValidationError()
    {
        var dto = new UpdateWorksheetVisibilityDto
        {
            TeacherSharing = WorksheetTeacherSharing.Private,
            StudentVisibility = (WorksheetStudentVisibility)999,
        };

        Validate(dto).ShouldContain(r => r.MemberNames.Contains(nameof(UpdateWorksheetVisibilityDto.StudentVisibility)));
    }
}
