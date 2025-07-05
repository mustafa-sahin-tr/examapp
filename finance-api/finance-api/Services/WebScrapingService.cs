using HtmlAgilityPack;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceApi.Services
{
    public interface IWebScrapingService
    {
        Task<List<AssetPriceResponseDto>> GetAssetPricesAsync(List<AssetPriceRequestDto> requests);
        Task<AssetPriceResponseDto> GetSingleAssetPriceAsync(AssetPriceRequestDto request);
    }

    public class WebScrapingService : IWebScrapingService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WebScrapingService> _logger;

        public WebScrapingService(HttpClient httpClient, ILogger<WebScrapingService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            // Google Finance için User-Agent ayarı
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        public async Task<List<AssetPriceResponseDto>> GetAssetPricesAsync(List<AssetPriceRequestDto> requests)
        {
            var results = new List<AssetPriceResponseDto>();

            foreach (var request in requests)
            {
                try
                {
                    var result = await GetSingleAssetPriceAsync(request);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scraping price for {Type}:{Code}", request.Type, request.Code);
                    results.Add(new AssetPriceResponseDto
                    {
                        Type = request.Type,
                        Code = request.Code,
                        IsSuccess = false,
                        ErrorMessage = ex.Message
                    });
                }

                // Rate limiting - 1 saniye bekle
                await Task.Delay(1000);
            }

            return results;
        }

        public async Task<AssetPriceResponseDto> GetSingleAssetPriceAsync(AssetPriceRequestDto request)
        {
            return request.Type switch
            {
                AssetType.Stock => await ScrapeGoogleFinanceStock(request.Code, "IST"),
                AssetType.USStock => await ScrapeGoogleFinanceStock(request.Code, "NASDAQ"),
                AssetType.Gold => await ScrapeGoogleFinanceGold(request.Code),
                AssetType.Fund => await ScrapeTefasFund(request.Code),
                _ => new AssetPriceResponseDto
                {
                    Type = request.Type,
                    Code = request.Code,
                    IsSuccess = false,
                    ErrorMessage = "Unsupported asset type"
                }
            };
        }

        private async Task<AssetPriceResponseDto> ScrapeGoogleFinanceStock(string code, string exchange)
        {
            try
            {
                var url = $"https://www.google.com/finance/quote/{code}:{exchange}";
                var response = await _httpClient.GetStringAsync(url);

                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                // Google Finance'ta fiyat için class selector
                var priceNode = doc.DocumentNode.SelectSingleNode("//div[@class='YMlKec fxKbKc']");

                if (priceNode != null)
                {
                    var priceText = priceNode.InnerText.Trim();

                    // Para birimi sembolünü ve fiyatı ayıkla
                    var cleanPrice = Regex.Replace(priceText, @"[^\d,.-]", "");
                    cleanPrice = cleanPrice.Replace(",", ".");

                    if (decimal.TryParse(cleanPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    {
                        // Para birimini belirle
                        var currency = exchange == "IST" ? "TRY" : "USD";

                        return new AssetPriceResponseDto
                        {
                            Type = exchange == "IST" ? AssetType.Stock : AssetType.USStock,
                            Code = code,
                            Price = price,
                            Unit = currency,
                            LastUpdated = DateTime.UtcNow,
                            IsSuccess = true
                        };
                    }
                }

                return new AssetPriceResponseDto
                {
                    Type = exchange == "IST" ? AssetType.Stock : AssetType.USStock,
                    Code = code,
                    IsSuccess = false,
                    ErrorMessage = "Could not parse price from Google Finance"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping Google Finance for {Code}:{Exchange}", code, exchange);
                return new AssetPriceResponseDto
                {
                    Type = exchange == "IST" ? AssetType.Stock : AssetType.USStock,
                    Code = code,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<AssetPriceResponseDto> ScrapeGoogleFinanceGold(string code)
        {
            try
            {
                // Altın için Google Finance URL'i (örnek: GOLD veya GLD)
                var url = $"https://www.google.com/finance/quote/{code}:NASDAQ";
                var response = await _httpClient.GetStringAsync(url);

                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var priceNode = doc.DocumentNode.SelectSingleNode("//div[@class='YMlKec fxKbKc']");

                if (priceNode != null)
                {
                    var priceText = priceNode.InnerText.Trim();
                    var cleanPrice = Regex.Replace(priceText, @"[^\d,.-]", "");
                    cleanPrice = cleanPrice.Replace(",", ".");

                    if (decimal.TryParse(cleanPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    {
                        return new AssetPriceResponseDto
                        {
                            Type = AssetType.Gold,
                            Code = code,
                            Price = price,
                            Unit = "USD",
                            LastUpdated = DateTime.UtcNow,
                            IsSuccess = true
                        };
                    }
                }

                return new AssetPriceResponseDto
                {
                    Type = AssetType.Gold,
                    Code = code,
                    IsSuccess = false,
                    ErrorMessage = "Could not parse gold price from Google Finance"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping gold price for {Code}", code);
                return new AssetPriceResponseDto
                {
                    Type = AssetType.Gold,
                    Code = code,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<AssetPriceResponseDto> ScrapeTefasFund(string code)
        {
            try
            {
                var url = $"https://www.tefas.gov.tr/FonAnaliz.aspx?FonKod={code}";
                var response = await _httpClient.GetStringAsync(url);

                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                // TEFAS'ta fon fiyatı için selector
                var priceNodes = doc.DocumentNode.SelectNodes("//ul[@class='top-list']//li");

                if (priceNodes != null)
                {
                    foreach (var node in priceNodes)
                    {
                        var text = node.InnerText;
                        if (text.Contains("Son Fiyat"))
                        {
                            var spanNode = node.SelectSingleNode(".//span");
                            if (spanNode != null)
                            {
                                var priceText = spanNode.InnerText.Trim();
                                var cleanPrice = priceText.Replace(",", ".");

                                if (decimal.TryParse(cleanPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                                {
                                    return new AssetPriceResponseDto
                                    {
                                        Type = AssetType.Fund,
                                        Code = code,
                                        Price = price,
                                        Unit = "TRY",
                                        LastUpdated = DateTime.UtcNow,
                                        IsSuccess = true
                                    };
                                }
                            }
                            break;
                        }
                    }
                }

                return new AssetPriceResponseDto
                {
                    Type = AssetType.Fund,
                    Code = code,
                    IsSuccess = false,
                    ErrorMessage = "Could not parse fund price from TEFAS"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping TEFAS for fund {Code}", code);
                return new AssetPriceResponseDto
                {
                    Type = AssetType.Fund,
                    Code = code,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
