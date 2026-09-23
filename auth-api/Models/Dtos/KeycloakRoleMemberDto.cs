namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// <c>GET /admin/realms/{realm}/roles/{role}/users</c> satırı (issue #267 yetkili hesap denetimi) — Keycloak
/// UserRepresentation'ın denetimde gereken alt kümesi. <see cref="ServiceAccountClientId"/> yalnızca gerçek servis
/// hesaplarında (client service account) dolu gelir; kullanıcı adı öneki (<c>service-account-</c>) tek başına kanıt değildir.
/// </summary>
public sealed record KeycloakRoleMember(
    string Id,
    string Username,
    string? Email,
    bool Enabled,
    DateTimeOffset? CreatedAt,
    string? ServiceAccountClientId,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    public string? Attribute(string name)
        => Attributes is not null && Attributes.TryGetValue(name, out var v) ? v : null;
}

/// <summary>Keycloak grubu (issue #267 dolaylı yetki kontrolü): id, ad ve tam yol (<c>/ust/alt</c>).</summary>
public sealed record KeycloakGroupRef(string Id, string Name, string Path);
