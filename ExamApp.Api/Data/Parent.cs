using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public class Parent
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }
    public virtual User User { get; set; }

    public virtual ICollection<Student> Children { get; set; }
}
