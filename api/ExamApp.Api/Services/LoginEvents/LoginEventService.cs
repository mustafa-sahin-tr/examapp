using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.LoginEvents;

/// <summary>
/// Login event'ini satır olarak ekler (issue #84). Normalizasyon: <c>OccurredAtUtc</c> Npgsql'in
/// <c>timestamp with time zone</c> için istediği <c>Kind=Utc</c>'ye çekilir, string alanlar trim'lenir.
///
/// Kural (issue #100): başarılı girişte <c>KeycloakUserId</c> zorunlu (doğrulanmış kimlik); başarısız
/// denemede opsiyonel. <c>AttemptedIdentifier</c> (doğrulanmamış e-posta, PII) yalnızca başarısız
/// denemede saklanır — başarılı girişte kimlik zaten <c>KeycloakUserId</c>'de olduğu için atılır
/// (veri minimizasyonu).
///
/// Geçiş normalizasyonu: deploy sırasında eski auth-api/BadgeService'in ürettiği başarısız deneme
/// mesajları hâlâ e-postayı (veya exchange hatasında <c>"unknown"</c>) <c>KeycloakUserId</c>'de taşır ve
/// <c>AttemptedIdentifier</c> göndermez. Bu satırlar migration'ın taşıdığı eski veriyle aynı biçime çekilir.
/// </summary>
public class LoginEventService : ILoginEventService
{
    private readonly AppDbContext _context;

    public LoginEventService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Başarılı girişte KeycloakUserId eksikse dönen mesaj sözlüğü anahtarı.</summary>
    public const string KeycloakUserIdRequiredOnSuccessKey = "loginEvents.keycloakUserIdRequired";

    /// <summary>Issue #100 öncesi auth-api'nin code-exchange hatasında yazdığı yer tutucu.</summary>
    private const string LegacyUnknownKeycloakUserId = "unknown";

    public async Task<LoginEventRecordResult> RecordAsync(LoginEventCreateDto request, CancellationToken ct = default)
    {
        var keycloakUserId = NullIfBlank(request.KeycloakUserId);
        if (request.Success && keycloakUserId is null)
        {
            return LoginEventRecordResult.Fail(KeycloakUserIdRequiredOnSuccessKey);
        }

        var attemptedIdentifier = request.Success ? null : NullIfBlank(request.AttemptedIdentifier);
        if (!request.Success && attemptedIdentifier is null && keycloakUserId is not null)
        {
            // Eski üretici (issue #100 öncesi): KeycloakUserId alanında doğrulanmamış girdi var.
            attemptedIdentifier = keycloakUserId == LegacyUnknownKeycloakUserId ? null : keycloakUserId;
            keycloakUserId = null;
        }

        var entity = new LoginEvent
        {
            KeycloakUserId = keycloakUserId,
            AttemptedIdentifier = attemptedIdentifier,
            Role = request.Role.Trim(),
            OccurredAtUtc = ToUtc(request.OccurredAtUtc),
            Success = request.Success,
            CreateTime = DateTime.UtcNow
        };

        _context.LoginEvents.Add(entity);
        await _context.SaveChangesAsync(ct);

        return LoginEventRecordResult.Ok(new LoginEventCreatedDto { Id = entity.Id });
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public Task<LoginEvent?> GetPreviousSuccessfulLoginAsync(string keycloakUserId, CancellationToken ct = default)
    {
        // Skip(1): en son başarılı kayıt mevcut oturumdur; ondan bir önceki istenir.
        // Not: login event'i BadgeService üzerinden async yazıldığından, login'in hemen ardından
        // çağrılırsa en son kayıt henüz yazılmamış olabilir (MVP'de kabul edilen sınırlama).
        return _context.LoginEvents
            .AsNoTracking()
            .Where(e => e.KeycloakUserId == keycloakUserId && e.Success)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Skip(1)
            .Take(1)
            .FirstOrDefaultAsync(ct);
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
