using System;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Hubs;

public class BadgeNotificationHub : Hub
{
    private readonly ILogger<BadgeNotificationHub> _logger;

    public BadgeNotificationHub(ILogger<BadgeNotificationHub> logger)
    {
        _logger = logger;
        
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR bağlantısı kuruldu. ConnectionId: {ConnectionId}, User: {UserId}",
            Context.ConnectionId,
            Context.User?.Identity?.Name ?? "anon");

        foreach (var claim in Context.User.Claims)
        {
            _logger.LogInformation("Claim: {Type} = {Value}", claim.Type, claim.Value);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("SignalR bağlantısı kapandı. ConnectionId: {ConnectionId}, Reason: {Reason}",
            Context.ConnectionId,
            exception?.Message ?? "normal");

        await base.OnDisconnectedAsync(exception);
    }
}
