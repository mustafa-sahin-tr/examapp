using System;

namespace ExamApp.Api.Models.Dtos;

public class UserProfileDto
{
    public int Id { get; set; }
    public string KeycloakId { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string Role { get; set; }
    public string Avatar { get; set;}
    /// <summary>
    /// issue #189: sunucu tarafında (Teacher/Student tablosundan) doğrulanmış okul kimliği.
    /// null = okulsuz/bağımsız kullanıcı, admin veya servis hesabı. Bkz. ISchoolContextResolver.
    /// </summary>
    public int? SchoolId { get; set; }
    // public int ProfileId { get; set;}    
    // public string? SchoolName { get; set; } // Student bilgisi
    // public string? Department { get; set; } // opsiyonel
    public StudentDto Student { get; set; }
    public TeacherDto Teacher { get; set; }
}

public class LoginResponseDto {
    public string Token { get; set; }
    public UserProfileDto Profile { get; set; }
}

