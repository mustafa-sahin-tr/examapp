namespace FinanceApi.Models
{
    public class AllowedCrypto
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Symbol { get; set; } = string.Empty;        // e.g. BTC
        public string Name { get; set; } = string.Empty;          // e.g. Bitcoin
        public string CoinGeckoId { get; set; } = string.Empty;   // e.g. bitcoin
        public string YahooSymbol { get; set; } = string.Empty;   // e.g. BTC-USD
        public bool IsEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
