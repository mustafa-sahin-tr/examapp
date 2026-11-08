using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.BookImport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1056: kitap aktarımı ve hızlı ekleme AI maliyet raporu (yalnız Admin / SuperAdmin). Yanıt kullanıcı adı taşıdığı için
/// <c>AdminDataAccessLogs</c>'a yazılır ve admin liste hız sınırındadır (#246, #262).
/// </summary>
[ApiController]
[Route("api/admin/book-import-costs")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class AdminBookImportCostsController(
    IBookImportCostReportService report, IAdminDataAccessAuditService dataAccessAudit, TimeProvider clock) : BaseController
{
    // GET ?from=&to=&kind=&userId=&page=&pageSize= — tarih yyyy-MM-dd (iki uç dahil, varsayılan: içinde bulunulan ay), kind Book | QuickAdd.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(AdminUserListRateLimiting.Policy)]
    [AdminDataAccess(AdminDataAccessResource.BookImportCostReport)]
    public async Task<ActionResult<BookImportCostReportDto>> Get(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? kind, [FromQuery] int? userId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!TryFilter(from, to, kind, userId, out var filter, out var error))
            return BadRequest(new { code = error });

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, BookImportCostReportService.MaxPageSize);
        var result = await report.GetAsync(filter, page, pageSize, ct);
        await dataAccessAudit.RecordListAccessAsync(new AdminListAccessRecord(
            KeyCloakId ?? string.Empty, AdminDataAccessResource.BookImportCostReport, null, false, page, pageSize, result.Jobs.Count, result.JobCount), ct);
        return result;
    }

    [HttpGet("jobs.csv")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(AdminUserListRateLimiting.Policy)]
    [AdminDataAccess(AdminDataAccessResource.BookImportCostReport)]
    public async Task<IActionResult> ExportJobs(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? kind, [FromQuery] int? userId, CancellationToken ct = default)
    {
        if (!TryFilter(from, to, kind, userId, out var filter, out var error))
            return BadRequest(new { code = error });

        var jobs = await report.ListJobsAsync(filter, ct);
        await dataAccessAudit.RecordListAccessAsync(new AdminListAccessRecord(
            KeyCloakId ?? string.Empty, AdminDataAccessResource.BookImportCostReport, null, false, 1, BookImportCostReportService.MaxExportRows, jobs.Count, jobs.Count), ct);
        return File(BookImportCostCsv.Write(jobs), "text/csv; charset=utf-8", $"aktarim-maliyetleri-{filter.From:yyyy-MM-dd}-{filter.To:yyyy-MM-dd}.csv");
    }

    private bool TryFilter(DateOnly? from, DateOnly? to, string? kind, int? userId, out BookImportCostFilter filter, out string? error)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? start.AddMonths(1).AddDays(-1);
        filter = new BookImportCostFilter(start, end, null, userId);
        error = null;

        if (end < start)
        {
            error = "bookImportCosts.invalidRange";
            return false;
        }
        if (string.IsNullOrWhiteSpace(kind))
            return true;
        // Enum.TryParse sayıları da kabul eder; yalnız adlar geçerli.
        if (!Enum.TryParse<BookImportJobKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || char.IsDigit(kind[0]))
        {
            error = "bookImportCosts.unknownKind";
            return false;
        }
        filter = filter with { Kind = parsed };
        return true;
    }
}
