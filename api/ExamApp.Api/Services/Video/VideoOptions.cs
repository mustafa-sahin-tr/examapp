using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Services.Video;

/// <summary>
/// "Video" config bölümü (issue #97). Sağlayıcı seçimi ve katılım penceresi gibi
/// sağlayıcıdan bağımsız ayarlar burada; Jitsi'ye özgü olanlar <see cref="Jitsi"/> altında.
/// </summary>
public class VideoOptions
{
    public const string SectionName = "Video";

    /// <summary>Şimdilik desteklenen tek değer "Jitsi". Farklıysa startup'ta hata fırlatılır.</summary>
    public string Provider { get; set; } = JitsiProviderName;

    public const string JitsiProviderName = "Jitsi";

    /// <summary>Randevu başlangıcından kaç dakika önce odaya girilebilir.</summary>
    [Range(0, 24 * 60)]
    public int JoinWindowBeforeMinutes { get; set; } = 15;

    /// <summary>Randevu bitişinden kaç dakika sonrasına kadar odaya girilebilir.</summary>
    [Range(0, 24 * 60)]
    public int JoinWindowAfterMinutes { get; set; } = 30;

    public JitsiOptions Jitsi { get; set; } = new();
}

/// <summary>
/// Self-host Jitsi (docker-jitsi-meet) ayarları. <see cref="AppId"/>/<see cref="AppSecret"/>
/// prosody'deki <c>JWT_APP_ID</c>/<c>JWT_APP_SECRET</c> ile birebir aynı olmalıdır.
/// Secret'lar appsettings.json'da hiç tutulmaz; yalnızca <c>Video__Jitsi__AppSecret</c> /
/// <c>Video__Jitsi__RoomSecret</c> env değişkenleri (veya user-secrets) ile gelir.
/// <para>
/// DİKKAT: Bu sınıftaki alanlar DataAnnotations ile doğrulanmaz — <c>ValidateDataAnnotations</c>
/// iç içe nesnelere inmez. Doğrulama <c>VideoServiceCollectionExtensions</c> içindeki açık
/// <c>Validate(...)</c> çağrılarında ve <c>JitsiVideoSessionProvider.EnsureConfigured</c>'dedir.
/// </para>
/// </summary>
public class JitsiOptions
{
    /// <summary>Tarayıcının erişebildiği taban adres, ör. "http://localhost:8000".</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>JWT <c>iss</c> claim'i ve prosody app id.</summary>
    public string AppId { get; set; } = "examapp";

    /// <summary>
    /// Prosody'nin VirtualHost'u (docker-jitsi-meet <c>XMPP_DOMAIN</c>), varsayılan "meet.jitsi".
    /// JWT <c>sub</c> claim'i bu değerdir — token doğrulaması <c>sub</c>'ı VirtualHost ile
    /// karşılaştırır, bu yüzden tarayıcının gördüğü public host (<see cref="PublicBaseUrl"/>)
    /// ile karıştırılmamalıdır.
    /// </summary>
    public string XmppDomain { get; set; } = "meet.jitsi";

    /// <summary>
    /// HS256 imza anahtarı (en az 32 karakter). appsettings.json'da tutulmaz —
    /// <c>Video__Jitsi__AppSecret</c> env değişkeni veya user-secrets ile verilir.
    /// </summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// Oda adındaki HMAC son ekinin anahtarı. AppSecret'tan ayrı tutulur; böylece oda adını
    /// tahmin etmek imza anahtarını bilmeyi gerektirmez ve iki secret bağımsız döndürülebilir.
    /// En az 16 karakter; <c>Video__Jitsi__RoomSecret</c> env değişkeni ile verilir.
    /// </summary>
    public string RoomSecret { get; set; } = string.Empty;

    /// <summary>
    /// Üretilen token'ın azami ömrü (dakika). Gerçek <c>exp</c> bununla katılım penceresinin
    /// kapanışından hangisi önceyse odur — bkz. <c>JitsiVideoSessionProvider</c>.
    /// </summary>
    public int TokenLifetimeMinutes { get; set; } = 60;
}
