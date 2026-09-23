using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.TeacherApprovals;

/// <summary>Admin tarafı: bağımsız öğretmen (issue #94) ve okul bağlantısı (issue #234) başvurularını listeleme / onaylama / reddetme.</summary>
public interface ITeacherApprovalService
{
    Task<List<PendingTeacherApplicationDto>> GetPendingApplicationsAsync(CancellationToken ct = default);

    /// <summary>
    /// <paramref name="actorAdminKeycloakId"/> issue #157: admin karar audit'i (<c>AdminUserActionLogs</c>)
    /// için ZORUNLUDUR (review #157: opsiyonel bırakılırsa audit sessizce atlanabilir). Controller
    /// <c>KeyCloakId</c> claim'i yoksa zaten 403 döner (bkz. AdminController), dolayısıyla buraya her zaman
    /// dolu bir değer gelir.
    /// </summary>
    Task<ResponseBaseDto> ApproveAsync(int teacherId, int adminUserId, string actorAdminKeycloakId, CancellationToken ct = default);
    Task<ResponseBaseDto> RejectAsync(int teacherId, string reason, int adminUserId, string actorAdminKeycloakId, CancellationToken ct = default);
}
