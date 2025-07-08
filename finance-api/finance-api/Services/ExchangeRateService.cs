using FinanceApi.Models;
using HtmlAgilityPack;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceApi.Services
{
    public interface IExchangeRateService
    {
        Task<ExchangeRate?> GetExchangeRateAsync(string fromCurrency, string toCurrency);
        Task<List<ExchangeRate>> GetAllExchangeRatesAsync();
        Task<decimal> ConvertCurrencyAsync(decimal amount, string fromCurrency, string toCurrency);
        Task UpdateExchangeRatesAsync();
    }

    public class ExchangeRateService : IExchangeRateService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ExchangeRateService> _logger;
        private readonly Dictionary<string, ExchangeRate> _exchangeRateCache = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public ExchangeRateService(HttpClient httpClient, ILogger<ExchangeRateService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<ExchangeRate?> GetExchangeRateAsync(string fromCurrency, string toCurrency)
        {
            if (fromCurrency == toCurrency)
            {
                return new ExchangeRate
                {
                    FromCurrency = fromCurrency,
                    ToCurrency = toCurrency,
                    Rate = 1.0m,
                    LastUpdated = DateTime.UtcNow
                };
            }

            var symbol = $"{fromCurrency}-{toCurrency}";

            if (_exchangeRateCache.ContainsKey(symbol))
            {
                var cachedRate = _exchangeRateCache[symbol];
                // Cache 5 dakika geçerli
                if (DateTime.UtcNow.Subtract(cachedRate.LastUpdated).TotalMinutes < 5)
                {
                    return cachedRate;
                }
            }

            try
            {
                var rate = await FetchExchangeRateFromGoogle(fromCurrency, toCurrency);
                if (rate != null)
                {
                    _exchangeRateCache[symbol] = rate;
                    return rate;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching exchange rate for {Symbol}", symbol);
            }

            return _exchangeRateCache.ContainsKey(symbol) ? _exchangeRateCache[symbol] : null;
        }

        public async Task<List<ExchangeRate>> GetAllExchangeRatesAsync()
        {
            var rates = new List<ExchangeRate>();

            // Ana döviz kurları
            var currencyPairs = new[]
            {
                ("USD", "TRY"),
                ("EUR", "TRY"),
                ("EUR", "USD"),
                ("TRY", "USD"),
            };

            foreach (var (from, to) in currencyPairs)
            {
                var rate = await GetExchangeRateAsync(from, to);
                if (rate != null)
                {
                    rates.Add(rate);
                }
            }

            return rates;
        }

        public async Task<decimal> ConvertCurrencyAsync(decimal amount, string fromCurrency, string toCurrency)
        {
            if (fromCurrency == toCurrency)
                return amount;

            var rate = await GetExchangeRateAsync(fromCurrency, toCurrency);
            if (rate == null)
            {
                _logger.LogWarning("Exchange rate not found for {FromCurrency} to {ToCurrency}", fromCurrency, toCurrency);
                return amount; // Fallback: return original amount
            }

            return amount * rate.Rate;
        }

        public async Task UpdateExchangeRatesAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                _logger.LogInformation("Updating exchange rates...");

                var rates = await GetAllExchangeRatesAsync();

                _logger.LogInformation("Updated {Count} exchange rates", rates.Count);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<ExchangeRate?> FetchExchangeRateFromGoogle(string fromCurrency, string toCurrency)
        {
            try
            {
                var url = $"https://www.google.com/finance/quote/{fromCurrency}-{toCurrency}";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                var response = await _httpClient.GetStringAsync(url);

                var rate = ParseGoogleFinanceResponse(response, fromCurrency, toCurrency);

                if (rate != null)
                {
                    _logger.LogInformation("Successfully fetched exchange rate: {Symbol} = {Rate}",
                        rate.Symbol, rate.Rate);
                }

                return rate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching exchange rate from Google Finance for {FromCurrency}-{ToCurrency}",
                    fromCurrency, toCurrency);
                return null;
            }
        }

        private ExchangeRate? ParseGoogleFinanceResponse(string html, string fromCurrency, string toCurrency)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Google Finance'da kur değeri için class'lar
                var priceSelectors = new[]
                {
                    "//div[@class='YMlKec fxKbKc']",
                    "//div[contains(@class, 'YMlKec')]",
                    "//span[@class='notranslate']",
                    "//div[@data-last-price]"
                };

                foreach (var selector in priceSelectors)
                {
                    var priceNode = doc.DocumentNode.SelectSingleNode(selector);
                    if (priceNode != null)
                    {
                        var priceText = priceNode.InnerText.Trim();
                        if (decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                        {
                            // Değişim yüzdesi için arama
                            var changePercentage = 0m;
                            var changeValue = 0m;

                            var changeNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'JwB6zf')]");
                            if (changeNodes != null)
                            {
                                foreach (var changeNode in changeNodes)
                                {
                                    var changeText = changeNode.InnerText.Trim();
                                    var percentMatch = Regex.Match(changeText, @"([+-]?\d+\.?\d*)%");
                                    if (percentMatch.Success)
                                    {
                                        decimal.TryParse(percentMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out changePercentage);
                                    }

                                    var valueMatch = Regex.Match(changeText, @"([+-]?\d+\.?\d+)");
                                    if (valueMatch.Success)
                                    {
                                        decimal.TryParse(valueMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out changeValue);
                                    }
                                }
                            }

                            return new ExchangeRate
                            {
                                FromCurrency = fromCurrency,
                                ToCurrency = toCurrency,
                                Rate = price,
                                ChangePercentage = changePercentage,
                                ChangeValue = changeValue,
                                LastUpdated = DateTime.UtcNow
                            };
                        }
                    }
                }

                _logger.LogWarning("Could not parse exchange rate from Google Finance HTML for {FromCurrency}-{ToCurrency}",
                    fromCurrency, toCurrency);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Google Finance response for {FromCurrency}-{ToCurrency}",
                    fromCurrency, toCurrency);
                return null;
            }
        }
    }
}
