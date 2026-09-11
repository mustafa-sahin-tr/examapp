using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Video;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExamApp.Api.Services.Video;

/// <summary>
/// Self-host Jitsi (docker-jitsi-meet) için <see cref="IVideoSessionProvider"/> implementasyonu (issue #97).
/// <para>
/// Oda adı deterministiktir: <c>booking-{id}-{HMAC-SHA256(bookingId, RoomSecret)[ilk 12 hex]}</c>.
/// Böylece şema değişikliğine gerek kalmadan aynı randevu için her iki taraf da aynı odaya düşer,
/// ama oda adı sıradan bir id tahminiyle bulunamaz.
/// </para>
/// <para>
/// Token, prosody'nin <c>token_verification</c> modülünün beklediği şekildedir:
/// <c>iss</c>=AppId, <c>aud</c>="jitsi", <c>sub</c>=prosody VirtualHost (XmppDomain, ör. "meet.jitsi"),
/// <c>room</c>=oda adı,
/// <c>context.user</c>={id,name,email,affiliation}. Öğretmen <c>affiliation=owner</c> alır (moderatör).
/// </para>
/// Bu sınıf durumsuzdur ve I/O yapmaz; zaman kaynağı test edilebilirlik için
/// <see cref="TimeProvider"/> üzerinden enjekte edilir.
/// </summary>
public sealed class JitsiVideoSessionProvider : IVideoSessionProvider
{
    /// <summary>Jitsi'nin prosody yapılandırmasında beklediği sabit audience.</summary>
    private const string JitsiAudience = "jitsi";

    /// <summary>Oda adına eklenen HMAC son ekinin hex uzunluğu.</summary>
    private const int RoomSuffixHexLength = 12;

    /// <summary>.env.example'daki dev default'ları işaretleyen belirteç — prod'da reddedilir.</summary>
    private const string DevSecretMarker = "ChangeMe";

    private readonly IOptions<VideoOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly IHostEnvironment _environment;

    public JitsiVideoSessionProvider(
        IOptions<VideoOptions> options, TimeProvider timeProvider, IHostEnvironment environment)
    {
        _options = options;
        _timeProvider = timeProvider;
        _environment = environment;
    }

    public Task<VideoSessionDto> CreateOrJoinSessionAsync(VideoSessionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var jitsi = _options.Value.Jitsi;
        EnsureConfigured(jitsi);

        var baseUrl = jitsi.PublicBaseUrl.TrimEnd('/');
        var domain = new Uri(baseUrl, UriKind.Absolute).Authority;

        var roomName = BuildRoomName(request.BookingId, jitsi.RoomSecret);
        var isModerator = string.Equals(request.ParticipantRole, VideoParticipantRoles.Teacher, StringComparison.OrdinalIgnoreCase);

        // Token ömrü katılım penceresini aşamaz: pencere kapandıktan sonra elde kalan bir
        // token'la odaya girilebilmesin. Çok kısa/negatif kalan süre olursa en az 1 dakika verilir
        // (çağıran zaten pencere içinde olduğunu doğrulamış oluyor).
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lifetimeCap = now.AddMinutes(jitsi.TokenLifetimeMinutes);
        var expiresAt = lifetimeCap < request.WindowClosesAtUtc ? lifetimeCap : request.WindowClosesAtUtc;
        if (expiresAt <= now)
            expiresAt = now.AddMinutes(1);

        // JWT "sub" prosody VirtualHost'udur (XMPP_DOMAIN), tarayıcının gördüğü public host değil.
        var token = CreateToken(jitsi, jitsi.XmppDomain, roomName, request, isModerator, now, expiresAt);

        var dto = new VideoSessionDto
        {
            Provider = VideoOptions.JitsiProviderName,
            RoomName = roomName,
            Domain = domain,
            BaseUrl = baseUrl,
            JoinUrl = $"{baseUrl}/{roomName}?jwt={Uri.EscapeDataString(token)}",
            Token = token,
            ExpiresAt = DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc),
            IsModerator = isModerator
        };

        return Task.FromResult(dto);
    }

    /// <summary>
    /// Secret'lar appsettings.json'da tutulmaz; yalnızca <c>Video__Jitsi__AppSecret</c> /
    /// <c>Video__Jitsi__RoomSecret</c> (env veya user-secrets) ile gelir. Eksikse sessizce
    /// zayıf token üretmek yerine burada patlarız.
    /// </summary>
    private void EnsureConfigured(JitsiOptions jitsi)
    {
        // HS256 imzası için anahtar en az 256 bit (32 bayt) olmalıdır.
        if ((jitsi.AppSecret?.Length ?? 0) < 32)
            throw new InvalidOperationException(
                "Video:Jitsi:AppSecret ayarlanmamış veya 32 karakterden kısa. Video__Jitsi__AppSecret ortam değişkenini ayarlayın.");

        if ((jitsi.RoomSecret?.Length ?? 0) < 16)
            throw new InvalidOperationException(
                "Video:Jitsi:RoomSecret ayarlanmamış veya 16 karakterden kısa. Video__Jitsi__RoomSecret ortam değişkenini ayarlayın.");

        // .env.example'daki dev default'ları ile prod'a çıkmak, odaya girecek token'ı herkesin
        // üretebilmesi demektir — Development dışında açıkça reddedilir.
        if (_environment.IsDevelopment())
            return;

        if (jitsi.AppSecret!.Contains(DevSecretMarker, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Video:Jitsi:AppSecret hâlâ dev varsayılanı ('ChangeMe') içeriyor. Development dışındaki ortamlarda gerçek bir secret ayarlanmalıdır.");

        if (jitsi.RoomSecret!.Contains(DevSecretMarker, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Video:Jitsi:RoomSecret hâlâ dev varsayılanı ('ChangeMe') içeriyor. Development dışındaki ortamlarda gerçek bir secret ayarlanmalıdır.");
    }

    /// <summary>
    /// Deterministik, tahmin edilemez oda adı. Yalnızca küçük harf + rakam + tire kullanılır;
    /// Jitsi oda adları URL parçası olduğu için bu karakter kümesi güvenlidir.
    /// </summary>
    internal static string BuildRoomName(int bookingId, string roomSecret)
    {
        var payload = Encoding.UTF8.GetBytes(bookingId.ToString(CultureInfo.InvariantCulture));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(roomSecret));
        var hash = hmac.ComputeHash(payload);

        var suffix = Convert.ToHexString(hash)[..RoomSuffixHexLength].ToLowerInvariant();
        return $"booking-{bookingId}-{suffix}";
    }

    private static string CreateToken(
        JitsiOptions jitsi,
        string xmppDomain,
        string roomName,
        VideoSessionRequest request,
        bool isModerator,
        DateTime now,
        DateTime expiresAt)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jitsi.AppSecret));

        var user = new Dictionary<string, object>
        {
            ["id"] = request.ParticipantUserId.ToString(CultureInfo.InvariantCulture),
            ["name"] = request.ParticipantDisplayName,
            ["affiliation"] = isModerator ? "owner" : "member",
            ["moderator"] = isModerator ? "true" : "false"
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jitsi.AppId,
            Audience = JitsiAudience,
            // "nbf" bir miktar geriye alınır; API ile Jitsi host'u arasındaki küçük saat farkı
            // token'ı "henüz geçerli değil" diye reddettirmesin.
            NotBefore = now.AddMinutes(-5),
            IssuedAt = now,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                // Jitsi'de "sub" kullanıcı değil, prosody VirtualHost'udur (XMPP_DOMAIN).
                ["sub"] = xmppDomain,
                ["room"] = roomName,
                ["context"] = new Dictionary<string, object> { ["user"] = user }
            }
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
