using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.StudentSchoolMemberships;

namespace ExamApp.Api.Services.StudentSchoolMemberships;

/// <summary>
/// issue #361: öğrencinin kendi kaydında seçtiği (beklemedeki, <c>Students.SchoolVerifiedAt</c> null) okul üyeliğinin onayı.
/// <para>
/// Onaylayıcılar (PO kararı, Tur 5 — #234'te öğretmen okul talebini yalnız platform admin onayladığı için okul yöneticisi
/// rolü yok): platform Admin (tüm okullar) ve aynı okulun ONAYLI öğretmenleri. Öğretmenin okulu YALNIZ kendi canlı
/// <c>Teachers</c> satırından gelir (<c>SchoolId</c> zaten admin onayıyla kurulur — #234 — yani doğrulanmıştır); hesabı
/// onaylı (<c>AccountApprovedAt</c> dolu) ve askıda olmayan (<c>AccountSuspendedAt</c> boş) olmalı. Bağımsız/okulsuz,
/// onaysız veya askıdaki öğretmen hiçbir başvuru görmez.
/// </para>
/// <para>
/// IDOR (#402 ile tutarlı): onaylayıcının okulu dışındaki, olmayan, silinmiş ya da beklemede olmayan öğrenci için karar
/// uçları <see cref="StudentSchoolDecisionStatus.NotFound"/> döner — varlık sızdırılmaz. Okul her zaman sunucu tarafında
/// çözülür; istemciden okul id'si alınmaz (admin liste filtresi hariç, o da yalnız daraltır).
/// </para>
/// </summary>
public interface IStudentSchoolMembershipService
{
    /// <summary>Onaylayıcının görebileceği bekleyen başvurular (en eski önce, sayfalı). Onay yetkisi yoksa boş sayfa.</summary>
    Task<Paged<StudentSchoolRequestDto>> ListPendingAsync(
        StudentSchoolApprover approver, int? schoolIdFilter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Onaylayıcının görebileceği bekleyen başvuru sayısı (menü rozeti).</summary>
    Task<int> CountPendingAsync(StudentSchoolApprover approver, CancellationToken ct = default);

    /// <summary>Beklemedeki üyeliği doğrular: <c>SchoolVerifiedAt</c> = şimdi, <c>SchoolVerifiedByUserId</c> = onaylayıcı.</summary>
    Task<StudentSchoolDecisionResult> ApproveAsync(StudentSchoolApprover approver, int studentId, CancellationToken ct = default);

    /// <summary>
    /// Beklemedeki üyeliği reddeder: <c>SchoolId</c> temizlenir (öğrenci okulsuz kalır; AYNI okulu 7 gün yeniden isteyemez,
    /// başka okul serbest — <c>LastRejectedSchoolId</c>/<c>SchoolRejectedAt</c>). Onay ve ret fail-closed audit'lenir
    /// (Requested → Succeeded/Conflict; kapsam dışı deneme NotFound).
    /// </summary>
    Task<StudentSchoolDecisionResult> RejectAsync(StudentSchoolApprover approver, int studentId, CancellationToken ct = default);
}

/// <summary>
/// issue #361: karar veren. <see cref="IsAdmin"/> controller'da JWT rolünden; öğretmen okulu servis tarafından DB'den çözülür
/// (bu kayıtta okul TAŞINMAZ — istemci/önbellek değerine güvenilmez). <see cref="KeycloakId"/> karar izinin aktörüdür
/// (AdminUserActionLogs.ActorKeycloakId).
/// </summary>
public sealed record StudentSchoolApprover(int UserId, bool IsAdmin, string KeycloakId);

public enum StudentSchoolDecisionStatus
{
    Success,
    /// <summary>Öğrenci yok/silinmiş, beklemede değil ya da onaylayıcının okulunda değil → 404 (varlık sızdırılmaz).</summary>
    NotFound,
    /// <summary>Okuma ile yazma arasında başka bir karar/okul değişikliği uygulandı → 409.</summary>
    Conflict
}

public sealed record StudentSchoolDecisionResult(StudentSchoolDecisionStatus Status, int? SchoolId = null);
