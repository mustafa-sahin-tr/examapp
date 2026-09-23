using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// issue #156: kullanıcının ETKİN realm rolleri ve realm-management client rolleri (realm-admin, manage-users, ...).
/// Admin hesap aksiyonlarında hedefin korumalı olup olmadığını sunucu tarafında belirlemek için.
/// </summary>
public sealed record KeycloakUserRolesDto(IReadOnlyList<string> RealmRoles, IReadOnlyList<string> RealmManagementRoles);

/// <summary>Keycloak <c>GET users/{id}/role-mappings</c> yanıtı (yalnızca kullanılan alanlar).</summary>
public sealed class KeycloakRoleMappingsDto
{
    [JsonPropertyName("realmMappings")]
    public List<KeycloakRoleDto>? RealmMappings { get; set; }

    [JsonPropertyName("clientMappings")]
    public Dictionary<string, KeycloakClientMappingDto>? ClientMappings { get; set; }
}

public sealed class KeycloakClientMappingDto
{
    /// <summary>Client'ın iç UUID'si.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("mappings")]
    public List<KeycloakRoleDto>? Mappings { get; set; }
}
