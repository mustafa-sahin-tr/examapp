namespace FinanceApi.Models
{
    public class Asset
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

    public enum AssetType
    {
        Stock = 0,      // Hisse Senedi
        USStock = 1,    // ABD Hisse Senedi
        Gold = 2,       // Altın
        Silver = 3,     // Gümüş
        Fund = 4        // Fon
    }
}
