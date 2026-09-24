using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.TeacherApprovals;

/// <summary>Admin tarafı: bağımsız öğretmen (issue #94) ve okul bağlantısı (issue #234) başvurularını listeleme / onaylama / reddetme.</summary>
public interface ITeacherApprovalService
{
    /// <summary>
    /// issue #187: sayfalı başvuru listesi. <paramref name="status"/>=Pending → yalnızca bekleyenler (en eski önce);
    /// All → bağımsız öğretmen / okul talebi olan ya da hakkında admin kararı (#157 audit) bulunan her öğretmen, her durumda:
    /// önce bekleyenler (en eski önce), sonra karar verilmişler (en yeni karar önce). Sayfa/boyut
    /// <c>AdminListPaging</c> ile normalize edilir. issue #262: e-posta MASKELİ (<c>a***@x.com</c>).
    /// </summary>
    Task<Paged<TeacherApplicationListItemDto>> ListApplicationsAsync(
        TeacherApplicationStatusFilter status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// issue #262 / #187: tek başvurunun detayı (her durumda) — TAM e-posta ile. Başvuru olmayan / bilinmeyen id → null (404).
    /// Kişisel veri döndürdüğü için çağıran erişimi audit'lemelidir.
    /// </summary>
    Task<TeacherApplicationDetailDto?> GetApplicationAsync(int teacherId, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="actorAdminKeycloakId"/> issue #157: admin karar audit'i (<c>AdminUserActionLogs</c>)
    /// için ZORUNLUDUR (review #157: opsiyonel bırakılırsa audit sessizce atlanabilir). Controller
    /// <c>KeyCloakId</c> claim'i yoksa zaten 403 döner (bkz. AdminController), dolayısıyla buraya her zaman
    /// dolu bir değer gelir.
    /// </summary>
    Task<ResponseBaseDto> ApproveAsync(int teacherId, int adminUserId, string actorAdminKeycloakId, CancellationToken ct = default);
    Task<ResponseBaseDto> RejectAsync(int teacherId, string reason, int adminUserId, string actorAdminKeycloakId, CancellationToken ct = default);
}
