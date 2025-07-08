using FinanceApi.Models;
using FinanceApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExchangeRateController : ControllerBase
    {
        private readonly IExchangeRateService _exchangeRateService;
        private readonly ILogger<ExchangeRateController> _logger;

        public ExchangeRateController(IExchangeRateService exchangeRateService, ILogger<ExchangeRateController> logger)
        {
            _exchangeRateService = exchangeRateService;
            _logger = logger;
        }

        /// <summary>
        /// Tüm döviz kurlarını döndürür
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<ExchangeRate>>> GetAllExchangeRates()
        {
            try
            {
                var rates = await _exchangeRateService.GetAllExchangeRatesAsync();
                return Ok(rates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all exchange rates");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Belirli bir döviz kuru döndürür
        /// </summary>
        [HttpGet("{fromCurrency}/{toCurrency}")]
        public async Task<ActionResult<ExchangeRate>> GetExchangeRate(string fromCurrency, string toCurrency)
        {
            try
            {
                var rate = await _exchangeRateService.GetExchangeRateAsync(fromCurrency.ToUpper(), toCurrency.ToUpper());
                if (rate == null)
                {
                    return NotFound($"Exchange rate for {fromCurrency}-{toCurrency} not found");
                }
                return Ok(rate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting exchange rate for {FromCurrency}-{ToCurrency}", fromCurrency, toCurrency);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Para birimi dönüştürme
        /// </summary>
        [HttpPost("convert")]
        public async Task<ActionResult<decimal>> ConvertCurrency([FromBody] CurrencyConversionRequest request)
        {
            try
            {
                var convertedAmount = await _exchangeRateService.ConvertCurrencyAsync(
                    request.Amount,
                    request.FromCurrency.ToUpper(),
                    request.ToCurrency.ToUpper());

                return Ok(new CurrencyConversionResponse
                {
                    OriginalAmount = request.Amount,
                    ConvertedAmount = convertedAmount,
                    FromCurrency = request.FromCurrency.ToUpper(),
                    ToCurrency = request.ToCurrency.ToUpper(),
                    Rate = convertedAmount / request.Amount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting currency");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Döviz kurlarını manuel günceller
        /// </summary>
        [HttpPost("update")]
        public async Task<ActionResult> UpdateExchangeRates()
        {
            try
            {
                await _exchangeRateService.UpdateExchangeRatesAsync();
                return Ok("Exchange rates updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating exchange rates");
                return StatusCode(500, "Internal server error");
            }
        }
    }

    public class CurrencyConversionRequest
    {
        public decimal Amount { get; set; }
        public string FromCurrency { get; set; } = string.Empty;
        public string ToCurrency { get; set; } = string.Empty;
    }

    public class CurrencyConversionResponse
    {
        public decimal OriginalAmount { get; set; }
        public decimal ConvertedAmount { get; set; }
        public string FromCurrency { get; set; } = string.Empty;
        public string ToCurrency { get; set; } = string.Empty;
        public decimal Rate { get; set; }
    }
}
