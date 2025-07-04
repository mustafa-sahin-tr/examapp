using Microsoft.EntityFrameworkCore;
using FinanceApi.Data;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;

namespace FinanceApi.Services
{
    public class AssetService : IAssetService
    {
        private readonly FinanceDbContext _context;
        private readonly IRealTimeDataService _realTimeDataService;
        private readonly ILogger<AssetService> _logger;

        public AssetService(FinanceDbContext context, IRealTimeDataService realTimeDataService, ILogger<AssetService> logger)
        {
            _context = context;
            _realTimeDataService = realTimeDataService;
            _logger = logger;
        }

        public async Task<IEnumerable<AssetDto>> GetAllAssetsAsync()
        {
            var assets = await _context.Assets
                .OrderBy(a => a.Type)
                .ThenBy(a => a.Symbol)
                .ToListAsync();

            return assets.Select(MapToDto);
        }

        public async Task<IEnumerable<AssetDto>> GetAssetsByTypeAsync(AssetType type)
        {
            var assets = await _context.Assets
                .Where(a => a.Type == type)
                .OrderBy(a => a.Symbol)
                .ToListAsync();

            return assets.Select(MapToDto);
        }

        public async Task<AssetDto?> GetAssetByIdAsync(string id)
        {
            var asset = await _context.Assets.FindAsync(id);
            return asset != null ? MapToDto(asset) : null;
        }

        public async Task<AssetDto?> GetAssetBySymbolAsync(string symbol, AssetType type)
        {
            var asset = await _context.Assets
                .FirstOrDefaultAsync(a => a.Symbol == symbol && a.Type == type);
            return asset != null ? MapToDto(asset) : null;
        }

        public async Task<AssetDto> CreateAssetAsync(CreateAssetDto createAssetDto)
        {
            var asset = new Asset
            {
                Symbol = createAssetDto.Symbol.ToUpper(),
                Name = createAssetDto.Name,
                Type = createAssetDto.Type,
                CurrentPrice = createAssetDto.CurrentPrice,
                Currency = createAssetDto.Currency.ToUpper(),
                LastUpdated = DateTime.UtcNow,
                ChangePercentage = 0,
                ChangeValue = 0
            };

            _context.Assets.Add(asset);
            await _context.SaveChangesAsync();

            return MapToDto(asset);
        }

        public async Task<AssetDto?> UpdateAssetAsync(string id, CreateAssetDto updateAssetDto)
        {
            var asset = await _context.Assets.FindAsync(id);
            if (asset == null) return null;

            asset.Symbol = updateAssetDto.Symbol.ToUpper();
            asset.Name = updateAssetDto.Name;
            asset.Type = updateAssetDto.Type;
            asset.CurrentPrice = updateAssetDto.CurrentPrice;
            asset.Currency = updateAssetDto.Currency.ToUpper();
            asset.LastUpdated = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return MapToDto(asset);
        }

        public async Task<AssetDto?> UpdateAssetPriceAsync(string id, UpdateAssetPriceDto updatePriceDto)
        {
            var asset = await _context.Assets.FindAsync(id);
            if (asset == null) return null;

            asset.CurrentPrice = updatePriceDto.CurrentPrice;
            asset.ChangePercentage = updatePriceDto.ChangePercentage;
            asset.ChangeValue = updatePriceDto.ChangeValue;
            asset.LastUpdated = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return MapToDto(asset);
        }

        public async Task<bool> DeleteAssetAsync(string id)
        {
            var asset = await _context.Assets.FindAsync(id);
            if (asset == null) return false;

            _context.Assets.Remove(asset);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task UpdateAssetPricesAsync()
        {
            try
            {
                await _realTimeDataService.UpdateAssetPricesAsync();
                _logger.LogInformation("Asset prices updated successfully using real-time data");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update prices with real-time data, falling back to simulation");

                // Fallback to simulation if real-time data fails
                var assets = await _context.Assets.ToListAsync();
                var random = new Random();

                foreach (var asset in assets)
                {
                    // Simulate price changes (-5% to +5%)
                    var changePercent = (random.NextDouble() - 0.5) * 10; // -5% to +5%
                    var oldPrice = asset.CurrentPrice;
                    var newPrice = oldPrice * (1 + (decimal)(changePercent / 100));

                    asset.CurrentPrice = Math.Round(newPrice, 4);
                    asset.ChangePercentage = Math.Round((decimal)changePercent, 2);
                    asset.ChangeValue = Math.Round(newPrice - oldPrice, 4);
                    asset.LastUpdated = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Fallback price simulation completed");
            }
        }

        private static AssetDto MapToDto(Asset asset)
        {
            return new AssetDto
            {
                Id = asset.Id,
                Symbol = asset.Symbol,
                Name = asset.Name,
                Type = asset.Type,
                CurrentPrice = asset.CurrentPrice,
                Currency = asset.Currency,
                LastUpdated = asset.LastUpdated,
                ChangePercentage = asset.ChangePercentage,
                ChangeValue = asset.ChangeValue
            };
        }
    }
}
