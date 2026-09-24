using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Consumers;

/// <summary>
/// Issue #277 (madde 4): exam API'nin register/complete-profile uçlarında (Teacher/Student/
/// ParentController) Keycloak'ta bir kullanıcının rolünü değiştirdiğinde yazdığı
/// <see cref="UserRoleChangedEvent"/>'i tüketip auth-api'nin kendi <c>Users.Role</c> kolonunu
/// (identity DB) günceller — önceden bu kolon yalnızca login/token-exchange akışında
/// senkronlanıyordu, bir sonraki login'e kadar eski rol görünüyordu.
///
/// Neden auth-api'de (BadgeService'te DEĞİL): yazılan veri (<c>Users.Role</c>) auth-api'nin
/// kendi DB'sinde. Kural (architecture.md, #225 örneğiyle aynı): event'in yazdığı veri hangi
/// serviste ise consumer orada olur.
///
/// Eşleştirme: <see cref="UserRoleChangedEvent.KeycloakId"/> — exam API'nin yerel
/// <c>Users.Id</c>'si auth-api'ninkiyle aynı id uzayında DEĞİL, sayısal eşleştirme yanlış
/// satırı günceller (bkz. event XML yorumu).
///
/// <para>
/// GÜVENLİK (issue #277 review, HIGH): <see cref="UserRoleChangedEvent.NewRole"/> KÖR
/// GÜVENİLMEZ ve doğrudan yazılmaz. Event yalnızca "bu kullanıcı için Keycloak'ı yeniden
/// oku" tetikleyicisi (re-sync trigger) olarak ele alınır — gerçek değer
/// <see cref="IKeycloakService.GetUserRealmRoleNamesAsync"/> ile Keycloak'tan TAZE okunur ve
/// yalnızca <see cref="AllowedRoles"/> allowlist'indeki (Student/Teacher/Parent) bir eşleşme
/// yazılır. Sebep: <c>ChangedAtUtc</c> event üretim anını taşır ama Keycloak'a yazma anıyla
/// aynı sırayı GARANTİ ETMEZ (ör. complete-profile'da Keycloak çağrısı başarısız olup local
/// DB'ye hiç dokunulmadığı, ya da iki eşzamanlı isteğin Keycloak'ta hangisinin son yazdığının
/// event sırasıyla uyuşmadığı senaryolarda "event'in taşıdığı rolü kör yaz" kalıcı bir
/// tutarsızlığa yol açabilirdi). Keycloak'ta app rolü YOKSA (henüz atanmamış / temizlenmiş)
/// <c>Users.Role</c> DOKUNULMAZ, yalnızca loglanır.
/// </para>
///
/// Idempotency / sırasız teslim: BİR "tazelik" kısayolu (event'in <c>ChangedAtUtc</c>'i
/// depolanan <see cref="User.RoleUpdatedAtUtc"/>'tan eskiyse Keycloak'a hiç gitmeden atla)
/// KASITLI OLARAK YOKTUR (issue #277 re-review, LOW-1). Böyle bir kısayol yanlış pozitif
/// üretebilirdi: <c>RoleUpdatedAtUtc</c>, login/complete-profile'ın JWT/istek zaman
/// damgalarından geldiği için Keycloak'a gerçek yazma anıyla SIRALI DEĞİLDİR (ör. eşzamanlı
/// complete-profile ile exam API register yarışı, ya da host'lar arası saat kayması) — event
/// "eski" görünse bile Keycloak'taki GERÇEK durum hâlâ farklı olabilir. Bu yüzden HER event
/// Keycloak'ı YENİDEN OKUR (ucuz, idempotent, doğal olarak sırasız-teslime dayanıklı — hangi
/// sırayla işlenirse işlensin sonuç her zaman Keycloak'taki güncel duruma yakınsar).
/// <see cref="User.RoleUpdatedAtUtc"/> (mikrosaniyeye yuvarlanmış, bkz.
/// <see cref="TruncateToMicroseconds"/>) yalnızca TEŞHİS amaçlı damgalanır ("en son ne zaman
/// senkronlandı"), karşılaştırma/atlama mantığında KULLANILMAZ.
///
/// Hata yolu: KeycloakId'ye ait kullanıcı bulunamazsa (auth-api'nin kendi register akışı
/// henüz o satırı yazmamış olabilir — geçici olabilir) <see cref="UserNotFoundForRoleSyncException"/>
/// fırlatılır; Keycloak geçici hatası (<c>KeycloakException</c>) da fırlatılır → ikisi de
/// <see cref="UserRoleChangedConsumerDefinition"/>: 1s/5s/15s aralıklı 3 retry → hâlâ
/// başarısızsa mesaj <c>auth-api_error</c> (dead-letter) kuyruğuna taşınır, Warning loglanır.
/// Sessiz yutma yok.
/// </summary>
public sealed class UserRoleChangedConsumer : IConsumer<UserRoleChangedEvent>
{
    /// <summary>
    /// Yazılabilecek TEK allowlist — event'ten VEYA Keycloak'tan gelen hiçbir değer bunun
    /// dışında <c>Users.Role</c>'e yazılmaz (issue #277 review L3). <c>KeycloakService</c>'in
    /// kendi <c>AppRoleNames</c>'iyle (private) aynı liste, kasıtlı olarak burada da
    /// tekrarlanır — consumer'ın güvenlik sınırı KeycloakService'in iç detayına bağlı olmamalı.
    /// </summary>
    private static readonly string[] AllowedRoles = { "Student", "Teacher", "Parent" };

    private readonly AppDbContext _db;
    private readonly IKeycloakService _keycloak;
    private readonly ILogger<UserRoleChangedConsumer> _logger;

    public UserRoleChangedConsumer(AppDbContext db, IKeycloakService keycloak, ILogger<UserRoleChangedConsumer> logger)
    {
        _db = db;
        _keycloak = keycloak;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UserRoleChangedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (string.IsNullOrWhiteSpace(e.KeycloakId))
        {
            _logger.LogWarning(
                "UserRoleChanged: KeycloakId boş (EventId={EventId}, UserId={UserId}); atlanıyor.",
                e.EventId, e.UserId);
            return;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.KeycloakId == e.KeycloakId, ct);
        if (user is null)
        {
            // Retry'a bırakılır — auth-api'nin kendi kaydı henüz senkron olmamış olabilir
            // (geçici bir sıralama sorunu). Kalıcıysa (satır hiç yok) dead-letter'a düşer.
            throw new UserNotFoundForRoleSyncException(e.KeycloakId, e.EventId);
        }

        // Event yalnızca tetikleyici — GERÇEK rol her zaman Keycloak'tan taze okunur (yukarıdaki
        // güvenlik notuna bkz.). Geçici Keycloak hatası (KeycloakException) burada YAKALANMAZ,
        // consumer definition'ın retry/dead-letter yoluna düşer.
        var currentRealmRoles = await _keycloak.GetUserRealmRoleNamesAsync(e.KeycloakId, ct);
        var resolvedRole = AllowedRoles.FirstOrDefault(allowed =>
            currentRealmRoles.Any(r => r.Equals(allowed, StringComparison.OrdinalIgnoreCase)));

        var now = TruncateToMicroseconds(DateTime.UtcNow);

        if (resolvedRole is null)
        {
            // Keycloak'ta hiç app rolü yok (henüz atanmamış / temizlenmiş) — Users.Role'e
            // DOKUNULMAZ. RoleUpdatedAtUtc yine de damgalanır — yalnızca TEŞHİS amaçlı
            // ("en son ne zaman senkronlandı"), bir sonraki event'in işlenmesini ETKİLEMEZ
            // (yukarıdaki sınıf yorumu — tazelik kısayolu kasıtlı olarak yok).
            user.RoleUpdatedAtUtc = now;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "UserRoleChanged: Keycloak'ta app rolü yok (KeycloakId={KeycloakId}, EventId={EventId}); " +
                "Users.Role değiştirilmedi.",
                e.KeycloakId, e.EventId);
            return;
        }

        user.Role = resolvedRole;
        user.RoleUpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "UserRoleChanged işlendi (Keycloak'tan taze okunan rol yazıldı). KeycloakId={KeycloakId}, " +
            "EventId={EventId}, ResolvedRole={ResolvedRole}.",
            e.KeycloakId, e.EventId, resolvedRole);
    }

    /// <summary>
    /// Postgres <c>timestamp with time zone</c> mikrosaniye hassasiyetinde saklar; .NET
    /// <see cref="DateTime"/> tick'leri 100ns'dir. DB'den geri okunan bir değer bu yüzden
    /// bellekteki orijinal değerle tick düzeyinde eşleşmeyebilir — karşılaştırma/yazmadan ÖNCE
    /// ikisi de mikrosaniyeye yuvarlanır (issue #277 review NIT2), aksi halde "aynı" iki zaman
    /// damgası &lt;= karşılaştırmasında farklı görünebilir.
    /// </summary>
    internal static DateTime TruncateToMicroseconds(DateTime dt)
    {
        const long ticksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000; // 10
        return new DateTime(dt.Ticks - (dt.Ticks % ticksPerMicrosecond), dt.Kind);
    }
}

/// <summary>
/// <see cref="UserRoleChangedConsumer"/>'ın "kullanıcı henüz auth-api'de yok" hata yolunu
/// beklenmeyen istisnalardan (DB erişilemez vb.) ayırt eden işaret tipi — ikisi de aynı
/// retry/dead-letter davranışını izler ama log seviyesi/mesajı farklı olabilir.
/// </summary>
public sealed class UserNotFoundForRoleSyncException(string keycloakId, Guid eventId)
    : Exception($"UserRoleChanged: KeycloakId={keycloakId} için auth-api'de kullanıcı bulunamadı (EventId={eventId}).")
{
    public string KeycloakId { get; } = keycloakId;
    public Guid EventId { get; } = eventId;
}

/// <summary>
/// Retry'ı yalnızca bu consumer'a scope'lar: 1s, 5s, 15s aralıklı 3 deneme (geçici sıralama
/// sorunu/DB kesintisi/Keycloak geçici hatası) → sonra <c>auth-api_error</c> dead-letter
/// kuyruğu. exam API'nin StudentPointsChangedConsumerDefinition'ıyla aynı desen.
/// </summary>
public sealed class UserRoleChangedConsumerDefinition : ConsumerDefinition<UserRoleChangedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<UserRoleChangedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
