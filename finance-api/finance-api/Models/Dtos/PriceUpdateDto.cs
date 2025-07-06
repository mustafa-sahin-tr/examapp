using FinanceApi.Models;

namespace FinanceApi.Models.Dtos
{
    public class PriceUpdateDto
    {
        public string AssetId { get; set; } = string.Empty;
        public AssetType Type { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal CurrentPrice { get; set; }
        public decimal PreviousPrice { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public string Unit { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public bool IsSuccess { get; set; }
    }
}
