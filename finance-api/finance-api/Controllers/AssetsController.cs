using Microsoft.AspNetCore.Mvc;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;
using FinanceApi.Services;

namespace FinanceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssetsController : ControllerBase
    {
        private readonly IAssetService _assetService;

        public AssetsController(IAssetService assetService)
        {
            _assetService = assetService;
        }

        /// <summary>
        /// Get all assets
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AssetDto>>> GetAssets()
        {
            var assets = await _assetService.GetAllAssetsAsync();
            return Ok(assets);
        }

        /// <summary>
        /// Get assets by type
        /// </summary>
        [HttpGet("type/{type}")]
        public async Task<ActionResult<IEnumerable<AssetDto>>> GetAssetsByType(AssetType type)
        {
            var assets = await _assetService.GetAssetsByTypeAsync(type);
            return Ok(assets);
        }

        /// <summary>
        /// Get asset by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<AssetDto>> GetAsset(string id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset == null)
            {
                return NotFound($"Asset with ID {id} not found");
            }
            return Ok(asset);
        }

        /// <summary>
        /// Get asset by symbol and type
        /// </summary>
        [HttpGet("symbol/{symbol}/type/{type}")]
        public async Task<ActionResult<AssetDto>> GetAssetBySymbol(string symbol, AssetType type)
        {
            var asset = await _assetService.GetAssetBySymbolAsync(symbol, type);
            if (asset == null)
            {
                return NotFound($"Asset with symbol {symbol} and type {type} not found");
            }
            return Ok(asset);
        }

        /// <summary>
        /// Create new asset
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<AssetDto>> CreateAsset(CreateAssetDto createAssetDto)
        {
            try
            {
                var asset = await _assetService.CreateAssetAsync(createAssetDto);
                return CreatedAtAction(nameof(GetAsset), new { id = asset.Id }, asset);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error creating asset: {ex.Message}");
            }
        }

        /// <summary>
        /// Update asset
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<AssetDto>> UpdateAsset(string id, CreateAssetDto updateAssetDto)
        {
            var asset = await _assetService.UpdateAssetAsync(id, updateAssetDto);
            if (asset == null)
            {
                return NotFound($"Asset with ID {id} not found");
            }
            return Ok(asset);
        }

        /// <summary>
        /// Update asset price
        /// </summary>
        [HttpPatch("{id}/price")]
        public async Task<ActionResult<AssetDto>> UpdateAssetPrice(string id, UpdateAssetPriceDto updatePriceDto)
        {
            var asset = await _assetService.UpdateAssetPriceAsync(id, updatePriceDto);
            if (asset == null)
            {
                return NotFound($"Asset with ID {id} not found");
            }
            return Ok(asset);
        }

        /// <summary>
        /// Delete asset
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsset(string id)
        {
            var result = await _assetService.DeleteAssetAsync(id);
            if (!result)
            {
                return NotFound($"Asset with ID {id} not found");
            }
            return NoContent();
        }

        /// <summary>
        /// Simulate price updates for all assets
        /// </summary>
        [HttpPost("simulate-price-updates")]
        public async Task<IActionResult> SimulatePriceUpdates()
        {
            try
            {
                await _assetService.UpdateAssetPricesAsync();
                return Ok(new { message = "Asset prices updated successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest($"Error updating asset prices: {ex.Message}");
            }
        }
    }
}
