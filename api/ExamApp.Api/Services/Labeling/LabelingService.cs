using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Labeling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Labeling;

/// <summary>İşlemi yapan kullanıcı (token'dan).</summary>
public sealed record LabelingActor(string? KeycloakId, string? Name);

/// <summary>Anthropic API anahtarı yok → 503.</summary>
public sealed class LabelingAiUnavailableException() : Exception("Claude labeling is not configured.");

/// <summary>Kullanıcının saatlik çağrı kotası (<c>Budget=false</c>) ya da günlük harcama sınırı doldu → 429.</summary>
public sealed class LabelingRateLimitException(TimeSpan? retryAfter, bool budget = false)
    : Exception("Claude labeling rate limit exceeded.")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;

    public bool Budget { get; } = budget;
}

public interface ILabelingService
{
    IReadOnlyList<LabelingAiModelDto> GetAiModels();

    Task<LabelingPageDto?> GetPageAsync(string imageSha256, CancellationToken ct = default);

    Task<LabelingAiDetectResponseDto> DetectWithAiAsync(LabelingAiDetectRequestDto request, LabelingActor actor, CancellationToken ct = default);

    Task<LabelingPageDto> SaveManualAsync(LabelingSaveManualRequestDto request, LabelingActor actor, CancellationToken ct = default);
}

/// <summary>
/// İşaretleme kaynakları: Model (YOLO anlık görüntüsü), Ai (Claude, model başına) ve Manuel (eğitime giden son hal).
/// Aynı sayfa + model için kayıtlı Claude sonucu varsa API'ye gidilmez (<c>Force</c> hariç); kota yalnız gerçek çağrıyı sayar.
/// </summary>
public sealed class LabelingService(
    AppDbContext db,
    IClaudeLabelDetector detector,
    IFixedWindowCounterStore rateLimitStore,
    IOptions<LabelingAiOptions> options,
    TimeProvider timeProvider,
    ILogger<LabelingService> logger) : ILabelingService
{
    private const int MaxQuestions = 300;
    private const int MaxOptionsPerQuestion = 26;
    private const string TotalBudgetKey = "labeling-ai-usd:all";
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan BudgetWindow = TimeSpan.FromHours(24);

    /// <summary>API anahtarı yoksa boş: UI Claude seçeneğini kapatır (her çağrı 503 olurdu).</summary>
    public IReadOnlyList<LabelingAiModelDto> GetAiModels() =>
        !detector.IsConfigured
            ? []
            : options.Value.Models
                .Select(m => new LabelingAiModelDto(m.Id, m.DisplayName, m.InputUsdPerMTok, m.OutputUsdPerMTok, m.IsDefault))
                .ToList();

    public async Task<LabelingPageDto?> GetPageAsync(string imageSha256, CancellationToken ct = default)
    {
        if (!LabelingImage.IsValidSha256(imageSha256))
            throw new LabelingValidationException("labeling.invalidHash");

        var page = await db.LabelingPages.AsNoTracking()
            .Include(p => p.Results)
            .FirstOrDefaultAsync(p => p.ImageSha256 == imageSha256, ct);
        return page is null ? null : ToDto(page);
    }

    public async Task<LabelingAiDetectResponseDto> DetectWithAiAsync(
        LabelingAiDetectRequestDto request, LabelingActor actor, CancellationToken ct = default)
    {
        var model = options.Value.Models.FirstOrDefault(m => string.Equals(m.Id, request.Model, StringComparison.Ordinal))
                    ?? throw new LabelingValidationException("labeling.unknownModel");

        using var image = LabelingImage.Decode(request.ImageBase64);

        if (!request.Force)
        {
            var cached = await db.LabelingResults.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Page.ImageSha256 == image.Sha256 && r.Source == LabelingSource.Ai &&
                                          r.ModelName == model.Id, ct);
            if (cached is not null)
                return new LabelingAiDetectResponseDto("cached", image.Sha256, ToDto(cached));
        }

        if (!detector.IsConfigured)
            throw new LabelingAiUnavailableException();

        var userBudgetKey = $"labeling-ai-usd:{actor.KeycloakId}";
        await EnsureBudgetAsync(userBudgetKey, options.Value.DailyBudgetUsdPerUser, ct);
        await EnsureBudgetAsync(TotalBudgetKey, options.Value.DailyBudgetUsdTotal, ct);

        var decision = await rateLimitStore.TryAcquireAsync(
            $"labeling-ai:{actor.KeycloakId}", 1, options.Value.LiveCallsPerHour, RateLimitWindow, ct);
        if (!decision.Allowed)
            throw new LabelingRateLimitException(decision.RetryAfter);

        var detection = await detector.DetectAsync(image, model, ct);
        var cost = (detection.InputTokens * model.InputUsdPerMTok + detection.OutputTokens * model.OutputUsdPerMTok) / 1_000_000m;

        // Ödenmiş sonuç: istemci bu arada vazgeçse de harcama sayılır ve sonuç kaydedilir (tekrar ödenmesin).
        await ChargeBudgetAsync(userBudgetKey, cost);
        await ChargeBudgetAsync(TotalBudgetKey, cost);
        logger.LogInformation(
            "[Labeling] Claude {Model} page {Hash}: {Questions} questions, {In}/{Out} tokens, ~${Cost:F4}",
            model.Id, image.Sha256[..16], detection.Predictions.Predictions.Count, detection.InputTokens,
            detection.OutputTokens, cost);

        var page = await GetOrCreatePageAsync(image.Sha256, image.Width, image.Height, CancellationToken.None);
        var saved = await UpsertResultAsync(page.Id, LabelingSource.Ai, model.Id, actor, result =>
        {
            result.BoxesJson = JsonSerializer.Serialize(detection.Predictions);
            result.InputTokens = detection.InputTokens;
            result.OutputTokens = detection.OutputTokens;
            result.EstimatedCostUsd = Math.Round(cost, 6);
        }, CancellationToken.None);

        return new LabelingAiDetectResponseDto("live", image.Sha256, ToDto(saved));
    }

    public async Task<LabelingPageDto> SaveManualAsync(
        LabelingSaveManualRequestDto request, LabelingActor actor, CancellationToken ct = default)
    {
        var baseSource = ParseBaseSource(request.BaseSource);
        // Görsel çözülmez: hash + boyut yeter (kutular sayfa sınırına kırpılır).
        var header = LabelingImage.ReadHeader(request.ImageBase64);
        var page = await GetOrCreatePageAsync(header.Sha256, header.Width, header.Height, ct);
        var predictions = Sanitize(request.Predictions, page.Width, page.Height);
        var snapshot = request.ModelSnapshot is null ? null : Sanitize(request.ModelSnapshot, page.Width, page.Height);

        await UpsertResultAsync(page.Id, LabelingSource.Manual, string.Empty, actor, result =>
        {
            result.BoxesJson = JsonSerializer.Serialize(predictions);
            result.BaseSource = baseSource;
            result.BaseModelName = baseSource == LabelingSource.Ai ? Truncate(request.BaseModelName, 64) : null;
        }, ct);

        if (snapshot is not null)
        {
            await UpsertResultAsync(page.Id, LabelingSource.Model, string.Empty, actor,
                result => result.BoxesJson = JsonSerializer.Serialize(snapshot), ct);
        }

        return (await GetPageAsync(header.Sha256, ct))!;
    }

    /// <summary>Kullanıcı / toplam günlük harcama sınırı (mikro-USD sayaç) dolduysa yeni canlı çağrı yapılmaz.</summary>
    private async Task EnsureBudgetAsync(string key, decimal limitUsd, CancellationToken ct)
    {
        var spent = await rateLimitStore.PeekAsync(key, ct);
        if (spent.Count >= ToMicroUsd(limitUsd))
            throw new LabelingRateLimitException(spent.RemainingWindow, budget: true);
    }

    private async Task ChargeBudgetAsync(string key, decimal costUsd)
    {
        var micro = ToMicroUsd(costUsd);
        if (micro > 0)
            await rateLimitStore.TryAcquireAsync(key, micro, int.MaxValue, BudgetWindow, CancellationToken.None);
    }

    private static int ToMicroUsd(decimal usd) => (int)Math.Clamp(Math.Ceiling(usd * 1_000_000m), 0, int.MaxValue);

    private async Task<LabelingPage> GetOrCreatePageAsync(string sha256, int width, int height, CancellationToken ct)
    {
        var page = await db.LabelingPages.FirstOrDefaultAsync(p => p.ImageSha256 == sha256, ct);
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
            return page;
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // Aynı sayfa eşzamanlı başka istekte oluşturuldu.
            db.Entry(page).State = EntityState.Detached;
            return await db.LabelingPages.FirstAsync(p => p.ImageSha256 == sha256, ct);
        }
    }

    private async Task<LabelingResult> UpsertResultAsync(
        int pageId, LabelingSource source, string modelName, LabelingActor actor, Action<LabelingResult> apply,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var result = await db.LabelingResults.FirstOrDefaultAsync(
                r => r.PageId == pageId && r.Source == source && r.ModelName == modelName, ct);
            if (result is null)
            {
                result = new LabelingResult { PageId = pageId, Source = source, ModelName = modelName, CreatedAt = now };
                db.LabelingResults.Add(result);
            }

            apply(result);
            result.CreatedByKeycloakId = actor.KeycloakId;
            result.CreatedByName = Truncate(actor.Name, 200);
            result.UpdatedAt = now;

            try
            {
                await db.SaveChangesAsync(ct);
                return result;
            }
            catch (DbUpdateException ex) when (attempt == 0 && DbUpdateExceptionClassifier.IsUniqueViolation(ex))
            {
                // Eşzamanlı ilk kayıt: bizimkini bırak, diğerinin üzerine yaz.
                db.Entry(result).State = EntityState.Detached;
            }
        }
    }

    private static LabelingSource? ParseBaseSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse<LabelingSource>(value, ignoreCase: true, out var parsed) && parsed is LabelingSource.Model or LabelingSource.Ai)
            return parsed;
        throw new LabelingValidationException("labeling.invalidBaseSource");
    }

    /// <summary>
    /// İstemcinin gönderdiği kutuları sınırlar: adet, negatif değer ve yalnız <c>class_id = 0</c> (question-detector'ın tek
    /// sınıfı); koordinatlar sayfa sınırına kırpılır. JSONB'ye yalnız bilinen alanlar yazılır.
    /// </summary>
    private static PredictionListDto Sanitize(PredictionListDto input, int pageWidth, int pageHeight)
    {
        var questions = input.Predictions ?? [];
        if (questions.Count > MaxQuestions || questions.Any(q => (q.Subpredictions?.Count ?? 0) > MaxOptionsPerQuestion))
            throw new LabelingValidationException("labeling.tooManyBoxes");

        PredictionDto Box(PredictionDto b, List<PredictionDto> children)
        {
            if (b.ClassId != 0 || b.X < 0 || b.Y < 0 || b.Width < 0 || b.Height < 0)
                throw new LabelingValidationException("labeling.invalidBox");
            var x = Math.Min(b.X, pageWidth);
            var y = Math.Min(b.Y, pageHeight);
            return new PredictionDto
            {
                ClassId = 0,
                X = x,
                Y = y,
                Width = Math.Min(b.Width, pageWidth - x),
                Height = Math.Min(b.Height, pageHeight - y),
                Subpredictions = children,
            };
        }

        return new PredictionListDto
        {
            Success = true,
            Predictions = questions
                .Select(q => Box(q, (q.Subpredictions ?? []).Select(a => Box(a, [])).ToList()))
                .ToList(),
        };
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static LabelingPageDto ToDto(LabelingPage page) =>
        new(page.ImageSha256, page.Width, page.Height,
            page.Results.OrderBy(r => r.Source).ThenBy(r => r.ModelName).Select(ToDto).ToList());

    private static LabelingResultDto ToDto(LabelingResult r) =>
        new(r.Source.ToString(),
            r.ModelName,
            JsonSerializer.Deserialize<PredictionListDto>(r.BoxesJson) ?? new PredictionListDto(),
            r.BaseSource?.ToString(),
            r.BaseModelName,
            r.InputTokens,
            r.OutputTokens,
            r.EstimatedCostUsd,
            r.CreatedByName,
            r.CreatedAt,
            r.UpdatedAt);
}
