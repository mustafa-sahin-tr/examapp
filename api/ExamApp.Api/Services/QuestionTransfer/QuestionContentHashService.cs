using ExamApp.Api.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.QuestionTransfer;

/// <param name="Filled">Bu çalışmada hash'i yazılan soru sayısı.</param>
/// <param name="Unresolved">Hâlâ hash'i olmayan soru sayısı. Sıfır değilse eşleşme eksik kalır, aynı soru kopya olarak yazılabilir.</param>
public sealed record QuestionHashFillResult(int Filled, int Unresolved);

public interface IQuestionContentHashService
{
    Task<int> CountMissingAsync(CancellationToken ct = default);

    /// <summary>
    /// Hash'i olmayan soruları doldurur. Görseli depoda bulunmayan soru kimlik almaz ve tekrar denenmez.
    /// Depo yanıt vermediği için okunamayan soru yazılmaz, sonraki çalışmada yeniden denenir.
    /// </summary>
    Task<QuestionHashFillResult> FillMissingAsync(CancellationToken ct = default);
}

public class QuestionContentHashService(AppDbContext db, IMinIoService minio, ILogger<QuestionContentHashService> logger) : IQuestionContentHashService
{
    public const string RecurringJobId = "question-content-hash";
    private const int BatchSize = 100;

    // Art arda bu kadar görsele ulaşılamıyorsa depo kapalıdır: kalan sorular için boşuna beklenmez.
    private const int StorageDownThreshold = 20;

    private enum ImageState
    {
        Found,
        Missing,
        Unavailable,
    }

    public Task<int> CountMissingAsync(CancellationToken ct = default) =>
        db.Questions.AsNoTracking().CountAsync(q => q.ContentFingerprint == null, ct);

    [Queue("question-transfer")]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync() => FillMissingAsync();

    public async Task<QuestionHashFillResult> FillMissingAsync(CancellationToken ct = default)
    {
        var filled = 0;
        var unavailable = 0;
        var lastId = 0;

        while (unavailable < StorageDownThreshold)
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
                var (image, state) = await ReadImageAsync(question.ImageUrl, ct);
                if (state == ImageState.Unavailable)
                {
                    if (++unavailable >= StorageDownThreshold)
                    {
                        logger.LogWarning("Stopped filling question hashes at question {QuestionId}: storage did not answer for {Count} images in a row", question.Id, unavailable);
                        break;
                    }
                    continue;
                }

                unavailable = 0;
                var hashes = QuestionContentHash.ForQuestion(question, image);
                // Görseli olmayan soruya içeriğinden kimlik verilmez: başka bir soruyla yanlış eşleşebilir.
                filled += await WriteAsync(question, state == ImageState.Missing ? hashes with { ContentHash = null } : hashes, ct);
            }
        }

        return new QuestionHashFillResult(filled, await CountMissingAsync(ct));
    }

    private async Task<(byte[]? Image, ImageState State)> ReadImageAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (null, ImageState.Found);

        try
        {
            await using var stream = await minio.GetFileStreamAsync(url);
            if (stream is null)
                return (null, await minio.FileExistsAsync(url, ct) ? ImageState.Unavailable : ImageState.Missing);

            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return (buffer.ToArray(), ImageState.Found);
        }
        catch (ArgumentException ex)
        {
            // Bozuk URL düzelmez: tek bir satır bütün doldurma işini ve içe aktarımları düşürmesin.
            logger.LogWarning(ex, "Question image URL could not be read as a storage path");
            return (null, ImageState.Missing);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Question image could not be read");
            return (null, ImageState.Unavailable);
        }
    }

    /// <summary>
    /// Denetim alanları değişmesin diye doğrudan yazılır: hash hesaplamak soruyu düzenlemek değildir.
    /// Soru okunduktan sonra düzenlendiyse yazılmaz: eski içeriğin hash'i yeni içeriğe kalırdı.
    /// </summary>
    private Task<int> WriteAsync(Question question, QuestionContentHashes hashes, CancellationToken ct)
    {
        var (id, updateTime) = (question.Id, question.UpdateTime);
        return db.Questions.Where(q => q.Id == id && q.ContentFingerprint == null && q.UpdateTime == updateTime)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.ContentHash, hashes.ContentHash)
                .SetProperty(q => q.ContentFingerprint, hashes.Fingerprint), ct);
    }
}
