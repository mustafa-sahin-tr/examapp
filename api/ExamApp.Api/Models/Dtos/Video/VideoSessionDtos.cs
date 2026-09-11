using System;

namespace ExamApp.Api.Models.Dtos.Video;

/// <summary>
/// Bir görüşme odasına katılmak için istemcinin ihtiyaç duyduğu her şey (issue #97).
/// Sağlayıcıdan bağımsız şekildedir — Jitsi'ye özgü hiçbir alan yoktur, ileride
/// Daily/Twilio implementasyonu da aynı şekli doldurabilir.
/// </summary>
public class VideoSessionDto
{
    /// <summary>Odayı üreten sağlayıcı. Şimdilik her zaman "Jitsi".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Deterministik oda adı — aynı booking için her zaman aynı değer üretilir.</summary>
    public string RoomName { get; set; } = string.Empty;

    /// <summary>Sağlayıcı host'u, şemasız (ör. "localhost:8000"). Gömülü istemci SDK'ları bunu ister.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Şemayla birlikte taban adres, ör. "http://localhost:8000".</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Doğrudan tarayıcıda açılabilecek tam katılım adresi (token dahil).</summary>
    public string JoinUrl { get; set; } = string.Empty;

    /// <summary>Odaya giriş için üretilmiş kısa ömürlü JWT. IFrame API kullanan istemciler bunu ayrıca verir.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Token'ın geçerlilik sonu (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Çağıran bu odada moderatör mü (öğretmen) — UI farklı kontrol gösterebilir.</summary>
    public bool IsModerator { get; set; }
}

/// <summary>
/// Servisten controller'a görüşme oturumu sonucu. ResponseBaseDto bayrakları HTTP koduna eşlenir
/// (404 randevu yok, 403 katılımcı değil, 409 onaysız randevu veya katılım penceresi dışı).
/// </summary>
public class VideoSessionResultDto : ResponseBaseDto
{
    public VideoSessionDto? Session { get; set; }
}
