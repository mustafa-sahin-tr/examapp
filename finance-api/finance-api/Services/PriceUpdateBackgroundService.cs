using FinanceApi.Data;
using FinanceApi.Hubs;
using FinanceApi.Models.Dtos;
using FinanceApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FinanceApi.Services
{
    public class PriceUpdateBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<PriceUpdateHub> _hubContext;
        private readonly ILogger<PriceUpdateBackgroundService> _logger;
        private readonly Dictionary<string, decimal> _lastPrices = new();
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
                    await UpdatePricesAsync();
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

        private async Task UpdatePricesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var webScrapingService = scope.ServiceProvider.GetRequiredService<IWebScrapingService>();

            try
            {
                // Tüm asset'leri al
                var assets = await context.Assets.ToListAsync();

                if (!assets.Any())
                {
                    _logger.LogDebug("No assets found to update prices");
                    return;
                }

                // Asset'leri price request'lere çevir
                var priceRequests = assets.Select(asset => new AssetPriceRequestDto
                {
                    Type = asset.Type,
                    Symbol = asset.Symbol
                }).ToList();

                _logger.LogDebug("Updating prices for {Count} assets", priceRequests.Count);

                // Fiyatları al
                var priceResponses = await webScrapingService.GetAssetPricesAsync(priceRequests);

                var priceUpdates = new List<PriceUpdateDto>();

                foreach (var priceResponse in priceResponses)
                {
                    if (!priceResponse.IsSuccess)
                    {
                        _logger.LogWarning("Failed to get price for {Type}:{Symbol} - {Error}",
                            priceResponse.Type, priceResponse.Symbol, priceResponse.ErrorMessage);
                        continue;
                    }

                    var asset = assets.FirstOrDefault(a => a.Type == priceResponse.Type && a.Symbol == priceResponse.Symbol);
                    if (asset == null)
                    {
                        _logger.LogWarning("Asset not found for {Type}:{Symbol}", priceResponse.Type, priceResponse.Symbol);
                        continue;
                    }

                    var priceKey = $"{priceResponse.Type}:{priceResponse.Symbol}";
                    var currentPrice = priceResponse.Price;
                    var previousPrice = _lastPrices.GetValueOrDefault(priceKey, currentPrice);

                    // Fiyat değişmişse güncelle
                    if (Math.Abs(currentPrice - previousPrice) > 0.001m)
                    {
                        var change = currentPrice - previousPrice;
                        var changePercent = previousPrice != 0 ? (change / previousPrice) * 100 : 0;

                        var priceUpdate = new PriceUpdateDto
                        {
                            AssetId = asset.Id,
                            Type = asset.Type,
                            Symbol = asset.Symbol,
                            Name = asset.Name,
                            CurrentPrice = currentPrice,
                            PreviousPrice = previousPrice,
                            Change = change,
                            ChangePercent = changePercent,
                            Unit = priceResponse.Unit,
                            LastUpdated = priceResponse.LastUpdated,
                            IsSuccess = true
                        };

                        priceUpdates.Add(priceUpdate);

                        // Asset'in son fiyatını güncelle
                        asset.CurrentPrice = currentPrice;
                        asset.LastUpdated = priceResponse.LastUpdated;

                        // Cache'i güncelle
                        _lastPrices[priceKey] = currentPrice;

                        _logger.LogDebug("Price updated for {Name} ({Symbol}): {PreviousPrice} -> {CurrentPrice} ({Change:+0.00;-0.00;0})",
                            asset.Name, asset.Symbol, previousPrice, currentPrice, change);
                    }
                    else
                    {
                        // İlk kez eklenen asset'ler için cache'e ekle
                        if (!_lastPrices.ContainsKey(priceKey))
                        {
                            _lastPrices[priceKey] = currentPrice;
                        }
                    }
                }

                // Veritabanını güncelle
                if (priceUpdates.Any())
                {
                    await context.SaveChangesAsync();

                    // SignalR ile tüm bağlı client'lara gönder
                    await _hubContext.Clients.All.SendAsync("PriceUpdated", priceUpdates);

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
