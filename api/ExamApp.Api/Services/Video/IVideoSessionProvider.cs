using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Video;

namespace ExamApp.Api.Services.Video;

/// <summary>Görüşmeye katılan kişinin rolü. Moderatör yetkisini bu belirler.</summary>
public static class VideoParticipantRoles
{
    public const string Teacher = "Teacher";
    public const string Student = "Student";
}

/// <summary>
/// Bir görüşme oturumu talebi. Sağlayıcı bu bilgiden deterministik oda adını ve
/// katılımcıya özel erişim token'ını üretir; kalıcı bir kayıt tutulmaz.
/// </summary>
/// <param name="BookingId">Oda adının türetildiği randevu kimliği.</param>
/// <param name="ParticipantUserId">Çağıranın kullanıcı kimliği (token'daki <c>context.user.id</c>).</param>
/// <param name="ParticipantDisplayName">Odada görünecek ad.</param>
/// <param name="ParticipantRole">"Teacher" | "Student" — bkz. <see cref="VideoParticipantRoles"/>.</param>
/// <param name="StartUtc">Randevu başlangıcı (UTC).</param>
/// <param name="EndUtc">Randevu bitişi (UTC).</param>
/// <param name="WindowClosesAtUtc">
/// Katılım penceresinin kapanış anı (UTC) — randevu bitişi + <c>JoinWindowAfterMinutes</c>.
/// Token bu andan sonrasına sarkmaz; pencere kapandıktan sonra elde kalan bir token'la
/// odaya girilememesini garanti eder.
/// </param>
public record VideoSessionRequest(
    int BookingId,
    int ParticipantUserId,
    string ParticipantDisplayName,
    string ParticipantRole,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime WindowClosesAtUtc);

/// <summary>
/// Sağlayıcıdan bağımsız görüşme oturumu arayüzü (issue #97). Jitsi implementasyonu
/// bu arayüzün arkasındadır; ileride Daily/Twilio'ya geçiş çağıran kodu etkilemez.
/// <para>
/// Yetki/durum kontrolü (randevu onaylı mı, çağıran katılımcı mı, katılım penceresi içinde mi)
/// bu arayüzün sorumluluğu DEĞİLDİR — onu <c>IBookingService</c> yapar. Burası yalnızca
/// "oda adı üret + token imzala".
/// </para>
/// </summary>
public interface IVideoSessionProvider
{
    Task<VideoSessionDto> CreateOrJoinSessionAsync(VideoSessionRequest request, CancellationToken ct = default);
}
