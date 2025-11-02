using FinanceApi.Hubs;
using FinanceApi.Services;
using Microsoft.AspNetCore.SignalR;
using FinanceApi.Models.Dtos;

namespace FinanceApi.Services
{
    public class PriceUpdateBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<PriceUpdateHub> _hubContext;
        private readonly ILogger<PriceUpdateBackgroundService> _logger;
        private readonly IConfiguration _configuration;

        public PriceUpdateBackgroundService(
            IServiceProvider serviceProvider,
            IHubContext<PriceUpdateHub> hubContext,
            ILogger<PriceUpdateBackgroundService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Configurasyondan interval'i al (varsayılan 15 saniye)
            var updateIntervalSeconds = _configuration.GetValue<int>("PriceUpdate:IntervalSeconds", 15);
            var interval = TimeSpan.FromSeconds(updateIntervalSeconds);

            _logger.LogInformation("Price update background service started with {Interval} seconds interval", updateIntervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await UpdatePricesAsync(stoppingToken);
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Price update background service is stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in price update background service");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Hata durumunda 5 saniye bekle
                }
            }
        }

        private async Task UpdatePricesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var priceUpdateService = scope.ServiceProvider.GetRequiredService<IPortfolioPriceUpdateService>();

            try
            {
                var priceUpdates = await priceUpdateService.RefreshTrackedPortfolioAssetsAsync(
                    cancellationToken: cancellationToken);

                if (priceUpdates.Any())
                {
                    await _hubContext.Clients.All.SendAsync("PriceUpdated", priceUpdates, cancellationToken);
                    _logger.LogInformation("Sent {Count} price updates to clients", priceUpdates.Count);
                }
                else
                {
                    _logger.LogDebug("No price changes detected");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating prices");
            }
        }
    }
}
