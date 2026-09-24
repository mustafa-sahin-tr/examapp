using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// issue #277 (madde 8): admin'in öğrencinin okulunu değiştirmesi. Öğrenci okulu kayıttan sonra öğrenci tarafından
/// değiştirilemez (#259 okul kilidi); meşru değişiklik yalnızca bu yoldan yapılır.
/// </summary>
public interface IAdminStudentSchoolService
{
    /// <summary>
    /// <paramref name="studentId"/> exam DB'deki Student.Id'dir. Sıra (#155/#156 deseni): öğrenci ve okul doğrulaması →
    /// aynı okulsa yan etkisiz başarı → hedef çözümü (<see cref="IAdminAccountTargetResolver"/>: kendisi/korumalı rol/
    /// hesap yok/upstream) → Requested audit (fail-closed, yazımdan önce) → koşullu UPDATE (okundu anki okul hâlâ aynıysa;
    /// değilse <see cref="AdminStudentSchoolChangeStatus.Conflict"/>) → profil önbelleği + Keycloak <c>school_id</c> ipucu
    /// (best-effort) → sonuç audit'i. Atama/test/booking verisine DOKUNULMAZ (bkz. uygulama notları).
    /// </summary>
    Task<AdminStudentSchoolChangeResult> ChangeSchoolAsync(
        int studentId, int schoolId, string actorKeycloakId, CancellationToken ct = default);
}

public enum AdminStudentSchoolChangeStatus
{
    Success,
    /// <summary>Öğrenci kaydı yok (veya silinmiş) → 404.</summary>
    TargetNotFound,
    /// <summary>İstenen okul yok → 400.</summary>
    SchoolNotFound,
    /// <summary>auth-api'de ya da Keycloak'ta hesap yok → 404.</summary>
    AccountNotFound,
    /// <summary>Admin kendi kaydını bu uçtan değiştiremez → 403.</summary>
    ForbiddenSelf,
    /// <summary>Hedef Admin / exam-service ya da realm-management client rolünde → 403.</summary>
    ForbiddenProtectedRole,
    /// <summary>Okuma ile yazma arasında öğrencinin okulu başka bir istekle değişti; değişiklik yapılmadı → 409.</summary>
    Conflict,
    /// <summary>auth-api veya Keycloak hatası / zaman aşımı; okul DEĞİŞMEDİ → 502.</summary>
    UpstreamFailure
}

public sealed record AdminStudentSchoolChangeResult(
    AdminStudentSchoolChangeStatus Status, int? PreviousSchoolId = null, int? SchoolId = null, bool Changed = false);
