using ExamApp.Api.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.QuestionTransfer;

public interface IQuestionContentHashService
{
    Task<int> CountMissingAsync(CancellationToken ct = default);

    /// <summary>Hash'i olmayan soruları doldurur ve doldurulan sayıyı döner. Görseli okunamayan soru eşleşmez, tekrar denenmez.</summary>
    Task<int> FillMissingAsync(CancellationToken ct = default);
}

public class QuestionContentHashService(AppDbContext db, IMinIoService minio, ILogger<QuestionContentHashService> logger) : IQuestionContentHashService
{
    public const string RecurringJobId = "question-content-hash";
    private const int BatchSize = 100;

    // Art arda bu kadar görsel okunamıyorsa görseller yok değil, depo erişilemiyordur: yanlış sonuç yazmadan durulur.
    private const int StorageDownThreshold = 20;

    public Task<int> CountMissingAsync(CancellationToken ct = default) =>
        db.Questions.AsNoTracking().CountAsync(q => q.ContentFingerprint == null, ct);

    [Queue("question-transfer")]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync() => FillMissingAsync();

    public async Task<int> FillMissingAsync(CancellationToken ct = default)
    {
        var filled = 0;
        var unavailable = 0;
        var lastId = 0;
        var pending = new List<(int Id, QuestionContentHashes Hashes)>();

        while (true)
        {
            var batch = await db.Questions.AsNoTracking()
                .Include(q => q.Answers)
                .Where(q => q.ContentFingerprint == null && q.Id > lastId)
                .OrderBy(q => q.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;

            foreach (var question in batch)
            {
                lastId = question.Id;
                var (image, readable) = await ReadImageAsync(question.ImageUrl);
                var hashes = QuestionContentHash.ForQuestion(question, image);
                if (readable)
                {
                    unavailable = 0;
                    filled += await WriteAsync(pending, ct);
                    filled += await WriteAsync([(question.Id, hashes)], ct);
                    continue;
                }

                // Görseli okunamayan soruya içeriğinden kimlik verilmez: başka bir soruyla yanlış eşleşebilir.
                pending.Add((question.Id, hashes with { ContentHash = null }));
                if (++unavailable >= StorageDownThreshold)
                {
                    logger.LogWarning("Stopped filling question hashes at question {QuestionId}: {Count} images in a row could not be read", question.Id, unavailable);
                    return filled;
                }
            }
        }

        filled += await WriteAsync(pending, ct);
        return filled;
    }

    private async Task<(byte[]? Image, bool Readable)> ReadImageAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (null, true);

        await using var stream = await minio.GetFileStreamAsync(url);
        if (stream is null)
            return (null, false);

        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return (buffer.ToArray(), true);
    }

    /// <summary>Denetim alanları değişmesin diye doğrudan yazılır: hash hesaplamak soruyu düzenlemek değildir.</summary>
    private async Task<int> WriteAsync(IReadOnlyCollection<(int Id, QuestionContentHashes Hashes)> rows, CancellationToken ct)
    {
        foreach (var (id, hashes) in rows)
        {
            await db.Questions.Where(q => q.Id == id && q.ContentFingerprint == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.ContentHash, hashes.ContentHash)
                    .SetProperty(q => q.ContentFingerprint, hashes.Fingerprint), ct);
        }

        var count = rows.Count;
        if (rows is List<(int, QuestionContentHashes)> list)
            list.Clear();
        return count;
    }
}
