using ExamApp.Api.Data;

namespace ExamApp.Api.Services.Tenancy;

/// <summary>
/// issue #190 varsayılan kural: okul eşitliği. Bkz. <see cref="ISchoolAccessPolicy"/>.
/// <para>
/// #192 için genişleme noktası: bağımsız öğretmen (<see cref="SchoolScope.IsIndependent"/>, Role=Teacher)
/// için <see cref="ApplyScope{T}"/> içinde Student sorgusuna "Approved Booking" alt sorgusu,
/// <see cref="CanAccess"/> yanına ise hedef öğrenci id'si alan async bir aşırı yükleme eklenecek.
/// Bu sınıf DbContext'e bağımlı DEĞİL; #192'de AppDbContext constructor ile enjekte edilebilir
/// (DI kaydı zaten Scoped).
/// </para>
/// </summary>
public sealed class SchoolAccessPolicy : ISchoolAccessPolicy
{
    public bool CanAccess(SchoolScope requester, int? targetSchoolId)
    {
        if (requester.IsUnrestricted)
            return true;

        // null == null → okulsuz kullanıcı yalnızca okulsuz kayıtları görür (okulsuz→okullu red).
        return requester.SchoolId == targetSchoolId;
    }

    public IQueryable<T> ApplyScope<T>(IQueryable<T> query, SchoolScope requester) where T : class, ISchoolScoped
    {
        if (requester.IsUnrestricted)
            return query;

        // Parametre null ise EF Core "SchoolId IS NULL" üretir (okulsuz → yalnızca okulsuz kayıtlar).
        var schoolId = requester.SchoolId;
        return query.Where(x => x.SchoolId == schoolId);
    }
}
