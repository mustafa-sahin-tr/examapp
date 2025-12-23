using FinanceApi.Data;
using FinanceApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly FinanceDbContext _context;
        private readonly ILogger<TestController> _logger;

        public TestController(FinanceDbContext context, ILogger<TestController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpPost("seed-assets")]
        public async Task<IActionResult> SeedTestAssets()
        {
            try
            {
                // Mevcut asset'leri kontrol et
                var existingAssets = await _context.Assets.ToListAsync();
                if (existingAssets.Any())
                {
                    return Ok(new { message = "Assets already exist", count = existingAssets.Count });
                }

                var testAssets = new List<Asset>
                {
                    new Asset
                    {
                        Id = Guid.NewGuid().ToString(),
                        Symbol = "ASELS",
                        Name = "Aselsan Elektronik Sanayi ve Ticaret A.Ş.",
                        Type = AssetType.Stock,
                        CurrentPrice = 85.50m,
                        Currency = "TRY",
                        LastUpdated = DateTime.UtcNow
                    },
                    new Asset
                    {
                        Id = Guid.NewGuid().ToString(),
                        Symbol = "AAPL",
                        Name = "Apple Inc.",
                        Type = AssetType.USStock,
                        CurrentPrice = 195.50m,
                        Currency = "USD",
                        LastUpdated = DateTime.UtcNow
                    },
                    new Asset
                    {
                        Id = Guid.NewGuid().ToString(),
                        Symbol = "GCW00",
                        Name = "Gold Futures",
                        Type = AssetType.Gold,
                        CurrentPrice = 2050.00m,
                        Currency = "USD",
                        LastUpdated = DateTime.UtcNow
                    },
                    new Asset
                    {
                        Id = Guid.NewGuid().ToString(),
                        Symbol = "AEF",
                        Name = "Ak Portföy Birinci Fon",
                        Type = AssetType.Fund,
                        CurrentPrice = 0.125896m,
                        Currency = "TRY",
                        LastUpdated = DateTime.UtcNow
                    },
                    new Asset
                    {
                        Id = Guid.NewGuid().ToString(),
                        Symbol = "TRY",
                        Name = "Türk Lirası Vadeli Mevduat",
                        Type = AssetType.FixedDeposit,
                        CurrentPrice = 1.00m,
                        Currency = "TRY",
                        LastUpdated = DateTime.UtcNow
                    },
                    new Asset
                    {
                        Id = Guid.NewGuid().ToString(),
                        Symbol = "USD",
                        Name = "Dolar Vadeli Mevduat",
                        Type = AssetType.FixedDeposit,
                        CurrentPrice = 1.00m,
                        Currency = "USD",
                        LastUpdated = DateTime.UtcNow
                    },
                    new Asset
                    {
                        Id = Guid.NewGuid().ToString(),
                        Symbol = "BTC",
                        Name = "Bitcoin",
                        Type = AssetType.Crypto,
                        CurrentPrice = 0m,
                        Currency = "USD",
                        LastUpdated = DateTime.UtcNow
                    },
                    new Asset
                    {
                        Id = Guid.NewGuid().ToString(),
                        Symbol = "ETH",
                        Name = "Ethereum",
                        Type = AssetType.Crypto,
                        CurrentPrice = 0m,
                        Currency = "USD",
                        LastUpdated = DateTime.UtcNow
                    }
                };

                _context.Assets.AddRange(testAssets);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Test assets seeded successfully");

                return Ok(new
                {
                    message = "Test assets seeded successfully",
                    count = testAssets.Count,
                    assets = testAssets.Select(a => new { a.Symbol, a.Name, a.Type }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding test assets");
                return StatusCode(500, new { message = "Error seeding test assets", error = ex.Message });
            }
        }

        [HttpGet("assets")]
        public async Task<IActionResult> GetAssets()
        {
            try
            {
                var assets = await _context.Assets.ToListAsync();
                return Ok(assets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assets");
                return StatusCode(500, new { message = "Error getting assets", error = ex.Message });
            }
        }

        [HttpDelete("clear-assets")]
        public async Task<IActionResult> ClearAssets()
        {
            try
            {
                var assets = await _context.Assets.ToListAsync();
                _context.Assets.RemoveRange(assets);
                await _context.SaveChangesAsync();

                return Ok(new { message = "All assets cleared", count = assets.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing assets");
                return StatusCode(500, new { message = "Error clearing assets", error = ex.Message });
            }
        }
    }
}
