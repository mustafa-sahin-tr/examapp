using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.ParentLinks;

// Veli–öğrenci bağlantısı (issue #419). Gateway üzerinden /api/exam/parent-links/... (mevcut /api/exam/{everything} route'u).

/// <summary>Hata kodları — UI bunlarla dallanır; metinler <c>parentLinks.errors.*</c> sözlüğünden.</summary>
public static class ParentLinkErrorCodes
{
    /// <summary>
    /// Kod yok / süresi dolmuş / kullanılmış / geçersizlenmiş / biçimi bozuk / öğrencinin veli tavanı dolu — bilinçli olarak
    /// TEK genel hata (kod geçerliliği için kâhin olmasın).
    /// </summary>
    public const string InvalidCode = "InvalidCode";

    /// <summary>Yalnızca birincil velinin ikinci veli kodu üretiminde (velinin redeem'inde InvalidCode'a katlanır).</summary>
    public const string StudentLimitReached = "StudentLimitReached";

    /// <summary>
    /// Issue #436: işlem (ikinci veli kodu, isteği onaylama/reddetme, bağlantı koparma) yalnız BİRİNCİL velinin — çağıran bu
    /// çocuğa Active bağlı ama birincil değil (403). Çocuğa bağlı olmayan çağıran bunu değil 404 alır.
    /// </summary>
    public const string NotPrimaryParent = "NotPrimaryParent";

    /// <summary>
    /// Issue #436: öğrencinin TEK Active velisi kendi bağlantısından ayrılamaz (her öğrencinin velisi olmalı, #437) — 409. Admin
    /// koparabilir; başka Active veli varsa ayrılma serbesttir ve birincillik ona geçer.
    /// </summary>
    public const string LastParentCannotLeave = "LastParentCannotLeave";

    /// <summary>Velinin kendi tavanı — kod aranmadan ÖNCE kontrol edilir (kod hakkında bilgi vermez).</summary>
    public const string ParentLimitReached = "ParentLimitReached";

    /// <summary>Aynı veli bu öğrenciye zaten bağlı ya da onay bekliyor.</summary>
    public const string AlreadyLinked = "AlreadyLinked";

    public const string NotFound = "NotFound";
    public const string ProfileNotFound = "ProfileNotFound";

    /// <summary>Eşzamanlı işlem kilidi zamanında alınamadı / kod üretilemedi (409, tekrar denenebilir).</summary>
    public const string Busy = "Busy";

    /// <summary>Hesap başına günlük başarısız deneme tavanı ya da platform geneli devre kesici (429).</summary>
    public const string RateLimited = "RateLimited";
}

/// <summary>Başarısız işlemin ortak gövdesi.</summary>
public class ParentLinkResponseDto : ResponseBaseDto
{
    public string? ErrorCode { get; set; }

    /// <summary>429 — <see cref="RetryAfterSeconds"/> Retry-After başlığına yazılır.</summary>
    public bool RateLimited { get; set; }

    public int? RetryAfterSeconds { get; set; }

    /// <summary>Issue #436: 403 (<see cref="ParentLinkErrorCodes.NotPrimaryParent"/>).</summary>
    public bool Forbidden { get; set; }
}

/// <summary>POST {linkId}/second-parent-code (issue #436) — düz kod YALNIZCA bu yanıtta bir kez döner.</summary>
public class ParentInviteCodeResultDto : ParentLinkResponseDto
{
    /// <summary>12 karakterlik kod (ayraçsız, büyük harf).</summary>
    public string? Code { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>POST {linkId}/second-parent-code yanıt gövdesi (başarı).</summary>
public sealed record ParentInviteCodeDto(string Code, DateTime ExpiresAt);

/// <summary>
/// POST redeem — velinin girdiği kod. Boşluk/tire tolere edilir, büyük/küçük harf duyarsız. Bilinçli olarak doğrulama
/// attribute'u YOK: boş / çok uzun / bozuk kod model doğrulamasının ProblemDetails'ine değil servisin genel
/// <c>InvalidCode</c> yanıtına düşer ve başarısız deneme sayılır (re-review madde 8).
/// </summary>
public class RedeemParentInviteCodeRequestDto
{
    public string? Code { get; set; }
}

/// <summary>POST redeem — başarıda oluşan, BİRİNCİL VELİNİN onayını bekleyen bağlantı (öğrenci verisi yok).</summary>
public class RedeemParentInviteCodeResultDto : ParentLinkResponseDto
{
    public LinkedChildDto? Child { get; set; }
}

/// <summary>Öğrencinin bağlı velisi — yalnızca ad (e-posta/telefon yok).</summary>
public class LinkedParentDto
{
    public int LinkId { get; set; }

    public string ParentName { get; set; } = string.Empty;

    public DateTime LinkedAt { get; set; }

    /// <summary>Issue #436: öğrencinin birincil velisi (bağlantıları o yönetir).</summary>
    public bool IsPrimary { get; set; }
}

/// <summary>
/// Öğrencinin onayını bekleyen veli isteği — issue #436'dan beri yalnızca geçiş dönemindeki #419 (LegacyV1) istekleri; yeni
/// istekler öğrenciye düşmez.
/// </summary>
public class PendingParentRequestDto
{
    public int LinkId { get; set; }

    public string ParentName { get; set; } = string.Empty;

    /// <summary>Velinin maskeli e-postası (<c>a***@g***.com</c>) — öğrenci isteği tanıyabilsin; çözülemezse boş.</summary>
    public string ParentEmailMasked { get; set; } = string.Empty;

    public DateTime RequestedAt { get; set; }

    /// <summary>Bu ana kadar onaylanmazsa istek düşer.</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// GET my-parents — öğrencinin bağlı velileri (salt okunur, issue #436 "Velim ne görüyor?") + geçiş dönemindeki eski bekleyen
/// istekler. Öğrenci kod üretmez, yeni istek onaylamaz, bağlantı koparmaz.
/// </summary>
public class StudentParentLinksDto
{
    public IReadOnlyList<LinkedParentDto> Items { get; set; } = Array.Empty<LinkedParentDto>();

    /// <summary>Yalnızca geçiş dönemindeki #419 (LegacyV1) istekleri — oluşturulmadan itibaren 30 gün onaylanabilir.</summary>
    public IReadOnlyList<PendingParentRequestDto> PendingRequests { get; set; } = Array.Empty<PendingParentRequestDto>();

    /// <summary>Aktif + bekleyen bağlantı tavanı.</summary>
    public int MaxActiveParents { get; set; }

    /// <summary>Issue #437: bu hesap veli gerektirir (her öğrenci — <c>ParentRequirement</c>). UI velisiz öğrenciye bilgi gösterir.</summary>
    public bool RequiresParent { get; set; }
}

/// <summary>
/// Velinin çocuğu. <see cref="Status"/> "Active" ise ad, sınıf, okul adı dolu; "Pending" (birincil velinin — geçiş dönemindeki
/// eski isteklerde öğrencinin — onayı bekleniyor) ise öğrenciye ait HİÇBİR veri dönmez — onaydan önce reşit olmayanın verisi
/// açılmaz. Issue #436: çağıran bu çocuğun birincil velisiyse <see cref="IsPrimary"/> true ve diğer veliler / bekleyen ikinci
/// veli istekleri (<see cref="CoParents"/>) + geçerli ikinci veli kodunun bitişi döner; birincil olmayana bunlar dönmez.
/// </summary>
public class LinkedChildDto
{
    public int LinkId { get; set; }

    /// <summary>
    /// Issue #420: öğrencinin exam DB id'si — veli paneli uçlarının (<c>api/parent/children/{studentId}/...</c>) anahtarı.
    /// Yalnızca Active iken dolu; Pending'de null (onaydan önce öğrenciye ait hiçbir şey açılmaz).
    /// </summary>
    public int? StudentId { get; set; }

    /// <summary>"Active" | "Pending".</summary>
    public string Status { get; set; } = string.Empty;

    public string? StudentName { get; set; }

    public string? GradeName { get; set; }

    public string? SchoolName { get; set; }

    /// <summary>Active iken bağlanma anı.</summary>
    public DateTime? LinkedAt { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>Pending iken onay süresinin sonu.</summary>
    public DateTime? PendingExpiresAt { get; set; }

    /// <summary>Issue #436: çağıran bu çocuğun birincil velisi (yalnız Active iken true olabilir).</summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Issue #436: yalnız birincil veliye — çocuğun DİĞER Active velileri ve onay bekleyen ikinci veli istekleri; birincil
    /// olmayana ve Pending satıra boş.
    /// </summary>
    public IReadOnlyList<CoParentDto> CoParents { get; set; } = Array.Empty<CoParentDto>();

    /// <summary>Issue #436: yalnız birincil veliye — kullanılmamış, süresi dolmamış ikinci veli kodunun bitişi (kod dönmez).</summary>
    public DateTime? SecondParentCodeExpiresAt { get; set; }

    /// <summary>Öğrenci başına açık (Active + bekleyen) veli tavanı.</summary>
    public int MaxParents { get; set; }

    /// <summary>
    /// Issue #436: yalnız birincil veliye — çocuğun AÇIK veli sayısı (kendisi + diğer Active + tüm bekleyen istekler, geçiş
    /// dönemindeki eski istekler dahil); tavan kontrolü sunucuyla aynı sayımı kullansın. Birincil olmayana null.
    /// </summary>
    public int? OpenParents { get; set; }
}

/// <summary>
/// Issue #436: birincil velinin gördüğü diğer veli. "Active" → ad + bağlanma; "Pending" (ikinci veli isteği) → ad + maskeli
/// e-posta (tanıyabilsin) + istek ve son onay anı. E-posta/telefon açık dönmez.
/// </summary>
public class CoParentDto
{
    public int LinkId { get; set; }

    /// <summary>"Active" | "Pending".</summary>
    public string Status { get; set; } = string.Empty;

    public string ParentName { get; set; } = string.Empty;

    /// <summary>Yalnız Pending: <c>a***@g***.com</c>; çözülemezse boş.</summary>
    public string ParentEmailMasked { get; set; } = string.Empty;

    public DateTime? LinkedAt { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? PendingExpiresAt { get; set; }
}
