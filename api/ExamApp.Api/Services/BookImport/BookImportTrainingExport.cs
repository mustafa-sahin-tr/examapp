using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

public static class BookImportTrainingSkip
{
    /// <summary>Sayfa onaylanmamış (incelemedeki / başarısız) bir testin de sayfası: kutuları gözden geçirilmedi.</summary>
    public const string OpenTest = "openTest";

    /// <summary>Sayfada inceleme uyarısı var ve işaretlemede düzeltilmedi.</summary>
    public const string Flagged = "flagged";

    /// <summary>Sayfanın tespit sonucu yok.</summary>
    public const string NoDetection = "noDetection";

    /// <summary>Claude sonucunda ikiden az şıklı soru var (düzeltilmedi).</summary>
    public const string FewChoices = "fewChoices";

    /// <summary>Sayfa görseli depoda yok.</summary>
    public const string ImageMissing = "imageMissing";
}

public sealed record BookImportTrainingBookDto(Guid JobId, string BookName, int Pages, int Questions, int ManualPages);

public sealed record BookImportTrainingSkipDto(Guid JobId, string BookName, int FilePage, string Reason);

public sealed record BookImportTrainingSummaryDto(
    int Pages,
    int CompletePages,
    int EmptyPages,
    int Questions,
    IReadOnlyDictionary<int, int> ChoiceCounts,
    IReadOnlyList<BookImportTrainingBookDto> Books,
    IReadOnlyList<BookImportTrainingSkipDto> Skipped);

public interface IBookImportTrainingExport
{
    /// <summary>Kuru çalıştırma: ne aktarılacağını ve neyin neden atlandığını döner, dosya üretmez.</summary>
    Task<BookImportTrainingSummaryDto> SummarizeAsync(Guid? jobId, CancellationToken ct);

    /// <summary>Sayfa görselleri + <c>manifest.json</c> (kutular, özet) ZIP olarak <paramref name="output"/>'a yazılır.</summary>
    Task<BookImportTrainingSummaryDto> WriteZipAsync(Guid? jobId, Stream output, CancellationToken ct);
}

/// <summary>
/// Issue #1133: kitap aktarımının incelenmiş sayfaları → question-detector eğitim verisi. Yalnız kitap işleri; sayfa onaylı bir
/// testin sayfası olmalı ve onaylanmamış bir testle paylaşılmamalı. Kutular sayfanın elle doğrulanmış sonucundan
/// (<see cref="LabelingSource.Manual"/>, işaretlemede düzeltme yazar), yoksa en son Claude sonucundan; uyarılı sayfa yalnız
/// düzeltildiyse alınır. Etkinlik bloğu olan sayfa "eksik" sayılır (tam sayfa soru modeline girmez, yalnız şık kırpmaları).
/// Veri setine yazma ve tekrar kontrolü question-detector'daki <c>training/import_book_export.py</c>'dedir (<c>page_id</c> =
/// görsel SHA-256'sının ilk 16 hanesi, işaretleme ekranıyla aynı).
/// </summary>
public sealed class BookImportTrainingExport(AppDbContext db, IMinIoService minio) : IBookImportTrainingExport
{
    internal const int MinChoices = 2;

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<BookImportTrainingSummaryDto> SummarizeAsync(Guid? jobId, CancellationToken ct) =>
        Summarize(await PlanAsync(jobId, ct));

    public async Task<BookImportTrainingSummaryDto> WriteZipAsync(Guid? jobId, Stream output, CancellationToken ct)
    {
        var plan = await PlanAsync(jobId, ct);
        var summary = Summarize(plan);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var pages = new List<object>(plan.Pages.Count);
        foreach (var page in plan.Pages)
        {
            var bytes = await ReadAsync(page.ImageUrl, ct);
            // Plan ile yazım arasında görsel değiştiyse (yeniden aktarım) kutular artık ona ait değil.
            if (bytes is null || Sha256(bytes) != page.Sha256)
                continue;
            var file = $"pages/{page.Sha256[..16]}{Path.GetExtension(page.ImageUrl)}";
            var entry = zip.CreateEntry(file, CompressionLevel.NoCompression);
            await using (var stream = entry.Open())
                await stream.WriteAsync(bytes, ct);
            pages.Add(new
            {
                pageId = page.Sha256[..16],
                file,
                page.JobId,
                page.BookName,
                page.FilePage,
                source = page.Source.ToString(),
                page.Complete,
                page.Width,
                page.Height,
                questions = page.Questions.Select(q => new
                {
                    box = Box(q),
                    answers = q.Subpredictions.Select(Box),
                }),
            });
        }

        var manifest = zip.CreateEntry("manifest.json");
        await using (var stream = manifest.Open())
            await JsonSerializer.SerializeAsync(stream, new { exportedAt = DateTime.UtcNow, summary, pages }, ManifestJson, ct);
        return summary;
    }

    private static int[] Box(PredictionDto p) => [p.X, p.Y, p.Width, p.Height];

    internal async Task<Plan> PlanAsync(Guid? jobId, CancellationToken ct)
    {
        var tests = await db.BookImportJobTests.AsNoTracking()
            .Where(t => t.Job.Kind == BookImportJobKind.Book && (jobId == null || t.JobId == jobId)
                        && t.Status != BookImportTestStatus.Removed && t.FileFrom != null && t.FileTo != null)
            .Select(t => new { t.JobId, t.Job.BookName, t.Status, From = t.FileFrom!.Value, To = t.FileTo!.Value, t.ReviewFlagsJson })
            .ToListAsync(ct);

        var plan = new Plan();
        foreach (var job in tests.GroupBy(t => (t.JobId, t.BookName)))
        {
            var approved = new SortedSet<int>();
            var open = new HashSet<int>();
            var flagged = new HashSet<int>();
            foreach (var test in job)
            {
                var range = Enumerable.Range(test.From, test.To - test.From + 1);
                if (test.Status != BookImportTestStatus.Approved)
                {
                    open.UnionWith(range);
                    continue;
                }
                approved.UnionWith(range);
                foreach (var flag in BookImportReviewFlag.Parse(test.ReviewFlagsJson).Where(f => f.Code != BookImportReviewFlag.CountMismatch))
                    flagged.UnionWith(flag.Pages);
            }
            if (approved.Count == 0)
                continue;

            var images = await db.BookImportPages.AsNoTracking()
                .Where(p => p.JobId == job.Key.JobId && approved.Contains(p.FilePage))
                .ToDictionaryAsync(p => p.FilePage, ct);
            var candidates = new List<(BookImportPage Page, string Sha256)>();
            foreach (var filePage in approved)
            {
                if (open.Contains(filePage))
                {
                    plan.Skip(job.Key.JobId, job.Key.BookName, filePage, BookImportTrainingSkip.OpenTest);
                    continue;
                }
                var bytes = images.TryGetValue(filePage, out var image) ? await ReadAsync(image.ImageUrl, ct) : null;
                if (bytes is null)
                {
                    plan.Skip(job.Key.JobId, job.Key.BookName, filePage, BookImportTrainingSkip.ImageMissing);
                    continue;
                }
                candidates.Add((image!, Sha256(bytes)));
            }

            var shas = candidates.Select(c => c.Sha256).ToList();
            var results = (await db.LabelingResults.AsNoTracking()
                    .Where(r => shas.Contains(r.Page.ImageSha256) && (r.Source == LabelingSource.Manual || r.Source == LabelingSource.Ai))
                    .Select(r => new { r.Page.ImageSha256, r.Source, r.UpdatedAt, r.BoxesJson })
                    .ToListAsync(ct))
                .GroupBy(r => r.ImageSha256)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Source == LabelingSource.Manual ? 0 : 1)
                    .ThenByDescending(r => r.UpdatedAt).First());

            foreach (var (page, sha) in candidates)
            {
                if (!results.TryGetValue(sha, out var result))
                {
                    plan.Skip(job.Key.JobId, job.Key.BookName, page.FilePage, BookImportTrainingSkip.NoDetection);
                    continue;
                }
                var manual = result.Source == LabelingSource.Manual;
                if (flagged.Contains(page.FilePage) && !manual)
                {
                    plan.Skip(job.Key.JobId, job.Key.BookName, page.FilePage, BookImportTrainingSkip.Flagged);
                    continue;
                }
                var blocks = Parse(result.BoxesJson);
                var questions = blocks.Where(b => (b.Kind ?? LabelingBlocks.McqQuestion) == LabelingBlocks.McqQuestion).ToList();
                if (!manual && questions.Any(q => q.Subpredictions.Count < MinChoices))
                {
                    plan.Skip(job.Key.JobId, job.Key.BookName, page.FilePage, BookImportTrainingSkip.FewChoices);
                    continue;
                }
                plan.Pages.Add(new PlannedPage(job.Key.JobId, job.Key.BookName, page.FilePage, page.ImageUrl, sha, page.Width,
                    page.Height, result.Source, questions, blocks.All(b => b.Kind != LabelingBlocks.Activity)));
            }
        }
        return plan;
    }

    private static List<PredictionDto> Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PredictionListDto>(json)?.Predictions ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static BookImportTrainingSummaryDto Summarize(Plan plan) => new(
        plan.Pages.Count,
        plan.Pages.Count(p => p.Complete),
        plan.Pages.Count(p => p.Questions.Count == 0),
        plan.Pages.Sum(p => p.Questions.Count),
        plan.Pages.SelectMany(p => p.Questions).GroupBy(q => q.Subpredictions.Count).OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count()),
        plan.Pages.GroupBy(p => (p.JobId, p.BookName))
            .Select(g => new BookImportTrainingBookDto(g.Key.JobId, g.Key.BookName, g.Count(), g.Sum(p => p.Questions.Count),
                g.Count(p => p.Source == LabelingSource.Manual)))
            .ToList(),
        plan.Skipped);

    private async Task<byte[]?> ReadAsync(string url, CancellationToken ct)
    {
        await using var stream = await minio.GetFileStreamAsync(url);
        if (stream is null)
            return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal sealed record PlannedPage(Guid JobId, string BookName, int FilePage, string ImageUrl, string Sha256, int Width,
        int Height, LabelingSource Source, IReadOnlyList<PredictionDto> Questions, bool Complete);

    internal sealed class Plan
    {
        public List<PlannedPage> Pages { get; } = [];

        public List<BookImportTrainingSkipDto> Skipped { get; } = [];

        public void Skip(Guid jobId, string bookName, int filePage, string reason) =>
            Skipped.Add(new BookImportTrainingSkipDto(jobId, bookName, filePage, reason));
    }
}
