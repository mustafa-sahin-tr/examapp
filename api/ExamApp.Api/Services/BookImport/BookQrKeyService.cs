using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.BookImport;

public enum QrKeyError
{
    None,
    NotFound,
    InvalidState,
    NoQr,
    NotAvailable,
    Running,
}

public sealed record QrKeyStart(QrKeyError Error, BookSetupQrRun? Run = null);

/// <summary>
/// #1219: onay ekranında "QR'dan al": anahtarı olmayan testlerin sayfasındaki QR bağlantısından (kurulum taramasında okunur)
/// yayıncının çözümlerini çeker, her sorunun ses kaydını yazıya çevirir ve cevap harfini çıkarır. Okunamayan harf '?' kalır; tahmin
/// yok. Ses çevirisi yavaş olduğu için Hangfire işinde çalışır (<see cref="StartAsync"/> kuyruğa ekler, <see cref="RunAsync"/>
/// okur); ilerleme <see cref="BookSetupSummary.QrRun"/>'da, okunan anahtar <see cref="BookKeySources.Qr"/> kaynağıyla test
/// satırına yazılır (onay ekranı yeniden okuyunca doldurulur, öğretmen onaylar). Claude kullanılmaz; ses hizmeti yoksa çalışmaz.
/// </summary>
public sealed class BookQrKeyService(AppDbContext db, QrSolutionClient client, IAudioTranscriber transcriber,
    IEnumerable<IQrKeyReader> readers, IBackgroundJobClient jobs, ILogger<BookQrKeyService> logger)
{
    /// <summary>question-detector'ın gövde sınırının (25 MiB) altında.</summary>
    private const int MaxAudioBytes = 24 * 1024 * 1024;

    /// <summary>Bu kadar süre ilerlemeyen okuma yarıda kalmış sayılır (API yeniden başladı): yeniden başlatılabilir.</summary>
    internal static readonly TimeSpan Stale = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Anahtarı olmayan, QR'ı okunabilir testleri (ya da yalnız <paramref name="order"/>) okumak üzere kuyruğa ekler. Çağıran işin
    /// görünür olduğunu (sahip / admin) denetlemiş olmalı.
    /// </summary>
    public async Task<QrKeyStart> StartAsync(Guid jobId, int? order, CancellationToken ct)
    {
        var job = await db.BookImportJobs.AsTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !IsAwaitingSetup(job) || BookSetupSummary.Parse(job.SetupJson) is not { } summary)
            return new(QrKeyError.InvalidState);
        if (summary.QrRun is { } current && IsActive(current))
            return new(QrKeyError.Running, current);

        var tests = await db.BookImportJobTests.AsNoTracking()
            .Where(t => t.JobId == jobId && t.AnswerKey == null && (order == null || t.Order == order))
            .OrderBy(t => t.Order)
            .Select(t => new { t.Order, t.FileFrom, t.FileTo })
            .ToListAsync(ct);
        if (order is not null && tests.Count == 0)
            return new(QrKeyError.NotFound);
        var orders = tests.Where(t => Readable(summary.QrLinkFor(t.FileFrom, t.FileTo))).Select(t => t.Order).ToList();
        if (orders.Count == 0)
            return new(QrKeyError.NoQr);
        if (!await transcriber.IsAvailableAsync(ct))
            return new(QrKeyError.NotAvailable);

        summary.QrRun = new BookSetupQrRun { Orders = orders, UpdatedAt = DateTimeOffset.UtcNow };
        job.SetupJson = summary.Serialize();
        await db.SaveChangesAsync(ct);
        jobs.Enqueue<BookQrKeyService>(s => s.RunAsync(jobId, CancellationToken.None));
        return new(QrKeyError.None, summary.QrRun);
    }

    /// <summary>Hangfire: sıradaki testleri okur. İş onaydan çıkarsa (onaylandı / iptal) durur; elle girilen anahtarın üstüne yazmaz.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        while (true)
        {
            var job = await db.BookImportJobs.AsTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job is null || BookSetupSummary.Parse(job.SetupJson) is not { QrRun: { } run } summary || run.Finished)
                return;
            var next = run.Orders.Skip(run.Done).FirstOrDefault();
            if (!IsAwaitingSetup(job) || next == 0)
            {
                await SaveRunAsync(job, summary, r => r.Finished = true, ct);
                return;
            }

            var row = await db.BookImportJobTests.AsTracking().FirstOrDefaultAsync(t => t.JobId == jobId && t.Order == next, ct);
            var failed = false;
            if (row is { AnswerKey: null } && summary.QrLinkFor(row.FileFrom, row.FileTo) is { } link)
            {
                var key = await ReadKeyAsync(jobId, next, link, ct);
                failed = key is null;
                // Okuma sürerken öğretmen onayladıysa / iptal ettiyse dokunma (onay isteği kendi anahtarını yazar).
                await db.Entry(row).ReloadAsync(ct);
                await db.Entry(job).ReloadAsync(ct);
                if (key is { Length: > 0 } && row.AnswerKey is null && IsAwaitingSetup(job))
                {
                    row.AnswerKey = key;
                    row.KeySource = BookKeySources.Qr;
                    row.KeyAnswers = key.Count(c => c != '?');
                }

                summary = BookSetupSummary.Parse(job.SetupJson) ?? summary;
                if (summary.QrRun is null)
                    return;
            }

            await SaveRunAsync(job, summary, r =>
            {
                r.Done++;
                if (failed)
                    r.Failed++;
                r.Finished = r.Done >= r.Orders.Count;
            }, ct);
        }
    }

    /// <summary>Testin anahtarı (soru sırasıyla, okunamayan '?'); çözüm listesi yoksa boş, çekilemezse null.</summary>
    private async Task<string?> ReadKeyAsync(Guid jobId, int order, string link, CancellationToken ct)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || readers.FirstOrDefault(r => r.Handles(uri)) is not { } reader)
            return null;
        try
        {
            var solutions = await reader.ReadAsync(uri, client, ct);
            if (solutions.Count > BookImportJobTest.MaxKeyLength)
                return string.Empty;
            var letters = new char[solutions.Count];
            for (var i = 0; i < solutions.Count; i++)
                letters[i] = await LetterAsync(reader, solutions[i], ct);
            return new string(letters);
        }
        catch (Exception e) when (e is QrFetchException or HttpRequestException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Book import job {JobId} test {Order}: QR key failed ({Link}): {Message}", jobId, order, link, e.Message);
            return null;
        }
    }

    private async Task<char> LetterAsync(IQrKeyReader reader, QrSolution solution, CancellationToken ct)
    {
        if (solution.Audio is null)
            return '?';
        var audio = await client.GetAsync(solution.Audio, reader.Handles, MaxAudioBytes, ct);
        var text = await transcriber.TranscribeAsync(audio, solution.Audio.Segments[^1], ct);
        return QrAnswerLetter.From(text);
    }

    private async Task SaveRunAsync(BookImportJob job, BookSetupSummary summary, Action<BookSetupQrRun> change, CancellationToken ct)
    {
        change(summary.QrRun!);
        summary.QrRun!.UpdatedAt = DateTimeOffset.UtcNow;
        job.SetupJson = summary.Serialize();
        await db.SaveChangesAsync(ct);
    }

    private bool Readable(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && readers.Any(r => r.Handles(uri));

    internal static bool IsActive(BookSetupQrRun run) => !run.Finished && DateTimeOffset.UtcNow - run.UpdatedAt < Stale;

    private static bool IsAwaitingSetup(BookImportJob job) =>
        job.DemoSourceJobId is null
        && job.Status == BookImportJobStatus.AwaitingConfirmation && job.Stage == BookImportStage.SetupConfirmation;
}
