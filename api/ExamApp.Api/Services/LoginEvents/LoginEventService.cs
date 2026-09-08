using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

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

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
