using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Bir öğretmenin, başkasına ait bir sınav için atama izni talebi oluşturma isteği (issue #13).
/// </summary>
public class CreateWorksheetAccessRequestDto
{
    [Required]
    public int WorksheetId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
