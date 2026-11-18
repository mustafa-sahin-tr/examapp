using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.BookImport;

/// <summary>#1234: temizlikte silinen bir cevap işareti (sayfa pikselleri; #1235 cevap anahtarı).</summary>
public sealed record PageMark(
    [property: JsonPropertyName("letter")] string Letter,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("w")] int W,
    [property: JsonPropertyName("h")] int H);

public sealed record PageCleanAnalysis(string Token, bool Watermark, int[]? MarkColour, int MarksSeen);

public sealed record PageCleanResult(byte[] Image, IReadOnlyList<PageMark> Marks, bool WatermarkRemoved);

/// <summary>Analiz jetonu servis tarafında kayboldu (servis yeniden başladı): yeniden analiz edilir.</summary>
public sealed class PageCleanExpiredException() : Exception("Page cleaning analysis expired.");

/// <summary>#1234: sayfa temizliği hizmeti (filigran + renkli cevap işaretleri).</summary>
public interface IPageCleaner
{
    Task<bool> IsAvailableAsync(CancellationToken ct);

    /// <param name="markColour">Doluysa işaret rengi öğrenilmez, bu renk kullanılır (kullanıcının seçtiği).</param>
    Task<PageCleanAnalysis> AnalyzeAsync(IReadOnlyList<byte[]> pages, int[]? markColour, CancellationToken ct);

    Task<PageCleanResult> CleanAsync(string token, byte[] page, CancellationToken ct);
}

/// <summary>
/// question-detector'ın <c>/clean/*</c> uçları. Adres <c>BookImport:PageCleanerUrl</c>, boşsa <c>BookImport:TranscriberUrl</c> (aynı
/// servis); ikisi de boşsa ya da servis scipy'sizse hizmet yok sayılır ve aktarım orijinal sayfalarla sürer.
/// </summary>
public sealed class DetectorPageCleaner(IHttpClientFactory httpClientFactory, IConfiguration configuration,
    ILogger<DetectorPageCleaner> logger) : IPageCleaner
{
    public const string HttpClientName = "page-cleaner";
    private static readonly TimeSpan StatusTtl = TimeSpan.FromMinutes(1);
    private static (string Url, bool Available, DateTimeOffset At)? _status;

    private string? BaseUrl =>
        (configuration["BookImport:PageCleanerUrl"] is { Length: > 0 } url ? url : configuration["BookImport:TranscriberUrl"])
        is { Length: > 0 } baseUrl ? baseUrl.TrimEnd('/') : null;

    private HttpClient Client => httpClientFactory.CreateClient(HttpClientName);

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (BaseUrl is not { } url)
            return false;
        if (_status is { } cached && cached.Url == url && DateTimeOffset.UtcNow - cached.At < StatusTtl)
            return cached.Available;
        var available = false;
        try
        {
            available = (await Client.GetFromJsonAsync<Status>($"{url}/clean/status", ct))?.Available == true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Page cleaner status check failed: {Message}", e.Message);
        }

        _status = (url, available, DateTimeOffset.UtcNow);
        return available;
    }

    public async Task<PageCleanAnalysis> AnalyzeAsync(IReadOnlyList<byte[]> pages, int[]? markColour, CancellationToken ct)
    {
        var url = BaseUrl ?? throw new InvalidOperationException("Page cleaner is not configured.");
        using var response = await Client.PostAsJsonAsync($"{url}/clean/analyze",
            new { pages = pages.Select(Convert.ToBase64String).ToList(), markColour }, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AnalyzeResult>(ct)
                     ?? throw new HttpRequestException("Empty page analysis.");
        return new PageCleanAnalysis(result.Token, result.Watermarks.Count > 0, result.MarkColour, result.MarksSeen);
    }

    public async Task<PageCleanResult> CleanAsync(string token, byte[] page, CancellationToken ct)
    {
        var url = BaseUrl ?? throw new InvalidOperationException("Page cleaner is not configured.");
        using var body = new ByteArrayContent(page);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/webp");
        using var response = await Client.PostAsync($"{url}/clean/page?token={Uri.EscapeDataString(token)}", body, ct);
        if (response.StatusCode == HttpStatusCode.Gone)
            throw new PageCleanExpiredException();
        response.EnsureSuccessStatusCode();
        var marks = response.Headers.TryGetValues("X-Page-Marks", out var values)
            ? JsonSerializer.Deserialize<List<PageMark>>(values.First()) ?? []
            : [];
        var watermark = response.Headers.TryGetValues("X-Watermark-Removed", out var removed) && removed.First() == "true";
        return new PageCleanResult(await response.Content.ReadAsByteArrayAsync(ct), marks, watermark);
    }

    private sealed record Status(bool Available);

    private sealed record AnalyzeResult(string Token, List<JsonElement> Watermarks, int[]? MarkColour, int MarksSeen);
}

/// <summary>#1234: işin temizlik özeti (<see cref="BookImportJob.CleanJson"/>); kurulum ekranı gösterir.</summary>
public sealed class BookPageCleaning
{
    public const string Running = "Running";
    public const string Done = "Done";
    /// <summary>Filigran da işaret rengi de bulunamadı; sayfalar orijinal.</summary>
    public const string NothingFound = "NothingFound";
    public const string Unavailable = "Unavailable";
    public const string Failed = "Failed";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Status { get; set; } = Running;
    public bool Watermark { get; set; }
    public int[]? MarkColour { get; set; }
    /// <summary>Kullanıcının seçtiği işaret rengiyle mi temizlendi.</summary>
    public bool ColourPicked { get; set; }
    public int Marks { get; set; }
    public int Pages { get; set; }
    public int Processed { get; set; }
    /// <summary>Kullanıcı "orijinal sayfaları kullan" dedi: etkin görseller orijinal.</summary>
    public bool UseOriginal { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Yarıda kalmış (API yeniden başladı) temizlik yeniden başlatılabilir.</summary>
    public bool IsActive => Status == Running && DateTimeOffset.UtcNow - UpdatedAt < BookQrKeyService.Stale;

    public static BookPageCleaning? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<BookPageCleaning>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);
}

public enum PageCleaningError
{
    None,
    InvalidState,
    Running,
    NotAvailable,
    NotCleaned,
}

/// <summary>
/// #1234: kitap sayfalarının filigranı ve renkli cevap işaretleri temizlenir. Sayfalar aşamasından sonra kendiliğinden (aktarım
/// işinde), kurulum onayı beklerken de elle ("Sayfaları temizle", isteğe bağlı seçilen işaret rengiyle; Hangfire). Orijinal görsel
/// <see cref="BookImportPage.OriginalImageUrl"/>'de kalır, temiz kopya <see cref="BookImportPage.CleanImageUrl"/>'de; etkin
/// görsel (<see cref="BookImportPage.ImageUrl"/>) temiz olandır, böylece kurulum, soru tespiti, düzeltme ve görüntüleyici
/// değişmeden temiz sayfayı okur. "Orijinal sayfaları kullan" yalnız etkin görseli değiştirir. Hesap sabittir: aynı sayfalar ve
/// aynı renk aynı sonucu verir; yeniden denemenin anlamı yalnız farklı bir işaret rengiyle.
/// </summary>
public sealed class BookPageCleaningService(AppDbContext db, IMinIoService minio, IPageCleaner? cleaner,
    IBackgroundJobClient? jobs, ILogger<BookPageCleaningService> logger)
{
    /// <summary>Analize gönderilen en çok örnek sayfa (servis medyan için 32'sini kullanır; işaret rengi de bunlardan).</summary>
    public const int SamplePages = 32;

    public static string CleanObjectName(Guid jobId, int filePage) => $"book-imports/{jobId}/clean/{filePage:D4}.webp";

    /// <summary>Kurulum onayı beklerken elle temizlik: kuyruğa ekler. Çağıran işin görünür olduğunu denetlemiş olmalı.</summary>
    public async Task<PageCleaningError> StartAsync(Guid jobId, int[]? markColour, CancellationToken ct)
    {
        var job = await db.BookImportJobs.AsTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !CanClean(job))
            return PageCleaningError.InvalidState;
        if (BookPageCleaning.Parse(job.CleanJson) is { IsActive: true })
            return PageCleaningError.Running;
        if (cleaner is null || jobs is null || !await cleaner.IsAvailableAsync(ct))
            return PageCleaningError.NotAvailable;

        job.CleanJson = new BookPageCleaning { MarkColour = markColour, ColourPicked = markColour is not null }.Serialize();
        await db.SaveChangesAsync(ct);
        jobs.Enqueue<BookPageCleaningService>(s => s.RunAsync(jobId, markColour, CancellationToken.None));
        return PageCleaningError.None;
    }

    /// <summary>Hangfire: elle başlatılan temizlik. İş onaydan çıktıysa (onaylandı / iptal) dokunmaz.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid jobId, int[]? markColour, CancellationToken ct)
    {
        var job = await db.BookImportJobs.AsTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !CanClean(job))
            return;
        await CleanAsync(job, markColour, resume: false, afterPage: null, ct);
    }

    /// <summary>"Orijinal sayfaları kullan" aç / kapa: yalnız etkin görsel değişir (temizlenmiş sayfalarda).</summary>
    public async Task<PageCleaningError> SetUseOriginalAsync(Guid jobId, bool useOriginal, CancellationToken ct)
    {
        var job = await db.BookImportJobs.AsTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !CanClean(job))
            return PageCleaningError.InvalidState;
        var summary = BookPageCleaning.Parse(job.CleanJson);
        if (summary is not { Status: BookPageCleaning.Done })
            return summary is { IsActive: true } ? PageCleaningError.Running : PageCleaningError.NotCleaned;

        var pages = await db.BookImportPages.AsTracking().Where(p => p.JobId == jobId && p.CleanImageUrl != null).ToListAsync(ct);
        foreach (var page in pages)
            page.ImageUrl = useOriginal ? page.OriginalImageUrl ?? page.ImageUrl : page.CleanImageUrl!;
        summary.UseOriginal = useOriginal;
        summary.UpdatedAt = DateTimeOffset.UtcNow;
        job.CleanJson = summary.Serialize();
        await db.SaveChangesAsync(ct);
        return PageCleaningError.None;
    }

    /// <summary>
    /// Analiz + sayfa sayfa temizlik. <paramref name="resume"/>: temiz kopyası olan sayfalar atlanır (aktarım işi yarıda kaldıysa;
    /// sonuç aynı). Hizmet yoksa ya da bir şey bulunamazsa sayfalar orijinalde kalır; hata aktarımı durdurmaz (orijinallere dönülür).
    /// </summary>
    public async Task CleanAsync(BookImportJob job, int[]? markColour, bool resume, Func<int, int, Task>? afterPage,
        CancellationToken ct)
    {
        var summary = BookPageCleaning.Parse(job.CleanJson) ?? new BookPageCleaning();
        summary.Status = BookPageCleaning.Running;
        summary.MarkColour = markColour ?? (resume ? summary.MarkColour : null);
        summary.ColourPicked = markColour is not null || resume && summary.ColourPicked;
        summary.Error = null;
        summary.UseOriginal = false;
        summary.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync(job, summary, ct);

        if (cleaner is null || !await cleaner.IsAvailableAsync(ct))
        {
            summary.Status = BookPageCleaning.Unavailable;
            await SaveAsync(job, summary, ct);
            return;
        }

        var pages = await db.BookImportPages.AsTracking().Where(p => p.JobId == job.Id).OrderBy(p => p.FilePage).ToListAsync(ct);
        try
        {
            var analysis = await AnalyzeAsync(pages, summary.ColourPicked ? summary.MarkColour : null, ct);
            summary.Watermark = analysis.Watermark;
            summary.MarkColour = analysis.MarkColour;
            summary.Pages = pages.Count;
            if (!analysis.Watermark && analysis.MarkColour is null)
            {
                Restore(pages);
                summary.Status = BookPageCleaning.NothingFound;
                await SaveAsync(job, summary, ct);
                return;
            }

            var token = analysis.Token;
            summary.Processed = 0;
            summary.Marks = 0;
            foreach (var page in pages)
            {
                if (!(resume && page.CleanImageUrl is not null))
                {
                    var original = await ReadAsync(page.OriginalImageUrl ?? page.ImageUrl, ct);
                    PageCleanResult result;
                    try
                    {
                        result = await cleaner.CleanAsync(token, original, ct);
                    }
                    catch (PageCleanExpiredException)
                    {
                        token = (await AnalyzeAsync(pages, summary.MarkColour, ct)).Token;
                        result = await cleaner.CleanAsync(token, original, ct);
                    }

                    using var image = new MemoryStream(result.Image, writable: false);
                    var url = await minio.UploadFileAsync(image, CleanObjectName(job.Id, page.FilePage), contentType: "image/webp");
                    page.OriginalImageUrl ??= page.ImageUrl;
                    page.CleanImageUrl = url;
                    page.ImageUrl = url;
                    page.MarksJson = result.Marks.Count > 0 ? JsonSerializer.Serialize(result.Marks) : null;
                }

                summary.Marks += CountMarks(page.MarksJson);
                summary.Processed++;
                summary.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveAsync(job, summary, ct);
                if (afterPage is not null)
                    await afterPage(summary.Processed, pages.Count);
            }

            summary.Status = BookPageCleaning.Done;
            await SaveAsync(job, summary, ct);
            logger.LogInformation("Book import job {JobId}: cleaned {Pages} pages (watermark {Watermark}, {Marks} answer marks)",
                job.Id, pages.Count, summary.Watermark, summary.Marks);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                      or PageCleanExpiredException)
        {
            logger.LogWarning("Book import job {JobId}: page cleaning failed, the original pages are used: {Message}", job.Id, e.Message);
            Restore(pages);
            summary.Status = BookPageCleaning.Failed;
            summary.Error = e.Message.Length > 300 ? e.Message[..300] : e.Message;
            await SaveAsync(job, summary, ct);
        }
    }

    /// <summary>Temizlik kurulumdan önce (aktarım işi) ya da kurulum onayı beklerken (elle); kitap aktarımında.</summary>
    internal static bool CanClean(BookImportJob job) =>
        job.Kind == BookImportJobKind.Book && job.Source != BookImportSource.LooseImages && job.DemoSourceJobId is null
        && job.Status == BookImportJobStatus.AwaitingConfirmation;

    private async Task<PageCleanAnalysis> AnalyzeAsync(List<BookImportPage> pages, int[]? markColour, CancellationToken ct)
    {
        var step = Math.Max(1, pages.Count / SamplePages);
        var sample = new List<byte[]>();
        foreach (var page in pages.Where((_, i) => i % step == 0).Take(SamplePages))
            sample.Add(await ReadAsync(page.OriginalImageUrl ?? page.ImageUrl, ct));
        return await cleaner!.AnalyzeAsync(sample, markColour, ct);
    }

    private async Task<byte[]> ReadAsync(string url, CancellationToken ct)
    {
        await using var stream = await minio.GetFileStreamAsync(url)
                                 ?? throw new InvalidOperationException($"Page image is missing: {url}");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static void Restore(IEnumerable<BookImportPage> pages)
    {
        foreach (var page in pages.Where(p => p.OriginalImageUrl is not null))
        {
            page.ImageUrl = page.OriginalImageUrl!;
            page.CleanImageUrl = null;
            page.MarksJson = null;
        }
    }

    private static int CountMarks(string? json)
    {
        if (json is null)
            return 0;
        try
        {
            return JsonSerializer.Deserialize<List<PageMark>>(json)?.Count ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private async Task SaveAsync(BookImportJob job, BookPageCleaning summary, CancellationToken ct)
    {
        job.CleanJson = summary.Serialize();
        await db.SaveChangesAsync(ct);
    }
}
