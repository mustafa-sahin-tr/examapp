using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Linq;
using System.Text;
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
        private readonly IPortfolioPriceUpdateService _portfolioPriceUpdateService;
        private readonly IEmailService _emailService;
        private readonly ILogger<PortfolioController> _logger;

        public PortfolioController(
            IPortfolioService portfolioService,
            IPortfolioPriceUpdateService portfolioPriceUpdateService,
            IEmailService emailService,
            ILogger<PortfolioController> logger)
        {
            _portfolioService = portfolioService;
            _portfolioPriceUpdateService = portfolioPriceUpdateService;
            _emailService = emailService;
            _logger = logger;
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
        public async Task<ActionResult<DashboardSummaryDto>> GetDashboardSummary(
            [FromQuery] string userId = "default-user",
            [FromQuery] string currency = "TRY")
        {
            var summary = await _portfolioService.GetDashboardSummaryAsync(userId, currency);
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

        [HttpPost("email-summary")]
        public async Task<IActionResult> EmailDashboardSummary([FromBody] EmailSummaryRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.RecipientEmail))
            {
                return BadRequest("Recipient email is required.");
            }

            try
            {
                await _portfolioPriceUpdateService.RefreshTrackedPortfolioAssetsAsync(
                    request.UserId,
                    HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh asset prices before sending summary for {UserId}", request.UserId);
            }

            var summary = await _portfolioService.GetDashboardSummaryAsync(request.UserId, request.Currency);
            var subject = string.IsNullOrWhiteSpace(request.Subject)
                ? $"Portfolio Summary - {DateTime.UtcNow:yyyy-MM-dd}"
                : request.Subject!;

            var culture = ResolveCulture(summary.Currency);
            var htmlBody = BuildSummaryHtml(summary, request.Message, culture);
            var textBody = BuildSummaryText(summary, request.Message, culture);

            try
            {
                await _emailService.SendAsync(request.RecipientEmail, subject, htmlBody, textBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send dashboard summary email to {Recipient}", request.RecipientEmail);
                return StatusCode(500, "Unable to send email. Check server logs for details.");
            }

            return Ok(new { success = true });
        }

    private static string BuildSummaryHtml(DashboardSummaryDto summary, string? introMessage, CultureInfo culture)
        {
            var sb = new StringBuilder();

            sb.Append("<div style=\"font-family:Arial,sans-serif;color:#1a202c;\">");

            if (!string.IsNullOrWhiteSpace(introMessage))
            {
                sb.AppendFormat("<p>{0}</p>", introMessage);
            }

            sb.AppendFormat("<h2 style=\"color:#2c5282;\">Portfolio Summary ({0})</h2>", summary.Currency);
            sb.Append("<table style=\"border-collapse:collapse;width:100%;margin-bottom:24px;background:#f8fafc;border-radius:8px;overflow:hidden;\">");
            sb.Append("<tr style=\"background:#2c5282;color:#fff;\"><th style=\"text-align:left;padding:12px;\">Metric</th><th style=\"text-align:right;padding:12px;\">Value</th></tr>");
            sb.AppendFormat("<tr><td style=\"padding:12px;border-bottom:1px solid #e2e8f0;\">Total Value</td><td style=\"padding:12px;text-align:right;border-bottom:1px solid #e2e8f0;font-weight:600;\">{0}</td></tr>", summary.TotalValue.ToString("C", culture));
            sb.AppendFormat("<tr><td style=\"padding:12px;border-bottom:1px solid #e2e8f0;\">Total Cost</td><td style=\"padding:12px;text-align:right;border-bottom:1px solid #e2e8f0;\">{0}</td></tr>", summary.TotalCost.ToString("C", culture));
            sb.AppendFormat("<tr><td style=\"padding:12px;border-bottom:1px solid #e2e8f0;\">Total P&amp;L</td><td style=\"padding:12px;text-align:right;border-bottom:1px solid #e2e8f0;color:{2};font-weight:600;\">{0} ({1})</td></tr>",
                summary.TotalProfitLoss.ToString("C", culture),
                summary.TotalProfitLossPercentage.ToString("F2", culture) + "%",
                summary.TotalProfitLoss >= 0 ? "#0f766e" : "#b91c1c");
            sb.AppendFormat("<tr><td style=\"padding:12px;\">Assets Tracked</td><td style=\"padding:12px;text-align:right;\">{0}</td></tr>", summary.AssetCount);
            sb.Append("</table>");

            var typeBreakdowns = summary.PortfoliosByType?
                .Where(group => group.Value is { Count: > 0 })
                .Select(group =>
                {
                    var totalValue = group.Value.Sum(p => p.CurrentValue);
                    var totalCost = group.Value.Sum(p => p.TotalCost);
                    var profitLoss = group.Value.Sum(p => p.ProfitLoss);
                    var profitLossPercentage = totalCost != 0 ? (profitLoss / totalCost) * 100 : 0m;

                    return new
                    {
                        group.Key,
                        TotalValue = totalValue,
                        TotalCost = totalCost,
                        ProfitLoss = profitLoss,
                        ProfitLossPercentage = profitLossPercentage,
                        AssetCount = group.Value.Count
                    };
                })
                .OrderByDescending(x => x.TotalValue)
                .ToList();

            if (typeBreakdowns?.Any() == true)
            {
                sb.Append("<div style=\"margin-top:24px;padding:16px;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;\">");
                sb.Append("<h3 style=\"color:#2f855a;margin:0 0 16px 0;\">Holdings by Asset Class</h3>");
                sb.Append("<table style=\"width:100%;border-collapse:collapse;\">");
                sb.Append("<tr style=\"background:#f0fff4;color:#22543d;\"><th style=\"text-align:left;padding:10px;\">Category</th><th style=\"text-align:right;padding:10px;\">Holdings</th><th style=\"text-align:right;padding:10px;\">Total Value</th><th style=\"text-align:right;padding:10px;\">P&amp;L</th><th style=\"text-align:right;padding:10px;\">Return %</th></tr>");

                foreach (var breakdown in typeBreakdowns)
                {
                    var profitColor = breakdown.ProfitLoss >= 0 ? "#0f766e" : "#b91c1c";
                    sb.Append("<tr style=\"border-bottom:1px solid #edf2f7;\">");
                    sb.AppendFormat("<td style=\"padding:12px;font-weight:600;\">{0}</td>", GetAssetTypeDisplayName(breakdown.Key));
                    sb.AppendFormat("<td style=\"padding:12px;text-align:right;color:#64748b;\">{0}</td>", breakdown.AssetCount);
                    sb.AppendFormat("<td style=\"padding:12px;text-align:right;font-weight:600;\">{0}</td>", breakdown.TotalValue.ToString("C", culture));
                    sb.AppendFormat("<td style=\"padding:12px;text-align:right;color:{1};\">{0}</td>",
                        breakdown.ProfitLoss.ToString("C", culture),
                        profitColor);
                    sb.AppendFormat("<td style=\"padding:12px;text-align:right;color:{1};\">{0}</td>",
                        breakdown.ProfitLossPercentage.ToString("F2", culture) + "%",
                        profitColor);
                    sb.Append("</tr>");
                }

                sb.Append("</table>");
                sb.Append("</div>");
            }

            sb.AppendFormat("<p style=\"color:#4a5568;font-size:12px;\">Last updated: {0}</p>", summary.LastUpdated.ToString("f", culture));
            sb.Append("</div>");

            return sb.ToString();
        }

    private static string BuildSummaryText(DashboardSummaryDto summary, string? introMessage, CultureInfo culture)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(introMessage))
            {
                sb.AppendLine(introMessage);
                sb.AppendLine();
            }

            sb.AppendLine($"Portfolio Summary ({summary.Currency})");
            sb.AppendLine(new string('-', 40));
            sb.AppendLine($"Total Value: {summary.TotalValue.ToString("C", culture)}");
            sb.AppendLine($"Total Cost: {summary.TotalCost.ToString("C", culture)}");
            sb.AppendLine($"Total P&L: {summary.TotalProfitLoss.ToString("C", culture)} ({summary.TotalProfitLossPercentage.ToString("F2", culture)}%)");
            sb.AppendLine($"Assets Tracked: {summary.AssetCount}");
            sb.AppendLine();

            var typeBreakdowns = summary.PortfoliosByType?
                .Where(group => group.Value is { Count: > 0 })
                .Select(group =>
                {
                    var totalValue = group.Value.Sum(p => p.CurrentValue);
                    var totalCost = group.Value.Sum(p => p.TotalCost);
                    var profitLoss = group.Value.Sum(p => p.ProfitLoss);
                    var profitLossPercentage = totalCost != 0 ? (profitLoss / totalCost) * 100 : 0m;

                    return new
                    {
                        group.Key,
                        TotalValue = totalValue,
                        ProfitLoss = profitLoss,
                        ProfitLossPercentage = profitLossPercentage,
                        AssetCount = group.Value.Count
                    };
                })
                .OrderByDescending(x => x.TotalValue)
                .ToList();

            if (typeBreakdowns?.Any() == true)
            {
                sb.AppendLine("Breakdown by Asset Type:");

                foreach (var breakdown in typeBreakdowns)
                {
                    sb.AppendLine(
                        $"- {GetAssetTypeDisplayName(breakdown.Key)} | Holdings: {breakdown.AssetCount} | " +
                        $"Value: {breakdown.TotalValue.ToString("C", culture)} | " +
                        $"P&L: {breakdown.ProfitLoss.ToString("C", culture)} ({breakdown.ProfitLossPercentage.ToString("F2", culture)}%)");
                }

                sb.AppendLine();
            }

            sb.AppendLine($"Last updated: {summary.LastUpdated.ToString("f", culture)}");

            return sb.ToString();
        }

        private static string GetAssetTypeDisplayName(AssetType assetType)
        {
            return assetType switch
            {
                AssetType.Stock => "BIST 100",
                AssetType.USStock => "US Stocks",
                AssetType.Gold => "Gold",
                AssetType.Silver => "Silver",
                AssetType.Fund => "Funds",
                AssetType.FixedDeposit => "Vadeli Mevduat",
                _ => assetType.ToString()
            };
        }

        private static CultureInfo ResolveCulture(string currencyCode)
        {
            var normalized = string.IsNullOrWhiteSpace(currencyCode)
                ? "TRY"
                : currencyCode.ToUpperInvariant();

            return normalized switch
            {
                "TRY" => CultureInfo.GetCultureInfo("tr-TR"),
                "USD" => CultureInfo.GetCultureInfo("en-US"),
                "EUR" => CultureInfo.GetCultureInfo("de-DE"),
                "GBP" => CultureInfo.GetCultureInfo("en-GB"),
                _ => CultureInfo.InvariantCulture
            };
        }
    }
}
