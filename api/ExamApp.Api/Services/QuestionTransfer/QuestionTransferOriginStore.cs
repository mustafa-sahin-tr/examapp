using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.QuestionTransfer;

internal static class QuestionTransferOriginStore
{
    public static async Task<string> GetOrCreateAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existing = await ReadAsync(db, ct);
        if (existing is not null)
            return existing;

        var row = new QuestionTransferOrigin { Key = $"origin-{Guid.NewGuid():N}" };
        db.QuestionTransferOrigins.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return row.Key;
        }
        catch (DbUpdateException)
        {
            // Aynı anda başka bir iş yazdı: onunki geçerli.
            db.Entry(row).State = EntityState.Detached;
            return await ReadAsync(db, ct) ?? throw new InvalidOperationException("Question transfer origin could not be created.");
        }
    }

    private static Task<string?> ReadAsync(AppDbContext db, CancellationToken ct) =>
        db.QuestionTransferOrigins.AsNoTracking()
            .Where(o => o.Id == QuestionTransferOrigin.SingleRowId)
            .Select(o => o.Key)
            .FirstOrDefaultAsync(ct);
}
