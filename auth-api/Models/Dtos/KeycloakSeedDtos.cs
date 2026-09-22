using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Toplu (seed) kullanıcı oluşturma girdisi — Keycloak UserRepresentation'ın ihtiyaç duyulan alt kümesi (issue #217).</summary>
public sealed record KeycloakSeedUser(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyDictionary<string, string>? Attributes);

/// <summary>Tekil admin API ile oluşturma sonucu: id + zaten var mıydı (409 → username ile bulundu).</summary>
public sealed record KeycloakUserCreateResult(string Id, bool AlreadyExisted);

/// <summary>
/// Önceden hash'lenmiş parola (Keycloak <c>CredentialRepresentation</c>'ın <c>secretData</c> /
/// <c>credentialData</c> JSON string'leri). Partial import'ta her kullanıcı için aynı hash kullanılır;
/// böylece Keycloak sunucu tarafında kullanıcı başına PBKDF2/argon2 hesaplamaz.
/// </summary>
public sealed record KeycloakHashedCredential(string SecretData, string CredentialData);

/// <summary>
/// <c>POST /admin/realms/{realm}/partialImport</c> sonucu. <see cref="Results"/> kullanıcı adı → (id, eylem).
/// <c>ifResourceExists=SKIP</c> ile mevcut kullanıcılar SKIPPED döner; id o durumda boş olabilir.
/// </summary>
public sealed record KeycloakPartialImportResult(
    int Added,
    int Skipped,
    int Overwritten,
    IReadOnlyDictionary<string, KeycloakPartialImportEntry> Results);

public sealed record KeycloakPartialImportEntry(string Action, string? Id);

/// <summary>
/// Kullanıcı arama sonucu (issue #218 temizliği): id + kullanıcı adı + e-posta + tek değerli attribute'lar
/// (tam temsil; <c>seed_origin</c> sahiplik kilidi yetim kararında okunur).
/// </summary>
public sealed record KeycloakUserSummary(string Id, string Username, string? Email, IReadOnlyDictionary<string, string>? Attributes = null)
{
    public string? Attribute(string name)
        => Attributes is not null && Attributes.TryGetValue(name, out var v) ? v : null;
}

/// <summary>Kullanıcı aramasında hangi alan içinde (infix) aranacağı.</summary>
public enum KeycloakUserSearchField
{
    Email,
    Username
}
