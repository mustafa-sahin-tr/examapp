using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Sayfa daha önce bankaya kaydedilmiş ve <c>Force</c> verilmedi ya da şu an başka bir istekte kaydediliyor → 409.
/// </summary>
public sealed class LabelingBankDuplicateException(LabelingBankInfoDto? existing)
    : Exception("Labeling page already saved to the question bank.")
{
    /// <summary>Önceki tamamlanmış kayıt; sayfa ilk kez kaydedilirken başka istek sürüyorsa null.</summary>
    public LabelingBankInfoDto? Existing { get; } = existing;
}

/// <summary>Sorular oluşturulamadı (görsel kırpma / DB) → 500; ayrıntı loglanır, istemciye sızdırılmaz.</summary>
public sealed class LabelingBankSaveException() : Exception("Saving labeling questions to the bank failed.");

public interface ILabelingBankService
{
    Task<LabelingBankSaveResponseDto> SaveToBankAsync(
        LabelingBankSaveRequestDto request, LabelingActor actor, int actingUserId, CancellationToken ct = default);
}

/// <summary>
/// İşaretleme ekranındaki soruları teste bağlamadan soru bankasına kaydeder (issue #470). Kırpma, MinIO yüklemesi, şık /
/// doğru cevap ve sınıflandırma için <c>QuestionCreatedEvent</c> outbox kaydı mevcut toplu kayıttan
/// (<see cref="IQuestionService.SaveBulkQuestion"/>, <c>TestId = null</c> → <c>WorksheetQuestion</c> yok) gelir; sahibi
/// <c>actingUserId</c> (audit <c>CreateUserId</c>). Aynı sayfa (ham görsel SHA-256, <see cref="LabelingPage"/>) ikinci kez
/// yalnız <c>Force</c> ile kaydedilir; süren bir kayıt <c>Force</c> ile de aşılamaz. Eğitime gönderimden bağımsızdır.
/// </summary>
public sealed class LabelingBankService(
    AppDbContext db,
    IQuestionService questionService,
    IFixedWindowCounterStore rateLimitStore,
    TimeProvider timeProvider,
    ILogger<LabelingBankService> logger) : ILabelingBankService
{
    public const int MaxQuestions = 100;
    public const int MinOptions = 2;
    public const int MaxOptions = 10;

    /// <summary>Kullanıcı başına saatlik bankaya kayıt sayısı (her kayıt kırpma + MinIO + ücretli sınıflandırma demek).</summary>
    public const int SavesPerHour = 30;

    /// <summary>Bu süreden eski "kaydediliyor" işareti bayat sayılır (süreç kayıt ortasında öldüyse sayfa kilitli kalmasın).</summary>
    public static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(10);

    private const int MinBoxEdge = 4;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);

    public async Task<LabelingBankSaveResponseDto> SaveToBankAsync(
        LabelingBankSaveRequestDto request, LabelingActor actor, int actingUserId, CancellationToken ct = default)
    {
        // Ucuz kontroller görsel çözülmeden önce.
        var questions = request.Questions ?? [];
        if (questions.Count == 0)
            throw new LabelingValidationException("labeling.bank.noQuestions");
        if (questions.Count > MaxQuestions)
            throw new LabelingValidationException("labeling.tooManyBoxes");
        foreach (var q in questions)
            ValidateShape(q);

        // Issue #565: sınıf zorunlu ve var olmalı — sınıflandırıcı taksonomiyi buna daraltır.
        if (request.GradeId <= 0)
            throw new LabelingValidationException("labeling.bank.gradeRequired");
        if (!await db.Grades.AnyAsync(g => g.Id == request.GradeId, ct))
            throw new LabelingValidationException("labeling.bank.gradeUnknown");

        string sha256;
        int width, height;
        string imageData;
        List<BulkQuestionDto> bulkQuestions;
        // Görsel çözülür ve EXIF yönü uygulanır: tuvaldeki kutular o uzayda; toplu kayıt bu (tam boy, metadata'sız) JPEG'i
        // kırpar. Çözülmüş görsel kayıttan önce bırakılır.
        using (var image = LabelingImage.Decode(request.ImageBase64))
        {
            sha256 = image.Sha256;
            width = image.Width;
            height = image.Height;
            bulkQuestions = questions.Select((q, i) => ToBulkQuestion(q, i, width, height)).ToList();
            imageData = "data:image/jpeg;base64," + Convert.ToBase64String(image.Encode(LabelingImage.MaxEdge).Jpeg);
        }

        var page = await GetOrCreatePageAsync(sha256, width, height, ct);
        var previous = ToBankInfo(page);
        if (previous is not null && !request.Force)
            throw new LabelingBankDuplicateException(previous);

        var decision = await rateLimitStore.TryAcquireAsync(
            $"labeling-bank:{actor.KeycloakId}", 1, SavesPerHour, RateLimitWindow, ct);
        if (!decision.Allowed)
            throw new LabelingRateLimitException(decision.RetryAfter);

        // "Kaydediliyor" işareti: tamamlanmış kayıt alanları (BankSavedAt...) yalnız başarıda yazılır. Son kayıt okuduğumuz
        // değerde değilse ya da başka bir kayıt sürüyorsa (bayat değilse) → 409.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var staleBefore = now - StaleClaimAfter;
        var previousAt = page.BankSavedAt;
        var claimed = await db.LabelingPages
            .Where(p => p.Id == page.Id && p.BankSavedAt == previousAt &&
                        (p.BankSaveStartedAt == null || p.BankSaveStartedAt < staleBefore))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.BankSaveStartedAt, now), ct);
        if (claimed == 0)
            throw new LabelingBankDuplicateException(await ReadBankInfoAsync(page.Id));

        var bulk = new BulkQuestionCreateDto
        {
            ImageData = imageData,
            Passages = [],
            Questions = bulkQuestions,
            Header = new HeaderInfo { TestId = null },
            GradeId = request.GradeId,
        };

        ResponseBaseDto? result;
        try
        {
            result = await questionService.SaveBulkQuestion(bulk, actingUserId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Labeling] bank save threw for page {Hash}", sha256[..16]);
            result = null;
        }

        var success = result is { Success: true };
        var name = Truncate(actor.Name, 200);
        await FinishClaimAsync(page.Id, now, success, actor.KeycloakId, name, bulkQuestions.Count);
        if (!success)
        {
            logger.LogError("[Labeling] bank save failed for page {Hash}: {Message}", sha256[..16], result?.Message);
            throw new LabelingBankSaveException();
        }

        logger.LogInformation("[Labeling] page {Hash}: {Count} questions saved to bank by {User}{Again}",
            sha256[..16], bulkQuestions.Count, actor.KeycloakId, previous is null ? "" : " (again)");
        return new LabelingBankSaveResponseDto(sha256, new LabelingBankInfoDto(now, name, bulkQuestions.Count));
    }

    internal static LabelingBankInfoDto? ToBankInfo(LabelingPage page) =>
        page.BankSavedAt is { } savedAt
            ? new LabelingBankInfoDto(savedAt, page.BankSavedByName, page.BankQuestionCount ?? 0)
            : null;

    /// <summary>
    /// "Kaydediliyor" işaretini kaldırır; başarıda tamamlanmış kayıt alanlarını da yazar. Yalnız kendi işaretimizse (bayat
    /// sayılıp başka bir istekçe alınmadıysa). İptalden bağımsız — sorular oluştuysa kayıt bilgisi kaybolmasın.
    /// </summary>
    private async Task FinishClaimAsync(int pageId, DateTime claimedAt, bool success, string? keycloakId, string? name, int count)
    {
        try
        {
            var mine = db.LabelingPages.Where(p => p.Id == pageId && p.BankSaveStartedAt == claimedAt);
            if (success)
            {
                await mine.ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.BankSaveStartedAt, (DateTime?)null)
                    .SetProperty(p => p.BankSavedAt, claimedAt)
                    .SetProperty(p => p.BankSavedByKeycloakId, keycloakId)
                    .SetProperty(p => p.BankSavedByName, name)
                    .SetProperty(p => p.BankQuestionCount, count), CancellationToken.None);
            }
            else
            {
                await mine.ExecuteUpdateAsync(s => s.SetProperty(p => p.BankSaveStartedAt, (DateTime?)null),
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // İşaret bayatlayınca (StaleClaimAfter) sayfa yeniden kaydedilebilir.
            logger.LogError(ex, "[Labeling] bank claim could not be finished for page {PageId}", pageId);
        }
    }

    private async Task<LabelingBankInfoDto?> ReadBankInfoAsync(int pageId)
    {
        var page = await db.LabelingPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pageId, CancellationToken.None);
        return page is null ? null : ToBankInfo(page);
    }

    /// <summary>Sayfa boyutundan bağımsız kurallar: şık sayısı, doğru şık, negatif / çok küçük kutu.</summary>
    private static void ValidateShape(LabelingBankQuestionDto q)
    {
        var options = q.Options ?? [];
        if (options.Count is < MinOptions or > MaxOptions)
            throw new LabelingValidationException("labeling.bank.invalidOptions");
        if (q.CorrectOptionIndex < 0 || q.CorrectOptionIndex >= options.Count)
            throw new LabelingValidationException("labeling.bank.correctAnswerMissing");
        if (options.Prepend(q).Any(b => b is null || b.X < 0 || b.Y < 0 || b.Width < MinBoxEdge || b.Height < MinBoxEdge))
            throw new LabelingValidationException("labeling.invalidBox");
    }

    /// <summary>
    /// İstemci kutusu → toplu kayıt sorusu. Soru sayfaya, şıklar soru kutusuna kırpılır (toplu kayıt şıkları soruya göre
    /// göreli kırpar; dışarı taşan şık bozuk görsel üretirdi). Şıklar gönderilen sırayla A, B, C, ... etiketlenir.
    /// </summary>
    private static BulkQuestionDto ToBulkQuestion(LabelingBankQuestionDto q, int index, int pageWidth, int pageHeight)
    {
        var question = Intersect(q, 0, 0, pageWidth, pageHeight) ?? throw new LabelingValidationException("labeling.invalidBox");
        return new BulkQuestionDto
        {
            Name = $"S{index + 1}",
            X = question.X,
            Y = question.Y,
            Width = question.Width,
            Height = question.Height,
            InteractionType = "mcq",
            Answers = q.Options
                .Select((o, j) =>
                {
                    var box = Intersect(o, question.X, question.Y, question.Width, question.Height)
                              ?? throw new LabelingValidationException("labeling.bank.optionOutsideQuestion");
                    return new BulkAnswerDto
                    {
                        Label = ((char)('A' + j)).ToString(),
                        X = box.X,
                        Y = box.Y,
                        Width = box.Width,
                        Height = box.Height,
                        IsCorrect = j == q.CorrectOptionIndex,
                        Order = j,
                    };
                })
                .ToList(),
        };
    }

    /// <summary>Kutu ile sınır dikdörtgeninin kesişimi; her iki kenarı en az <see cref="MinBoxEdge"/> değilse null.</summary>
    private static (int X, int Y, int Width, int Height)? Intersect(
        LabelingBankBoxDto box, int boundX, int boundY, int boundWidth, int boundHeight)
    {
        var left = Math.Max(box.X, boundX);
        var top = Math.Max(box.Y, boundY);
        var right = Math.Min((long)box.X + box.Width, (long)boundX + boundWidth);
        var bottom = Math.Min((long)box.Y + box.Height, (long)boundY + boundHeight);
        if (right - left < MinBoxEdge || bottom - top < MinBoxEdge)
            return null;
        return (left, top, (int)(right - left), (int)(bottom - top));
    }

    private async Task<LabelingPage> GetOrCreatePageAsync(string sha256, int width, int height, CancellationToken ct)
    {
        var page = await db.LabelingPages.AsNoTracking().FirstOrDefaultAsync(p => p.ImageSha256 == sha256, ct);
        if (page is not null)
            return page;

        page = new LabelingPage
        {
            ImageSha256 = sha256,
            Width = width,
            Height = height,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.LabelingPages.Add(page);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // Aynı sayfa eşzamanlı başka istekte oluşturuldu.
            db.Entry(page).State = EntityState.Detached;
            return await db.LabelingPages.AsNoTracking().FirstAsync(p => p.ImageSha256 == sha256, ct);
        }

        db.Entry(page).State = EntityState.Detached;
        return page;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
