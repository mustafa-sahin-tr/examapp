using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

public sealed partial class BookImportService
{
    /// <summary>Issue #1265: bir yanıtta en çok satır (olay ve çağrı ayrı ayrı); ekran kalanını sonraki yoklamada alır.</summary>
    internal const int MaxActivityRows = 500;

    public async Task<BookImportActivityDto?> GetActivityAsync(BookImportOwner owner, Guid id, long afterId, bool calls,
        long afterCallId, CancellationToken ct)
    {
        if (!await Visible(owner).AnyAsync(j => j.Id == id, ct))
            return null;

        var events = await _db.BookImportJobEvents.AsNoTracking()
            .Where(e => e.JobId == id && e.Id > afterId)
            .OrderBy(e => e.Id).Take(MaxActivityRows)
            .Select(e => new { e.Id, e.CreatedAt, e.Stage, e.Code, e.DataJson })
            .ToListAsync(ct);
        var result = new BookImportActivityDto
        {
            Events = events.Select(e => new BookImportEventDto
            {
                Id = e.Id,
                At = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
                Stage = e.Stage.ToString(),
                Code = e.Code,
                Data = e.DataJson is null ? null : JsonSerializer.Deserialize<JsonElement>(e.DataJson),
            }).ToList(),
        };
        if (calls)
        {
            var rows = await _db.BookImportCosts.AsNoTracking()
                .Where(c => c.JobId == id && c.Id > afterCallId)
                .OrderBy(c => c.Id).Take(MaxActivityRows)
                .Select(c => new { c.Id, c.CreatedAt, c.Stage, c.TestId, c.ModelId, c.InputTokens, c.OutputTokens, c.CostUsd })
                .ToListAsync(ct);
            result.Calls = rows.Select(c => new BookImportCallDto
            {
                Id = c.Id,
                At = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
                Stage = c.Stage.ToString(),
                TestId = c.TestId,
                ModelId = c.ModelId,
                InputTokens = c.InputTokens,
                OutputTokens = c.OutputTokens,
                CostUsd = c.CostUsd,
            }).ToList();
        }
        return result;
    }
}
