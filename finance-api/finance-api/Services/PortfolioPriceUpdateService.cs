using FinanceApi.Data;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FinanceApi.Services
{
    public interface IPortfolioPriceUpdateService
    {
        Task<IReadOnlyList<PriceUpdateDto>> RefreshTrackedPortfolioAssetsAsync(
            string? userId = null,
            CancellationToken cancellationToken = default);
    }

    public class PortfolioPriceUpdateService : IPortfolioPriceUpdateService
    {
        private readonly FinanceDbContext _context;
        private readonly IWebScrapingService _webScrapingService;
        private readonly ILogger<PortfolioPriceUpdateService> _logger;

        public PortfolioPriceUpdateService(
            FinanceDbContext context,
            IWebScrapingService webScrapingService,
            ILogger<PortfolioPriceUpdateService> logger)
        {
            _context = context;
            _webScrapingService = webScrapingService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<PriceUpdateDto>> RefreshTrackedPortfolioAssetsAsync(
            string? userId = null,
            CancellationToken cancellationToken = default)
        {
            var assetIdsQuery = _context.Portfolios
                .Where(p => p.TotalQuantity > 0);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                assetIdsQuery = assetIdsQuery.Where(p => p.UserId == userId);
            }

            var trackedAssetIds = await assetIdsQuery
                .Select(p => p.AssetId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (!trackedAssetIds.Any())
            {
                _logger.LogDebug("No portfolio assets found for price refresh (UserId: {UserId})", userId ?? "ALL");
                return Array.Empty<PriceUpdateDto>();
            }

            var assets = await _context.Assets
                .Where(a => trackedAssetIds.Contains(a.Id))
                .ToListAsync(cancellationToken);

            var assetLookup = assets.ToDictionary(
                asset => BuildKey(asset.Type, asset.Symbol),
                asset => asset,
                StringComparer.OrdinalIgnoreCase);

            var priceRequests = assets.Select(asset => new AssetPriceRequestDto
            {
                Type = asset.Type,
                Symbol = asset.Symbol
            }).ToList();

            _logger.LogDebug("Refreshing prices for {Count} assets (UserId: {UserId})", priceRequests.Count, userId ?? "ALL");

            var priceResponses = await _webScrapingService.GetAssetPricesAsync(priceRequests);
            var updates = new List<PriceUpdateDto>(priceResponses.Count);

            foreach (var priceResponse in priceResponses)
            {
                if (!priceResponse.IsSuccess)
                {
                    _logger.LogWarning("Failed to refresh price for {Type}:{Symbol} - {Error}",
                        priceResponse.Type, priceResponse.Symbol, priceResponse.ErrorMessage);
                    continue;
                }

                var key = BuildKey(priceResponse.Type, priceResponse.Symbol);
                if (!assetLookup.TryGetValue(key, out var asset))
                {
                    _logger.LogWarning("Tracked asset not found for {Type}:{Symbol}", priceResponse.Type, priceResponse.Symbol);
                    continue;
                }

                var previousPrice = asset.CurrentPrice;
                var currentPrice = priceResponse.Price;
                var change = currentPrice - previousPrice;
                var changePercent = previousPrice != 0 ? change / previousPrice * 100 : 0;

                asset.CurrentPrice = currentPrice;
                asset.ChangeValue = change;
                asset.ChangePercentage = changePercent;
                asset.LastUpdated = priceResponse.LastUpdated == default
                    ? DateTime.UtcNow
                    : priceResponse.LastUpdated;

                if (!string.IsNullOrWhiteSpace(priceResponse.Unit))
                {
                    asset.Currency = priceResponse.Unit;
                }

                updates.Add(new PriceUpdateDto
                {
                    AssetId = asset.Id,
                    Type = asset.Type,
                    Symbol = asset.Symbol,
                    Name = asset.Name,
                    CurrentPrice = currentPrice,
                    PreviousPrice = previousPrice,
                    Change = change,
                    ChangePercent = changePercent,
                    Unit = string.IsNullOrWhiteSpace(priceResponse.Unit) ? asset.Currency : priceResponse.Unit,
                    LastUpdated = asset.LastUpdated,
                    IsSuccess = true
                });
            }

            if (updates.Count > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return updates;
        }

        private static string BuildKey(AssetType type, string symbol) => $"{(int)type}|{symbol}";
    }
}
