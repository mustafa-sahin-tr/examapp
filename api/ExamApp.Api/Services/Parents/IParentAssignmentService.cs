using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.ParentDashboard;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Veli ödev/test takibi (issue #421, epic #407 V3) — salt okunur; yazma ucu yoktur. Her çağrı sırası: <b>kapı → audit → veri</b>
/// (<see cref="IParentChildAccess"/> → <see cref="IParentAccessAuditLog"/> → sorgular). Kapıdan geçmeyen istek hiçbir veri
/// sorgusu çalıştırmaz ve audit yazmaz.
/// </summary>
public interface IParentAssignmentService
{
    /// <summary>
    /// Çocuğun ödev/test listesi (V2 özetiyle aynı kapsam ve kovalar), sayfalı. Erişim yoksa null → 404.
    /// </summary>
    /// <param name="status">Kova filtresi; null = hepsi.</param>
    /// <param name="page">1 tabanlı sayfa (çağıran doğrular; aralık dışı sayfa boş liste döner).</param>
    Task<ParentChildAssignmentListDto?> GetAssignmentsAsync(
        int parentUserId, int studentId, ParentAssignmentBucket? status, int page, CancellationToken ct = default);

    /// <summary>
    /// Çocuğun bitmiş bir test oturumunun özeti. Erişim yoksa <see cref="ParentTestResultLookup.ChildAccessible"/> false;
    /// oturum bu çocuğa ait değilse / yoksa / henüz bitmemişse <see cref="ParentTestResultLookup.Result"/> null (ikisi de 404).
    /// </summary>
    Task<ParentTestResultLookup> GetTestResultAsync(
        int parentUserId, int studentId, int testInstanceId, CancellationToken ct = default);
}

/// <summary>Test sonucu okuma sonucu: çocuğa erişim var mı + (varsa) sonuç.</summary>
public sealed record ParentTestResultLookup(bool ChildAccessible, ParentChildTestResultDto? Result)
{
    public static readonly ParentTestResultLookup NoAccess = new(false, null);
}
