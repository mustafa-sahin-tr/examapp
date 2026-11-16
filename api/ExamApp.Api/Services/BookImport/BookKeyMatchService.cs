using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.BookImport;

public enum KeyMatchError
{
    None,
    NotFound,
    InvalidState,
    NotConfigured,
    Budget,
    AiFailed,
}

public sealed record KeyMatchResult(KeyMatchError Error, BookImportKeyMatchDto? Match = null, decimal AiCostUsd = 0);

/// <summary>
/// Onay ekranında anahtarı olmayan testte "AI ile bul" (öğretmen ister, kurulum kendiliğinden çağırmaz; diğer yol "Anahtar gir"):
/// önce kurulumun hiçbir teste bağlayamadığı anahtar satırlarından (<see cref="BookSetupSummary.OrphanRows"/>) birini ucuz modelle
/// seçer; satır yoksa ya da uymadıysa anahtar sayfalarında testin satırını güçlü modelle arar. Hiçbir şey kaydedilmez: öneri ekranda
/// doldurulur, öğretmen bakıp onaylar. Öneri kodla denetlenir: testin görülen soru sayısı biliniyorsa cevap sayısı tutmalı. Maliyet
/// işin maliyetine ve aktarım bütçesine yazılır.
/// </summary>
public sealed partial class BookKeyMatchService(AppDbContext db, IMinIoService minio, IBookSetupAi ai, IFixedWindowCounterStore budgetStore,
    IOptions<LabelingAiOptions> aiOptions, IConfiguration configuration)
{
    /// <summary>Görsel aramada testin önünde ve arkasında modele verilen test sayısı (sırayı anlaması için).</summary>
    private const int Neighbours = 6;

    [GeneratedRegex("^[A-E?]+$")]
    private static partial Regex KeyPattern();

    /// <summary>Çağıran işin görünür olduğunu (sahip / admin) denetlemiş olmalı.</summary>
    public async Task<KeyMatchResult> FindAsync(Guid jobId, int order, CancellationToken ct)
    {
        var job = await db.BookImportJobs.AsTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.DemoSourceJobId is not null
            || job.Status != BookImportJobStatus.AwaitingConfirmation || job.Stage != BookImportStage.SetupConfirmation
            || BookSetupSummary.Parse(job.SetupJson) is not { } summary)
            return new(KeyMatchError.InvalidState);

        var tests = await db.BookImportJobTests.AsNoTracking()
            .Where(t => t.JobId == jobId)
            .OrderBy(t => t.Order)
            .Select(t => new KeyMatchTest(t.Order, t.Unit, t.TestNo, t.Title, t.FileFrom, t.FileTo, t.ExpectedQuestions))
            .ToListAsync(ct);
        var index = tests.FindIndex(t => t.Order == order);
        if (index < 0)
            return new(KeyMatchError.NotFound);
        if (!ai.IsConfigured)
            return new(KeyMatchError.NotConfigured);

        var test = tests[index];
        var budget = new BookImportBudget(db, budgetStore, aiOptions.Value, job, job.CostCeilingUsd ?? BookImportJobRunner.MaxJobCost(configuration))
        {
            Stage = BookImportStage.SetupConfirmation,
        };
        var cost = new BookSetupCost { Budget = budget };
        try
        {
            var match = await FromOrphanRowsAsync(test, summary.OrphanRows, cost, ct)
                        ?? await FromKeyPagesAsync(jobId, test, Around(tests, index), summary, cost, ct);
            return new(KeyMatchError.None, match, job.AiCostUsd);
        }
        catch (BookImportPause)
        {
            return new(KeyMatchError.Budget);
        }
        catch (BookImportFailure)
        {
            return new(KeyMatchError.AiFailed);
        }
        finally
        {
            await budget.FlushAsync();
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<BookImportKeyMatchDto?> FromOrphanRowsAsync(KeyMatchTest test, IReadOnlyList<OrphanKey> rows, BookSetupCost cost,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return null;
        var suggested = await ai.MatchKeysAsync([test], rows.Select(r => new KeyMatchRow(r.Label, r.AnswerKey.Length, r.KeyPage)).ToList(),
            cost, ct);
        return Accept([test], rows, suggested).FirstOrDefault();
    }

    private async Task<BookImportKeyMatchDto?> FromKeyPagesAsync(Guid jobId, KeyMatchTest test, IReadOnlyList<KeyMatchTest> around,
        BookSetupSummary summary, BookSetupCost cost, CancellationToken ct)
    {
        var keyPages = summary.AnswerKeyRanges.SelectMany(r => Enumerable.Range(r.FileFrom, r.FileTo - r.FileFrom + 1)).ToHashSet();
        if (keyPages.Count == 0)
            return null;
        var pages = await db.BookImportPages.AsNoTracking()
            .Where(p => p.JobId == jobId && keyPages.Contains(p.FilePage))
            .OrderBy(p => p.FilePage)
            .Select(p => new { p.FilePage, p.ImageUrl })
            .ToListAsync(ct);
        var images = new List<BookPageImage>(pages.Count);
        foreach (var page in pages)
        {
            await using var stream = await minio.GetFileStreamAsync(page.ImageUrl);
            if (stream is null)
                continue;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            images.Add(new BookPageImage(page.FilePage, buffer.ToArray()));
        }

        var answers = await ai.FindKeyAsync(images, test, around, cost, ct);
        if (answers is null || answers.Length > BookImportJobTest.MaxKeyLength || !KeyPattern().IsMatch(answers)
            || test.ExpectedQuestions is { } expected && expected != answers.Length)
            return null;
        return new BookImportKeyMatchDto { Order = test.Order, Label = null, AnswerKey = answers };
    }

    private static List<KeyMatchTest> Around(List<KeyMatchTest> tests, int index)
    {
        var from = Math.Max(0, index - Neighbours);
        return tests.GetRange(from, Math.Min(tests.Count, index + Neighbours + 1) - from);
    }

    /// <summary>Önerinin denetimi: bilinmeyen test / satır, iki teste önerilen satır ve soru sayısı tutmayan satır alınmaz.</summary>
    internal static List<BookImportKeyMatchDto> Accept(IReadOnlyList<KeyMatchTest> tests, IReadOnlyList<OrphanKey> rows,
        IReadOnlyDictionary<int, string> suggested)
    {
        var byOrder = tests.ToDictionary(t => t.Order);
        var byLabel = rows.GroupBy(r => r.Label).ToDictionary(g => g.Key, g => g.First());
        var shared = suggested.Values.GroupBy(l => l).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var matches = new List<BookImportKeyMatchDto>();
        foreach (var (order, label) in suggested.OrderBy(p => p.Key))
        {
            if (!byOrder.TryGetValue(order, out var test) || !byLabel.TryGetValue(label, out var row) || shared.Contains(label))
                continue;
            if (test.ExpectedQuestions is { } expected && expected != row.AnswerKey.Length)
                continue;
            matches.Add(new BookImportKeyMatchDto
            {
                Order = order, Label = label, AnswerKey = row.AnswerKey, KeyConflicts = row.KeyConflicts,
            });
        }

        return matches;
    }
}
