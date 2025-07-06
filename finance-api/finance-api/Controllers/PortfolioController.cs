using Microsoft.AspNetCore.Mvc;
using FinanceApi.Services;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;

namespace FinanceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PortfolioController : ControllerBase
    {
        private readonly IPortfolioService _portfolioService;

        public PortfolioController(IPortfolioService portfolioService)
        {
            _portfolioService = portfolioService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PortfolioDto>>> GetUserPortfolio([FromQuery] string userId = "default-user")
        {
            var portfolio = await _portfolioService.GetUserPortfolioAsync(userId);
            return Ok(portfolio);
        }

        [HttpGet("type/{type}")]
        public async Task<ActionResult<IEnumerable<PortfolioDto>>> GetUserPortfolioByType(
            AssetType type,
            [FromQuery] string userId = "default-user")
        {
            var portfolio = await _portfolioService.GetUserPortfolioByTypeAsync(userId, type);
            return Ok(portfolio);
        }

        [HttpGet("asset/{assetId}")]
        public async Task<ActionResult<PortfolioDto>> GetUserPortfolioByAsset(
            string assetId,
            [FromQuery] string userId = "default-user")
        {
            var portfolio = await _portfolioService.GetUserPortfolioByAssetAsync(userId, assetId);

            if (portfolio == null)
                return NotFound();

            return Ok(portfolio);
        }

        [HttpGet("dashboard")]
        public async Task<ActionResult<DashboardSummaryDto>> GetDashboardSummary([FromQuery] string userId = "default-user")
        {
            var summary = await _portfolioService.GetDashboardSummaryAsync(userId);
            return Ok(summary);
        }

        [HttpGet("performance")]
        public async Task<ActionResult<IEnumerable<AssetTypePerformanceDto>>> GetAssetTypePerformance([FromQuery] string userId = "default-user")
        {
            var performance = await _portfolioService.GetAssetTypePerformanceAsync(userId);
            return Ok(performance);
        }

        [HttpPost("recalculate/{assetId}")]
        public async Task<IActionResult> RecalculatePortfolio(
            string assetId,
            [FromQuery] string userId = "default-user")
        {
            await _portfolioService.RecalculatePortfolioAsync(userId, assetId);
            return Ok();
        }

        [HttpPost("recalculate-all")]
        public async Task<IActionResult> RecalculateAllPortfolios([FromQuery] string userId = "default-user")
        {
            await _portfolioService.RecalculateAllPortfoliosAsync(userId);
            return Ok();
        }
    }
}
