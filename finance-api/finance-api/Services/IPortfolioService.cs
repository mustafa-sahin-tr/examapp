using FinanceApi.Models;
using FinanceApi.Models.Dtos;

namespace FinanceApi.Services
{
    public interface IPortfolioService
    {
        Task<IEnumerable<PortfolioDto>> GetUserPortfolioAsync(string userId);
        Task<IEnumerable<PortfolioDto>> GetUserPortfolioByTypeAsync(string userId, AssetType type);
        Task<PortfolioDto?> GetUserPortfolioByAssetAsync(string userId, string assetId);
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(string userId, string targetCurrency = "TRY");
        Task<IEnumerable<AssetTypePerformanceDto>> GetAssetTypePerformanceAsync(string userId);
        Task RecalculatePortfolioAsync(string userId, string assetId);
        Task RecalculateAllPortfoliosAsync(string userId);
    }
}
