using System;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #277 (madde 4): register akışının rol değişikliği bağlamı — Keycloak subject'i ve istek anındaki rol
/// (<c>UserProfileDto.Role</c>; auth-api <c>Users.Role</c>'den gelir, yani senkronlanmak istenen değerin ta kendisi).
/// </summary>
public sealed record UserRoleChangeRequest(string KeycloakId, int UserId, string? CurrentRole);

/// <summary>
/// issue #277 (madde 4): <see cref="UserRoleChangedEvent"/> outbox satırı üreticisi. auth-api bu event'i tüketip kendi
/// <c>Users.Role</c> kolonunu bir sonraki login'i beklemeden günceller.
/// <para>
/// exam DB'de yerel bir <c>Users</c> tablosu YOKTUR (rol Keycloak'ta + profil önbelleğinde durur); bu yüzden satır,
/// register akışının kendi DB yazımıyla (Teacher/Student/Parent satırı) AYNI transaction/SaveChanges içinde yazılır.
/// Yalnızca rol gerçekten değişiyorsa (<see cref="IsChange"/>): mevcut rol hedef rolle aynıysa event üretilmez.
/// </para>
/// </summary>
public static class UserRoleChangeOutbox
{
    public static bool IsChange(UserRoleChangeRequest? request, UserRole newRole)
        => request is not null
           && !string.IsNullOrWhiteSpace(request.KeycloakId)
           && !string.Equals(request.CurrentRole, newRole.ToString(), StringComparison.Ordinal);

    /// <summary>
    /// Yeni outbox satırı. <paramref name="eventId"/>/<paramref name="changedAtUtc"/> çağıran tarafından execution strategy
    /// lambda'sının DIŞINDA bir kez belirlenmelidir ki retry'da aynı değişiklik için aynı EventId yazılsın.
    /// </summary>
    public static OutboxMessage Create(UserRoleChangeRequest request, UserRole newRole, Guid eventId, DateTime changedAtUtc)
        => new()
        {
            Type = OutboxEventRegistry.NameFor<UserRoleChangedEvent>(),
            CreatedAt = changedAtUtc,
            Content = JsonSerializer.Serialize(new UserRoleChangedEvent
            {
                EventId = eventId,
                KeycloakId = request.KeycloakId,
                UserId = request.UserId,
                NewRole = newRole.ToString(),
                ChangedAtUtc = changedAtUtc
            })
        };
}
