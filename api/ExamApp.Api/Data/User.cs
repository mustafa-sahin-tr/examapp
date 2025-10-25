using System;

namespace ExamApp.Api.Data;

public enum UserRole
{
    Student,
    Teacher,
    Parent
}

public class User
{
    public int Id { get; set; }
    public string KeycloakId { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string Role { get; set; }
    public string Avatar { get; set;}
    public int ProfileId { get; set;}   
}

