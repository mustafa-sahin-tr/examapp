using Microsoft.EntityFrameworkCore;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;
using FinanceApi.Data;

namespace FinanceApi.Services
{
    public class PortfolioService : IPortfolioService
    {
        private readonly FinanceDbContext _context;

        public PortfolioService(FinanceDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<PortfolioDto>> GetUserPortfolioAsync(string userId)
        {
            var portfolios = await _context.Portfolios
                .Include(p => p.Asset)
                .Where(p => p.UserId == userId && p.TotalQuantity > 0)
                .ToListAsync();

            return portfolios.Select(p => new PortfolioDto
            {
                Id = p.Id,
                AssetId = p.AssetId,
                AssetSymbol = p.Asset.Symbol,
                AssetName = p.Asset.Name,
                AssetType = p.Asset.Type,
                TotalQuantity = p.TotalQuantity,
                AveragePrice = p.AveragePrice,
                CurrentPrice = p.Asset.CurrentPrice,
                CurrentValue = p.CurrentValue,
                TotalCost = p.TotalCost,
                ProfitLoss = p.ProfitLoss,
                ProfitLossPercentage = p.ProfitLossPercentage,
                Currency = p.Asset.Currency,
                LastUpdated = p.LastUpdated
            });
        }

        public async Task<IEnumerable<PortfolioDto>> GetUserPortfolioByTypeAsync(string userId, AssetType type)
        {
            var portfolios = await _context.Portfolios
                .Include(p => p.Asset)
                .Where(p => p.UserId == userId && p.Asset.Type == type && p.TotalQuantity > 0)
                .ToListAsync();

            return portfolios.Select(p => new PortfolioDto
            {
                Id = p.Id,
                AssetId = p.AssetId,
                AssetSymbol = p.Asset.Symbol,
                AssetName = p.Asset.Name,
                AssetType = p.Asset.Type,
                TotalQuantity = p.TotalQuantity,
                AveragePrice = p.AveragePrice,
                CurrentPrice = p.Asset.CurrentPrice,
                CurrentValue = p.CurrentValue,
                TotalCost = p.TotalCost,
                ProfitLoss = p.ProfitLoss,
                ProfitLossPercentage = p.ProfitLossPercentage,
                Currency = p.Asset.Currency,
                LastUpdated = p.LastUpdated
            });
        }

        public async Task<PortfolioDto?> GetUserPortfolioByAssetAsync(string userId, string assetId)
        {
            var portfolio = await _context.Portfolios
                .Include(p => p.Asset)
                .FirstOrDefaultAsync(p => p.UserId == userId && p.AssetId == assetId);

            if (portfolio == null)
                return null;

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
                CurrentValue = portfolio.CurrentValue,
                TotalCost = portfolio.TotalCost,
                ProfitLoss = portfolio.ProfitLoss,
                ProfitLossPercentage = portfolio.ProfitLossPercentage,
                Currency = portfolio.Asset.Currency,
                LastUpdated = portfolio.LastUpdated
            };
        }

        public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(string userId)
        {
            var portfolios = await _context.Portfolios
                .Include(p => p.Asset)
                .Where(p => p.UserId == userId && p.TotalQuantity > 0)
                .ToListAsync();

            var totalValue = portfolios.Sum(p => p.CurrentValue);
            var totalCost = portfolios.Sum(p => p.TotalCost);
            var totalProfitLoss = portfolios.Sum(p => p.ProfitLoss);
            var totalProfitLossPercentage = totalCost > 0 ? (totalProfitLoss / totalCost) * 100 : 0;

            return new DashboardSummaryDto
            {
                TotalValue = totalValue,
                TotalCost = totalCost,
                TotalProfitLoss = totalProfitLoss,
                TotalProfitLossPercentage = totalProfitLossPercentage,
                AssetCount = portfolios.Count()
            };
        }

        public async Task<IEnumerable<AssetTypePerformanceDto>> GetAssetTypePerformanceAsync(string userId)
        {
            var portfolios = await _context.Portfolios
                .Include(p => p.Asset)
                .Where(p => p.UserId == userId && p.TotalQuantity > 0)
                .ToListAsync();

            var assetTypeGroups = portfolios
                .GroupBy(p => p.Asset.Type)
                .Select(g => new AssetTypePerformanceDto
                {
                    AssetType = g.Key,
                    TotalValue = g.Sum(p => p.CurrentValue),
                    TotalCost = g.Sum(p => p.TotalCost),
                    TotalProfitLoss = g.Sum(p => p.ProfitLoss),
                    TotalProfitLossPercentage = g.Sum(p => p.TotalCost) > 0 ? (g.Sum(p => p.ProfitLoss) / g.Sum(p => p.TotalCost)) * 100 : 0,
                    AssetCount = g.Count()
                });

            return assetTypeGroups;
        }

        public async Task RecalculatePortfolioAsync(string userId, string assetId)
        {
            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.UserId == userId && p.AssetId == assetId);

            if (portfolio == null)
                return;

            // Tüm BUY transaction'larını al ve average price'ı yeniden hesapla
            var buyTransactions = await _context.Transactions
                .Where(t => t.AssetId == assetId &&
                           t.UserId == userId &&
                           t.Type == TransactionType.BUY)
                .ToListAsync();

            var sellTransactions = await _context.Transactions
                .Where(t => t.AssetId == assetId &&
                           t.UserId == userId &&
                           t.Type == TransactionType.SELL)
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

            // Eğer quantity sıfır veya negatif olursa portfolio'yu sil
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
    }
}
