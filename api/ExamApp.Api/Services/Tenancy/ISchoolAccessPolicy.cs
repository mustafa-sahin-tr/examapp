using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Tenancy;

/// <summary>
/// issue #190: öğretmen/öğrenci profil ve liste erişiminde okul izolasyonunun TEK karar noktası.
/// Servisler bu kuralı kendileri yazmaz; ya <see cref="ApplyScope{T}"/> ile sorguyu daraltır (liste uçları —
/// sonradan süzme değil, sayfalama ile tutarlı sorgu filtresi) ya da <see cref="CanAccess"/> ile tekil kaydı
/// doğrular (profil uçları — red → 404, 403 değil; varlık sızdırılmaz).
/// <para>
/// Genişleme noktası (#192): bağımsız öğretmenin "kendi öğrencileri" (Approved Booking) kuralı
/// <see cref="SchoolAccessPolicy"/> içine eklenir — <see cref="SchoolScope.IsIndependent"/> ve
/// <see cref="SchoolScope.UserId"/> bu amaçla taşınır; çağıran servisler değişmez.
/// </para>
/// </summary>
public interface ISchoolAccessPolicy
{
    /// <summary>
    /// İstek sahibi, <paramref name="targetSchoolId"/> okuluna ait tekil bir kaydı görebilir mi?
    /// Admin/servis → her zaman true. Aksi halde okul eşitliği (null == null → okulsuz→okulsuz izinli;
    /// okulsuz→okullu ve farklı okul → false).
    /// </summary>
    bool CanAccess(SchoolScope requester, int? targetSchoolId);

    /// <summary>
    /// Liste sorgusuna okul filtresini SQL düzeyinde uygular. Admin/servis için sorgu değişmez.
    /// Skip/Take'ten ÖNCE çağrılmalı ki sayfa farklı okul kayıtlarıyla dolmasın.
    /// </summary>
    IQueryable<T> ApplyScope<T>(IQueryable<T> query, SchoolScope requester) where T : class, ISchoolScoped;
}
