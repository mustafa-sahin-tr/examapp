using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

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

        // If sourceKey not provided, infer from manifest.json inside the zip.
        var sourceKey = string.IsNullOrWhiteSpace(request.SourceKey)
            ? await InferSourceKeyFromZipAsync(request.File, ct)
            : request.SourceKey.Trim();

        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey;
        var objectName = $"question-transfer/imports/{Guid.NewGuid()}.zip";

        await using var uploadStream = request.File.OpenReadStream();
        var url = await _minio.UploadFileAsync(uploadStream, objectName);

        var job = await _service.StartImportAsync(sourceKey, url, ct);
        return Ok(job);
    }

    [HttpPost("imports/preview")]
    [Authorize]
    [RequestSizeLimit(200_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<QuestionTransferImportPreviewDto>> PreviewImport([FromForm] StartQuestionImportFormDto request, CancellationToken ct)
    {
        if (request?.File == null || request.File.Length == 0)
        {
            return BadRequest(new { message = "File is required." });
        }

        var preview = await _service.PreviewImportAsync(request.File, request.SourceKey, ct);
        return Ok(preview);
    }

    [HttpGet("exports/{sourceKey}/bundles")]
    [Authorize]
    public async Task<ActionResult> ListExportBundles([FromRoute] string sourceKey, CancellationToken ct)
    {
        var bundles = await _service.ListExportBundlesAsync(sourceKey, ct);
        return Ok(bundles);
    }

    [HttpGet("exports/sources")]
    [Authorize]
    public async Task<ActionResult> ListSources(CancellationToken ct)
    {
        var sources = await _service.ListSourceKeysAsync(ct);
        return Ok(sources);
    }

    [HttpGet("exports/{sourceKey}/bundles/{bundleNo:int}/download")]
    [Authorize]
    public async Task<IActionResult> DownloadBundle([FromRoute] string sourceKey, [FromRoute] int bundleNo, CancellationToken ct)
    {
        var stream = await _service.GetExportBundleStreamAsync(sourceKey, bundleNo, ct);
        if (stream == null) return NotFound();
        return File(stream, "application/zip", $"question-transfer-{sourceKey}-bundle-{bundleNo:D4}.zip");
    }

    [HttpGet("exports/{sourceKey}/bundles/{bundleNo:int}/map")]
    [Authorize]
    public async Task<IActionResult> DownloadBundleMap([FromRoute] string sourceKey, [FromRoute] int bundleNo, CancellationToken ct)
    {
        var stream = await _service.GetExportBundleMapStreamAsync(sourceKey, bundleNo, ct);
        if (stream == null) return NotFound();
        return File(stream, "application/json", $"question-transfer-{sourceKey}-bundle-{bundleNo:D4}.map.json");
    }

    [HttpGet("exports/{sourceKey}/index")]
    [Authorize]
    public async Task<IActionResult> DownloadSourceIndex([FromRoute] string sourceKey, CancellationToken ct)
    {
        var stream = await _service.GetExportSourceIndexStreamAsync(sourceKey, ct);
        if (stream == null) return NotFound();
        return File(stream, "application/json", $"question-transfer-{sourceKey}-index.json");
    }

    // Downloads a single ZIP that contains index.json + all bundle zips (+ bundle map JSONs if present).
    [HttpGet("exports/{sourceKey}/package")]
    [Authorize]
    public IActionResult DownloadSourcePackage([FromRoute] string sourceKey)
    {
        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim();

        return new FileCallbackResult("application/zip", async (output, ctx) =>
        {
            var ct = ctx.HttpContext.RequestAborted;
            var bundles = await _service.ListExportBundlesAsync(sourceKey, ct);

            // IMPORTANT: ZipArchive writes central directory synchronously when disposed.
            // Kestrel disallows synchronous writes to Response.Body by default.
            // Build ZIP into a temp file stream (sync allowed), then CopyToAsync to the response.
            var tmpPath = Path.Combine(Path.GetTempPath(), $"question-transfer-{sourceKey}-package-{Guid.NewGuid():N}.zip");
            try
            {
                await using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        // We intentionally generate an IMPORT-COMPATIBLE zip here:
                        // - root manifest.json
                        // - questions/... and passages/... files directly at root paths
                        // This lets /imports and /imports/preview work with the package zip.

                        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var mergedQuestionsJson = new List<JsonElement>();
                        var seenExternalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var b in bundles.OrderBy(x => x.BundleNo))
                        {
                            var bundleStream = await _service.GetExportBundleStreamAsync(sourceKey, b.BundleNo, ct);
                            if (bundleStream == null)
                            {
                                continue;
                            }

                            var bundleTmpPath = Path.Combine(Path.GetTempPath(), $"question-transfer-{sourceKey}-bundle-{b.BundleNo:D4}-{Guid.NewGuid():N}.zip");
                            try
                            {
                                await using (bundleStream)
                                {
                                    await using var bundleFsWrite = new FileStream(bundleTmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                                    await bundleStream.CopyToAsync(bundleFsWrite, ct);
                                }

                                await using var bundleFsRead = new FileStream(bundleTmpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                using var bundleZip = new ZipArchive(bundleFsRead, ZipArchiveMode.Read, leaveOpen: false);

                                // Merge questions from the bundle manifest.json
                                var manifestEntry = bundleZip.GetEntry("manifest.json");
                                if (manifestEntry != null)
                                {
                                    using var ms = manifestEntry.Open();
                                    using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);
                                    if (doc.RootElement.TryGetProperty("questions", out var qArr) && qArr.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var q in qArr.EnumerateArray())
                                        {
                                            var externalKey = q.TryGetProperty("externalKey", out var ek)
                                                ? (ek.GetString() ?? string.Empty)
                                                : string.Empty;

                                            if (string.IsNullOrWhiteSpace(externalKey))
                                            {
                                                continue;
                                            }

                                            if (seenExternalKeys.Add(externalKey))
                                            {
                                                mergedQuestionsJson.Add(q.Clone());
                                            }
                                        }
                                    }
                                }

                                // Copy all files except manifest.json (we’ll write a merged root manifest).
                                foreach (var entry in bundleZip.Entries)
                                {
                                    if (string.IsNullOrWhiteSpace(entry.Name))
                                    {
                                        continue; // directory
                                    }
                                    if (entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    if (!seenEntries.Add(entry.FullName))
                                    {
                                        continue; // already added (e.g., shared passage)
                                    }

                                    var outEntry = zip.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                                    await using var outStream = outEntry.Open();
                                    await using var inStream = entry.Open();
                                    await inStream.CopyToAsync(outStream, ct);
                                }
                            }
                            finally
                            {
                                try { System.IO.File.Delete(bundleTmpPath); } catch { /* best effort */ }
                            }
                        }

                        // Write merged manifest.json (root)
                        var manifest = new
                        {
                            version = 2,
                            sourceKey = sourceKey,
                            bundleNo = 0,
                            exportedAtUtc = DateTime.UtcNow,
                            questions = mergedQuestionsJson,
                        };

                        var mergedManifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Fastest);
                        await using (var manifestStream = mergedManifestEntry.Open())
                        {
                            await JsonSerializer.SerializeAsync(manifestStream, manifest, cancellationToken: ct);
                        }
                    }

                    fs.Position = 0;
                    await fs.CopyToAsync(output, ct);
                }
            }
            finally
            {
                try { System.IO.File.Delete(tmpPath); } catch { /* best effort */ }
            }
        })
        {
            FileDownloadName = $"question-transfer-{sourceKey}-package.zip",
        };
    }

    private static async Task<string?> InferSourceKeyFromZipAsync(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = zip.GetEntry("manifest.json");
        if (manifestEntry == null) return null;

        using var manifestStream = manifestEntry.Open();
        using var doc = await JsonDocument.ParseAsync(manifestStream, cancellationToken: ct);
        return doc.RootElement.TryGetProperty("sourceKey", out var sk) ? sk.GetString() : null;
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
        var job = await _service.GetJobAsync(id, ct);
        if (job == null) return NotFound();

        var stream = await _service.GetJobFileStreamAsync(id, ct);
        if (stream == null) return NotFound();

        // Export jobs now primarily produce per-source index/map JSONs + bundle zips.
        // The job's FileUrl may point to index.json; serve a correct filename and content-type
        // so the browser doesn't save JSON as .zip (which looks like a corrupted ZIP).
        var url = (job.FileUrl ?? string.Empty).Trim();
        if (url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = url.EndsWith("index.json", StringComparison.OrdinalIgnoreCase)
                ? $"question-transfer-{job.SourceKey}-index.json"
                : $"question-transfer-{id}.json";

            return File(stream, "application/json", fileName);
        }

        if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return File(stream, "application/zip", $"question-transfer-{id}.zip");
        }

        return File(stream, "application/octet-stream", $"question-transfer-{id}");
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
