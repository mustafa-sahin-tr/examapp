using HtmlAgilityPack;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;
using System.Text.Json;
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
        private readonly IAllowedCryptoService _allowedCryptoService;

        public WebScrapingService(HttpClient httpClient, ILogger<WebScrapingService> logger, IAllowedCryptoService allowedCryptoService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _allowedCryptoService = allowedCryptoService;

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
                    _logger.LogError(ex, "Error scraping price for {Type}:{Symbol}", request.Type, request.Symbol);
                    results.Add(new AssetPriceResponseDto
                    {
                        Type = request.Type,
                        Symbol = request.Symbol,
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
                AssetType.Stock => await ScrapeGoogleFinanceStock(request.Symbol, "IST"),
                AssetType.USStock => await ScrapeGoogleFinanceStock(request.Symbol, "NASDAQ"),
                AssetType.Gold => await ScrapeGoogleFinanceGold(request.Symbol),
                AssetType.Fund => await ScrapeTefasFund(request.Symbol),
                AssetType.Crypto => await ScrapeCoinGeckoCrypto(request.Symbol),
                _ => new AssetPriceResponseDto
                {
                    Type = request.Type,
                    Symbol = request.Symbol,
                    IsSuccess = false,
                    ErrorMessage = "Unsupported asset type"
                }
            };
        }

        private async Task<AssetPriceResponseDto> ScrapeCoinGeckoCrypto(string symbol)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    return new AssetPriceResponseDto
                    {
                        Type = AssetType.Crypto,
                        Symbol = symbol,
                        IsSuccess = false,
                        ErrorMessage = "Symbol is required"
                    };
                }

                // Accept either CoinGecko id (e.g. "bitcoin") or ticker (e.g. "BTC").
                var normalizedInput = symbol.Trim();
                var allowed = await _allowedCryptoService.FindByAnyKeyAsync(normalizedInput);
                if (allowed == null)
                {
                    return new AssetPriceResponseDto
                    {
                        Type = AssetType.Crypto,
                        Symbol = normalizedInput,
                        IsSuccess = false,
                        ErrorMessage = "Crypto is not allowed"
                    };
                }

                var coinId = allowed.CoinGeckoId;

                var url = $"https://api.coingecko.com/api/v3/simple/price?ids={Uri.EscapeDataString(coinId)}&vs_currencies=usd&include_last_updated_at=true";
                using var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty(coinId, out var coinElement))
                {
                    return new AssetPriceResponseDto
                    {
                        Type = AssetType.Crypto,
                        Symbol = normalizedInput,
                        IsSuccess = false,
                        ErrorMessage = $"Coin not found: {coinId}"
                    };
                }

                if (!coinElement.TryGetProperty("usd", out var priceElement))
                {
                    return new AssetPriceResponseDto
                    {
                        Type = AssetType.Crypto,
                        Symbol = normalizedInput,
                        IsSuccess = false,
                        ErrorMessage = "Could not parse crypto price"
                    };
                }

                var price = priceElement.GetDecimal();

                var lastUpdated = DateTime.UtcNow;
                if (coinElement.TryGetProperty("last_updated_at", out var lastUpdatedElement) &&
                    lastUpdatedElement.ValueKind == JsonValueKind.Number &&
                    lastUpdatedElement.TryGetInt64(out var unixSeconds))
                {
                    lastUpdated = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                }

                return new AssetPriceResponseDto
                {
                    Type = AssetType.Crypto,
                    Symbol = allowed.Symbol,
                    Price = price,
                    Unit = "USD",
                    LastUpdated = lastUpdated,
                    IsSuccess = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping CoinGecko for crypto {Symbol}", symbol);
                return new AssetPriceResponseDto
                {
                    Type = AssetType.Crypto,
                    Symbol = symbol,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<AssetPriceResponseDto> ScrapeGoogleFinanceStock(string symbol, string exchange)
        {
            try
            {
                var url = $"https://www.google.com/finance/quote/{symbol}:{exchange}";
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
                            Symbol = symbol,
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
                    Symbol = symbol,
                    IsSuccess = false,
                    ErrorMessage = "Could not parse price from Google Finance"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping Google Finance for {Symbol}:{Exchange}", symbol, exchange);
                return new AssetPriceResponseDto
                {
                    Type = exchange == "IST" ? AssetType.Stock : AssetType.USStock,
                    Symbol = symbol,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<AssetPriceResponseDto> ScrapeGoogleFinanceGold(string symbol)
        {
            try
            {
                // Altın için Google Finance URL'i - COMEX:GCW00 formatında
                string url;
                if (symbol.Contains(":"))
                {
                    // Zaten exchange:symbol formatında ise direkt kullan
                    url = $"https://www.google.com/finance/quote/{symbol}";
                }
                else
                {
                    // Sadece symbol verilmişse COMEX exchange'i ekle
                    url = $"https://www.google.com/finance/quote/{symbol}:COMEX";
                }

                var response = await _httpClient.GetStringAsync(url);

                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var priceNode = doc.DocumentNode.SelectSingleNode("//div[@class='YMlKec fxKbKc']");

                if (priceNode != null)
                {
                    var priceText = priceNode.InnerText.Trim();
                    var cleanPrice = Regex.Replace(priceText, @"[^\d,.-]", "");
                    // cleanPrice = cleanPrice.Replace(",", ".");

                    if (decimal.TryParse(cleanPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    {
                        return new AssetPriceResponseDto
                        {
                            Type = AssetType.Gold,
                            Symbol = symbol,
                            Price = price - 100,
                            Unit = "USD", // COMEX altın fiyatları USD/oz cinsindendir
                            LastUpdated = DateTime.UtcNow,
                            IsSuccess = true
                        };
                    }
                }

                return new AssetPriceResponseDto
                {
                    Type = AssetType.Gold,
                    Symbol = symbol,
                    IsSuccess = false,
                    ErrorMessage = "Could not parse gold price from Google Finance"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping gold price for {Symbol}", symbol);
                return new AssetPriceResponseDto
                {
                    Type = AssetType.Gold,
                    Symbol = symbol,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<AssetPriceResponseDto> ScrapeTefasFund(string symbol)
        {
            try
            {
                // TEFAS için özel HttpClient oluştur
                using var tefasClient = new HttpClient();

                // Timeout ayarla
                tefasClient.Timeout = TimeSpan.FromSeconds(30);

                // Gerçek tarayıcı davranışını simüle et - daha güncel headers
                tefasClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
                tefasClient.DefaultRequestHeaders.Add("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                tefasClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
                tefasClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                tefasClient.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
                tefasClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Chromium\";v=\"122\", \"Not(A:Brand\";v=\"24\", \"Google Chrome\";v=\"122\"");
                tefasClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
                tefasClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"macOS\"");
                tefasClient.DefaultRequestHeaders.Add("sec-fetch-dest", "document");
                tefasClient.DefaultRequestHeaders.Add("sec-fetch-mode", "navigate");
                tefasClient.DefaultRequestHeaders.Add("sec-fetch-site", "none");
                tefasClient.DefaultRequestHeaders.Add("sec-fetch-user", "?1");
                tefasClient.DefaultRequestHeaders.Add("upgrade-insecure-requests", "1");
                tefasClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

                // Retry mekanizması ile çoklu deneme
                var maxRetries = 3;
                var delay = 1000; // 1 saniye

                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        // Her denemede farklı bir yaklaşım
                        string response = null;

                        if (attempt == 0)
                        {
                            // İlk deneme: Direkt fon sayfasına git
                            var fundUrl = $"https://www.tefas.gov.tr/FonAnaliz.aspx?FonKod={symbol}";
                            response = await tefasClient.GetStringAsync(fundUrl);
                        }
                        else if (attempt == 1)
                        {
                            // İkinci deneme: Önce ana sayfaya git
                            var mainPageUrl = "https://www.tefas.gov.tr/";
                            await tefasClient.GetStringAsync(mainPageUrl);
                            await Task.Delay(800);

                            tefasClient.DefaultRequestHeaders.Remove("Referer");
                            tefasClient.DefaultRequestHeaders.Add("Referer", mainPageUrl);

                            var fundUrl = $"https://www.tefas.gov.tr/FonAnaliz.aspx?FonKod={symbol}";
                            response = await tefasClient.GetStringAsync(fundUrl);
                        }
                        else
                        {
                            // Üçüncü deneme: Alternatif endpoint
                            var alternateUrl = $"https://www.tefas.gov.tr/FonKarsilastirma.aspx?FonKod={symbol}";
                            response = await tefasClient.GetStringAsync(alternateUrl);
                        }

                        // HTML parsing
                        var doc = new HtmlDocument();
                        doc.LoadHtml(response);

                        // TEFAS'ta fon fiyatı için gelişmiş selector'lar
                        decimal? price = await ExtractTefasPrice(doc, symbol);

                        if (price.HasValue)
                        {
                            return new AssetPriceResponseDto
                            {
                                Type = AssetType.Fund,
                                Symbol = symbol,
                                Price = price.Value,
                                Unit = "TRY",
                                LastUpdated = DateTime.UtcNow,
                                IsSuccess = true
                            };
                        }

                        _logger.LogWarning("Attempt {Attempt} failed for TEFAS fund {Symbol}. Retrying...", attempt + 1, symbol);

                        // Sonraki deneme için bekleme
                        if (attempt < maxRetries - 1)
                        {
                            await Task.Delay(delay * (attempt + 1));
                        }
                    }
                    catch (HttpRequestException httpEx)
                    {
                        _logger.LogWarning("HTTP error on attempt {Attempt} for TEFAS fund {Symbol}: {Message}",
                            attempt + 1, symbol, httpEx.Message);

                        if (attempt == maxRetries - 1)
                        {
                            throw; // Son deneme, exception'ı yukarı fırlat
                        }

                        await Task.Delay(delay * (attempt + 1));
                    }
                }

                // Tüm denemeler başarısız
                return new AssetPriceResponseDto
                {
                    Type = AssetType.Fund,
                    Symbol = symbol,
                    IsSuccess = false,
                    ErrorMessage = "Could not retrieve fund price from TEFAS after multiple attempts"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping TEFAS for fund {Symbol}", symbol);
                return new AssetPriceResponseDto
                {
                    Type = AssetType.Fund,
                    Symbol = symbol,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<decimal?> ExtractTefasPrice(HtmlDocument doc, string symbol)
        {
            // Çoklu extraction stratejileri
            var extractionStrategies = new List<Func<HtmlDocument, decimal?>>
            {
            // Strateji 1: TEFAS spesifik yapısından fiyat çekme (main-indicators)
            (document) => {
                var mainIndicators = document.DocumentNode.SelectSingleNode("//div[@class='main-indicators']");
                if (mainIndicators != null)
                {
                var topList = mainIndicators.SelectSingleNode(".//ul[@class='top-list']");
                if (topList != null)
                {
                    var firstLi = topList.SelectSingleNode(".//li[1]");
                    if (firstLi != null)
                    {
                    var priceSpan = firstLi.SelectSingleNode(".//span");
                    if (priceSpan != null)
                    {
                        var price = ExtractPriceFromText(priceSpan.InnerText);
                        if (price.HasValue && price.Value > 0.01m && price.Value < 50000m)
                        return price;
                    }
                    }
                }
                }
                return null;
            },
            
            // Strateji 2: Temel fon bilgilerinden fiyat çekme
            (document) => {
                var priceNodes = document.DocumentNode.SelectNodes("//div[contains(@class,'price')]//span | //span[contains(@class,'price')]");
                if (priceNodes != null)
                {
                foreach (var node in priceNodes)
                {
                    var price = ExtractPriceFromText(node.InnerText);
                    if (price.HasValue && price.Value > 0.01m && price.Value < 50000m)
                    return price;
                }
                }
                return null;
            },
            
            // Strateji 3: Tablo yapısından fiyat çekme
            (document) => {
                var tableNodes = document.DocumentNode.SelectNodes("//table//td | //table//th");
                if (tableNodes != null)
                {
                for (int i = 0; i < tableNodes.Count; i++)
                {
                    var cellText = tableNodes[i].InnerText.Trim();
                    if (cellText.Contains("Son Fiyat") || cellText.Contains("Birim Fiyat") || cellText.Contains("Güncel"))
                    {
                    // Sonraki hücrede fiyat olabilir
                    if (i + 1 < tableNodes.Count)
                    {
                        var priceCell = tableNodes[i + 1];
                        var price = ExtractPriceFromText(priceCell.InnerText);
                        if (price.HasValue && price.Value > 0.01m && price.Value < 50000m)
                        return price;
                    }
                    }
                }
                }
                return null;
            },
            
            // Strateji 4: Label-Value çiftlerinden fiyat çekme
            (document) => {
                var labelNodes = document.DocumentNode.SelectNodes("//label | //span | //div");
                if (labelNodes != null)
                {
                foreach (var node in labelNodes)
                {
                    var text = node.InnerText.Trim();
                    if (text.Contains("Son Fiyat") || text.Contains("Birim Fiyat"))
                    {
                    // Aynı parent içindeki diğer elementlere bak
                    var parent = node.ParentNode;
                    if (parent != null)
                    {
                        var siblings = parent.SelectNodes(".//span | .//div | .//td");
                        if (siblings != null)
                        {
                        foreach (var sibling in siblings)
                        {
                            var price = ExtractPriceFromText(sibling.InnerText);
                            if (price.HasValue && price.Value > 0.01m && price.Value < 50000m)
                            return price;
                        }
                        }
                    }
                    }
                }
                }
                return null;
            },
            
            // Strateji 5: Regex ile tüm sayısal değerleri tara
            (document) => {
                var allText = document.DocumentNode.InnerText;
                var pricePattern = @"(\d{1,2}[,\.]\d{2,4})";
                var matches = Regex.Matches(allText, pricePattern);

                foreach (Match match in matches)
                {
                var price = ExtractPriceFromText(match.Value);
                if (price.HasValue && price.Value > 0.5m && price.Value < 5000m)
                    return price;
                }
                return null;
            }
            };

            // Stratejileri sırayla dene
            foreach (var strategy in extractionStrategies)
            {
                try
                {
                    var price = strategy(doc);
                    if (price.HasValue)
                    {
                        _logger.LogDebug("Successfully extracted price {Price} for fund {Symbol}", price.Value, symbol);
                        return price;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Extraction strategy failed for fund {Symbol}", symbol);
                }
            }

            return null;
        }

        private decimal? ExtractPriceFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var cleanText = text.Trim();

            // Virgülü noktaya çevir
            cleanText = cleanText.Replace(",", ".");

            // Sadece rakam ve nokta bırak
            cleanText = Regex.Replace(cleanText, @"[^\d\.]", "");

            if (decimal.TryParse(cleanText, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            {
                return price;
            }

            return null;
        }
    }
}
