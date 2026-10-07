using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.ParentLinks;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Veli–öğrenci bağlantısı (issue #419, epic #407 V1). Öğrenci tek kullanımlık davet kodu üretir; veli kodu girince bağlantı
/// PENDING açılır ve öğrenci onaylayınca Active olur (review kararı: kod tek başına erişim vermez). Yalnızca Active bağlantı
/// bir şey açar. İki taraf da koparabilir (soft: Revoked). Sahiplik her metotta kullanıcı id'sinden doğrulanır; başkasının
/// bağlantısı "yok" (404) sayılır. Bağlanma (onay) / aktif bağlantıyı koparma event'leri aynı transaction'da outbox'a yazılır.
/// </summary>
public interface IParentLinkService
{
    /// <summary>
    /// Öğrencinin yeni davet kodu: önceki geçerli kodlar geçersizlenir, düz kod yalnızca bu yanıtta döner.
    /// Öğrenci kaydı yoksa NotFound; açık (Active + Pending) veli tavanı doluysa Conflict (StudentLimitReached).
    /// </summary>
    Task<ParentInviteCodeResultDto> CreateInviteCodeAsync(int studentUserId, CancellationToken ct = default);

    /// <summary>Öğrencinin aktif velileri + onay bekleyen istekler (yalnızca ad). Öğrenci kaydı yoksa null.</summary>
    Task<StudentParentLinksDto?> GetStudentParentsAsync(int studentUserId, CancellationToken ct = default);

    /// <summary>Öğrenci bekleyen isteği onaylar → Active + ParentLinkedEvent. Başkasının / süresi dolmuş istek NotFound.</summary>
    Task<ParentLinkResponseDto> ApproveAsync(int linkId, int studentUserId, CancellationToken ct = default);

    /// <summary>Öğrenci bekleyen isteği reddeder → Revoked (event yok). Aktif bağlantı için revoke kullanılır.</summary>
    Task<ParentLinkResponseDto> RejectAsync(int linkId, int studentUserId, CancellationToken ct = default);

    /// <summary>
    /// Velinin kodu kullanması → Pending bağlantı. Kod herhangi bir nedenle geçersizse (öğrencinin tavanı dolu dahil) TEK genel
    /// hata (InvalidCode) — hangi kısmın tuttuğu söylenmez; başarısızlık hesap/platform sayaçlarına yazılır. Velinin kendi
    /// tavanı kod aranmadan önce kontrol edilir. Veli kaydı yoksa NotFound (ProfileNotFound).
    /// </summary>
    Task<RedeemParentInviteCodeResultDto> RedeemAsync(int parentUserId, string? code, CancellationToken ct = default);

    /// <summary>Velinin çocukları: Active (ad, sınıf, okul adı) + Pending (öğrenci verisi YOK). Veli kaydı yoksa null.</summary>
    Task<IReadOnlyList<LinkedChildDto>?> GetParentChildrenAsync(int parentUserId, CancellationToken ct = default);

    /// <summary>
    /// Bağlantıyı koparır (Active ya da Pending) — kullanıcı bağlantının öğrencisi ya da velisi olmalı, değilse NotFound.
    /// Zaten koparılmış kendi bağlantısında idempotent başarı (yeni event yok). Event yalnızca Active koparılınca.
    /// </summary>
    Task<ParentLinkResponseDto> RevokeAsync(int linkId, int userId, CancellationToken ct = default);
}
