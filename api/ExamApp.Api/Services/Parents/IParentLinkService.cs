using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.ParentLinks;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Veli–öğrenci bağlantısı (issue #419 V1 → issue #436 veli-öncelikli model, epic #435). Bağlantıyı VELİ tarafı kurar; öğrenci
/// onaylamaz, kod üretmez, koparmaz — yalnızca bağlı velilerini görür. Öğrencinin <b>birincil velisi</b> "ikinci veli davet
/// kodu" üretir; ikinci veli kodu girince bağlantı Pending olur, birincil veli onaylayınca Active. Bağlantıyı yalnız birincil
/// veli (ya da admin) koparır; bir veli kendi bağlantısından ayrılabilir / bekleyen isteğini iptal edebilir (tek Active veli ayrılamaz). Geçiş: #419'dan kalan Pending istekleri
/// (LegacyV1) öğrenci 30 gün daha onaylayabilir/reddedebilir. Yalnızca Active bağlantı erişim verir. Sahiplik her metotta
/// kullanıcı id'sinden doğrulanır: çocuğa bağlı olmayan çağırana bağlantı "yok" (404); bağlı ama birincil olmayana 403
/// (NotPrimaryParent). Bağlanma (Active'e geçiş) / aktif bağlantıyı koparma event'leri aynı transaction'da outbox'a yazılır.
/// </summary>
public interface IParentLinkService
{
    /// <summary>Öğrencinin aktif velileri (salt okunur) + geçiş dönemindeki eski bekleyen istekler. Öğrenci kaydı yoksa null.</summary>
    Task<StudentParentLinksDto?> GetStudentParentsAsync(int studentUserId, CancellationToken ct = default);

    /// <summary>
    /// Geçiş dönemi: öğrenci #419'dan kalan (LegacyV1) bekleyen isteği onaylar → Active + ParentLinkedEvent. Yeni (#436) istek,
    /// başkasının / süresi dolmuş istek NotFound — yeni bağlantılar öğrenci onayına düşmez.
    /// </summary>
    Task<ParentLinkResponseDto> ApproveLegacyAsync(int linkId, int studentUserId, CancellationToken ct = default);

    /// <summary>Geçiş dönemi: öğrenci LegacyV1 bekleyen isteği reddeder → Revoked (event yok). Diğer her şey NotFound.</summary>
    Task<ParentLinkResponseDto> RejectLegacyAsync(int linkId, int studentUserId, CancellationToken ct = default);

    /// <summary>
    /// Birincil veli: kendi Active bağlantısındaki (<paramref name="linkId"/>) çocuk için "ikinci veli davet kodu" (tek kullanımlık,
    /// hash'li, 7 gün; çocuk başına tek geçerli kod — öncekiler geçersizlenir). Düz kod yalnızca bu yanıtta döner. Bağlantı
    /// çağıranın değilse / Active değilse NotFound; birincil değilse Forbidden; açık veli tavanı doluysa Conflict (StudentLimitReached).
    /// </summary>
    Task<ParentInviteCodeResultDto> CreateSecondParentCodeAsync(int linkId, int parentUserId, CancellationToken ct = default);

    /// <summary>
    /// Velinin kodu kullanması → birincil velinin onayını bekleyen Pending bağlantı (Origin=InviteCode). Kod herhangi bir nedenle
    /// geçersizse (eski öğrenci kodu, kodu üreten velinin bağlantısı bitmiş, öğrencinin tavanı dolu dahil) TEK genel hata
    /// (InvalidCode) — hangi kısmın tuttuğu söylenmez; başarısızlık hesap/platform sayaçlarına yazılır. Velinin kendi tavanı kod
    /// aranmadan önce kontrol edilir. Veli kaydı yoksa NotFound (ProfileNotFound).
    /// </summary>
    Task<RedeemParentInviteCodeResultDto> RedeemAsync(int parentUserId, string? code, CancellationToken ct = default);

    /// <summary>
    /// Birincil veli bekleyen ikinci veli isteğini onaylar → Active + ParentLinkedEvent. Çağıran çocuğa Active bağlı değilse /
    /// istek yoksa / süresi dolmuşsa NotFound; bağlı ama birincil değilse Forbidden.
    /// </summary>
    Task<ParentLinkResponseDto> ApproveSecondParentAsync(int linkId, int parentUserId, CancellationToken ct = default);

    /// <summary>Birincil veli bekleyen ikinci veli isteğini reddeder → Revoked (event yok). Yetki kuralları onayla aynı.</summary>
    Task<ParentLinkResponseDto> RejectSecondParentAsync(int linkId, int parentUserId, CancellationToken ct = default);

    /// <summary>
    /// Velinin çocukları: Active (ad, sınıf, okul adı; birincilse diğer veliler + bekleyen istekler) + kendi Pending isteği
    /// (öğrenci verisi YOK). Veli kaydı yoksa null.
    /// </summary>
    Task<IReadOnlyList<LinkedChildDto>?> GetParentChildrenAsync(int parentUserId, CancellationToken ct = default);

    /// <summary>
    /// Veli bağlantıyı koparır (soft: Revoked). Birincil veli çocuğun herhangi bir bağlantısını; her veli KENDİ bağlantısını
    /// (ayrılma) ya da bekleyen isteğini. Kendi bağlantısından ayrılan öğrencinin tek Active velisiyse Conflict
    /// (LastParentCannotLeave; her öğrencinin velisi olmalı); birincil ayrılırsa birincillik kalan en eski Active veliye geçer.
    /// Bağlı ama birincil olmayan veli başkasının bağlantısını koparamaz (Forbidden); çocuğa bağlı olmayan NotFound. Zaten
    /// koparılmış bağlantıda idempotent başarı (event yok). Event yalnızca Active koparılınca.
    /// </summary>
    Task<ParentLinkResponseDto> RevokeAsync(int linkId, int parentUserId, CancellationToken ct = default);

    /// <summary>
    /// Admin: herhangi bir bağlantıyı koparır (Active ya da Pending; tek veli dahil) ve <c>AdminUserActionLogs</c>'a yazar — bulunamayan
    /// (NotFound) ve zaten koparılmış (NoChange) hedefler de denetlenir. Birincil koparılırsa birincillik devredilir.
    /// </summary>
    Task<ParentLinkResponseDto> AdminRevokeAsync(int linkId, int adminUserId, string adminKeycloakId, CancellationToken ct = default);
}
