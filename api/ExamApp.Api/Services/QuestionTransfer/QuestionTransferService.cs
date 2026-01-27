using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace ExamApp.Api.Services.QuestionTransfer;

public interface IQuestionTransferService
{
    Task<QuestionTransferJobDto> StartExportAsync(StartQuestionExportDto request, CancellationToken ct);
    Task<QuestionTransferJobDto> StartImportAsync(string sourceKey, string uploadedFileUrl, CancellationToken ct);
    Task<QuestionTransferJobDto?> GetJobAsync(Guid id, CancellationToken ct);
    Task<List<QuestionTransferJobDto>> ListJobsAsync(int take, CancellationToken ct);
    Task<Stream?> GetJobFileStreamAsync(Guid jobId, CancellationToken ct);

    Task<List<string>> ListSourceKeysAsync(CancellationToken ct);

    Task<List<QuestionTransferExportBundleDto>> ListExportBundlesAsync(string sourceKey, CancellationToken ct);
    Task<Stream?> GetExportBundleStreamAsync(string sourceKey, int bundleNo, CancellationToken ct);
    Task<Stream?> GetExportBundleMapStreamAsync(string sourceKey, int bundleNo, CancellationToken ct);
    Task<Stream?> GetExportSourceIndexStreamAsync(string sourceKey, CancellationToken ct);

    Task<QuestionTransferImportPreviewDto> PreviewImportAsync(IFormFile file, string? sourceOverride, CancellationToken ct);
}

public class QuestionTransferService : IQuestionTransferService
{
    private readonly AppDbContext _db;
    private readonly IMinIoService _minio;
    private readonly IBackgroundJobClient _jobs;
    private readonly string _minioBucket;

    public QuestionTransferService(
        AppDbContext db,
        IMinIoService minio,
        IBackgroundJobClient jobs,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _db = db;
        _minio = minio;
        _jobs = jobs;
        _minioBucket = configuration.GetSection("MinioConfig")["BucketName"] ?? "exam-questions";
    }

    public async Task<QuestionTransferJobDto> StartExportAsync(StartQuestionExportDto request, CancellationToken ct)
    {
        // QuestionIds may be empty to indicate "export all".

        var job = new QuestionTransferJob
        {
            Id = Guid.NewGuid(),
            Kind = QuestionTransferJobKind.Export,
            Status = QuestionTransferJobStatus.Queued,
            SourceKey = string.IsNullOrWhiteSpace(request.SourceKey) ? "default" : request.SourceKey.Trim(),
            RequestJson = JsonSerializer.Serialize(request),
            TotalItems = request.QuestionIds?.Count ?? 0,
            ProcessedItems = 0,
            Message = "Queued"
        };

        _db.Add(job);
        await _db.SaveChangesAsync(ct);

        _jobs.Enqueue<QuestionTransferJobRunner>(r => r.RunExportAsync(job.Id));

        return ToDto(job);
    }

    public async Task<QuestionTransferJobDto> StartImportAsync(string sourceKey, string uploadedFileUrl, CancellationToken ct)
    {
        var job = new QuestionTransferJob
        {
            Id = Guid.NewGuid(),
            Kind = QuestionTransferJobKind.Import,
            Status = QuestionTransferJobStatus.Queued,
            SourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim(),
            FileUrl = uploadedFileUrl,
            Message = "Queued"
        };

        _db.Add(job);
        await _db.SaveChangesAsync(ct);

        _jobs.Enqueue<QuestionTransferJobRunner>(r => r.RunImportAsync(job.Id));

        return ToDto(job);
    }

    public async Task<QuestionTransferJobDto?> GetJobAsync(Guid id, CancellationToken ct)
    {
        var job = await _db.Set<QuestionTransferJob>().FirstOrDefaultAsync(j => j.Id == id, ct);
        return job == null ? null : ToDto(job);
    }

    public async Task<List<QuestionTransferJobDto>> ListJobsAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 200);
        var jobs = await _db.Set<QuestionTransferJob>()
            .OrderByDescending(j => j.CreateTime)
            .Take(take)
            .ToListAsync(ct);

        return jobs.Select(ToDto).ToList();
    }

    public async Task<List<string>> ListSourceKeysAsync(CancellationToken ct)
    {
        // Sources may exist via bundles or jobs; merge both.
        var bundleKeys = await _db.Set<QuestionTransferExportBundle>()
            .Select(b => b.SourceKey)
            .Distinct()
            .ToListAsync(ct);

        var jobKeys = await _db.Set<QuestionTransferJob>()
            .Select(j => j.SourceKey)
            .Distinct()
            .ToListAsync(ct);

        return bundleKeys
            .Concat(jobKeys)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k)
            .Take(200)
            .ToList();
    }

    public async Task<Stream?> GetJobFileStreamAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.Set<QuestionTransferJob>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || string.IsNullOrWhiteSpace(job.FileUrl))
        {
            return null;
        }

        return await _minio.GetFileStreamAsync(job.FileUrl);
    }

    public async Task<List<QuestionTransferExportBundleDto>> ListExportBundlesAsync(string sourceKey, CancellationToken ct)
    {
        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim();
        var bundles = await _db.Set<QuestionTransferExportBundle>()
            .Where(b => b.SourceKey == sourceKey)
            .OrderBy(b => b.BundleNo)
            .ToListAsync(ct);

        return bundles.Select(b => new QuestionTransferExportBundleDto
        {
            SourceKey = b.SourceKey,
            BundleNo = b.BundleNo,
            QuestionCount = b.QuestionCount,
            FileUrl = b.FileUrl,
        }).ToList();
    }

    public async Task<Stream?> GetExportBundleStreamAsync(string sourceKey, int bundleNo, CancellationToken ct)
    {
        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim();
        if (bundleNo <= 0) return null;

        var bundle = await _db.Set<QuestionTransferExportBundle>()
            .FirstOrDefaultAsync(b => b.SourceKey == sourceKey && b.BundleNo == bundleNo, ct);

        if (bundle == null || string.IsNullOrWhiteSpace(bundle.FileUrl))
        {
            return null;
        }

        return await _minio.GetFileStreamAsync(bundle.FileUrl);
    }

    public async Task<Stream?> GetExportBundleMapStreamAsync(string sourceKey, int bundleNo, CancellationToken ct)
    {
        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim();
        if (bundleNo <= 0) return null;

        var objectName = $"question-transfer/exports/{sourceKey}/bundle-{bundleNo:D4}.map.json";
        var url = $"/img/{_minioBucket}/{objectName}";
        return await _minio.GetFileStreamAsync(url);
    }

    public async Task<Stream?> GetExportSourceIndexStreamAsync(string sourceKey, CancellationToken ct)
    {
        sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? "default" : sourceKey.Trim();

        var objectName = $"question-transfer/exports/{sourceKey}/index.json";
        var url = $"/img/{_minioBucket}/{objectName}";
        return await _minio.GetFileStreamAsync(url);
    }

    public async Task<QuestionTransferImportPreviewDto> PreviewImportAsync(IFormFile file, string? sourceOverride, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("File is required.");
        }

        await using var input = file.OpenReadStream();
        using var zip = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = zip.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("manifest.json not found in zip.");

        using var manifestStream = manifestEntry.Open();
        using var doc = await JsonDocument.ParseAsync(manifestStream, cancellationToken: ct);

        var sourceKeyFromManifest = doc.RootElement.TryGetProperty("sourceKey", out var sk)
            ? (sk.GetString() ?? string.Empty)
            : string.Empty;

        var sourceKey = !string.IsNullOrWhiteSpace(sourceOverride)
            ? sourceOverride.Trim()
            : (string.IsNullOrWhiteSpace(sourceKeyFromManifest) ? "default" : sourceKeyFromManifest.Trim());

        var questions = doc.RootElement.GetProperty("questions").EnumerateArray().ToList();
        var questionCount = questions.Count;

        var externalKeys = questions
            .Select(q => q.TryGetProperty("externalKey", out var ek) ? (ek.GetString() ?? string.Empty) : string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var alreadyImported = 0;
        const int chunkSize = 500;
        for (var i = 0; i < externalKeys.Count; i += chunkSize)
        {
            var chunk = externalKeys.Skip(i).Take(chunkSize).ToList();
            alreadyImported += await _db.Set<QuestionTransferImportMap>()
                .CountAsync(m => m.SourceKey == sourceKey && chunk.Contains(m.ExternalQuestionKey), ct);
        }

        return new QuestionTransferImportPreviewDto
        {
            SourceKey = sourceKey,
            QuestionCount = questionCount,
            AlreadyImportedCount = alreadyImported,
        };
    }

    private static QuestionTransferJobDto ToDto(QuestionTransferJob job)
    {
        return new QuestionTransferJobDto
        {
            Id = job.Id,
            Kind = job.Kind.ToString(),
            Status = job.Status.ToString(),
            SourceKey = job.SourceKey,
            TotalItems = job.TotalItems,
            ProcessedItems = job.ProcessedItems,
            FileUrl = job.FileUrl,
            Message = job.Message,
        };
    }

    // Execution logic lives in QuestionTransferJobRunner (Hangfire job)
}
