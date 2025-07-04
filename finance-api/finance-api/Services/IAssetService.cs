using FinanceApi.Models;
using FinanceApi.Models.Dtos;

namespace FinanceApi.Services
{
    public interface IAssetService
    {
        Task<IEnumerable<AssetDto>> GetAllAssetsAsync();
        Task<IEnumerable<AssetDto>> GetAssetsByTypeAsync(AssetType type);
        Task<AssetDto?> GetAssetByIdAsync(string id);
        Task<AssetDto?> GetAssetBySymbolAsync(string symbol, AssetType type);
        Task<AssetDto> CreateAssetAsync(CreateAssetDto createAssetDto);
        Task<AssetDto?> UpdateAssetAsync(string id, CreateAssetDto updateAssetDto);
        Task<AssetDto?> UpdateAssetPriceAsync(string id, UpdateAssetPriceDto updatePriceDto);
        Task<bool> DeleteAssetAsync(string id);
        Task UpdateAssetPricesAsync(); // For price simulation
    }
}
