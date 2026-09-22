namespace ExamApp.Api.Services.Tenancy;

/// <summary>
/// issue #190: istek sahibinin tenant (okul) bağlamı — yetki kararlarına giren tek girdi.
/// <para>
/// <see cref="Controllers.BaseController.GetSchoolScopeAsync"/> tarafından üretilir; <see cref="SchoolId"/> her
/// zaman sunucu tarafında (Teacher/Student tablosu, issue #189) doğrulanmış değerdir — client'tan gelen
/// schoolId parametresiyle ASLA kurulmaz.
/// </para>
/// <list type="bullet">
/// <item><see cref="IsUnrestricted"/> = admin veya servis hesabı: okul filtresi uygulanmaz (epic #160: admin cross-tenant).</item>
/// <item><see cref="SchoolId"/> = null ve <see cref="IsUnrestricted"/> = false: okulsuz/bağımsız kullanıcı.</item>
/// </list>
/// <see cref="UserId"/> yalnızca #192'de karar verir: bağımsız istek sahibi (<see cref="IsIndependent"/>) için
/// Student kapsamı "bu UserId'li öğretmenin Approved Booking'i olan öğrenciler" olarak daraltılır.
/// </summary>
public readonly record struct SchoolScope(bool IsUnrestricted, int? SchoolId, int UserId)
{
    /// <summary>Admin / servis hesabı: tüm okullar görünür.</summary>
    public static SchoolScope Unrestricted(int userId) => new(true, null, userId);

    /// <summary>Okula bağlı (schoolId dolu) veya okulsuz (schoolId null) normal kullanıcı.</summary>
    public static SchoolScope For(int userId, int? schoolId) => new(false, schoolId, userId);

    /// <summary>
    /// Okulsuz/bağımsız kullanıcı (epic #160 Karar 2: legacy SchoolId=null kayıtlar dahil — yani
    /// <c>Teacher.IsIndependentTutor</c> bayrağına değil SchoolId=null olmasına bakılır; #192 kuralı ikisine de uygulanır).
    /// </summary>
    public bool IsIndependent => !IsUnrestricted && !SchoolId.HasValue;
}
