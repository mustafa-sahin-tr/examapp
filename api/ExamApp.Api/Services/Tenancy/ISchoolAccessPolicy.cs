using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Tenancy;

/// <summary>
/// issue #190: öğretmen/öğrenci profil ve liste erişiminde okul izolasyonunun TEK karar noktası.
/// Servisler bu kuralı kendileri yazmaz; ya <see cref="ApplyScope{T}"/> ile sorguyu daraltır (liste uçları —
/// sonradan süzme değil, sayfalama ile tutarlı sorgu filtresi) ya da <see cref="CanAccess"/> /
/// <see cref="CanAccessStudentAsync"/> ile tekil kaydı doğrular (profil uçları — red → 404, 403 değil; varlık sızdırılmaz).
/// <para>
/// issue #192: bağımsız (okulsuz, <see cref="SchoolScope.IsIndependent"/>) öğretmenin "kendi öğrencileri"
/// = en az bir Approved Booking'i olan öğrenciler. Bu kural yalnızca <c>Student</c> hedefinde geçerlidir:
/// <see cref="ApplyScope{T}"/> <c>IQueryable&lt;Student&gt;</c> için otomatik uygular; tekil öğrenci için
/// <see cref="CanAccessStudentAsync"/> kullanılır (<see cref="CanAccess"/> öğrenci hedefi için YETERSİZDİR).
/// Okula bağlı öğretmen ve admin için #190 kuralı değişmez.
/// </para>
/// <para>
/// Rol kontrolü ÇAĞIRANIN sorumluluğudur: policy yalnızca tenant/booking kapsamına bakar, istek sahibinin
/// Teacher/Student/Admin olup olmadığını bilmez (<c>[Authorize(Roles=...)]</c> controller'da). Örn. okulsuz bir
/// Student'ın <see cref="SchoolScope"/>'u da <see cref="SchoolScope.IsIndependent"/> döner; öğretmen-only uçlara
/// bu scope'u geçirmemek çağıranın işidir.
/// </para>
/// </summary>
public interface ISchoolAccessPolicy
{
    /// <summary>
    /// İstek sahibi, <paramref name="targetSchoolId"/> okuluna ait tekil bir kaydı görebilir mi?
    /// Admin/servis → her zaman true. Aksi halde okul eşitliği (null == null → okulsuz→okulsuz izinli;
    /// okulsuz→okullu ve farklı okul → false). Öğrenci hedefi için <see cref="CanAccessStudentAsync"/> kullan.
    /// </summary>
    bool CanAccess(SchoolScope requester, int? targetSchoolId);

    /// <summary>
    /// İstek sahibi, <paramref name="studentId"/> öğrencisini görebilir/hedefleyebilir mi?
    /// Admin/servis → true. Okullu → okul eşitliği. Bağımsız → istek sahibinin en az bir Approved Booking'i olmalı
    /// (Pending/Rejected sayılmaz). Öğrenci yoksa (veya soft-delete edilmişse) false — var/yok oracle'ı kapalı.
    /// </summary>
    Task<bool> CanAccessStudentAsync(SchoolScope requester, int studentId, CancellationToken ct = default);

    /// <summary>
    /// Liste sorgusuna kapsam filtresini SQL düzeyinde uygular. Admin/servis için sorgu değişmez.
    /// <c>IQueryable&lt;Student&gt;</c> + bağımsız istek sahibi → Approved Booking EXISTS alt sorgusu (#192);
    /// diğer durumlarda okul eşitliği (#190). Skip/Take'ten ÖNCE çağrılmalı ki sayfa kapsam dışı kayıtlarla dolmasın.
    /// </summary>
    IQueryable<T> ApplyScope<T>(IQueryable<T> query, SchoolScope requester) where T : class, ISchoolScoped;
}
