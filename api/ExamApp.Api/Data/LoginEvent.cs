using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Kullanıcı login denemesi kaydı (issue #84). auth-api'de üretilen login outbox event'i,
/// BadgeService consumer'ı tarafından tüketilip <c>POST api/login-events</c> ile buraya yazılır.
/// Sadece yazma tarafı bu issue kapsamındadır; admin dashboard okuma/aggregate tarafı issue #6.
/// Kullanıcı FK'sı bilinçli olarak yok: login olan kullanıcının bu DB'de (Student/Teacher) karşılığı
/// olmayabilir, bu yüzden Keycloak <c>sub</c> claim'i düz string olarak saklanır.
/// </summary>
public class LoginEvent : BaseEntity
{
    public int Id { get; set; }

    /// <summary>Keycloak <c>sub</c> claim'i.</summary>
    [MaxLength(64)]
    public string KeycloakUserId { get; set; } = string.Empty;

    /// <summary>Login anındaki realm rolü (Student / Teacher / Admin ...).</summary>
    [MaxLength(50)]
    public string Role { get; set; } = string.Empty;

    /// <summary>Login denemesinin gerçekleştiği an (UTC).</summary>
    public DateTime OccurredAtUtc { get; set; }

    public bool Success { get; set; }
}
