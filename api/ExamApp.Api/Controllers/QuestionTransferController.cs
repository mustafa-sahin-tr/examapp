using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.QuestionTransfer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Controllers;

[ApiController]
[Route("api/question-transfer")]
public class QuestionTransferController : ControllerBase
{
    private readonly IQuestionTransferService _service;
    private readonly IMinIoService _minio;

    public QuestionTransferController(IQuestionTransferService service, IMinIoService minio)
    {
        _service = service;
        _minio = minio;
    }

    [HttpPost("exports")]
    [Authorize]
    public async Task<ActionResult<QuestionTransferJobDto>> StartExport([FromBody] StartQuestionExportDto request, CancellationToken ct)
    {
        var job = await _service.StartExportAsync(request, ct);
        return Ok(job);
    }

    [HttpPost("imports")]
    [Authorize]
    [RequestSizeLimit(200_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<QuestionTransferJobDto>> StartImport([FromForm] StartQuestionImportFormDto request, CancellationToken ct)
    {
        if (request?.File == null || request.File.Length == 0)
        {
            return BadRequest(new { message = "File is required." });
        }

        var sourceKey = string.IsNullOrWhiteSpace(request.SourceKey) ? "default" : request.SourceKey.Trim();
        var objectName = $"question-transfer/imports/{Guid.NewGuid()}.zip";

        await using var uploadStream = request.File.OpenReadStream();
        var url = await _minio.UploadFileAsync(uploadStream, objectName);

        var job = await _service.StartImportAsync(sourceKey, url, ct);
        return Ok(job);
    }

    [HttpGet("jobs")]
    [Authorize]
    public async Task<ActionResult> ListJobs([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var jobs = await _service.ListJobsAsync(take, ct);
        return Ok(jobs);
    }

    [HttpGet("jobs/{id:guid}")]
    [Authorize]
    public async Task<ActionResult> GetJob([FromRoute] Guid id, CancellationToken ct)
    {
        var job = await _service.GetJobAsync(id, ct);
        if (job == null) return NotFound();
        return Ok(job);
    }

    [HttpGet("jobs/{id:guid}/download")]
    [Authorize]
    public async Task<IActionResult> Download([FromRoute] Guid id, CancellationToken ct)
    {
        var stream = await _service.GetJobFileStreamAsync(id, ct);
        if (stream == null) return NotFound();
        return File(stream, "application/octet-stream", $"question-transfer-{id}.zip");
    }

    // Creates an HttpOnly cookie scoped to /hangfire so the dashboard can be opened in a browser.
    [HttpPost("hangfire/login")]
    [Authorize]
    public async Task<IActionResult> HangfireLogin(CancellationToken ct)
    {
        await HttpContext.SignInAsync(
            "HangfireCookie",
            User,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            });

        return Ok(new { message = "Hangfire session created" });
    }

    [HttpPost("hangfire/logout")]
    [Authorize]
    public async Task<IActionResult> HangfireLogout(CancellationToken ct)
    {
        await HttpContext.SignOutAsync("HangfireCookie");
        return Ok(new { message = "Hangfire session cleared" });
    }
}
