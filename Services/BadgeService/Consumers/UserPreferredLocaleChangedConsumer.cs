using BadgeService.Entities;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Dil tercihi akışının tüketici ucu (issue #185): auth-api'nin kullanıcı kaydı ve
/// <c>PUT /me/locale</c> akışlarında yazdığı <see cref="UserPreferredLocaleChangedEvent"/>'i
/// alır ve <see cref="UserLocalePreference"/> tablosuna upsert eder. Senkron auth-api çağrısı
/// yoktur; bildirim üretimi bu yerel kopyayı <see cref="IUserLocaleResolver"/> üzerinden okur.
///
/// Idempotency: doğal anahtar <c>UserId</c> (PK) üzerinden upsert. Aynı mesaj tekrar
/// gelirse ya da eski/yeni event'ler sırasız teslim edilirse, kayıttaki
/// <see cref="UserLocalePreference.UpdatedAtUtc"/> event'in <c>ChangedAtUtc</c>'inden daha
/// yeniyse yazma atlanır (out-of-order no-op) — böylece geç gelen eski bir event, sonradan
/// işlenmiş daha yeni bir tercihi geri almaz.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry (bkz.
/// <see cref="UserPreferredLocaleChangedConsumerDefinition"/>) → hâlâ başarısızsa mesaj
/// <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır. Sessiz yutma yok.
/// </summary>
public class UserPreferredLocaleChangedConsumer : IConsumer<UserPreferredLocaleChangedEvent>
{
    private readonly BadgeDbContext _db;
    private readonly ILogger<UserPreferredLocaleChangedConsumer> _logger;

    public UserPreferredLocaleChangedConsumer(BadgeDbContext db, ILogger<UserPreferredLocaleChangedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UserPreferredLocaleChangedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var locale = SupportedLocales.Normalize(e.PreferredLocale);

        var existing = await _db.UserLocalePreferences.FirstOrDefaultAsync(p => p.UserId == e.UserId, ct);

        if (existing is null)
        {
            _db.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = e.UserId,
                KeycloakId = string.IsNullOrWhiteSpace(e.KeycloakId) ? null : e.KeycloakId,
                Locale = locale,
                UpdatedAtUtc = e.ChangedAtUtc
            });
        }
        else if (e.ChangedAtUtc > existing.UpdatedAtUtc)
        {
            existing.KeycloakId = string.IsNullOrWhiteSpace(e.KeycloakId) ? existing.KeycloakId : e.KeycloakId;
            existing.Locale = locale;
            existing.UpdatedAtUtc = e.ChangedAtUtc;
        }
        else
        {
            _logger.LogInformation(
                "UserPreferredLocaleChanged eski/duplicate (UserId={UserId}, EventChangedAt={EventChangedAt}, StoredUpdatedAt={StoredUpdatedAt}); atlanıyor.",
                e.UserId, e.ChangedAtUtc, existing.UpdatedAtUtc);
            return;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Eşzamanlı ikinci teslim (ilk INSERT'i) PK'ya takıldı — idempotent no-op.
            _logger.LogInformation(
                "UserPreferredLocaleChanged eşzamanlı duplicate (UserId={UserId}); atlanıyor.", e.UserId);
            return;
        }

        _logger.LogInformation(
            "UserPreferredLocaleChanged işlendi. UserId={UserId}, Locale={Locale}", e.UserId, locale);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
