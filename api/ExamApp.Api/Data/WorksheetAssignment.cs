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

    /// <summary>
    /// issue #277 (madde 7): sınıf hedefli atamanın BİLİNÇLİ olarak tüm okulların o sınıfına açık olduğunu belirtir.
    /// Öğrenci görünürlük predikatı (<c>WorksheetStudentAccess.AssignmentVisibleTo</c>) artık
    /// <c>IsPlatformWide || SchoolId == öğrencinin okulu</c> kuralını uygular — <c>SchoolId == null</c> tek başına
    /// "herkese açık" anlamına GELMEZ (fail-closed). Yalnızca admin'in (Unrestricted) sınıf ataması true yazılır
    /// (<c>WorksheetAssignmentService.AssignWorksheetAsync</c>). Mevcut SchoolId=null sınıf atamaları migration
    /// <c>AddWorksheetAssignmentIsPlatformWide</c> ile true'ya çekildi (davranış korunur). Öğrenci hedefli atamalarda false.
    /// </summary>
    public bool IsPlatformWide { get; set; }

    [Required]
    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    [NotMapped]
    public bool IsGradeScoped => GradeId.HasValue && !StudentId.HasValue;

    [NotMapped]
    public bool IsStudentScoped => StudentId.HasValue;
}
