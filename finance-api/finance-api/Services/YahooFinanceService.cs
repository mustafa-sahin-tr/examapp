using System.Text.Json;
using FinanceApi.Models;
using FinanceApi.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceApi.Services
{
    public class YahooFinanceService : IRealTimeDataService
    {
        private readonly HttpClient _httpClient;
        private readonly FinanceDbContext _context;
        private readonly ILogger<YahooFinanceService> _logger;

        public YahooFinanceService(HttpClient httpClient, FinanceDbContext context, ILogger<YahooFinanceService> logger)
        {
            _httpClient = httpClient;
            _context = context;
            _logger = logger;
        }

        public async Task<decimal?> GetCurrentPriceAsync(string symbol, string market = "BIST")
        {
            try
            {
                var yahooSymbol = GetYahooSymbol(symbol, market);
                if (string.IsNullOrEmpty(yahooSymbol))
                {
                    _logger.LogWarning($"Symbol mapping not found for {symbol} in {market}");
                    return null;
                }

                // Rate limiting - istekler arasında bekle
                await Task.Delay(1000);

                var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{yahooSymbol}";
                var response = await _httpClient.GetStringAsync(url);

                using var document = JsonDocument.Parse(response);
                var chart = document.RootElement.GetProperty("chart");
                var result = chart.GetProperty("result")[0];
                var meta = result.GetProperty("meta");

                var currentPrice = meta.GetProperty("regularMarketPrice").GetDecimal();
                return currentPrice;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                _logger.LogWarning($"Rate limit exceeded for {symbol}, retrying after delay...");
                await Task.Delay(5000); // 5 saniye bekle
                return await GetCurrentPriceAsync(symbol, market); // Tekrar dene
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching price for {symbol}");
                return null;
            }
        }

        public async Task<List<AssetPriceUpdate>> GetMultiplePricesAsync(List<string> symbols, string market = "BIST")
        {
            var updates = new List<AssetPriceUpdate>();

            try
            {
                var yahooSymbols = symbols
                    .Select(s => GetYahooSymbol(s, market))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (!yahooSymbols.Any())
                {
                    _logger.LogWarning("No valid Yahoo symbols found");
                    return updates;
                }

                // Rate limiting - istekler arasında bekle
                await Task.Delay(1000);

                var symbolsQuery = string.Join(",", yahooSymbols);
                var url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={symbolsQuery}";

                var response = await _httpClient.GetStringAsync(url);
                using var document = JsonDocument.Parse(response);

                var quoteResponse = document.RootElement.GetProperty("quoteResponse");
                var results = quoteResponse.GetProperty("result");

                foreach (var result in results.EnumerateArray())
                {
                    try
                    {
                        var yahooSymbol = result.GetProperty("symbol").GetString() ?? "";
                        var originalSymbol = GetOriginalSymbol(yahooSymbol, market);

                        if (string.IsNullOrEmpty(originalSymbol)) continue;

                        var currentPrice = result.GetProperty("regularMarketPrice").GetDecimal();
                        var previousClose = result.GetProperty("regularMarketPreviousClose").GetDecimal();
                        var changeValue = currentPrice - previousClose;
                        var changePercentage = previousClose != 0 ? (changeValue / previousClose) * 100 : 0;

                        var currency = market == "BIST" ? "TRY" : "USD";
                        if (result.TryGetProperty("currency", out var currencyElement))
                        {
                            currency = currencyElement.GetString() ?? currency;
                        }

                        updates.Add(new AssetPriceUpdate
                        {
                            Symbol = originalSymbol,
                            CurrentPrice = currentPrice,
                            ChangeValue = changeValue,
                            ChangePercentage = changePercentage,
                            LastUpdated = DateTime.UtcNow,
                            Currency = currency
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing individual quote result");
                    }
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                _logger.LogWarning($"Rate limit exceeded for market {market}, retrying after delay...");
                await Task.Delay(5000); // 5 saniye bekle
                return await GetMultiplePricesAsync(symbols, market); // Tekrar dene
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching multiple prices for market {market}");
            }

            return updates;
        }

        public async Task UpdateAssetPricesAsync()
        {
            try
            {
                _logger.LogInformation("Starting asset price update...");

                var assets = await _context.Assets.ToListAsync();
                var groupedAssets = assets.GroupBy(a => a.Type);

                foreach (var group in groupedAssets)
                {
                    var market = GetMarketFromAssetType(group.Key);
                    var symbols = group.Select(a => a.Symbol).ToList();

                    _logger.LogInformation($"Updating {symbols.Count} {market} assets: {string.Join(", ", symbols)}");

                    var updates = await GetMultiplePricesAsync(symbols, market);

                    foreach (var update in updates)
                    {
                        var asset = group.FirstOrDefault(a => a.Symbol == update.Symbol);
                        if (asset != null)
                        {
                            asset.CurrentPrice = update.CurrentPrice;
                            asset.ChangePercentage = update.ChangePercentage;
                            asset.ChangeValue = update.ChangeValue;
                            asset.LastUpdated = update.LastUpdated;

                            _logger.LogInformation($"Updated {asset.Symbol}: {asset.CurrentPrice} {asset.Currency} ({update.ChangePercentage:F2}%)");
                        }
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Asset price update completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during asset price update");
                throw;
            }
        }

        private string GetYahooSymbol(string symbol, string market)
        {
            return market.ToUpper() switch
            {
                "BIST" => $"{symbol}.IS", // Otomatik olarak .IS suffix ekle
                "US" => symbol,           // US stock'ları direkt kullan
                "GOLD" => "GC=F",        // Gold futures
                "SILVER" => "SI=F",      // Silver futures
                _ => symbol
            };
        }

        private string GetOriginalSymbol(string yahooSymbol, string market)
        {
            return market.ToUpper() switch
            {
                "BIST" => yahooSymbol.Replace(".IS", ""),  // .IS suffix'ini kaldır
                "US" => yahooSymbol,
                "GOLD" => "GOLD",
                "SILVER" => "SILVER",
                _ => yahooSymbol
            };
        }

        private string GetMarketFromAssetType(AssetType assetType)
        {
            return assetType switch
            {
                AssetType.Stock => "BIST", // BIST100
                AssetType.USStock => "US",
                AssetType.Gold => "US",
                AssetType.Silver => "US",
                AssetType.Fund => "BIST",
                _ => "BIST"
            };
        }
    }
}
