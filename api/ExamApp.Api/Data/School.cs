using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public class School : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }
}
