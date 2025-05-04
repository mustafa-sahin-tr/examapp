using System;

namespace ExamApp.Api.Data;

using System.ComponentModel.DataAnnotations;

public class User : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [MaxLength(50)]
    public string KeycloakId { get; set; } = null!;
    
    [Required, MaxLength(100)]
    public string FullName { get; set; }

    [Required, MaxLength(100)]
    public string Email { get; set; }
    
    public string? PasswordHash { get; set; }

    public string? AvatarUrl { get; set; }

    [Required]
    public UserRole Role { get; set; } // Öğrenci, Öğretmen, Veli

    public virtual Student? Student { get; set; }
    public virtual Teacher? Teacher { get; set; }
    public virtual Parent? Parent { get; set; }
}

public enum UserRole
{
    Student,
    Teacher,
    Parent
}
