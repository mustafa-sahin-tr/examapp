using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// issue #313: admin'in öğretmeni doğrudan bir okula bağlaması / okulunu değiştirmesi. Öğretmen okul bağını kendisi kuramaz
/// (#234: talep → admin onayı) ve <c>TeacherService.Save</c> bağımsız → okullu geçişi 409 ile reddeder; takılı kalmış kayıtlar
/// (ör. bağımsız başvurusu reddedilmiş eski okul öğretmeni) yalnızca bu yoldan düzeltilir. Sözleşme
/// <see cref="IAdminStudentSchoolService"/> (#277 madde 8) ile paraleldir.
/// </summary>
public interface IAdminTeacherSchoolService
{
    /// <summary>
    /// <paramref name="teacherId"/> exam DB'deki Teacher.Id'dir. Sıra: öğretmen ve okul doğrulaması → hedef çözümü
    /// (<see cref="IAdminAccountTargetResolver"/>: kendisi/korumalı rol/hesap yok/upstream; öğrenci ucundan farklı olarak
    /// aynı-okul kısa devresinden ÖNCE, #313 review D1) → durum kapıları (askı, hesap onayı, aktif bağımsız → 409) → aynı
    /// okul ve bekleyen talep yoksa yan etkisiz başarı → Requested audit (fail-closed, yazımdan önce) → koşullu UPDATE (okunduğu andaki okul/talep/başvuru
    /// durumu hâlâ aynıysa; değilse <see cref="AdminTeacherSchoolChangeStatus.Conflict"/>) → profil önbelleği + Keycloak
    /// <c>school_id</c> ipucu (best-effort) → sonuç audit'i. Atama/worksheet verisine DOKUNULMAZ (bkz. uygulama notları).
    /// </summary>
    Task<AdminTeacherSchoolChangeResult> ChangeSchoolAsync(
        int teacherId, int schoolId, string actorKeycloakId, CancellationToken ct = default);
}

public enum AdminTeacherSchoolChangeStatus
{
    Success,
    /// <summary>Öğretmen kaydı yok (veya silinmiş) → 404.</summary>
    TargetNotFound,
    /// <summary>İstenen okul yok → 400.</summary>
    SchoolNotFound,
    /// <summary>auth-api'de ya da Keycloak'ta hesap yok → 404.</summary>
    AccountNotFound,
    /// <summary>Admin kendi öğretmen kaydının okulunu bu uçtan değiştiremez → 403.</summary>
    ForbiddenSelf,
    /// <summary>Hedef Admin / exam-service ya da realm-management client rolünde → 403.</summary>
    ForbiddenProtectedRole,
    /// <summary>Okuma ile yazma arasında öğretmenin okulu/talebi/başvuru durumu başka bir istekle değişti → 409.</summary>
    Conflict,
    /// <summary>
    /// issue #313 review (K1): onaylı bağımsız öğretmen (IsIndependentTutor + Approved) → 409. Okul kapsamı
    /// (<c>SchoolScope.IsIndependent</c>) SchoolId'ye bakar; bağlamak randevulu öğrencilere erişimi keserken tutor
    /// araması/takvimi açık kalırdı. Önce bağımsız profil kapatılmalı.
    /// </summary>
    IndependentActive,
    /// <summary>issue #313 review (U2): öğretmen hesabı henüz onaylanmamış (AccountApprovedAt null, askıda değil) → 409.</summary>
    AccountNotApproved,
    /// <summary>issue #313 review (U1-b): öğretmen hesabı askıda (AccountSuspendedAt dolu) → 409.</summary>
    AccountSuspended,
    /// <summary>auth-api veya Keycloak hatası / zaman aşımı; okul DEĞİŞMEDİ → 502.</summary>
    UpstreamFailure
}

/// <param name="ProfileCacheStale">
/// issue #313 review (O1): okul değişti ama profil önbelleği denemelere rağmen düşürülemedi — öğretmen en geç 1 saat eski
/// okulun kapsamında görünebilir (audit outcome <c>SucceededCacheStale</c>).
/// </param>
public sealed record AdminTeacherSchoolChangeResult(
    AdminTeacherSchoolChangeStatus Status, int? PreviousSchoolId = null, int? SchoolId = null, bool Changed = false,
    bool ProfileCacheStale = false);
