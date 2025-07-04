using System.Text.Json;

namespace FinanceApi.Services
{
    public interface IRealTimeDataService
    {
        Task UpdateAssetPricesAsync();
        Task<decimal?> GetCurrentPriceAsync(string symbol, string market = "BIST");
        Task<List<AssetPriceUpdate>> GetMultiplePricesAsync(List<string> symbols, string market = "BIST");
    }

    public class AssetPriceUpdate
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal CurrentPrice { get; set; }
        public decimal ChangePercentage { get; set; }
        public decimal ChangeValue { get; set; }
        public DateTime LastUpdated { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}
