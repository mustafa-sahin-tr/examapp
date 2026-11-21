using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Video;

/// <summary>
/// issue #490: Jitsi odasını prosody'nin stok <c>mod_muc_end_meeting</c> modülüyle kapatır
/// (<c>POST {InternalHttpUrl}/end-meeting?conference={oda}@{MucDomain}</c>, <c>Authorization: Bearer &lt;JWT&gt;</c>).
/// Modül token'ı katılımcı token'larıyla aynı anahtarla (<c>JWT_APP_SECRET</c>) doğrular ve odayı <c>room:destroy</c> ile
/// yok eder; içerideki herkes "toplantı sonlandırıldı" ile düşer. Modül prosody'de <c>GLOBAL_MODULES=muc_end_meeting</c>
/// ile açılır (docker-compose.yml, AppHost). Uç prosody'nin iç HTTP portundadır; jitsi-web nginx'i onu dışarı açmaz.
/// </summary>
public sealed class JitsiVideoRoomCloser : IVideoRoomCloser
{
    /// <summary>Kapatma token'ının ömrü: tek istek için yeter, sızarsa da hemen geçersizleşir.</summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly IOptions<VideoOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JitsiVideoRoomCloser> _logger;

    public JitsiVideoRoomCloser(
        HttpClient http, IOptions<VideoOptions> options, TimeProvider timeProvider, ILogger<JitsiVideoRoomCloser> logger)
    {
        _http = http;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> CloseRoomAsync(int bookingId, CancellationToken ct = default)
    {
        var jitsi = _options.Value.Jitsi;
        if (string.IsNullOrWhiteSpace(jitsi.InternalHttpUrl))
            return false;

        if ((jitsi.AppSecret?.Length ?? 0) < 32 || (jitsi.RoomSecret?.Length ?? 0) < 16)
        {
            _logger.LogWarning("[Video] Oda kapatılamadı: Jitsi secret'ları ayarlı değil. BookingId={BookingId}", bookingId);
            return false;
        }

        var roomName = JitsiVideoSessionProvider.BuildRoomName(bookingId, jitsi.RoomSecret!);
        var conference = $"{roomName}@{jitsi.MucDomain}";
        var url = $"{jitsi.InternalHttpUrl.TrimEnd('/')}/end-meeting?conference={Uri.EscapeDataString(conference)}";

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var service = new Dictionary<string, object>
        {
            ["id"] = "examapp-api",
            ["name"] = "ExamApp",
            ["affiliation"] = "owner",
            ["moderator"] = "true"
        };
        var token = JitsiVideoSessionProvider.SignToken(jitsi, jitsi.XmppDomain, roomName, service, now, now + TokenLifetime);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, ct);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    _logger.LogInformation("[Video] Görüşme odası sunucuda kapatıldı. BookingId={BookingId}", bookingId);
                    return true;
                case HttpStatusCode.NotFound:
                    // Odada kimse yok (oda hiç açılmamış ya da boşalınca prosody silmiş): yapılacak bir şey yok.
                    return false;
                default:
                    _logger.LogWarning("[Video] Oda kapatma isteği reddedildi. BookingId={BookingId} Status={Status}",
                        bookingId, (int)response.StatusCode);
                    return false;
            }
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[Video] Oda kapatma isteği prosody'ye ulaşmadı. BookingId={BookingId}", bookingId);
            return false;
        }
    }
}
