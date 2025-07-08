using FinanceApi.Data;
using FinanceApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FinanceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProfitLossController : ControllerBase
    {
        private readonly FinanceDbContext _context;
        private readonly ILogger<ProfitLossController> _logger;

        public ProfitLossController(FinanceDbContext context, ILogger<ProfitLossController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Belirtilen tarih aralığında kar/zarar geçmişini döndürür
        /// </summary>
        [HttpGet("history")]
        public async Task<ActionResult<IEnumerable<ProfitLossHistoryDto>>> GetProfitLossHistory(
            [FromQuery] string userId = "default-user",
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string timeFrame = "hourly") // hourly, daily, weekly, monthly
        {
            try
            {
                var start = startDate ?? DateTime.UtcNow.AddDays(-7);
                var end = endDate ?? DateTime.UtcNow;

                var query = _context.ProfitLossHistories
                    .Where(p => p.UserId == userId && p.Timestamp >= start && p.Timestamp <= end)
                    .OrderBy(p => p.Timestamp);

                var histories = await query.ToListAsync();

                // Zaman çerçevesine göre grupla
                var groupedHistories = GroupByTimeFrame(histories, timeFrame);

                var result = groupedHistories.Select(h => new ProfitLossHistoryDto
                {
                    Id = h.Id,
                    Timestamp = h.Timestamp,
                    TotalProfitLoss = h.TotalProfitLoss,
                    TotalInvestment = h.TotalInvestment,
                    TotalCurrentValue = h.TotalCurrentValue,
                    ProfitLossPercentage = h.ProfitLossPercentage,
                    AssetTypeBreakdown = JsonSerializer.Deserialize<List<AssetTypeBreakdownDto>>(h.AssetTypeBreakdown) ?? new List<AssetTypeBreakdownDto>()
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting profit/loss history");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Asset tipi bazında kar/zarar geçmişini döndürür
        /// </summary>
        [HttpGet("history/asset-type/{assetType}")]
        public async Task<ActionResult<IEnumerable<AssetTypeProfitLossDto>>> GetAssetTypeProfitLossHistory(
            string assetType,
            [FromQuery] string userId = "default-user",
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string timeFrame = "hourly")
        {
            try
            {
                if (!Enum.TryParse<AssetType>(assetType, true, out var assetTypeEnum))
                {
                    return BadRequest("Invalid asset type");
                }

                var start = startDate ?? DateTime.UtcNow.AddDays(-7);
                var end = endDate ?? DateTime.UtcNow;

                var query = _context.AssetTypeProfitLosses
                    .Include(atp => atp.ProfitLossHistory)
                    .Where(atp => atp.AssetType == assetTypeEnum &&
                                  atp.ProfitLossHistory.UserId == userId &&
                                  atp.ProfitLossHistory.Timestamp >= start &&
                                  atp.ProfitLossHistory.Timestamp <= end)
                    .OrderBy(atp => atp.ProfitLossHistory.Timestamp);

                var histories = await query.ToListAsync();

                var result = histories.Select(atp => new AssetTypeProfitLossDto
                {
                    Id = atp.Id,
                    AssetType = atp.AssetType.ToString(),
                    ProfitLoss = atp.ProfitLoss,
                    Investment = atp.Investment,
                    CurrentValue = atp.CurrentValue,
                    ProfitLossPercentage = atp.ProfitLossPercentage,
                    AssetCount = atp.AssetCount,
                    Timestamp = atp.ProfitLossHistory.Timestamp
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting asset type profit/loss history");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Belirli bir asset için kar/zarar geçmişini döndürür
        /// </summary>
        [HttpGet("history/asset/{assetId}")]
        public async Task<ActionResult<IEnumerable<AssetProfitLossDto>>> GetAssetProfitLossHistory(
            string assetId,
            [FromQuery] string userId = "default-user",
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string timeFrame = "hourly")
        {
            try
            {
                var start = startDate ?? DateTime.UtcNow.AddDays(-7);
                var end = endDate ?? DateTime.UtcNow;

                var query = _context.AssetProfitLosses
                    .Include(ap => ap.ProfitLossHistory)
                    .Include(ap => ap.Asset)
                    .Where(ap => ap.AssetId == assetId &&
                                 ap.ProfitLossHistory.UserId == userId &&
                                 ap.ProfitLossHistory.Timestamp >= start &&
                                 ap.ProfitLossHistory.Timestamp <= end)
                    .OrderBy(ap => ap.ProfitLossHistory.Timestamp);

                var histories = await query.ToListAsync();

                var result = histories.Select(ap => new AssetProfitLossDto
                {
                    Id = ap.Id,
                    AssetId = ap.AssetId,
                    AssetSymbol = ap.Asset.Symbol,
                    AssetName = ap.Asset.Name,
                    AssetType = ap.Asset.Type.ToString(),
                    ProfitLoss = ap.ProfitLoss,
                    Investment = ap.Investment,
                    CurrentValue = ap.CurrentValue,
                    ProfitLossPercentage = ap.ProfitLossPercentage,
                    Quantity = ap.Quantity,
                    AveragePrice = ap.AveragePrice,
                    CurrentPrice = ap.CurrentPrice,
                    Timestamp = ap.ProfitLossHistory.Timestamp
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting asset profit/loss history");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Güncel kar/zarar durumunu döndürür
        /// </summary>
        [HttpGet("current")]
        public async Task<ActionResult<ProfitLossHistoryDto>> GetCurrentProfitLoss([FromQuery] string userId = "default-user")
        {
            try
            {
                var latest = await _context.ProfitLossHistories
                    .Where(p => p.UserId == userId)
                    .OrderByDescending(p => p.Timestamp)
                    .FirstOrDefaultAsync();

                if (latest == null)
                {
                    return NotFound("No profit/loss data found");
                }

                var result = new ProfitLossHistoryDto
                {
                    Id = latest.Id,
                    Timestamp = latest.Timestamp,
                    TotalProfitLoss = latest.TotalProfitLoss,
                    TotalInvestment = latest.TotalInvestment,
                    TotalCurrentValue = latest.TotalCurrentValue,
                    ProfitLossPercentage = latest.ProfitLossPercentage,
                    AssetTypeBreakdown = JsonSerializer.Deserialize<List<AssetTypeBreakdownDto>>(latest.AssetTypeBreakdown) ?? new List<AssetTypeBreakdownDto>()
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current profit/loss");
                return StatusCode(500, "Internal server error");
            }
        }

        private List<ProfitLossHistory> GroupByTimeFrame(List<ProfitLossHistory> histories, string timeFrame)
        {
            return timeFrame.ToLower() switch
            {
                "daily" => histories.GroupBy(h => h.Date)
                                  .Select(g => g.OrderByDescending(h => h.Timestamp).First())
                                  .OrderBy(h => h.Timestamp)
                                  .ToList(),
                "weekly" => histories.GroupBy(h => GetWeekOfYear(h.Date))
                                   .Select(g => g.OrderByDescending(h => h.Timestamp).First())
                                   .OrderBy(h => h.Timestamp)
                                   .ToList(),
                "monthly" => histories.GroupBy(h => new { h.Date.Year, h.Date.Month })
                                    .Select(g => g.OrderByDescending(h => h.Timestamp).First())
                                    .OrderBy(h => h.Timestamp)
                                    .ToList(),
                _ => histories // hourly (default)
            };
        }

        private int GetWeekOfYear(DateTime date)
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var calendar = culture.Calendar;
            var weekOfYear = calendar.GetWeekOfYear(date, culture.DateTimeFormat.CalendarWeekRule, culture.DateTimeFormat.FirstDayOfWeek);
            return weekOfYear;
        }
    }

    // DTOs
    public class ProfitLossHistoryDto
    {
        public string Id { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public decimal TotalProfitLoss { get; set; }
        public decimal TotalInvestment { get; set; }
        public decimal TotalCurrentValue { get; set; }
        public decimal ProfitLossPercentage { get; set; }
        public List<AssetTypeBreakdownDto> AssetTypeBreakdown { get; set; } = new();
    }

    public class AssetTypeBreakdownDto
    {
        public string AssetType { get; set; } = string.Empty;
        public decimal ProfitLoss { get; set; }
        public decimal Investment { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal ProfitLossPercentage { get; set; }
        public int AssetCount { get; set; }
    }

    public class AssetTypeProfitLossDto
    {
        public string Id { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty;
        public decimal ProfitLoss { get; set; }
        public decimal Investment { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal ProfitLossPercentage { get; set; }
        public int AssetCount { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AssetProfitLossDto
    {
        public string Id { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string AssetSymbol { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty;
        public decimal ProfitLoss { get; set; }
        public decimal Investment { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal ProfitLossPercentage { get; set; }
        public decimal Quantity { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
