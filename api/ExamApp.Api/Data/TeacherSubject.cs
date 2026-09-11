using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Bağımsız öğretmenin verdiği dersler (issue #95) — Teacher ↔ Subject many-to-many join tablosu.
/// GradeSubject ile aynı FK deseni; ancak BaseEntity'den türemez: AppDbContext.ApplyAuditInfo
/// BaseEntity satırlarını soft-delete'e çevirdiği için ders listesi güncellemede kaldırılan satır
/// gerçekten silinemez ve (TeacherId, SubjectId) unique index'i tekrar eklemede çakışırdı.
/// </summary>
public class TeacherSubject
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TeacherId { get; set; }

    [ForeignKey("TeacherId")]
    public Teacher Teacher { get; set; } = null!;

    [Required]
    public int SubjectId { get; set; }

    [ForeignKey("SubjectId")]
    public Subject Subject { get; set; } = null!;
}
