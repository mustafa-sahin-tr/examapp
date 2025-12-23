namespace FinanceApi.Models.Dtos
{
    public class AllowedCryptoDto
    {
        public string Id { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CoinGeckoId { get; set; } = string.Empty;
        public string YahooSymbol { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }

    public class CreateAllowedCryptoDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? CoinGeckoId { get; set; }
        public string? YahooSymbol { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public class UpdateAllowedCryptoDto
    {
        public string? Name { get; set; }
        public string? CoinGeckoId { get; set; }
        public string? YahooSymbol { get; set; }
        public bool? IsEnabled { get; set; }
    }
}
