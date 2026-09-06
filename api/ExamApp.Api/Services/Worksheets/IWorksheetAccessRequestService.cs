using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Atama izni request/approve akışı (issue #13). Öğretmen başkasının sınavı için izin talep eder;
/// sahibi onaylar/reddeder. Onay kalıcı <c>WorksheetAccessGrant</c> olarak tutulur.
/// Bildirimler outbox event üzerinden BadgeService'e taşınır — servisten servise senkron çağrı yok.
/// </summary>
public interface IWorksheetAccessRequestService
{
    Task<ResponseBaseDto> CreateRequestAsync(int worksheetId, string? note, int userId, string? userKeycloakId, bool isAdmin, CancellationToken ct = default);

    Task<List<WorksheetAccessRequestDto>> GetIncomingAsync(int ownerUserId, bool includeDecided, CancellationToken ct = default);

    Task<int> GetIncomingPendingCountAsync(int ownerUserId, CancellationToken ct = default);

    Task<ResponseBaseDto> ApproveAsync(int requestId, int ownerUserId, bool isAdmin, CancellationToken ct = default);

    Task<ResponseBaseDto> RejectAsync(int requestId, int ownerUserId, bool isAdmin, CancellationToken ct = default);

    Task<ResponseBaseDto> RevokeGrantAsync(int worksheetId, int teacherUserId, int ownerUserId, bool isAdmin, CancellationToken ct = default);
}
