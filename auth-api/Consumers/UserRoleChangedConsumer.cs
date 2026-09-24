using ExamApp.Api.Data;
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
/// Idempotency / sırasız teslim: <see cref="User.RoleUpdatedAtUtc"/> event'in
/// <see cref="UserRoleChangedEvent.ChangedAtUtc"/>'inden daha yeniyse yazma atlanır — aynı
/// mesajın tekrar teslimi (ChangedAtUtc eşit → yazma atlanır) ve sıra dışı teslim (eski event
/// geç gelirse) her ikisi de bu tek koşulla no-op olur. UserPreferredLocaleChangedConsumer
/// (BadgeService) ile birebir aynı desen.
///
/// Hata yolu: KeycloakId'ye ait kullanıcı bulunamazsa (auth-api'nin kendi register akışı
/// henüz o satırı yazmamış olabilir — geçici olabilir) <see cref="UserNotFoundForRoleSyncException"/>
/// fırlatılır → <see cref="UserRoleChangedConsumerDefinition"/>: 1s/5s/15s aralıklı 3 retry →
/// hâlâ bulunamazsa mesaj <c>auth-api_error</c> (dead-letter) kuyruğuna taşınır, Warning loglanır.
/// Beklenmeyen hata (DB erişilemez vb.) aynı retry/dead-letter yoluna fırlatılır. Sessiz yutma yok.
/// </summary>
public sealed class UserRoleChangedConsumer : IConsumer<UserRoleChangedEvent>
{
    private readonly AppDbContext _db;
    private readonly ILogger<UserRoleChangedConsumer> _logger;

    public UserRoleChangedConsumer(AppDbContext db, ILogger<UserRoleChangedConsumer> logger)
    {
        _db = db;
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

        if (user.RoleUpdatedAtUtc is { } lastUpdated && e.ChangedAtUtc <= lastUpdated)
        {
            _logger.LogInformation(
                "UserRoleChanged eski/duplicate (KeycloakId={KeycloakId}, EventId={EventId}, " +
                "EventChangedAt={EventChangedAt}, StoredRoleUpdatedAt={StoredRoleUpdatedAt}); atlanıyor.",
                e.KeycloakId, e.EventId, e.ChangedAtUtc, lastUpdated);
            return;
        }

        user.Role = e.NewRole;
        user.RoleUpdatedAtUtc = e.ChangedAtUtc;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "UserRoleChanged işlendi. KeycloakId={KeycloakId}, EventId={EventId}, NewRole={NewRole}.",
            e.KeycloakId, e.EventId, e.NewRole);
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
/// sorunu/DB kesintisi) → sonra <c>auth-api_error</c> dead-letter kuyruğu. exam API'nin
/// StudentPointsChangedConsumerDefinition'ıyla aynı desen.
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
