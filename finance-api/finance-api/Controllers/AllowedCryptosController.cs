using FinanceApi.Models;
using FinanceApi.Models.Dtos;
using FinanceApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AllowedCryptosController : ControllerBase
    {
        private readonly IAllowedCryptoService _allowedCryptoService;

        public AllowedCryptosController(IAllowedCryptoService allowedCryptoService)
        {
            _allowedCryptoService = allowedCryptoService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<AllowedCryptoDto>>> GetAll(CancellationToken cancellationToken)
        {
            var items = await _allowedCryptoService.GetAllAsync(cancellationToken);
            return Ok(items.Select(Map));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<AllowedCryptoDto>> GetById(string id, CancellationToken cancellationToken)
        {
            var item = await _allowedCryptoService.GetByIdAsync(id, cancellationToken);
            if (item == null)
            {
                return NotFound();
            }

            return Ok(Map(item));
        }

        [HttpPost]
        public async Task<ActionResult<AllowedCryptoDto>> Create([FromBody] CreateAllowedCryptoDto dto, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(dto.Symbol))
            {
                return BadRequest("Symbol is required");
            }

            var (name, coinGeckoId, yahooSymbol) = ResolveDefaults(dto.Symbol, dto.Name, dto.CoinGeckoId, dto.YahooSymbol);

            if (string.IsNullOrWhiteSpace(coinGeckoId) || string.IsNullOrWhiteSpace(yahooSymbol))
            {
                return BadRequest("For new coins, provide coinGeckoId and yahooSymbol (e.g. coinGeckoId=solana, yahooSymbol=SOL-USD)");
            }

            var entity = new AllowedCrypto
            {
                Symbol = dto.Symbol,
                Name = name,
                CoinGeckoId = coinGeckoId,
                YahooSymbol = yahooSymbol,
                IsEnabled = dto.IsEnabled
            };

            var created = await _allowedCryptoService.CreateAsync(entity, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, Map(created));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<AllowedCryptoDto>> Update(string id, [FromBody] UpdateAllowedCryptoDto dto, CancellationToken cancellationToken)
        {
            var updated = await _allowedCryptoService.UpdateAsync(id, entity =>
            {
                if (dto.Name != null) entity.Name = dto.Name;
                if (dto.CoinGeckoId != null) entity.CoinGeckoId = dto.CoinGeckoId;
                if (dto.YahooSymbol != null) entity.YahooSymbol = dto.YahooSymbol;
                if (dto.IsEnabled.HasValue) entity.IsEnabled = dto.IsEnabled.Value;
            }, cancellationToken);

            if (updated == null)
            {
                return NotFound();
            }

            return Ok(Map(updated));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
        {
            var deleted = await _allowedCryptoService.DeleteAsync(id, cancellationToken);
            if (!deleted)
            {
                return NotFound();
            }

            return NoContent();
        }

        private static AllowedCryptoDto Map(AllowedCrypto entity) => new()
        {
            Id = entity.Id,
            Symbol = entity.Symbol,
            Name = entity.Name,
            CoinGeckoId = entity.CoinGeckoId,
            YahooSymbol = entity.YahooSymbol,
            IsEnabled = entity.IsEnabled
        };

        private static (string name, string coinGeckoId, string yahooSymbol) ResolveDefaults(
            string symbol,
            string? name,
            string? coinGeckoId,
            string? yahooSymbol)
        {
            var upper = symbol.Trim().ToUpperInvariant();

            if (upper is "BTC" or "XBT")
            {
                return (
                    string.IsNullOrWhiteSpace(name) ? "Bitcoin" : name,
                    string.IsNullOrWhiteSpace(coinGeckoId) ? "bitcoin" : coinGeckoId,
                    string.IsNullOrWhiteSpace(yahooSymbol) ? "BTC-USD" : yahooSymbol);
            }

            if (upper == "ETH")
            {
                return (
                    string.IsNullOrWhiteSpace(name) ? "Ethereum" : name,
                    string.IsNullOrWhiteSpace(coinGeckoId) ? "ethereum" : coinGeckoId,
                    string.IsNullOrWhiteSpace(yahooSymbol) ? "ETH-USD" : yahooSymbol);
            }

            return (
                string.IsNullOrWhiteSpace(name) ? upper : name,
                coinGeckoId ?? string.Empty,
                yahooSymbol ?? string.Empty);
        }
    }
}
