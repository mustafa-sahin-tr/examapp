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

    /// <summary>Yalnızca öğrencinin KENDİ kod üretiminde (velinin redeem'inde InvalidCode'a katlanır).</summary>
    public const string StudentLimitReached = "StudentLimitReached";

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
}

/// <summary>POST invite-code — düz kod YALNIZCA bu yanıtta bir kez döner.</summary>
public class ParentInviteCodeResultDto : ParentLinkResponseDto
{
    /// <summary>12 karakterlik kod (ayraçsız, büyük harf).</summary>
    public string? Code { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>POST invite-code yanıt gövdesi (başarı).</summary>
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

/// <summary>POST redeem — başarıda oluşan ONAY BEKLEYEN bağlantı (öğrenci verisi yok).</summary>
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
}

/// <summary>Öğrencinin onayını bekleyen veli isteği.</summary>
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

/// <summary>GET my-parents — bağlı veliler, bekleyen istekler + geçerli davet kodunun bitişi (kod kendisi dönmez).</summary>
public class StudentParentLinksDto
{
    public IReadOnlyList<LinkedParentDto> Items { get; set; } = Array.Empty<LinkedParentDto>();

    public IReadOnlyList<PendingParentRequestDto> PendingRequests { get; set; } = Array.Empty<PendingParentRequestDto>();

    /// <summary>Kullanılmamış, süresi dolmamış bir kod varsa bitişi; yoksa null.</summary>
    public DateTime? ActiveInviteExpiresAt { get; set; }

    /// <summary>Aktif + bekleyen bağlantı tavanı.</summary>
    public int MaxActiveParents { get; set; }
}

/// <summary>
/// Velinin çocuğu. <see cref="Status"/> "Active" ise ad, sınıf, okul adı dolu; "Pending" (öğrenci onayı bekleniyor) ise
/// öğrenciye ait HİÇBİR veri dönmez — onaydan önce reşit olmayanın verisi açılmaz.
/// </summary>
public class LinkedChildDto
{
    public int LinkId { get; set; }

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
}
