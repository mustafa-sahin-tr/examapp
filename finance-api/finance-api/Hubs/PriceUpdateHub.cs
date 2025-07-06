using Microsoft.AspNetCore.SignalR;

namespace FinanceApi.Hubs
{
    public class PriceUpdateHub : Hub
    {
        private readonly ILogger<PriceUpdateHub> _logger;

        public PriceUpdateHub(ILogger<PriceUpdateHub> logger)
        {
            _logger = logger;
        }

        public async Task JoinGroup(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
            _logger.LogInformation("User {UserId} joined price update group", userId);
        }

        public async Task LeaveGroup(string userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User_{userId}");
            _logger.LogInformation("User {UserId} left price update group", userId);
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
