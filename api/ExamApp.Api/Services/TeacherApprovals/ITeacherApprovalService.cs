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

    Task<ResponseBaseDto> ApproveAsync(int teacherId, int adminUserId, CancellationToken ct = default);
    Task<ResponseBaseDto> RejectAsync(int teacherId, string reason, int adminUserId, CancellationToken ct = default);
}
