using FinanceApi.Data;
using FinanceApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FinanceApi.Services
{
    public class ProfitLossHistoryService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProfitLossHistoryService> _logger;
        private readonly IConfiguration _configuration;

        public ProfitLossHistoryService(
            IServiceProvider serviceProvider,
            ILogger<ProfitLossHistoryService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 10 dakikalık interval (configurasyondan alınabilir)
            var intervalMinutes = _configuration.GetValue<int>("ProfitLossHistory:IntervalMinutes", 10);
            var interval = TimeSpan.FromSeconds(intervalMinutes);

            _logger.LogInformation("Profit/Loss history service started with {Interval} minutes interval", intervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CalculateAndSaveProfitLossHistory();
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Profit/Loss history service is stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while calculating profit/loss history");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task CalculateAndSaveProfitLossHistory()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var portfolioService = scope.ServiceProvider.GetRequiredService<IPortfolioService>();

            try
            {
                _logger.LogInformation("Starting profit/loss history calculation at {Time}", DateTime.UtcNow);

                // Tüm kullanıcıları al (şu an için test amaçlı tek kullanıcı)
                var users = await dbContext.Transactions
                    .Select(t => t.UserId)
                    .Distinct()
                    .ToListAsync();

                foreach (var userId in users)
                {
                    await CalculateUserProfitLossHistory(dbContext, portfolioService, userId);
                }

                _logger.LogInformation("Profit/loss history calculation completed at {Time}", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in profit/loss history calculation");
            }
        }

        private async Task CalculateUserProfitLossHistory(FinanceDbContext dbContext, IPortfolioService portfolioService, string userId)
        {
            var now = DateTime.UtcNow;
            var portfolios = await portfolioService.GetUserPortfolioAsync(userId);

            if (!portfolios.Any())
            {
                _logger.LogInformation("No portfolios found for user {UserId}", userId);
                return;
            }

            // Ana kar/zarar hesaplama
            var totalProfitLoss = 0m;
            var totalInvestment = 0m;
            var totalCurrentValue = 0m;

            var assetTypeProfitLosses = new List<AssetTypeProfitLoss>();
            var assetProfitLosses = new List<AssetProfitLoss>();

            // Asset tipi bazında grupla
            var portfoliosByAssetType = portfolios
                .GroupBy(p => p.AssetType)
                .ToList();

            foreach (var assetTypeGroup in portfoliosByAssetType)
            {
                var assetType = assetTypeGroup.Key;
                var assetTypeInvestment = 0m;
                var assetTypeCurrentValue = 0m;
                var assetTypeProfitLoss = 0m;

                foreach (var portfolio in assetTypeGroup)
                {
                    var investment = portfolio.TotalQuantity * portfolio.AveragePrice;
                    var currentValue = portfolio.TotalQuantity * portfolio.CurrentPrice;
                    var profitLoss = currentValue - investment;

                    assetTypeInvestment += investment;
                    assetTypeCurrentValue += currentValue;
                    assetTypeProfitLoss += profitLoss;

                    totalInvestment += investment;
                    totalCurrentValue += currentValue;
                    totalProfitLoss += profitLoss;
                }

                var assetTypeProfitLossPercentage = assetTypeInvestment > 0 ? (assetTypeProfitLoss / assetTypeInvestment) * 100 : 0;

                assetTypeProfitLosses.Add(new AssetTypeProfitLoss
                {
                    AssetType = assetType,
                    ProfitLoss = assetTypeProfitLoss,
                    Investment = assetTypeInvestment,
                    CurrentValue = assetTypeCurrentValue,
                    ProfitLossPercentage = assetTypeProfitLossPercentage,
                    AssetCount = assetTypeGroup.Count()
                });
            }

            // Bireysel asset kar/zarar hesaplama
            foreach (var portfolio in portfolios)
            {
                var investment = portfolio.TotalQuantity * portfolio.AveragePrice;
                var currentValue = portfolio.TotalQuantity * portfolio.CurrentPrice;
                var profitLoss = currentValue - investment;
                var profitLossPercentage = investment > 0 ? (profitLoss / investment) * 100 : 0;

                assetProfitLosses.Add(new AssetProfitLoss
                {
                    AssetId = portfolio.AssetId,
                    ProfitLoss = profitLoss,
                    Investment = investment,
                    CurrentValue = currentValue,
                    ProfitLossPercentage = profitLossPercentage,
                    Quantity = portfolio.TotalQuantity,
                    AveragePrice = portfolio.AveragePrice,
                    CurrentPrice = portfolio.CurrentPrice
                });
            }

            var totalProfitLossPercentage = totalInvestment > 0 ? (totalProfitLoss / totalInvestment) * 100 : 0;

            // Ana kar/zarar geçmişi kaydı
            var profitLossHistory = new ProfitLossHistory
            {
                UserId = userId,
                Timestamp = now,
                TotalProfitLoss = totalProfitLoss,
                TotalInvestment = totalInvestment,
                TotalCurrentValue = totalCurrentValue,
                ProfitLossPercentage = totalProfitLossPercentage,
                AssetTypeBreakdown = JsonSerializer.Serialize(assetTypeProfitLosses.Select(atp => new
                {
                    AssetType = atp.AssetType.ToString(),
                    ProfitLoss = atp.ProfitLoss,
                    Investment = atp.Investment,
                    CurrentValue = atp.CurrentValue,
                    ProfitLossPercentage = atp.ProfitLossPercentage,
                    AssetCount = atp.AssetCount
                })),
                Date = now.Date,
                Hour = now.Hour
            };

            // Veritabanına kaydet
            await dbContext.ProfitLossHistories.AddAsync(profitLossHistory);
            await dbContext.SaveChangesAsync();

            // Asset tipi bazında detayları kaydet
            foreach (var assetTypeProfitLoss in assetTypeProfitLosses)
            {
                assetTypeProfitLoss.ProfitLossHistoryId = profitLossHistory.Id;
                await dbContext.AssetTypeProfitLosses.AddAsync(assetTypeProfitLoss);
            }

            // Bireysel asset detaylarını kaydet
            foreach (var assetProfitLoss in assetProfitLosses)
            {
                assetProfitLoss.ProfitLossHistoryId = profitLossHistory.Id;
                await dbContext.AssetProfitLosses.AddAsync(assetProfitLoss);
            }

            await dbContext.SaveChangesAsync();

            _logger.LogInformation("Saved profit/loss history for user {UserId}: Total P/L: {ProfitLoss:C}, Percentage: {Percentage:F2}%",
                userId, totalProfitLoss, totalProfitLossPercentage);
        }
    }
}
