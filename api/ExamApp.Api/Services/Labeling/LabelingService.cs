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
/// <summary>İşaretleme çağıranı. <c>IsAdmin</c> (issue #577): kırpma tekrarında başkasının sorusunun ayrıntısını görebilir.</summary>
public sealed record LabelingActor(string? KeycloakId, string? Name, bool IsAdmin = false);

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

    Task<LabelingPromptStateDto> GetPromptStateAsync(CancellationToken ct = default);

    Task<LabelingPromptStateDto> SavePromptAsync(LabelingSavePromptRequestDto request, LabelingActor actor, CancellationToken ct = default);
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
    private const int PromptHistoryLimit = 20;
    private const int MaxOptionsPerQuestion = 26;

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
        var prompt = await GetActivePromptAsync(ct);

        if (!request.Force)
        {
            // Kayıt yalnız aynı prompt içeriğiyle üretildiyse geçerli (parmak izi; sürüm numarası değil): prompt ya da koddaki
            // sabit kısım değiştiyse Claude'a yeniden sorulur.
            var fingerprint = prompt.Fingerprint;
            var cached = await db.LabelingResults.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Page.ImageSha256 == image.Sha256 && r.Source == LabelingSource.Ai &&
                                          r.ModelName == model.Id && r.PromptHash == fingerprint, ct);
            if (cached is not null)
                return new LabelingAiDetectResponseDto("cached", image.Sha256, ToDto(cached));
        }

        if (!detector.IsConfigured)
            throw new LabelingAiUnavailableException();

        await LabelingAiQuota.EnsureAndAcquireAsync(rateLimitStore, actor, options.Value, 1, ct);

        ClaudeDetection detection;
        try
        {
            detection = await detector.DetectAsync(image, model, prompt, ct);
        }
        catch (LabelingAiException ex) when (ex.Usage is { } failed)
        {
            // Yanıt geldi ama kullanılamadı (kesik / ret / şema dışı): token ödendi, bütçeye sayılır (bütçe kaçağı olmasın).
            var failedCost = LabelingAiQuota.Cost(model, failed.Input, failed.Output);
            await LabelingAiQuota.ChargeAsync(rateLimitStore, actor, failedCost);
            logger.LogWarning(
                "[Labeling] Claude {Model} page {Hash} failed after a paid response: {In}/{Out} tokens, ~${Cost:F4} charged",
                model.Id, image.Sha256[..16], failed.Input, failed.Output, failedCost);
            throw;
        }
        var cost = LabelingAiQuota.Cost(model, detection.InputTokens, detection.OutputTokens);

        // Ödenmiş sonuç: istemci bu arada vazgeçse de harcama sayılır ve sonuç kaydedilir (tekrar ödenmesin).
        await LabelingAiQuota.ChargeAsync(rateLimitStore, actor, cost);
        logger.LogInformation(
            "[Labeling] Claude {Model} prompt v{Prompt} page {Hash}: {Questions} questions, {In}/{Out} tokens, ~${Cost:F4}",
            model.Id, prompt.Version, image.Sha256[..16], detection.Predictions.Predictions.Count, detection.InputTokens,
            detection.OutputTokens, cost);

        var page = await GetOrCreatePageAsync(image.Sha256, image.Width, image.Height, CancellationToken.None);
        var saved = await UpsertResultAsync(page.Id, LabelingSource.Ai, model.Id, actor, result =>
        {
            result.BoxesJson = JsonSerializer.Serialize(detection.Predictions);
            result.InputTokens = detection.InputTokens;
            result.OutputTokens = detection.OutputTokens;
            result.EstimatedCostUsd = Math.Round(cost, 6);
            result.PromptVersion = prompt.Version;
            result.PromptHash = prompt.Fingerprint;
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
            result.BasePromptVersion = baseSource == LabelingSource.Ai && request.BasePromptVersion is >= 0
                ? request.BasePromptVersion
                : null;
        }, ct);

        if (snapshot is not null)
        {
            await UpsertResultAsync(page.Id, LabelingSource.Model, string.Empty, actor,
                result => result.BoxesJson = JsonSerializer.Serialize(snapshot), ct);
        }

        return (await GetPageAsync(header.Sha256, ct))!;
    }

    public async Task<LabelingPromptStateDto> GetPromptStateAsync(CancellationToken ct = default)
    {
        var history = await db.LabelingPromptVersions.AsNoTracking()
            .OrderByDescending(p => p.Version)
            .Take(PromptHistoryLimit)
            .ToListAsync(ct);
        var defaults = ToDto(LabelingPromptSettings.Default, null, null);
        var active = history.Count > 0 ? ToDto(history[0]) : defaults;
        return new LabelingPromptStateDto(active, defaults, history.Select(ToDto).ToList());
    }

    /// <summary>
    /// Yeni sürüm ekler (eskiler kalır; sonuçlar hangi sürümle üretildiğini taşır). İçerik etkin sürümle aynıysa yeni sürüm
    /// açılmaz (boşuna yeniden ücretlendirme olmasın). Eşzamanlı kayıtta birkaç kez yeniden dener.
    /// </summary>
    public async Task<LabelingPromptStateDto> SavePromptAsync(
        LabelingSavePromptRequestDto request, LabelingActor actor, CancellationToken ct = default)
    {
        var question = request.QuestionInstruction?.Trim() ?? string.Empty;
        var option = request.OptionInstruction?.Trim() ?? string.Empty;
        if (question.Length == 0 || option.Length == 0)
            throw new LabelingValidationException("labeling.promptRequired");
        if (question.Length > LabelingPromptSettings.MaxInstructionLength ||
            option.Length > LabelingPromptSettings.MaxInstructionLength)
            throw new LabelingValidationException("labeling.promptTooLong");
        if (request.QuestionPadding is < 0 or > LabelingPromptSettings.MaxPadding ||
            request.OptionPadding is < 0 or > LabelingPromptSettings.MaxPadding)
            throw new LabelingValidationException("labeling.invalidPadding");

        var active = await GetActivePromptAsync(ct);
        var requested = new LabelingPromptSettings(0, question, option, request.QuestionPadding, request.OptionPadding,
            request.ExpandQuestionToOptions);
        if (requested.HasSameContent(active))
            return await GetPromptStateAsync(ct);

        for (var attempt = 0; ; attempt++)
        {
            var latest = await db.LabelingPromptVersions.MaxAsync(p => (int?)p.Version, ct) ?? 0;
            var entity = new LabelingPromptVersion
            {
                Version = latest + 1,
                QuestionInstruction = question,
                OptionInstruction = option,
                QuestionPadding = request.QuestionPadding,
                OptionPadding = request.OptionPadding,
                ExpandQuestionToOptions = request.ExpandQuestionToOptions,
                CreatedByKeycloakId = actor.KeycloakId,
                CreatedByName = Truncate(actor.Name, 200),
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            };
            db.LabelingPromptVersions.Add(entity);
            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("[Labeling] prompt v{Version} saved by {User}", entity.Version, actor.KeycloakId);
                return await GetPromptStateAsync(ct);
            }
            catch (DbUpdateException ex) when (attempt < 4 && DbUpdateExceptionClassifier.IsUniqueViolation(ex))
            {
                // Aynı sürüm numarasını başka bir kayıt aldı: sonraki numarayla tekrar dene.
                db.Entry(entity).State = EntityState.Detached;
            }
        }
    }

    private async Task<LabelingPromptSettings> GetActivePromptAsync(CancellationToken ct)
    {
        var latest = await db.LabelingPromptVersions.AsNoTracking()
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);
        return latest is null
            ? LabelingPromptSettings.Default
            : new LabelingPromptSettings(latest.Version, latest.QuestionInstruction, latest.OptionInstruction,
                latest.QuestionPadding, latest.OptionPadding, latest.ExpandQuestionToOptions);
    }

    private static LabelingPromptDto ToDto(LabelingPromptVersion p) =>
        new(p.Version, p.QuestionInstruction, p.OptionInstruction, p.QuestionPadding, p.OptionPadding,
            p.ExpandQuestionToOptions, p.CreatedByName, p.CreatedAt);

    private static LabelingPromptDto ToDto(LabelingPromptSettings p, string? createdByName, DateTime? createdAt) =>
        new(p.Version, p.QuestionInstruction, p.OptionInstruction, p.QuestionPadding, p.OptionPadding,
            p.ExpandQuestionToOptions, createdByName, createdAt);

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
    /// sınıfı); koordinatlar sayfa sınırına kırpılır. JSONB'ye yalnız bilinen alanlar yazılır. Issue #578: üst düzey kutunun
    /// <c>kind</c>/<c>component</c>/<c>interaction_type</c>'ı <see cref="LabelingBlocks.IsValidManual"/> ile doğrulanır
    /// (geçersiz → <c>labeling.invalidBlockKind</c>); şıklarda bu alanlar yazılmaz.
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

        PredictionDto Block(PredictionDto q)
        {
            if (!LabelingBlocks.IsValidManual(q.Kind, q.Component, q.InteractionType))
                throw new LabelingValidationException("labeling.invalidBlockKind");
            var block = Box(q, (q.Subpredictions ?? []).Select(a => Box(a, [])).ToList());
            block.Kind = q.Kind;
            block.Component = q.Component;
            block.InteractionType = q.InteractionType;
            block.Label = CleanLabel(q.Label);
            return block;
        }

        return new PredictionListDto
        {
            Success = true,
            Predictions = questions.Select(Block).ToList(),
            SectionLabel = CleanLabel(input.SectionLabel),
        };
    }

    /// <summary>
    /// Issue #580: blok etiketi / bölüm başlığı — kırpılır, boşsa null; en çok <see cref="PredictionDto.MaxLabelLength"/> karakter ve
    /// kontrol karakteri yok (aksi halde 400 <c>labeling.invalidLabel</c>).
    /// </summary>
    private static string? CleanLabel(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > PredictionDto.MaxLabelLength || trimmed.Any(char.IsControl))
            throw new LabelingValidationException("labeling.invalidLabel");
        return trimmed;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static LabelingPageDto ToDto(LabelingPage page) =>
        new(page.ImageSha256, page.Width, page.Height,
            page.Results.OrderBy(r => r.Source).ThenBy(r => r.ModelName).Select(ToDto).ToList(),
            LabelingBankService.ToBankInfo(page));

    private static LabelingResultDto ToDto(LabelingResult r) =>
        new(r.Source.ToString(),
            r.ModelName,
            JsonSerializer.Deserialize<PredictionListDto>(r.BoxesJson) ?? new PredictionListDto(),
            r.BaseSource?.ToString(),
            r.BaseModelName,
            r.InputTokens,
            r.OutputTokens,
            r.EstimatedCostUsd,
            r.PromptVersion,
            r.CreatedByName,
            r.CreatedAt,
            r.UpdatedAt,
            r.BasePromptVersion);
}
