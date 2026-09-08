using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// POST api/login-events gövdesi (issue #84). Servis-to-servis kontrat: BadgeService login consumer'ı
/// auth-api outbox event'inden bu şekle map'leyip gönderir.
/// </summary>
public class LoginEventCreateDto
{
    /// <summary>Keycloak <c>sub</c> claim'i.</summary>
    [Required]
    [MaxLength(64)]
    public string KeycloakUserId { get; set; } = string.Empty;

    /// <summary>Login anındaki realm rolü (Student / Teacher / Admin ...).</summary>
    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = string.Empty;

    /// <summary>Login denemesinin gerçekleştiği an (UTC, ISO-8601).</summary>
    [Required]
    public DateTime OccurredAtUtc { get; set; }

    public bool Success { get; set; }
}

/// <summary>201 cevabı: sadece oluşturulan kaydın kimliği.</summary>
public class LoginEventCreatedDto
{
    public int Id { get; set; }
}
