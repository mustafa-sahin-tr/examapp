using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// Admin liste erişiminin audit girdisi (issue #246). HTTP'ye açılmaz; controller → <c>IAdminDataAccessAuditService</c>.
/// Kasıtlı olarak PII taşıyacak alan yok: filtreler yalnızca id/bayrak, sonuç yalnızca sayı.
/// Uçlara ileride serbest metin arama (ad/e-posta) eklenirse ham değer buraya KONMAZ — yalnızca varlığı (bool) eklenmeli.
/// </summary>
public sealed record AdminListAccessRecord(
    string ActorKeycloakId,
    AdminDataAccessResource Resource,
    int? SchoolIdFilter,
    bool UnassignedFilter,
    int Page,
    int PageSize,
    int ReturnedCount,
    int TotalCount);

/// <summary>
/// Admin hesap aksiyonunun audit girdisi (issue #156). HTTP'ye açılmaz. Sır/PII alanı yok ve eklenmemeli
/// (geçici şifre, e-posta, ad buraya KONMAZ).
/// </summary>
public sealed record AdminUserActionRecord(
    string ActorKeycloakId,
    AdminUserAction Action,
    AdminUserTargetType TargetType,
    int TargetId);
