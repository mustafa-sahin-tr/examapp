using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Labeling;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Issue #1052: yüklenen bir fotoğraf ve isteğe bağlı cevap anahtarı (fotoğraftaki soruların okuma sırasıyla).</summary>
public sealed record QuickAddPhoto(string? FileName, long Length, Stream Content, string? AnswerKey);

public sealed record StartQuickAddRequest(int GradeId, int? SubjectId, IReadOnlyList<QuickAddPhoto> Photos);

public enum StartQuickAddError
{
    None,
    InvalidGrade,
    InvalidSubject,
    NoPhotos,
    TooManyPhotos,
    PhotoTooLarge,
    InvalidImage,
    InvalidAnswerKey,
    QuotaReached,
}

/// <param name="Photo">Hatalı fotoğrafın 1 tabanlı sırası (fotoğrafa bağlı hatalarda).</param>
public sealed record StartQuickAddResult(BookImportJobDto? Job, StartQuickAddError Error, EntitlementCheck? Quota = null, int? Photo = null);

public interface IQuickAddService
{
    Task<StartQuickAddResult> StartAsync(BookImportOwner owner, StartQuickAddRequest request, CancellationToken ct);

    /// <summary>Bu ayki fotoğraf hakkı.</summary>
    Task<BookImportQuotaDto> GetQuotaAsync(BookImportOwner owner, CancellationToken ct);
}

/// <summary>
/// Issue #1052 (epic #1048): hızlı soru ekleme. Fotoğraflar yüklemede çözülür (EXIF yönü uygulanır, uzun kenar
/// <see cref="PhotoMaxEdge"/>'e küçülür, meta verisiz JPEG), işin sayfaları olarak MinIO'ya yazılır; fotoğraf başına bir test
/// satırı açılır ve iş doğrudan soru aktarımı aşamasından kuyruğa girer (<see cref="BookImportJobRunner"/>, kitap ve kurulum yok).
/// Hak (<see cref="EntitlementFeatures.QuickAdd"/>) fotoğraf başına sayılır; kalan hak yetmezse hiçbir şey saklanmaz.
/// </summary>
public sealed class QuickAddService(
    AppDbContext db, IMinIoService minio, IBackgroundJobClient jobs, IEntitlementService entitlements) : IQuickAddService
{
    public const int MaxPhotos = 10;
    public const long MaxPhotoBytes = 15 * 1024 * 1024;

    /// <summary>Saklanan fotoğrafın uzun kenarı: kitap sayfalarıyla aynı ölçek (~2363 px), kırpmalar okunur kalır.</summary>
    public const int PhotoMaxEdge = 2400;

    /// <summary>İşin adı (taslak testin adı ve takip ekranı); tarih takip ekranında ayrıca görünür.</summary>
    public const string JobName = "Hızlı ekleme";

    public async Task<StartQuickAddResult> StartAsync(BookImportOwner owner, StartQuickAddRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        if (!await db.Grades.AnyAsync(g => g.Id == request.GradeId, ct))
            return new(null, StartQuickAddError.InvalidGrade);
        if (request.SubjectId is { } subjectId && !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            return new(null, StartQuickAddError.InvalidSubject);
        var photos = request.Photos ?? [];
        if (photos.Count == 0)
            return new(null, StartQuickAddError.NoPhotos);
        if (photos.Count > MaxPhotos)
            return new(null, StartQuickAddError.TooManyPhotos);

        var keys = new string?[photos.Count];
        for (var i = 0; i < photos.Count; i++)
        {
            if (photos[i].Length is <= 0 or > MaxPhotoBytes)
                return new(null, StartQuickAddError.PhotoTooLarge, Photo: i + 1);
            if (!TryParseAnswerKey(photos[i].AnswerKey, out keys[i]))
                return new(null, StartQuickAddError.InvalidAnswerKey, Photo: i + 1);
        }

        var quota = await entitlements.CheckAsync(owner.Subject, EntitlementFeatures.QuickAdd, ct);
        if (!quota.Allowed || quota.Remaining is { } remaining && remaining < photos.Count)
            return new(null, StartQuickAddError.QuotaReached, quota);

        // Önce hepsi çözülür: bozuk bir fotoğraf varsa hiçbir şey yüklenmez.
        var encoded = new List<(byte[] Jpeg, int Width, int Height)>(photos.Count);
        for (var i = 0; i < photos.Count; i++)
        {
            try
            {
                using var buffer = new MemoryStream();
                await photos[i].Content.CopyToAsync(buffer, ct);
                using var image = LabelingImage.Decode(buffer.ToArray());
                var region = image.Encode(PhotoMaxEdge);
                encoded.Add((region.Jpeg, region.Width, region.Height));
            }
            catch (LabelingValidationException)
            {
                return new(null, StartQuickAddError.InvalidImage, Photo: i + 1);
            }
        }

        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            Kind = BookImportJobKind.QuickAdd,
            BookName = JobName,
            GradeId = request.GradeId,
            SubjectId = request.SubjectId,
            FileSizeBytes = encoded.Sum(e => (long)e.Jpeg.Length),
            PageCount = photos.Count,
            Status = BookImportJobStatus.Queued,
            Stage = BookImportStage.Questions,
        };
        for (var i = 0; i < encoded.Count; i++)
        {
            var photo = i + 1;
            using var stream = new MemoryStream(encoded[i].Jpeg, writable: false);
            var url = await minio.UploadFileAsync(stream, PhotoObjectName(job.Id, photo), contentType: "image/jpeg");
            job.Pages.Add(new BookImportPage
            {
                FilePage = photo, ImageUrl = url, Width = encoded[i].Width, Height = encoded[i].Height,
                SizeBytes = encoded[i].Jpeg.LongLength,
            });
            job.Tests.Add(new BookImportJobTest
            {
                Order = photo,
                TestNo = photo,
                Title = $"Fotoğraf {photo}",
                FileFrom = photo,
                FileTo = photo,
                AnswerKey = keys[i],
                ExpectedQuestions = keys[i]?.Length,
                KeyAnswers = keys[i]?.Count(c => c != '?') ?? 0,
            });
        }

        db.SetCurrentUser(owner.UserId);
        db.BookImportJobs.Add(job);
        await db.SaveChangesAsync(ct);
        // Fotoğraf başına bir kullanım; referans iş + sıra olduğundan yeniden deneme tekrar saymaz.
        for (var photo = 1; photo <= photos.Count; photo++)
            await entitlements.RecordUsageAsync(owner.UserId, EntitlementFeatures.QuickAdd, $"quick-add:{job.Id}:{photo}", ct);

        jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(job.Id));
        return new(BookImportService.ToDto(job), StartQuickAddError.None);
    }

    public async Task<BookImportQuotaDto> GetQuotaAsync(BookImportOwner owner, CancellationToken ct)
    {
        var check = await entitlements.CheckAsync(owner.Subject, EntitlementFeatures.QuickAdd, ct);
        return new BookImportQuotaDto
        {
            Allowed = check.Allowed, Limit = check.Limit, Used = check.Used, ResetsAtUtc = check.ResetsAtUtc, PlanKey = check.PlanKey,
        };
    }

    public static string PhotoObjectName(Guid jobId, int photo) => $"quick-adds/{jobId}/photos/{photo:D4}.jpg";

    /// <summary>
    /// Cevap anahtarı: harfler sırayla, boşluk / virgül / noktalı virgülle ayrık ya da bitişik ("A C B", "ACB"), isteğe bağlı numaralı
    /// ("1-A 2) C 3.B"). Bilinmeyen cevap "?", "-" ya da "_". Boş → anahtar yok (null). Numara yalnız okunur, sırayı belirlemez.
    /// </summary>
    internal static bool TryParseAnswerKey(string? input, out string? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(input))
            return true;
        var result = new StringBuilder();
        var text = input.Trim().ToUpperInvariant();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c is ',' or ';')
            {
                i++;
                continue;
            }
            if (char.IsAsciiDigit(c))
            {
                // Numara ve ardındaki ayırıcı ("12-", "3)", "4.", "5:") atlanır; ardından bir cevap gelmeli.
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                    i++;
                if (i < text.Length && text[i] is '-' or ')' or '.' or ':')
                    i++;
                while (i < text.Length && text[i] == ' ')
                    i++;
                if (i >= text.Length || !IsAnswer(text[i]))
                    return false;
                continue;
            }
            if (!IsAnswer(c))
                return false;
            result.Append(c is '-' or '_' ? '?' : c);
            i++;
        }
        if (result.Length == 0 || result.Length > BookImportJobTest.MaxKeyLength)
            return false;
        key = result.ToString();
        return true;
    }

    private static bool IsAnswer(char c) => c is >= 'A' and <= 'E' or '?' or '-' or '_';
}
