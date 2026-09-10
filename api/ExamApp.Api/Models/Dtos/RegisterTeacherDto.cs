using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class RegisterTeacherDto
{
    public int? SchoolId { get; set; }

    /// <summary>Bağımsız öğretmen kaydı (issue #92). true ise hesap admin onayı bekler (Pending).</summary>
    public bool IsIndependentTutor { get; set; } = false;
}
