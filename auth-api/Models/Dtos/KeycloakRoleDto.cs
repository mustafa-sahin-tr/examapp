using System;

namespace ExamApp.Api.Models.Dtos;

public class KeycloakRoleDto
{
    public string id { get; set; } = null!;
    public string name { get; set; } = null!;
    public string? description { get; set; }
    public bool composite { get; set; }
    public bool clientRole { get; set; }
    public string? containerId { get; set; }

    // Frontend için displayName özelliği
    [System.Text.Json.Serialization.JsonIgnore]
    public string displayName => GetDisplayName();

    private string GetDisplayName()
    {
        return name switch
        {
            "student" => "Öğrenci",
            "teacher" => "Öğretmen",
            "parent" => "Veli",
            "admin" => "Yönetici",
            _ => name // Varsayılan olarak name'i döndür
        };
    }
}

