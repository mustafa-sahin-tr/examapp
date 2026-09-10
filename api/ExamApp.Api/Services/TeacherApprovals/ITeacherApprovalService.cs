using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.TeacherApprovals;

/// <summary>Admin tarafı: bağımsız öğretmen başvurularını listeleme / onaylama / reddetme (issue #94).</summary>
public interface ITeacherApprovalService
{
    Task<List<PendingTeacherApplicationDto>> GetPendingApplicationsAsync(CancellationToken ct = default);

    Task<ResponseBaseDto> ApproveAsync(int teacherId, int adminUserId, CancellationToken ct = default);
    Task<ResponseBaseDto> RejectAsync(int teacherId, string reason, int adminUserId, CancellationToken ct = default);
}
