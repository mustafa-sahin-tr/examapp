using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class WorksheetAssignment : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int WorksheetId { get; set; }

    [ForeignKey(nameof(WorksheetId))]
    public Worksheet Worksheet { get; set; } = null!;

    public int? StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public Student? Student { get; set; }

    public int? GradeId { get; set; }

    [ForeignKey(nameof(GradeId))]
    public Grade? Grade { get; set; }

    // 🟢 issue #12: sınıf hedefli atamalarda (özellikle non-owner PublicAssignable atamalarında)
    // atamanın hangi okulla sınırlı olduğunu tutar. Null ise okul kısıtlaması yok (legacy/admin ataması).
    public int? SchoolId { get; set; }

    [ForeignKey(nameof(SchoolId))]
    public School? School { get; set; }

    [Required]
    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    [NotMapped]
    public bool IsGradeScoped => GradeId.HasValue && !StudentId.HasValue;

    [NotMapped]
    public bool IsStudentScoped => StudentId.HasValue;
}
