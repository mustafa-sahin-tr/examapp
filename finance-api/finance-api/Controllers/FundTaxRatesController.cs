using FinanceApi.Data;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FundTaxRatesController : ControllerBase
    {
        private readonly FinanceDbContext _context;

        public FundTaxRatesController(FinanceDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<FundTaxRateDto>>> GetAll([FromQuery] string userId = "default-user")
        {
            var items = await _context.FundTaxRates
                .AsNoTracking()
                .Include(x => x.Asset)
                .Where(x => x.UserId == userId)
                .OrderBy(x => x.Asset.Symbol)
                .Select(x => new FundTaxRateDto
                {
                    Id = x.Id,
                    AssetId = x.AssetId,
                    AssetSymbol = x.Asset.Symbol,
                    RatePercent = x.RatePercent,
                    UpdatedAt = x.UpdatedAt
                })
                .ToListAsync();

            return Ok(items);
        }

        [HttpGet("{assetId}")]
        public async Task<ActionResult<FundTaxRateDto>> GetByAssetId(string assetId, [FromQuery] string userId = "default-user")
        {
            var item = await _context.FundTaxRates
                .AsNoTracking()
                .Include(x => x.Asset)
                .Where(x => x.UserId == userId && x.AssetId == assetId)
                .Select(x => new FundTaxRateDto
                {
                    Id = x.Id,
                    AssetId = x.AssetId,
                    AssetSymbol = x.Asset.Symbol,
                    RatePercent = x.RatePercent,
                    UpdatedAt = x.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (item == null)
            {
                return NotFound();
            }

            return Ok(item);
        }

        [HttpPut("{assetId}")]
        public async Task<ActionResult<FundTaxRateDto>> Upsert(string assetId, [FromBody] UpsertFundTaxRateDto dto, [FromQuery] string userId = "default-user")
        {
            if (dto.RatePercent < 0 || dto.RatePercent > 100)
            {
                return BadRequest("RatePercent must be between 0 and 100.");
            }

            var asset = await _context.Assets.FirstOrDefaultAsync(a => a.Id == assetId);
            if (asset == null)
            {
                return NotFound("Asset not found.");
            }

            if (asset.Type != AssetType.Fund)
            {
                return BadRequest("Stopaj oranı sadece Fund tipi asset için tanımlanabilir.");
            }

            var existing = await _context.FundTaxRates
                .Include(x => x.Asset)
                .FirstOrDefaultAsync(x => x.UserId == userId && x.AssetId == assetId);

            if (existing == null)
            {
                existing = new FundTaxRate
                {
                    UserId = userId,
                    AssetId = assetId,
                    RatePercent = dto.RatePercent,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.FundTaxRates.Add(existing);
            }
            else
            {
                existing.RatePercent = dto.RatePercent;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            // Ensure asset loaded for response
            existing.Asset ??= asset;

            return Ok(new FundTaxRateDto
            {
                Id = existing.Id,
                AssetId = existing.AssetId,
                AssetSymbol = existing.Asset.Symbol,
                RatePercent = existing.RatePercent,
                UpdatedAt = existing.UpdatedAt
            });
        }

        [HttpDelete("{assetId}")]
        public async Task<IActionResult> Delete(string assetId, [FromQuery] string userId = "default-user")
        {
            var existing = await _context.FundTaxRates
                .FirstOrDefaultAsync(x => x.UserId == userId && x.AssetId == assetId);

            if (existing == null)
            {
                return NotFound();
            }

            _context.FundTaxRates.Remove(existing);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
