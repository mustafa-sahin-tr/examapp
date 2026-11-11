using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public class Parent : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }    

    /// <summary>Issue #1112: veli haftalık raporun e-postasını kapattı (uygulama içi bildirim yine gelir).</summary>
    public bool WeeklyReportEmailOptOut { get; set; }

    public virtual ICollection<Student> Children { get; set; }
}
