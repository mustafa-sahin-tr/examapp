using Microsoft.AspNetCore.Mvc;
using FinanceApi.Models.Dtos;
using FinanceApi.Services;

namespace FinanceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssetPricesController : ControllerBase
    {
        private readonly IWebScrapingService _webScrapingService;
        private readonly ILogger<AssetPricesController> _logger;

        public AssetPricesController(IWebScrapingService webScrapingService, ILogger<AssetPricesController> logger)
        {
            _webScrapingService = webScrapingService;
            _logger = logger;
        }

        [HttpPost("bulk")]
        public async Task<ActionResult<List<AssetPriceResponseDto>>> GetAssetPrices([FromBody] List<AssetPriceRequestDto> requests)
        {
            try
            {
                if (requests == null || !requests.Any())
                {
                    return BadRequest("Request list cannot be empty");
                }

                var results = await _webScrapingService.GetAssetPricesAsync(requests);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing bulk asset price request");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("{type}/{code}")]
        public async Task<ActionResult<AssetPriceResponseDto>> GetSingleAssetPrice(string type, string code)
        {
            try
            {
                if (!Enum.TryParse<Models.AssetType>(type, true, out var assetType))
                {
                    return BadRequest($"Invalid asset type: {type}");
                }

                var request = new AssetPriceRequestDto
                {
                    Type = assetType,
                    Symbol = code.ToUpper()
                };

                var result = await _webScrapingService.GetSingleAssetPriceAsync(request);

                if (!result.IsSuccess)
                {
                    return StatusCode(500, result.ErrorMessage);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting single asset price for {Type}:{Code}", type, code);
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("sample")]
        public async Task<ActionResult<List<AssetPriceResponseDto>>> GetSamplePrices()
        {
            try
            {
                var sampleRequests = new List<AssetPriceRequestDto>
                {
                    new() { Type = Models.AssetType.Stock, Symbol = "AKBNK" },
                    new() { Type = Models.AssetType.Stock, Symbol = "TUPRS" },
                    new() { Type = Models.AssetType.USStock, Symbol = "AAPL" },
                    new() { Type = Models.AssetType.Fund, Symbol = "YHS" },
                    new() { Type = Models.AssetType.Gold, Symbol = "GLD" },
                    new() { Type = Models.AssetType.Crypto, Symbol = "BTC" },
                    new() { Type = Models.AssetType.Crypto, Symbol = "ETH" }
                };

                var results = await _webScrapingService.GetAssetPricesAsync(sampleRequests);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sample prices");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
