using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.LoginEvents;

/// <summary>
/// Login event'ini olduğu gibi satır olarak ekler (issue #84). İş kuralı yok; tek normalizasyon
/// <c>OccurredAtUtc</c>'nin Npgsql'in <c>timestamp with time zone</c> için istediği <c>Kind=Utc</c>'ye çekilmesi.
/// </summary>
public class LoginEventService : ILoginEventService
{
    private readonly AppDbContext _context;

    public LoginEventService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<LoginEventCreatedDto> RecordAsync(LoginEventCreateDto request, CancellationToken ct = default)
    {
        var entity = new LoginEvent
        {
            KeycloakUserId = request.KeycloakUserId.Trim(),
            Role = request.Role.Trim(),
            OccurredAtUtc = ToUtc(request.OccurredAtUtc),
            Success = request.Success,
            CreateTime = DateTime.UtcNow
        };

        _context.LoginEvents.Add(entity);
        await _context.SaveChangesAsync(ct);

        return new LoginEventCreatedDto { Id = entity.Id };
    }

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
