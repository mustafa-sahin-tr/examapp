using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Admin'in kişisel veri içeren listelere erişim kaydı (issue #246, KVKK hesap verebilirlik).
/// Yalnızca ekleme yapılır (append-only): <see cref="BaseEntity"/>'den türemez, soft delete / update alanı yoktur.
///
/// Bilinçli olarak PII içermez: admin kimliği Keycloak <c>sub</c> (takma ad niteliğinde kimlik; ad/e-posta değil),
/// filtre yalnızca okul id'si + "okulsuz" bayrağı, sonuç yalnızca satır sayısı. Dönen kullanıcıların id'leri/adları yazılmaz.
/// Kullanıcı FK'sı yok — <see cref="LoginEvent"/> ile aynı gerekçe (admin'in bu DB'de satırı olmayabilir).
///
/// issue #262: saklama süresi sınırlı (<c>AdminDataAccessLog:RetentionDays</c>, varsayılan 180 gün) — süresi dolan satırları
/// günlük Hangfire işi (<c>AdminDataAccessLogRetentionJob</c>) toplu siler. Rate limit'e takılan (429) istekler de
/// <see cref="Outcome"/>=<see cref="AdminDataAccessOutcome.RateLimited"/> ile buraya yazılır.
/// </summary>
public class AdminDataAccessLog
{
    public long Id { get; set; }

    /// <summary>Erişen admin'in Keycloak <c>sub</c> claim'i.</summary>
    [MaxLength(64)]
    public string ActorKeycloakId { get; set; } = string.Empty;

    /// <summary>Erişilen liste (string olarak saklanır; sorgularda okunabilir kalsın).</summary>
    public AdminDataAccessResource Resource { get; set; }

    /// <summary>Filtre: okul id'si; filtre yoksa null.</summary>
    public int? SchoolIdFilter { get; set; }

    /// <summary>Filtre: yalnızca okulsuz kullanıcılar istendi mi.</summary>
    public bool UnassignedFilter { get; set; }

    /// <summary>Normalize edilmiş (kırpılmış) sayfa numarası.</summary>
    public int Page { get; set; }

    /// <summary>Normalize edilmiş (1..100) sayfa boyutu.</summary>
    public int PageSize { get; set; }

    /// <summary>Yanıtta dönen satır sayısı.</summary>
    public int ReturnedCount { get; set; }

    /// <summary>Filtreye uyan toplam kayıt sayısı (yanıttaki totalCount).</summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Detay uçlarında erişilen kaydın id'si (issue #262: öğretmen başvurusu detayı → Teacher.Id); liste uçlarında null.
    /// </summary>
    public int? TargetId { get; set; }

    /// <summary>
    /// issue #262: isteğin sonucu. <see cref="AdminDataAccessOutcome.RateLimited"/> satırlarında veri DÖNMEMİŞTİR;
    /// sayfa/sayı alanları 0'dır. Kalıcı değer string'dir.
    /// </summary>
    public AdminDataAccessOutcome Outcome { get; set; } = AdminDataAccessOutcome.Served;

    /// <summary>Erişim anı (UTC).</summary>
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>Audit'lenen admin veri uçları. Kalıcı değer string'dir; yeniden adlandırma geçmiş kayıtları bozar.</summary>
public enum AdminDataAccessResource
{
    StudentList = 1,
    TeacherList = 2,

    /// <summary>issue #262: <c>GET api/admin/teacher-applications</c> (bekleyen öğretmen başvuruları, maskeli e-posta).</summary>
    TeacherApplicationList = 3,

    /// <summary>issue #262: <c>GET api/admin/teacher-applications/{id}</c> (tam e-posta).</summary>
    TeacherApplicationDetail = 4
}

/// <summary>issue #262: audit satırının sonucu. Kalıcı değer string'dir; yeniden adlandırma geçmiş kayıtları bozar.</summary>
public enum AdminDataAccessOutcome
{
    /// <summary>Veri döndü.</summary>
    Served = 1,

    /// <summary>
    /// Kullanıcı başına rate limit aşıldı (429); veri dönmedi. Kötüye kullanım incelemesi için saklanır — pencere başına
    /// yalnızca İLK red yazılır (sonraki 429'lar log'da).
    /// </summary>
    RateLimited = 2,

    /// <summary>issue #262 review: detay ucunda kayıt bulunamadı (404); veri dönmedi. Id tarama denemeleri görünür olsun.</summary>
    NotFound = 3
}
