using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinanceApi.Data;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FinanceApi.Services
{
    public class PortfolioService : IPortfolioService
    {
        private readonly FinanceDbContext _context;
        private readonly IExchangeRateService _exchangeRateService;

        public PortfolioService(FinanceDbContext context, IExchangeRateService exchangeRateService)
        {
            _context = context;
            _exchangeRateService = exchangeRateService;
        }

        public async Task<IEnumerable<PortfolioDto>> GetUserPortfolioAsync(string userId)
        {
            var fundTaxRates = await GetFundTaxRateMapAsync(userId);

            var portfolios = await _context.Portfolios
                .AsNoTracking()
                .Include(p => p.Asset)
                .Where(p => p.UserId == userId && p.TotalQuantity > 0)
                .OrderBy(p => p.Asset.Symbol)
                .ToListAsync();

            return portfolios.Select(p => MapPortfolio(p, fundTaxRates)).ToList();
        }

        public async Task<IEnumerable<PortfolioDto>> GetUserPortfolioByTypeAsync(string userId, AssetType type)
        {
            var fundTaxRates = await GetFundTaxRateMapAsync(userId);

            var portfolios = await _context.Portfolios
                .AsNoTracking()
                .Include(p => p.Asset)
                .Where(p => p.UserId == userId && p.TotalQuantity > 0 && p.Asset.Type == type)
                .OrderBy(p => p.Asset.Symbol)
                .ToListAsync();

            return portfolios.Select(p => MapPortfolio(p, fundTaxRates)).ToList();
        }

        public async Task<PortfolioDto?> GetUserPortfolioByAssetAsync(string userId, string assetId)
        {
            var portfolio = await _context.Portfolios
                .AsNoTracking()
                .Include(p => p.Asset)
                .FirstOrDefaultAsync(p => p.UserId == userId && p.AssetId == assetId);

            if (portfolio == null)
            {
                return null;
            }

            var fundTaxRates = await GetFundTaxRateMapAsync(userId);
            return MapPortfolio(portfolio, fundTaxRates);
        }

        public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(string userId, string targetCurrency = "TRY")
        {
            var normalizedCurrency = string.IsNullOrWhiteSpace(targetCurrency)
                ? "TRY"
                : targetCurrency.ToUpperInvariant();

            var fundTaxRates = await GetFundTaxRateMapAsync(userId);

            var portfolioEntities = await _context.Portfolios
                .AsNoTracking()
                .Include(p => p.Asset)
                .Where(p => p.UserId == userId && p.TotalQuantity > 0)
                .ToListAsync();

            var portfolioDtos = portfolioEntities.Select(p => MapPortfolio(p, fundTaxRates)).ToList();

            if (portfolioDtos.Any())
            {
                await ConvertPortfoliosToCurrencyAsync(portfolioDtos, normalizedCurrency);
            }

            // ProfitLoss: net P&L (fonlarda stopaj sonrası)
            var totalValue = portfolioDtos.Sum(p => p.CurrentValue);
            var totalCost = portfolioDtos.Sum(p => p.TotalCost);
            var totalProfitLoss = portfolioDtos.Sum(p => p.ProfitLoss);
            var totalProfitLossPercentage = totalCost > 0 ? (totalProfitLoss / totalCost) * 100 : 0;

            var portfoliosByType = portfolioDtos
                .GroupBy(p => p.AssetType)
                .ToDictionary(g => g.Key, g => g.ToList());

            var lastUpdated = portfolioDtos.Any()
                ? portfolioDtos.Max(p => p.LastUpdated)
                : DateTime.UtcNow;

            return new DashboardSummaryDto
            {
                TotalValue = totalValue,
                TotalCost = totalCost,
                TotalProfitLoss = totalProfitLoss,
                TotalProfitLossPercentage = totalProfitLossPercentage,
                AssetCount = portfolioDtos.Count,
                PortfoliosByType = portfoliosByType,
                LastUpdated = lastUpdated,
                Currency = normalizedCurrency
            };
        }

        public async Task<IEnumerable<AssetTypePerformanceDto>> GetAssetTypePerformanceAsync(string userId)
        {
            var fundTaxRates = await GetFundTaxRateMapAsync(userId);

            var portfolios = await _context.Portfolios
                .AsNoTracking()
                .Include(p => p.Asset)
                .Where(p => p.UserId == userId && p.TotalQuantity > 0)
                .ToListAsync();

            var portfolioDtos = portfolios.Select(p => MapPortfolio(p, fundTaxRates)).ToList();

            return portfolioDtos
                .GroupBy(p => p.AssetType)
                .Select(g => new AssetTypePerformanceDto
                {
                    AssetType = g.Key,
                    TotalValue = g.Sum(p => p.CurrentValue),
                    TotalCost = g.Sum(p => p.TotalCost),
                    TotalProfitLoss = g.Sum(p => p.ProfitLoss),
                    TotalProfitLossPercentage = g.Sum(p => p.TotalCost) > 0
                        ? (g.Sum(p => p.ProfitLoss) / g.Sum(p => p.TotalCost)) * 100
                        : 0,
                    AssetCount = g.Count()
                })
                .ToList();
        }

        public async Task RecalculatePortfolioAsync(string userId, string assetId)
        {
            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.UserId == userId && p.AssetId == assetId);

            if (portfolio == null)
            {
                return;
            }

            // Basit ortalama maliyet: tüm BUY işlemlerinin ortalaması.
            // SELL işlemleri sadece quantity azaltır.
            var buyTransactions = await _context.Transactions
                .Where(t => t.AssetId == assetId && t.UserId == userId && t.Type == TransactionType.BUY)
                .ToListAsync();

            var sellTransactions = await _context.Transactions
                .Where(t => t.AssetId == assetId && t.UserId == userId && t.Type == TransactionType.SELL)
                .ToListAsync();

            if (buyTransactions.Any())
            {
                var totalBuyQuantity = buyTransactions.Sum(t => t.Quantity);
                var totalSellQuantity = sellTransactions.Sum(t => t.Quantity);
                var totalCost = buyTransactions.Sum(t => t.Quantity * t.Price);

                portfolio.TotalQuantity = totalBuyQuantity - totalSellQuantity;
                portfolio.AveragePrice = totalBuyQuantity > 0 ? totalCost / totalBuyQuantity : 0;
            }
            else
            {
                portfolio.TotalQuantity = 0;
                portfolio.AveragePrice = 0;
            }

            if (portfolio.TotalQuantity <= 0)
            {
                _context.Portfolios.Remove(portfolio);
            }

            portfolio.LastUpdated = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task RecalculateAllPortfoliosAsync(string userId)
        {
            var portfolios = await _context.Portfolios
                .Where(p => p.UserId == userId)
                .ToListAsync();

            foreach (var portfolio in portfolios)
            {
                await RecalculatePortfolioAsync(userId, portfolio.AssetId);
            }
        }

        private async Task ConvertPortfoliosToCurrencyAsync(List<PortfolioDto> portfolios, string targetCurrency)
        {
            var rateCache = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (var portfolio in portfolios)
            {
                var sourceCurrency = string.IsNullOrWhiteSpace(portfolio.Currency)
                    ? targetCurrency
                    : portfolio.Currency.ToUpperInvariant();

                if (sourceCurrency == targetCurrency)
                {
                    portfolio.Currency = targetCurrency;
                    continue;
                }

                var currentPrice = await ConvertWithCacheAsync(portfolio.CurrentPrice, sourceCurrency, targetCurrency, rateCache);
                var averagePrice = await ConvertWithCacheAsync(portfolio.AveragePrice, sourceCurrency, targetCurrency, rateCache);

                portfolio.CurrentPrice = Math.Round(currentPrice, 4);
                portfolio.AveragePrice = Math.Round(averagePrice, 4);

                portfolio.TotalCost = Math.Round(portfolio.TotalQuantity * portfolio.AveragePrice, 2);
                portfolio.CurrentValue = Math.Round(portfolio.TotalQuantity * portfolio.CurrentPrice, 2);

                var grossProfitLoss = Math.Round(portfolio.CurrentValue - portfolio.TotalCost, 2);
                var grossProfitLossPercentage = portfolio.TotalCost > 0
                    ? Math.Round((grossProfitLoss / portfolio.TotalCost) * 100, 2)
                    : 0;

                var netProfitLoss = ApplyWithholdingTax(grossProfitLoss, portfolio.AssetType, portfolio.WithholdingTaxRatePercent);
                var netProfitLossPercentage = portfolio.TotalCost > 0
                    ? Math.Round((netProfitLoss / portfolio.TotalCost) * 100, 2)
                    : 0;

                portfolio.GrossProfitLoss = grossProfitLoss;
                portfolio.GrossProfitLossPercentage = grossProfitLossPercentage;
                portfolio.NetProfitLoss = netProfitLoss;
                portfolio.NetProfitLossPercentage = netProfitLossPercentage;

                // Back-compat: UI halen ProfitLoss alanını kullanıyor (net)
                portfolio.ProfitLoss = netProfitLoss;
                portfolio.ProfitLossPercentage = netProfitLossPercentage;

                portfolio.Currency = targetCurrency;
            }
        }

        private async Task<decimal> ConvertWithCacheAsync(
            decimal amount,
            string fromCurrency,
            string toCurrency,
            IDictionary<string, decimal> cache)
        {
            if (amount == 0m || fromCurrency == toCurrency)
            {
                return amount;
            }

            var cacheKey = $"{fromCurrency}->{toCurrency}";

            if (!cache.TryGetValue(cacheKey, out var rate))
            {
                var convertedUnit = await _exchangeRateService.ConvertCurrencyAsync(1m, fromCurrency, toCurrency);
                if (convertedUnit == 0m)
                {
                    return amount;
                }

                rate = convertedUnit;
                cache[cacheKey] = rate;
            }

            return amount * rate;
        }

        private static decimal ApplyWithholdingTax(decimal grossProfitLoss, AssetType assetType, decimal ratePercent)
        {
            if (assetType != AssetType.Fund)
            {
                return grossProfitLoss;
            }

            // Zarar varsa stopaj yok.
            if (grossProfitLoss <= 0m)
            {
                return grossProfitLoss;
            }

            var rate = Math.Clamp(ratePercent, 0m, 100m) / 100m;
            return grossProfitLoss * (1m - rate);
        }

        private async Task<Dictionary<string, decimal>> GetFundTaxRateMapAsync(string userId)
        {
            return await _context.FundTaxRates
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .ToDictionaryAsync(x => x.AssetId, x => x.RatePercent);
        }

        private PortfolioDto MapPortfolio(Portfolio portfolio, IReadOnlyDictionary<string, decimal> fundTaxRates)
        {
            var totalCost = portfolio.TotalCost;
            var currentValue = portfolio.CurrentValue;

            var grossProfitLoss = currentValue - totalCost;
            var grossProfitLossPercentage = totalCost > 0 ? (grossProfitLoss / totalCost) * 100 : 0;

            var rate = 0m;
            if (portfolio.Asset.Type == AssetType.Fund && fundTaxRates.TryGetValue(portfolio.AssetId, out var configuredRate))
            {
                rate = configuredRate;
            }

            var netProfitLoss = ApplyWithholdingTax(grossProfitLoss, portfolio.Asset.Type, rate);
            var netProfitLossPercentage = totalCost > 0 ? (netProfitLoss / totalCost) * 100 : 0;

            return new PortfolioDto
            {
                Id = portfolio.Id,
                AssetId = portfolio.AssetId,
                AssetSymbol = portfolio.Asset.Symbol,
                AssetName = portfolio.Asset.Name,
                AssetType = portfolio.Asset.Type,
                TotalQuantity = portfolio.TotalQuantity,
                AveragePrice = portfolio.AveragePrice,
                CurrentPrice = portfolio.Asset.CurrentPrice,
                CurrentValue = currentValue,
                TotalCost = totalCost,

                GrossProfitLoss = grossProfitLoss,
                GrossProfitLossPercentage = grossProfitLossPercentage,
                WithholdingTaxRatePercent = rate,
                NetProfitLoss = netProfitLoss,
                NetProfitLossPercentage = netProfitLossPercentage,

                ProfitLoss = netProfitLoss,
                ProfitLossPercentage = netProfitLossPercentage,

                Currency = portfolio.Asset.Currency,
                LastUpdated = portfolio.LastUpdated
            };
        }
    }
}
