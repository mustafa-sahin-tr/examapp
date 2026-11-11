using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using Hangfire;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Issue #1122: parçalı yüklemeyi açan formdaki kitap bilgisi ve dosyanın toplam boyutu.</summary>
public sealed record BeginBookImportUploadRequest(string BookName, int GradeId, int? SubjectId, string? FileName, long FileSizeBytes,
    BookImportSource Source = BookImportSource.Pdf, int ImageCount = 0);

public sealed record BookImportUploadDto(Guid Id, long PartSizeBytes, int PartCount);

public sealed record BeginBookImportUploadResult(BookImportUploadDto? Upload, StartBookImportError Error, EntitlementCheck? Quota = null);

public enum BookImportUploadPartResult
{
    Ok,
    NotFound,
    InvalidPart,
    NotPdf,
    /// <summary>Issue #1144: JPEG / PNG / WebP değil ya da çözülemiyor.</summary>
    NotImage,
    /// <summary>Issue #1144: sayfa görseli <see cref="BookImportService.MinPageImageWidth"/>'ten dar.</summary>
    ImageTooSmall,
}

public enum CompleteBookImportUploadError
{
    None,
    NotFound,
    Incomplete,
    NotPdf,
    QuotaReached,
}

public sealed record CompleteBookImportUploadResult(BookImportJobDto? Job, CompleteBookImportUploadError Error, EntitlementCheck? Quota = null);

/// <summary>
/// Issue #1122: büyük PDF parça parça gelir (Cloudflare 100 MB, gateway 30 MB sınırının altında). Parçalar MinIO'da oturumun önekinde
/// ayrı nesnedir; aynı parçayı yeniden göndermek üzerine yazar (tarayıcı hatalı parçayı yeniden dener), sıra serbesttir.
/// Tamamlama parçaları sırayla geçici dosyada birleştirir, boyutları ve PDF başlığını denetler, sonra tek istekli yüklemedeki işi
/// başlatır. Oturuma yalnız sahibi dokunur (Admin dahil); başkasınınki yok sayılır.
/// </summary>
public sealed partial class BookImportService
{
    /// <summary>Gateway'in Kestrel gövde sınırının (30 MB) altında.</summary>
    public const long UploadPartSizeBytes = 25L * 1024 * 1024;

    /// <summary>Kullanıcı başına açık oturum; yenisi açılınca en eskisi bırakılır (sayfayı yenileyip yeniden başlayan kilitlenmesin).</summary>
    public const int MaxOpenUploadsPerUser = 3;

    /// <summary>Bu kadar eski yarım oturum süpürülür.</summary>
    public static readonly TimeSpan UploadExpiry = TimeSpan.FromHours(24);

    private readonly string _uploadBucket;
    private readonly int _maxPages;

    /// <summary>Issue #1144: tek sayfa görselinin sınırı (hızlı eklemeyle aynı).</summary>
    public const long MaxPageImageBytes = 15L * 1024 * 1024;

    /// <summary>Issue #1144: daha dar sayfa görseli okunmaz (sayfa 2363 px'e büyütülür; tespit küçük yazıda bozulur).</summary>
    public const int MinPageImageWidth = 1000;

    private static readonly SkiaSharp.SKEncodedImageFormat[] PageImageFormats =
        [SkiaSharp.SKEncodedImageFormat.Jpeg, SkiaSharp.SKEncodedImageFormat.Png, SkiaSharp.SKEncodedImageFormat.Webp];

    public async Task<BeginBookImportUploadResult> BeginUploadAsync(BookImportOwner owner, BeginBookImportUploadRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        var images = request.Source == BookImportSource.Images;
        // Görsellerde toplam boyut sınırı yok (görsel başına sınır parçada); sayfa sınırı yüklemeden önce.
        var error = await ValidateUploadAsync(request.BookName, request.GradeId, request.SubjectId, images ? 1 : request.FileSizeBytes, ct);
        if (error == StartBookImportError.None && images && request.ImageCount is var count && (count < 1 || count > _maxPages))
            error = StartBookImportError.TooManyPages;
        if (error != StartBookImportError.None)
            return new(null, error);

        var quota = await _entitlements.CheckAsync(owner.Subject, EntitlementFeatures.BookImport, ct);
        if (!quota.Allowed)
            return new(null, StartBookImportError.QuotaReached, quota);

        var open = await _db.BookImportUploads
            .Where(u => u.OwnerUserId == owner.UserId)
            .OrderByDescending(u => u.CreatedAt)
            .Skip(MaxOpenUploadsPerUser - 1)
            .ToListAsync(ct);
        foreach (var stale in open)
            await DiscardUploadAsync(stale, ct);

        var upload = new BookImportUpload
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner.UserId,
            CreatedAt = DateTime.UtcNow,
            BookName = request.BookName.Trim(),
            GradeId = request.GradeId,
            SubjectId = request.SubjectId,
            FileName = TrimFileName(request.FileName),
            FileSizeBytes = images ? 0 : request.FileSizeBytes,
            PartSizeBytes = images ? MaxPageImageBytes : UploadPartSizeBytes,
            PartCount = images ? request.ImageCount : (int)((request.FileSizeBytes + UploadPartSizeBytes - 1) / UploadPartSizeBytes),
            Source = request.Source,
        };
        _db.BookImportUploads.Add(upload);
        await _db.SaveChangesAsync(ct);
        return new(ToDto(upload), StartBookImportError.None);
    }

    /// <summary>
    /// <paramref name="partNumber"/> 1'den başlar; boyutu tam beklenen olmalı (son parça kalan). İlk parça PDF başlığıyla başlamazsa
    /// oturum bırakılır.
    /// </summary>
    public async Task<BookImportUploadPartResult> UploadPartAsync(BookImportOwner owner, Guid id, int partNumber, Stream content,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);

        var upload = await FindUploadAsync(owner, id, ct);
        if (upload is null)
            return BookImportUploadPartResult.NotFound;
        if (upload.Source == BookImportSource.Images)
            return await UploadImageAsync(upload, partNumber, content, ct);
        if (partNumber < 1 || partNumber > upload.PartCount || !content.CanSeek || content.Length != ExpectedPartSize(upload, partNumber))
            return BookImportUploadPartResult.InvalidPart;

        content.Position = 0;
        if (partNumber == 1 && !await StartsWithPdfMagicAsync(content, ct))
        {
            await DiscardUploadAsync(upload, ct);
            return BookImportUploadPartResult.NotPdf;
        }

        content.Position = 0;
        await _minio.UploadFileAsync(content, PartObjectName(upload.Id, partNumber), contentType: "application/octet-stream");
        return BookImportUploadPartResult.Ok;
    }

    public async Task<CompleteBookImportUploadResult> CompleteUploadAsync(BookImportOwner owner, Guid id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var upload = await FindUploadAsync(owner, id, ct);
        if (upload is null)
            return new(null, CompleteBookImportUploadError.NotFound);

        var quota = await _entitlements.CheckAsync(owner.Subject, EntitlementFeatures.BookImport, ct);
        if (!quota.Allowed)
            return new(null, CompleteBookImportUploadError.QuotaReached, quota);

        if (upload.Source == BookImportSource.Images)
            return await CompleteImagesAsync(owner, upload, ct);

        // 200 MB'a kadar: bellek yerine diskte birleştirilir; dosya kapanınca silinir.
        await using var assembled = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        for (var part = 1; part <= upload.PartCount; part++)
        {
            await using var stream = await _minio.GetFileStreamAsync(PartUrl(upload.Id, part));
            if (stream is null)
                return new(null, CompleteBookImportUploadError.Incomplete);
            var before = assembled.Length;
            await stream.CopyToAsync(assembled, ct);
            if (assembled.Length - before != ExpectedPartSize(upload, part))
                return new(null, CompleteBookImportUploadError.Incomplete);
        }

        assembled.Position = 0;
        if (assembled.Length != upload.FileSizeBytes || !await StartsWithPdfMagicAsync(assembled, ct))
            return new(null, CompleteBookImportUploadError.NotPdf);

        // Aynı oturumu iki kez tamamlayan istek ikinci işi açmasın: satırı silen kazanır.
        if (await _db.BookImportUploads.Where(u => u.Id == upload.Id).ExecuteDeleteAsync(ct) == 0)
            return new(null, CompleteBookImportUploadError.NotFound);

        assembled.Position = 0;
        var job = await CreateJobAsync(owner, upload.BookName, upload.GradeId, upload.SubjectId, upload.FileName, upload.FileSizeBytes,
            assembled, ct);
        await DeletePartsAsync(upload);
        return new(ToDto(job), CompleteBookImportUploadError.None);
    }

    public async Task<bool> AbortUploadAsync(BookImportOwner owner, Guid id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var upload = await FindUploadAsync(owner, id, ct);
        if (upload is null)
            return false;
        await DiscardUploadAsync(upload, ct);
        return true;
    }

    public async Task<int> PurgeStaleUploadsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - UploadExpiry;
        var stale = await _db.BookImportUploads.Where(u => u.CreatedAt < cutoff).ToListAsync(ct);
        foreach (var upload in stale)
            await DiscardUploadAsync(upload, ct);
        return stale.Count;
    }

    private Task<BookImportUpload?> FindUploadAsync(BookImportOwner owner, Guid id, CancellationToken ct) =>
        _db.BookImportUploads.SingleOrDefaultAsync(u => u.Id == id && u.OwnerUserId == owner.UserId, ct);

    private async Task DiscardUploadAsync(BookImportUpload upload, CancellationToken ct)
    {
        await _db.BookImportUploads.Where(u => u.Id == upload.Id).ExecuteDeleteAsync(ct);
        await DeletePartsAsync(upload);
    }

    /// <summary>Gelmemiş parçanın silinmesi zararsızdır (MinIO yok sayar).</summary>
    private async Task DeletePartsAsync(BookImportUpload upload)
    {
        for (var part = 1; part <= upload.PartCount; part++)
            await _minio.DeleteFileByUrlAsync(upload.Source == BookImportSource.Images ? ImageUrl(upload.Id, part) : PartUrl(upload.Id, part));
    }

    /// <summary>
    /// Issue #1144: bir sayfa görseli. İçerik başlıktan tanınır (uzantıya bakılmaz), boyut ve genişlik denetlenir; görsel olduğu gibi
    /// saklanır, sayfa biçimine Sayfalar aşamasında çevrilir. Aynı sırayı yeniden göndermek üzerine yazar.
    /// </summary>
    private async Task<BookImportUploadPartResult> UploadImageAsync(BookImportUpload upload, int imageNumber, Stream content,
        CancellationToken ct)
    {
        if (imageNumber < 1 || imageNumber > upload.PartCount || !content.CanSeek || content.Length is <= 0 or > MaxPageImageBytes)
            return BookImportUploadPartResult.InvalidPart;

        content.Position = 0;
        var bytes = new byte[content.Length];
        await content.ReadExactlyAsync(bytes, ct);
        SkiaImageHeader header;
        try
        {
            header = SkiaImaging.ReadHeader(bytes);
        }
        catch (Exception ex) when (ex is ImageDecodeException or NotSupportedException or ArgumentException)
        {
            return BookImportUploadPartResult.NotImage;
        }
        if (!PageImageFormats.Contains(header.Format) || !SkiaImaging.IsWithinLimits(header.Width, header.Height))
            return BookImportUploadPartResult.NotImage;
        if ((header.IsTransposed ? header.Height : header.Width) < MinPageImageWidth)
            return BookImportUploadPartResult.ImageTooSmall;

        content.Position = 0;
        await _minio.UploadFileAsync(content, ImageObjectName(upload.Id, imageNumber), contentType: "application/octet-stream");
        return BookImportUploadPartResult.Ok;
    }

    /// <summary>
    /// Issue #1144: bütün görseller geldiyse işi açar. Görseller kopyalanmaz: iş yükleme önekini kaynak alır (<see cref="BookImportJob.FileUrl"/>),
    /// Sayfalar aşaması bitince siler. Yükleme satırı silinir (yarım oturum süpürücüsü kaynağa dokunmasın).
    /// </summary>
    private async Task<CompleteBookImportUploadResult> CompleteImagesAsync(BookImportOwner owner, BookImportUpload upload, CancellationToken ct)
    {
        for (var image = 1; image <= upload.PartCount; image++)
        {
            if (!await _minio.FileExistsAsync(ImageUrl(upload.Id, image), ct))
                return new(null, CompleteBookImportUploadError.Incomplete);
        }

        if (await _db.BookImportUploads.Where(u => u.Id == upload.Id).ExecuteDeleteAsync(ct) == 0)
            return new(null, CompleteBookImportUploadError.NotFound);

        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            BookName = upload.BookName,
            GradeId = upload.GradeId,
            SubjectId = upload.SubjectId,
            FileName = upload.FileName,
            Source = BookImportSource.Images,
            FileUrl = ImagePrefixUrl(upload.Id),
            PageCount = upload.PartCount,
            Status = BookImportJobStatus.Queued,
            Stage = BookImportStage.Upload,
        };
        _db.SetCurrentUser(owner.UserId);
        _db.BookImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(job.Id));
        return new(ToDto(job), CompleteBookImportUploadError.None);
    }

    private static string ImageObjectName(Guid id, int imageNumber) => $"book-import-uploads/{id}/{PageImageFileName(imageNumber)}";

    /// <summary>Issue #1144: kaynak önekinin altında n. sayfa görselinin adı (runner önekle birleştirir).</summary>
    internal static string PageImageFileName(int imageNumber) => $"image-{imageNumber:D4}";

    private string ImagePrefixUrl(Guid id) => $"/img/{_uploadBucket}/book-import-uploads/{id}/";

    private string ImageUrl(Guid id, int imageNumber) => ImagePrefixUrl(id) + PageImageFileName(imageNumber);

    private static long ExpectedPartSize(BookImportUpload upload, int partNumber) =>
        partNumber < upload.PartCount ? upload.PartSizeBytes : upload.FileSizeBytes - upload.PartSizeBytes * (upload.PartCount - 1);

    private static string PartObjectName(Guid id, int partNumber) => $"book-import-uploads/{id}/part-{partNumber:D3}";

    /// <summary><see cref="MinIoService.UploadFileAsync"/>'in varsayılan bucket için döndürdüğü adres.</summary>
    private string PartUrl(Guid id, int partNumber) => $"/img/{_uploadBucket}/{PartObjectName(id, partNumber)}";

    private static BookImportUploadDto ToDto(BookImportUpload upload) => new(upload.Id, upload.PartSizeBytes, upload.PartCount);
}
