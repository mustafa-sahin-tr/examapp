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
    /// <param name="scope">issue #1277: okul yöneticisi kapsamı; null → platform admini (tüm başvurular).</param>
    Task<Paged<TeacherApplicationListItemDto>> ListApplicationsAsync(
        TeacherApplicationStatusFilter status, int page, int pageSize, CancellationToken ct = default,
        SchoolApplicationScope? scope = null);

    /// <summary>
    /// issue #262 / #187: tek başvurunun detayı (her durumda). E-posta yalnızca Pending'de TAM, Approved/Rejected'da maskeli.
    /// Başvuru olmayan / bilinmeyen id → null (404).
    /// Kişisel veri döndürdüğü için çağıran erişimi audit'lemelidir.
    /// </summary>
    Task<TeacherApplicationDetailDto?> GetApplicationAsync(int teacherId, CancellationToken ct = default,
        SchoolApplicationScope? scope = null);

    /// <summary>issue #1277: okul yöneticisinin bekleyen başvuru sayısı (menü rozeti).</summary>
    Task<int> CountPendingAsync(SchoolApplicationScope scope, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="actorAdminKeycloakId"/> issue #157: admin karar audit'i (<c>AdminUserActionLogs</c>)
    /// için ZORUNLUDUR (review #157: opsiyonel bırakılırsa audit sessizce atlanabilir). Controller
    /// <c>KeyCloakId</c> claim'i yoksa zaten 403 döner (bkz. AdminController), dolayısıyla buraya her zaman
    /// dolu bir değer gelir.
    /// </summary>
    /// <param name="scope">issue #1277: okul yöneticisi kararı; kapsam dışı başvuru → NotFound.</param>
    Task<ResponseBaseDto> ApproveAsync(int teacherId, int adminUserId, string actorAdminKeycloakId, CancellationToken ct = default,
        SchoolApplicationScope? scope = null);
    Task<ResponseBaseDto> RejectAsync(int teacherId, string reason, int adminUserId, string actorAdminKeycloakId, CancellationToken ct = default,
        SchoolApplicationScope? scope = null);
}

/// <summary>
/// issue #1277 (#1071 S2): okul yöneticisinin gördüğü / karar verdiği başvurular. Kararlar yalnız
/// <c>RequestedSchoolId == SchoolId</c> olan okul bağlantısı talepleri; bağımsız öğretmen başvurusu ve okul talebi olmayan
/// ilk hesap başvurusu platform admininde kalır. Yöneticinin kendi kaydı (<c>ActorUserId</c>) kapsam dışıdır.
/// "Tümü" geçmişinde o okula onaylanmış başvurular da görünür.
/// </summary>
public sealed record SchoolApplicationScope(int SchoolId, int ActorUserId);
