using System;

namespace ExamApp.Api.Models.Dtos;

using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;

public class RegisterDto
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [MinLength(6)]
    public string Password { get; set; }

    [Required]
    public UserRole Role { get; set; } // Öğrenci, Öğretmen veya Veli
}
