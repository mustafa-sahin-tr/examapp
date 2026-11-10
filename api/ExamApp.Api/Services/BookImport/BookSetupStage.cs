using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Issue #1021: <see cref="BookImportJob.SetupJson"/> içeriği (test satırları ayrı tabloda).</summary>
public sealed class BookSetupSummary
{
    public int SchemaRevision { get; set; }
    public int? Offset { get; set; }
    public List<BookSetupRange> AnswerKeyRanges { get; set; } = new();
    public List<int> SelfAssessmentPages { get; set; } = new();
    public List<BookSetupUnit> Units { get; set; } = new();

    /// <summary>Anahtarda olup kitapta testi bulunamayan satırlar ("2-07").</summary>
    public List<string> OrphanKeys { get; set; } = new();

    public bool NoAnswerKey { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static BookSetupSummary? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<BookSetupSummary>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record BookSetupRange(int FileFrom, int FileTo);

public sealed record BookSetupUnit(int Unit, string? Title, int FileFrom, int FileTo, int? TopicId);

/// <summary>
/// Issue #1021: kurulum aşaması — sayfa görsellerini toplu tarar, cevap anahtarı sayfalarını iki kez (tam sayfa + döşemeli)
/// okur, <see cref="BookSetupPlanner"/> ile test listesini çıkarır, üniteleri konulara eşler ve sonucu işe yazar. Kesilirse
/// (bütçe duraklaması dahil, #1024) baştan çalışır (tarama ucuz). Maliyet başarısızlıkta da işe yazılır.
/// </summary>
internal sealed class BookSetupStage(
    AppDbContext db, IMinIoService minio, IBookSetupAi ai, BookImportBudget budget, int surveyBatchSize, ILogger logger)
{
    public async Task RunAsync(BookImportJob job, CancellationToken ct = default)
    {
        if (!ai.IsConfigured)
            throw new BookImportFailure("Claude is not configured (Anthropic:ApiKey is empty).");

        var pages = await db.BookImportPages.AsNoTracking().Where(p => p.JobId == job.Id).OrderBy(p => p.FilePage)
            .Select(p => new { p.FilePage, p.ImageUrl }).ToListAsync(ct);
        if (pages.Count == 0)
            throw new BookImportFailure("The book has no page images.");

        budget.Stage = BookImportStage.Setup;
        budget.TestId = null;
        var cost = new BookSetupCost { Budget = budget };
        try
        {
            var batches = pages.Chunk(surveyBatchSize).ToList();
            job.StageTotal = batches.Count;
            job.StageProcessed = 0;
            await db.SaveChangesAsync(ct);

            var surveyed = new List<SurveyedPage>();
            var toc = new List<SurveyedTocRow>();
            foreach (var batch in batches)
            {
                var images = new List<BookPageImage>(batch.Length);
                foreach (var page in batch)
                    images.Add(await LoadAsync(page.FilePage, page.ImageUrl));
                var result = await ai.SurveyAsync(images, cost, ct);
                surveyed.AddRange(result.Pages);
                toc.AddRange(result.Toc);
                job.StageProcessed++;
                await db.SaveChangesAsync(ct);
            }

            var keyPages = surveyed.Where(p => p.Kind == BookPageKind.AnswerKey).Select(p => p.FilePage).Order().ToList();
            job.StageTotal += keyPages.Count * 2;
            await db.SaveChangesAsync(ct);

            var firstRead = new List<AnswerKeyRow>();
            var secondRead = new List<AnswerKeyRow>();
            foreach (var filePage in keyPages)
            {
                var image = await LoadAsync(filePage, pages.First(p => p.FilePage == filePage).ImageUrl);
                var whole = Tag(await ai.ReadAnswerKeyAsync(image, tiled: false, strong: false, cost, ct), filePage);
                job.StageProcessed++;
                await db.SaveChangesAsync(ct);
                var tiles = Tag(await ai.ReadAnswerKeyAsync(image, tiled: true, strong: false, cost, ct), filePage);
                job.StageProcessed++;
                await db.SaveChangesAsync(ct);

                // #1122: iki ucuz okuma uyuşursa yeter; çelişen sayfayı güçlü model okur, ucuz okuma karşılaştırma olarak kalır
                // (kalan farklar incelemede işaretlenir).
                if (SameAnswers(whole, tiles))
                {
                    firstRead.AddRange(whole);
                    secondRead.AddRange(tiles);
                    continue;
                }

                job.StageTotal++;
                firstRead.AddRange(Tag(await ai.ReadAnswerKeyAsync(image, tiled: false, strong: true, cost, ct), filePage));
                secondRead.AddRange(whole);
                job.StageProcessed++;
                await db.SaveChangesAsync(ct);
            }

            await SavePageNumbersAsync(job, surveyed, ct);
            var plan = BookSetupPlanner.Plan(surveyed, toc, firstRead, secondRead);
            if (plan.Tests.Count == 0)
                throw new BookImportFailure("No tests were found in the book (no page with a test header).");

            var topics = await MapUnitsAsync(job, plan, cost, ct);
            await SaveAsync(job, plan, topics, cost, ct);
            logger.LogInformation(
                "Book import job {JobId}: setup found {Tests} tests, {KeyPages} key pages; cost ${Cost:F4} ({Input} in / {Output} out tokens)",
                job.Id, plan.Tests.Count, keyPages.Count, cost.Usd, cost.InputTokens, cost.OutputTokens);
        }
        finally
        {
            // Maliyet çağrı başına işe yazıldı (BookImportBudget.Record); kalan satırlar ve bütçe sayaçları yazılır.
            await budget.FlushAsync();
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static List<AnswerKeyRow> Tag(IReadOnlyList<AnswerKeyRow> rows, int filePage) =>
        rows.Select(r => r with { KeyPage = filePage }).ToList();

    /// <summary>İki okuma aynı (ünite, test, soru) hücrelerinde aynı harfi mi verdi (döşemelerde iki kez okunan satır bir sayılır).</summary>
    private static bool SameAnswers(IReadOnlyList<AnswerKeyRow> a, IReadOnlyList<AnswerKeyRow> b)
    {
        static Dictionary<(int, int, int), char> Cells(IReadOnlyList<AnswerKeyRow> rows)
        {
            var cells = new Dictionary<(int, int, int), char>();
            foreach (var row in rows)
                foreach (var (n, answer) in row.Answers)
                    cells.TryAdd((row.Unit, row.TestNo, n), answer);
            return cells;
        }

        var x = Cells(a);
        var y = Cells(b);
        return x.Count > 0 && x.Count == y.Count && x.All(c => y.TryGetValue(c.Key, out var other) && other == c.Value);
    }

    /// <summary>Issue #1022: sayfadaki en büyük soru no, soru aktarımının sayfa bazlı numaralaması için.</summary>
    private async Task SavePageNumbersAsync(BookImportJob job, IReadOnlyList<SurveyedPage> surveyed, CancellationToken ct)
    {
        var numbers = surveyed.GroupBy(p => p.FilePage).ToDictionary(g => g.Key, g => g.First().MaxQuestionNumber);
        var rows = await db.BookImportPages.AsTracking().Where(p => p.JobId == job.Id).ToListAsync(ct);
        foreach (var row in rows)
            row.MaxQuestionNumber = numbers.GetValueOrDefault(row.FilePage) is > 0 and <= BookSetupPlanner.MaxQuestionsPerTest and var n
                ? n
                : null;
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyDictionary<int, int?>> MapUnitsAsync(BookImportJob job, BookSetupPlan plan, BookSetupCost cost, CancellationToken ct)
    {
        if (job.SubjectId is not { } subjectId)
            return new Dictionary<int, int?>();
        var topics = await db.Topics.AsNoTracking()
            .Where(t => t.SubjectId == subjectId && t.GradeId == job.GradeId)
            .OrderBy(t => t.Id)
            .Select(t => new TopicCandidate(t.Id, t.Name))
            .ToListAsync(ct);
        return await ai.MapUnitsAsync(plan.Units, topics, cost, ct);
    }

    private async Task SaveAsync(BookImportJob job, BookSetupPlan plan, IReadOnlyDictionary<int, int?> topics, BookSetupCost cost,
        CancellationToken ct)
    {
        // Yeniden çalışmada önceki kurulumun test satırları silinir (henüz soru aktarılmadı: aşama onaydan önce).
        await db.BookImportJobTests.Where(t => t.JobId == job.Id).ExecuteDeleteAsync(ct);
        db.BookImportJobTests.AddRange(plan.Tests.Select(t => new BookImportJobTest
        {
            JobId = job.Id,
            Order = t.Order,
            Unit = t.Unit,
            TestNo = t.TestNo,
            Title = t.Title,
            FileFrom = t.FileFrom,
            FileTo = t.FileTo,
            Minutes = t.Minutes,
            ExpectedQuestions = t.ExpectedQuestions,
            AnswerKey = t.AnswerKey,
            KeyConflicts = t.KeyConflicts,
            KeyAnswers = t.AnswerKey?.Count(c => c != '?') ?? 0,
            FlagsJson = t.Flags.Count == 0 ? null : JsonSerializer.Serialize(t.Flags),
        }));

        job.SetupJson = new BookSetupSummary
        {
            SchemaRevision = ClaudeBookSetupAi.SchemaRevision,
            Offset = plan.Offset,
            AnswerKeyRanges = plan.AnswerKeyRanges.Select(r => new BookSetupRange(r.From, r.To)).ToList(),
            SelfAssessmentPages = plan.SelfAssessmentPages.ToList(),
            Units = plan.Units.Select(u => new BookSetupUnit(u.Unit, u.Title, u.FileFrom, u.FileTo, topics.GetValueOrDefault(u.Unit))).ToList(),
            OrphanKeys = plan.OrphanKeys.ToList(),
            NoAnswerKey = plan.NoAnswerKey,
            InputTokens = cost.InputTokens,
            OutputTokens = cost.OutputTokens,
        }.Serialize();
        await db.SaveChangesAsync(ct);
    }

    private async Task<BookPageImage> LoadAsync(int filePage, string url)
    {
        await using var stream = await minio.GetFileStreamAsync(url)
                                 ?? throw new BookImportFailure($"Page {filePage} image could not be read from storage.");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return new BookPageImage(filePage, buffer.ToArray());
    }
}
