using FinanceApi.Models;

namespace FinanceApi.Models.Dtos
{
    public class PortfolioDto
    {
        public int AssetId { get; set; }
        public AssetDto? Asset { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal TotalCost { get; set; }
        public decimal ProfitLoss { get; set; }
        public decimal ProfitLossPercentage { get; set; }
        public List<TransactionDto> Transactions { get; set; } = new List<TransactionDto>();
    }

    public class DashboardSummaryDto
    {
        public decimal TotalValue { get; set; }
        public decimal TotalCost { get; set; }
        public decimal TotalProfitLoss { get; set; }
        public decimal TotalProfitLossPercentage { get; set; }
        public Dictionary<AssetType, List<PortfolioDto>> PortfoliosByType { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }

    public class AssetTypePerformanceDto
    {
        public AssetType Type { get; set; }
        public decimal TotalValue { get; set; }
        public decimal TotalCost { get; set; }
        public decimal ProfitLoss { get; set; }
        public decimal ProfitLossPercentage { get; set; }
        public int AssetCount { get; set; }
    }
}
