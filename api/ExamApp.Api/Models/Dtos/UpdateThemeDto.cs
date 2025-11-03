using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class UpdateThemeDto
{
    [Required]
    [StringLength(20)]
    public string ThemePreset { get; set; } = "standard"; // minimal, standard, enhanced, full

    public string? ThemeCustomConfig { get; set; } // JSON string for custom config
}