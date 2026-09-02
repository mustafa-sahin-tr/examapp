using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Controllers;

/// <summary>
/// Frontend bildirim zili için okuma / okundu-işaretleme uçları.
/// Gateway üzerinden <c>/api/badge/notifications/...</c> olarak erişilir
/// (mevcut <c>api/badge/{everything}</c> route'u; ekstra Ocelot kaydı gerekmez).
///
/// Sahiplik (IDOR koruması): tüm sorgular çağıranın Keycloak subject'i
/// (<see cref="ClaimTypes.NameIdentifier"/>) ile filtrelenir; path'ten user id alınmaz.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly BadgeDbContext _db;

    public NotificationsController(BadgeDbContext db)
    {
        _db = db;
    }

    public record NotificationDto(
        int Id, string Type, string Title, string Body, string? Data, bool IsRead, DateTime CreatedAt);

    private string? CallerSub => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Çağıranın son bildirimleri (varsayılan: yalnızca okunmamış, en fazla 20).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> GetMineAsync(
        [FromQuery] bool unreadOnly = true,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        var sub = CallerSub;
        if (string.IsNullOrEmpty(sub))
            return Forbid();

        take = Math.Clamp(take, 1, 100);

        var query = _db.Notifications.AsNoTracking().Where(n => n.UserKeycloakId == sub);
        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new NotificationDto(
                n.Id, n.Type, n.Title, n.Body, n.Data, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>Çağıranın okunmamış bildirim sayısı (zil rozeti için).</summary>
    [HttpGet("me/unread-count")]
    public async Task<ActionResult<int>> GetMyUnreadCountAsync(CancellationToken ct)
    {
        var sub = CallerSub;
        if (string.IsNullOrEmpty(sub))
            return Forbid();

        var count = await _db.Notifications.CountAsync(n => n.UserKeycloakId == sub && !n.IsRead, ct);
        return Ok(count);
    }

    /// <summary>Çağırana ait tek bir bildirimi okundu işaretle.</summary>
    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkReadAsync(int id, CancellationToken ct)
    {
        var sub = CallerSub;
        if (string.IsNullOrEmpty(sub))
            return Forbid();

        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);
        // Varlık sızdırmamak için başkasının bildirimi de 404 döner.
        if (notification == null || notification.UserKeycloakId != sub)
            return NotFound();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}
