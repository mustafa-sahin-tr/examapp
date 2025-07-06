using FinanceApi.Models;

namespace FinanceApi.Models.Dtos
{
    public class AssetPriceRequestDto
    {
        public AssetType Type { get; set; }
        public string Symbol { get; set; } = string.Empty;
    }

    public class AssetPriceResponseDto
    {
        public AssetType Type { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Unit { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
