using System;

namespace ExamApp.Api.Data;
using System.ComponentModel.DataAnnotations;

public class Teacher : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }
    public virtual User User { get; set; }    

    [MaxLength(100)]
    public string SchoolName { get; set; }



}
