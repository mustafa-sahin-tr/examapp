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
    int TotalCount,
    TeacherApplicationStatusFilter? StatusFilter = null); // issue #187: yalnızca öğretmen başvurusu listesinde

/// <summary>
/// Admin detay ucu erişiminin audit girdisi (issue #262; ilk kullanım: <c>GET api/admin/teacher-applications/{id}</c>).
/// HTTP'ye açılmaz. Yalnızca erişilen kaydın id'si — dönen e-posta/ad buraya KONMAZ.
/// <paramref name="Outcome"/>: <c>Served</c> (veri döndü) ya da <c>NotFound</c> (404; veri dönmedi).
/// </summary>
public sealed record AdminDetailAccessRecord(
    string ActorKeycloakId,
    AdminDataAccessResource Resource,
    int TargetId,
    AdminDataAccessOutcome Outcome = AdminDataAccessOutcome.Served);

/// <summary>
/// Rate limit'e takılan (429) admin veri isteğinin audit girdisi (issue #262). Veri dönmediği için sayfa/sayı yok;
/// filtreler query string'den en iyi çabayla okunur (geçersiz değer → null/false). <paramref name="TargetId"/> detay uçlarında.
/// </summary>
public sealed record AdminRateLimitedAccessRecord(
    string ActorKeycloakId,
    AdminDataAccessResource Resource,
    int? SchoolIdFilter,
    bool UnassignedFilter,
    int? TargetId,
    TeacherApplicationStatusFilter? StatusFilter = null); // issue #187: yalnızca öğretmen başvurusu listesinde

/// <summary>
/// Admin hesap aksiyonunun audit girdisi (issue #156). HTTP'ye açılmaz. Sır/PII alanı yok ve eklenmemeli
/// (geçici şifre, e-posta, ad buraya KONMAZ).
/// </summary>
public sealed record AdminUserActionRecord(
    string ActorKeycloakId,
    AdminUserAction Action,
    AdminUserTargetType TargetType,
    int TargetId);
