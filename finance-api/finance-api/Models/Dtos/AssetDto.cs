using FinanceApi.Models;

namespace FinanceApi.Models.Dtos
{
    public class AssetDto
    {
        public string Id { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AssetType Type { get; set; }
        public decimal CurrentPrice { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public decimal ChangePercentage { get; set; }
        public decimal ChangeValue { get; set; }
    }

    public class CreateAssetDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AssetType Type { get; set; }
        public decimal CurrentPrice { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    public class UpdateAssetPriceDto
    {
        public decimal CurrentPrice { get; set; }
        public decimal ChangePercentage { get; set; }
        public decimal ChangeValue { get; set; }
    }
}
