using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Issue #1056: raporun süzgeci; tarih aralığının iki ucu da dahil (UTC günleri).</summary>
public sealed record BookImportCostFilter(DateOnly From, DateOnly To, BookImportJobKind? Kind, int? UserId);

/// <summary>
/// Issue #1056: aktarım maliyet raporu. Maliyet, aralıkta yazılan <see cref="BookImportCost"/> satırlarının toplamıdır (ay sınırını
/// aşan iş her aya kendi çağrılarıyla girer). İş kümesi: aralıkta açılan ya da aralıkta maliyet yazılan işler; iptal ve hata dahil,
/// önbellekten dönen çağrılar maliyet yazmadığı için maliyetsiz iş de görünür. Sayfa ve soru sayıları işin tamamına aittir.
/// </summary>
public interface IBookImportCostReportService
{
    Task<BookImportCostReportDto> GetAsync(BookImportCostFilter filter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>CSV dışa aktarımı: filtreye uyan işler, en çok <see cref="BookImportCostReportService.MaxExportRows"/> satır.</summary>
    Task<List<BookImportCostJobDto>> ListJobsAsync(BookImportCostFilter filter, CancellationToken ct = default);
}

public sealed class BookImportCostReportService(AppDbContext db, IAuthApiClient authApi, IAdminSubscriptionService subscriptions)
    : IBookImportCostReportService
{
    public const int MaxPageSize = 100;
    public const int MaxExportRows = 10_000;

    /// <summary>Kullanıcı tablosunda en yüksek maliyetli bu kadar kişi gösterilir.</summary>
    public const int MaxUsers = 100;

    public async Task<BookImportCostReportDto> GetAsync(BookImportCostFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var jobs = Jobs(filter);
        var costs = Costs(filter);

        var jobCount = await jobs.CountAsync(ct);
        var pages = await jobs.SumAsync(j => j.PageCount ?? 0, ct);
        var questions = await db.BookImportJobTests.AsNoTracking()
            .Where(t => t.Status != BookImportTestStatus.Removed && jobs.Any(j => j.Id == t.JobId))
            .SumAsync(t => t.QuestionsSaved, ct);
        var totalCost = await costs.SumAsync(c => c.CostUsd, ct);

        var stages = await costs.GroupBy(c => new { c.Stage, c.ModelId })
            .Select(g => new
            {
                g.Key.Stage, g.Key.ModelId, Calls = g.Count(), Input = g.Sum(c => c.InputTokens),
                Output = g.Sum(c => c.OutputTokens), Usd = g.Sum(c => c.CostUsd),
            })
            .ToListAsync(ct);

        var jobsPerUser = await jobs.GroupBy(j => j.CreateUserId ?? 0).Select(g => new { UserId = g.Key, Jobs = g.Count() }).ToListAsync(ct);
        var costPerUser = (await costs.GroupBy(c => c.Job.CreateUserId ?? 0).Select(g => new { UserId = g.Key, Usd = g.Sum(c => c.CostUsd) }).ToListAsync(ct))
            .ToDictionary(u => u.UserId, u => u.Usd);
        var topUsers = jobsPerUser
            .Select(u => new BookImportCostUserDto { UserId = u.UserId, Jobs = u.Jobs, CostUsd = costPerUser.GetValueOrDefault(u.UserId) })
            .OrderByDescending(u => u.CostUsd).ThenBy(u => u.UserId)
            .Take(MaxUsers)
            .ToList();

        var jobRows = await JobRowsAsync(filter, (page - 1) * pageSize, pageSize, ct);

        var names = await NamesAsync(topUsers.Select(u => u.UserId).Concat(jobRows.Select(j => j.OwnerId)), ct);
        var plans = await subscriptions.CurrentPlansAsync(topUsers.Select(u => u.UserId).ToList(), ct);
        foreach (var user in topUsers)
        {
            if (names.TryGetValue(user.UserId, out var lookup))
            {
                user.FullName = lookup.FullName;
                user.Role = lookup.Role;
            }
            if (plans.TryGetValue(user.UserId, out var plan))
            {
                user.PlanKey = plan.Key;
                user.PlanName = plan.Name;
            }
        }
        foreach (var job in jobRows)
            job.OwnerName = names.GetValueOrDefault(job.OwnerId)?.FullName;

        return new BookImportCostReportDto
        {
            From = filter.From,
            To = filter.To,
            Totals = new BookImportCostTotalsDto
            {
                Jobs = jobCount,
                Pages = pages,
                QuestionsSaved = questions,
                CostUsd = totalCost,
                CostPerQuestionUsd = questions > 0 ? totalCost / questions : null,
            },
            Users = topUsers,
            JobCount = jobCount,
            Jobs = jobRows,
            Stages = stages.OrderBy(s => s.Stage).ThenBy(s => s.ModelId)
                .Select(s => new BookImportCostDto
                {
                    Stage = s.Stage.ToString(), ModelId = s.ModelId, Calls = s.Calls,
                    InputTokens = s.Input, OutputTokens = s.Output, CostUsd = s.Usd,
                }).ToList(),
        };
    }

    public async Task<List<BookImportCostJobDto>> ListJobsAsync(BookImportCostFilter filter, CancellationToken ct = default)
    {
        var rows = await JobRowsAsync(filter, 0, MaxExportRows, ct);
        var names = await NamesAsync(rows.Select(j => j.OwnerId), ct);
        foreach (var job in rows)
            job.OwnerName = names.GetValueOrDefault(job.OwnerId)?.FullName;
        return rows;
    }

    private async Task<List<BookImportCostJobDto>> JobRowsAsync(BookImportCostFilter filter, int skip, int take, CancellationToken ct)
    {
        var (from, to) = Range(filter);
        var rows = await Jobs(filter)
            .OrderByDescending(j => j.CreateTime).ThenBy(j => j.Id)
            .Skip(skip).Take(take)
            .Select(j => new
            {
                j.Id, j.Kind, j.BookName, j.CreateUserId, j.CreateTime, j.Status, j.PageCount,
                Questions = j.Tests.Where(t => t.Status != BookImportTestStatus.Removed).Sum(t => t.QuestionsSaved),
                Usd = j.Costs.Where(c => c.CreatedAt >= from && c.CreatedAt < to).Sum(c => c.CostUsd),
            })
            .ToListAsync(ct);
        return rows.Select(j => new BookImportCostJobDto
        {
            Id = j.Id,
            Kind = j.Kind.ToString(),
            BookName = j.BookName,
            OwnerId = j.CreateUserId ?? 0,
            CreatedAt = j.CreateTime,
            Status = j.Status.ToString(),
            Pages = j.PageCount ?? 0,
            QuestionsSaved = j.Questions,
            CostUsd = j.Usd,
        }).ToList();
    }

    private IQueryable<BookImportJob> Jobs(BookImportCostFilter filter)
    {
        var (from, to) = Range(filter);
        var query = db.BookImportJobs.AsNoTracking()
            .Where(j => (j.CreateTime >= from && j.CreateTime < to) || j.Costs.Any(c => c.CreatedAt >= from && c.CreatedAt < to));
        if (filter.Kind is { } kind)
            query = query.Where(j => j.Kind == kind);
        if (filter.UserId is { } userId)
            query = query.Where(j => j.CreateUserId == userId);
        return query;
    }

    private IQueryable<BookImportCost> Costs(BookImportCostFilter filter)
    {
        var (from, to) = Range(filter);
        var query = db.BookImportCosts.AsNoTracking().Where(c => c.CreatedAt >= from && c.CreatedAt < to);
        if (filter.Kind is { } kind)
            query = query.Where(c => c.Job.Kind == kind);
        if (filter.UserId is { } userId)
            query = query.Where(c => c.Job.CreateUserId == userId);
        return query;
    }

    private static (DateTime From, DateTime To) Range(BookImportCostFilter filter) =>
        (filter.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), filter.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    private async Task<Dictionary<int, UserLookupResultDto>> NamesAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];
        return (await authApi.GetUsersByIdsAsync(ids, ct)).ToDictionary(u => u.Id);
    }
}
