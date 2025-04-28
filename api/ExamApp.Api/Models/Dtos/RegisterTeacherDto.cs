using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class RegisterTeacherDto
{
    [Required]
    [MaxLength(100)]
    public string SchoolName { get; set; }
}
