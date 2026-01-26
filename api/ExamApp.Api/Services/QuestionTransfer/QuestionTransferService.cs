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

namespace ExamApp.Api.Services.QuestionTransfer;

public interface IQuestionTransferService
{
    Task<QuestionTransferJobDto> StartExportAsync(StartQuestionExportDto request, CancellationToken ct);
    Task<QuestionTransferJobDto> StartImportAsync(string sourceKey, string uploadedFileUrl, CancellationToken ct);
    Task<QuestionTransferJobDto?> GetJobAsync(Guid id, CancellationToken ct);
    Task<List<QuestionTransferJobDto>> ListJobsAsync(int take, CancellationToken ct);
    Task<Stream?> GetJobFileStreamAsync(Guid jobId, CancellationToken ct);
}

public class QuestionTransferService : IQuestionTransferService
{
    private readonly AppDbContext _db;
    private readonly IMinIoService _minio;
    private readonly IBackgroundJobClient _jobs;

    public QuestionTransferService(AppDbContext db, IMinIoService minio, IBackgroundJobClient jobs)
    {
        _db = db;
        _minio = minio;
        _jobs = jobs;
    }

    public async Task<QuestionTransferJobDto> StartExportAsync(StartQuestionExportDto request, CancellationToken ct)
    {
        if (request.QuestionIds == null || request.QuestionIds.Count == 0)
        {
            throw new ArgumentException("QuestionIds is required.");
        }

        var job = new QuestionTransferJob
        {
            Id = Guid.NewGuid(),
            Kind = QuestionTransferJobKind.Export,
            Status = QuestionTransferJobStatus.Queued,
            SourceKey = string.IsNullOrWhiteSpace(request.SourceKey) ? "default" : request.SourceKey.Trim(),
            RequestJson = JsonSerializer.Serialize(request),
            TotalItems = request.QuestionIds.Count,
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

    public async Task<Stream?> GetJobFileStreamAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.Set<QuestionTransferJob>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || string.IsNullOrWhiteSpace(job.FileUrl))
        {
            return null;
        }

        return await _minio.GetFileStreamAsync(job.FileUrl);
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
