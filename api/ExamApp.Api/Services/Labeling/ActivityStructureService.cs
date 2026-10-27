using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Labeling;

public interface IActivityStructureService
{
    /// <summary>
    /// Bloklar → taslak planlar (istek sırasıyla). Hatalar: <see cref="LabelingValidationException"/> (400),
    /// <see cref="LabelingRateLimitException"/> (429, canlı çağrı gereken blok sayısı kadar kota baştan düşülür),
    /// <see cref="LabelingAiUnavailableException"/> (503, canlı çağrı gerekiyorsa). Tek bloğun Claude hatası yalnız o bloğu
    /// <c>status = error</c> yapar; <c>Error</c> ve <c>ValidationErrors</c> yerelleştirme ANAHTARIDIR (controller çevirir).
    /// </summary>
    Task<ActivityStructureResponseDto> ExtractAsync(
        ActivityStructureRequestDto request, LabelingActor actor, CancellationToken ct = default);

    Task<ActivityPromptStateDto> GetPromptsAsync(CancellationToken ct = default);

    /// <summary>Tipin talimatını yeni sürüm olarak kaydeder; içerik etkin sürümle aynıysa sürüm açılmaz, etkin sürüm döner.</summary>
    Task<ActivityPromptVersionDto> SavePromptAsync(
        string interactionType, ActivitySavePromptRequestDto request, LabelingActor actor, CancellationToken ct = default);
}

/// <summary>
/// Issue #579: Claude ile etkinlik yapısı çıkarımı. Blok başına bir çağrı, tipe göre talimat + şema; çağrılar paralel
/// (<c>Anthropic:MaxParallelCrops</c>). Önbellek: (kırpma SHA-256, tip, model) satırı, yalnız o tipin etkin prompt parmak
/// iziyle üretildiyse döner (bir tipin talimatı değişince yalnız o tip yeniden çıkarılır). Kota/bütçe #578 ile ortak
/// (<see cref="LabelingAiQuota"/>): her canlı blok saatlik kotadan 1 düşer; en kötü durum maliyeti baştan bütçeden ayrılır,
/// çağrılar bitince gerçek maliyete (ödenmiş başarısız çağrı dahil) düzeltilir. Kayıttan dönenler dahil her istek kullanıcı
/// başına dakikalık sınıra (<c>Anthropic:ActivityStructureRequestsPerMinute</c>) tabidir.
/// </summary>
public sealed class ActivityStructureService(
    AppDbContext db,
    IActivityStructureExtractor extractor,
    IInteractionGraderRegistry graders,
    ImageHelper imageHelper,
    IFixedWindowCounterStore rateLimitStore,
    IOptions<LabelingAiOptions> options,
    TimeProvider timeProvider,
    ILogger<ActivityStructureService> logger,
    IHostApplicationLifetime? lifetime = null) : IActivityStructureService
{
    public const string StatusOk = "ok";
    public const string StatusError = "error";
    public const string OriginLive = "live";
    public const string OriginCached = "cached";
    public const string AiFailedKey = "labeling.activity.aiFailed";

    private const int PromptHistoryLimit = 20;
    private static readonly TimeSpan RequestRateWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Kota alındıktan sonra canlı çağrılar istek iptalinden BAĞIMSIZ koşar (ödenmiş sonuç ve bütçe kaybolmasın); yalnız bu süre
    /// ve uygulama kapanışı keser. Süresi dolunca başlamamış çağrıların kotası iade edilir.
    /// </summary>
    internal TimeSpan LiveCallTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public async Task<ActivityStructureResponseDto> ExtractAsync(
        ActivityStructureRequestDto request, LabelingActor actor, CancellationToken ct = default)
    {
        // Review L1: kayıttan dönen istek de kırpma hash'i + DB sorgusu harcar; kullanıcı başına dakikalık sınır.
        var requestDecision = await rateLimitStore.TryAcquireAsync($"labeling-activity-req:{actor.KeycloakId}", 1,
            options.Value.ActivityStructureRequestsPerMinute, RequestRateWindow, ct);
        if (!requestDecision.Allowed)
            throw new LabelingRateLimitException(requestDecision.RetryAfter);

        var model = options.Value.Models.FirstOrDefault(m => string.Equals(m.Id, request.Model, StringComparison.Ordinal))
                    ?? throw new LabelingValidationException("labeling.unknownModel");
        var blocks = ValidateBlocks(request.Blocks);

        using var image = LabelingImage.Decode(request.ImageBase64);
        var pageBounds = new Rectangle(0, 0, image.Width, image.Height);
        var rects = blocks.Select(b => Rectangle.Intersect(b.Rect, pageBounds)).ToList();
        if (rects.Any(r => r.Width < 1 || r.Height < 1))
            throw new LabelingValidationException("labeling.invalidRegion");

        var hashes = CropHashes(image, rects);
        var prompts = new Dictionary<string, ActivityPromptSettings>(StringComparer.Ordinal);
        foreach (var type in blocks.Select(b => b.Type).Distinct())
            prompts[type] = await GetActivePromptAsync(type, ct);

        // Kayıt: aynı kırpma + tip + model ve o tipin ETKİN prompt parmak izi (sürüm numarası değil).
        var cached = new Dictionary<(string Hash, string Type), LabelingActivityResult>();
        if (!request.Force)
        {
            var distinctHashes = hashes.Distinct().ToList();
            var rows = await db.LabelingActivityResults.AsNoTracking()
                .Where(r => distinctHashes.Contains(r.CropSha256) && r.ModelName == model.Id)
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                if (prompts.TryGetValue(row.InteractionType, out var prompt) && row.PromptHash == prompt.Fingerprint)
                    cached[(row.CropSha256, row.InteractionType)] = row;
            }
        }

        // Canlı iş: kayıtsız (kırpma, tip) çiftleri; aynı istekte aynı kırpma + tip iki kez geldiyse tek çağrı.
        var live = blocks.Select((b, i) => (Key: (Hash: hashes[i], b.Type), Index: i))
            .Where(x => !cached.ContainsKey(x.Key))
            .GroupBy(x => x.Key, x => x.Index)
            .Select(g => new LiveWork(g.Key.Hash, g.Key.Type, rects[g.First()], prompts[g.Key.Type]))
            .ToList();

        var outcomes = new Dictionary<(string Hash, string Type), LiveOutcome>();
        if (live.Count > 0)
        {
            if (!extractor.IsConfigured)
                throw new LabelingAiUnavailableException();

            // Review M2: en kötü durum maliyeti (blok başına kırpma görseli + talimat girdisi, MaxOutputTokens çıktı) bütçeden
            // baştan ayrılır; eşzamanlı istekler bütçeyi bu tahminden fazla aşamaz. Sonra gerçek maliyete düzeltilir.
            var page = new Size(image.Width, image.Height);
            var estimate = live.Sum(w => LabelingAiQuota.Cost(model,
                ClaudeActivityStructureExtractor.WorstCaseInputTokens(w.Rect, page, options.Value.MaxImageEdge),
                ClaudeActivityStructureExtractor.MaxOutputTokens));
            var reserved = await LabelingAiQuota.ReserveBudgetAsync(rateLimitStore, actor, options.Value, estimate, ct);
            try
            {
                await LabelingAiQuota.AcquireCallsAsync(rateLimitStore, actor, options.Value, live.Count, ct);
            }
            catch
            {
                await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, 0m);
                throw;
            }

            // Review W1: buradan sonra istek iptali (istemci gitti) çağrıları kesmez — ödenmiş sonuçlar kaydedilir, bütçe
            // gerçek maliyete düzeltilir. Yalnız sunucu zaman aşımı / uygulama kapanışı keser.
            using var callCts = CancellationTokenSource.CreateLinkedTokenSource(
                lifetime?.ApplicationStopping ?? CancellationToken.None);
            callCts.CancelAfter(LiveCallTimeout);
            using var gate = new SemaphoreSlim(options.Value.MaxParallelCrops);
            // RunLiveAsync istisna atmaz: her bloğun sonucu ayrı toplanır.
            var results = await Task.WhenAll(live.Select(work => RunLiveAsync(work, image, model, gate, callCts.Token)));
            foreach (var outcome in results)
                outcomes[(outcome.Work.Hash, outcome.Work.Type)] = outcome;

            await LabelingAiQuota.SettleAsync(rateLimitStore, actor, reserved, results.Sum(o => o.Cost));
            await LabelingAiQuota.RefundCallsAsync(rateLimitStore, actor, results.Count(o => !o.Started));

            // DbContext eşzamanlı kullanılamaz: kayıtlar çağrılar bittikten sonra sırayla, istek iptalinden bağımsız. Kayıt
            // hatası yalnız loglanır; sonuç yine döner (yalnız önbelleğe girmez).
            foreach (var outcome in results.Where(o => o.Extraction is not null))
                cached[(outcome.Work.Hash, outcome.Work.Type)] = await UpsertAsync(outcome, model, actor);
        }

        var response = new List<ActivityStructureResultDto>(blocks.Count);
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var key = (hashes[i], block.Type);
            if (outcomes.TryGetValue(key, out var outcome) && outcome.Extraction is null)
            {
                response.Add(new ActivityStructureResultDto(block.Key, block.Type, StatusError, null, null, null, false, [],
                    outcome.Work.Prompt.Version, outcome.FailedUsage is { } u ? (int)Math.Min(u.Input, int.MaxValue) : null,
                    outcome.FailedUsage is { } o ? (int)Math.Min(o.Output, int.MaxValue) : null,
                    outcome.FailedUsage is null ? null : Math.Round(outcome.Cost, 6), AiFailedKey, outcome.Work.Hash));
                continue;
            }

            var row = cached[key];
            var isLive = outcomes.ContainsKey(key);
            response.Add(ToResult(block, row, isLive ? OriginLive : OriginCached));
        }

        return new ActivityStructureResponseDto(response);
    }

    public async Task<ActivityPromptStateDto> GetPromptsAsync(CancellationToken ct = default)
    {
        // Issue #580: dört yapı tipine ek olarak üç çözüm talimatı (answerKey, solveFromKey, solveAi) — aynı DTO, ayrı sekme.
        var types = new List<ActivityPromptTypeStateDto>(ActivityStructureSettings.PromptKinds.Count);
        foreach (var type in ActivityStructureSettings.PromptKinds)
        {
            var history = await db.LabelingActivityPromptVersions.AsNoTracking()
                .Where(p => p.InteractionType == type)
                .OrderByDescending(p => p.Version)
                .Take(PromptHistoryLimit)
                .ToListAsync(ct);
            var defaults = ToDto(ActivityStructureSettings.Default(type));
            types.Add(new ActivityPromptTypeStateDto(type, history.Count > 0 ? ToDto(history[0]) : defaults, defaults,
                history.Select(ToDto).ToList()));
        }
        return new ActivityPromptStateDto(types);
    }

    public async Task<ActivityPromptVersionDto> SavePromptAsync(
        string interactionType, ActivitySavePromptRequestDto request, LabelingActor actor, CancellationToken ct = default)
    {
        var type = ActivityStructureSettings.CanonicalPromptKind(interactionType)
                   ?? throw new LabelingValidationException("labeling.activity.unknownType");
        var instruction = request.Instruction?.Trim() ?? string.Empty;
        if (instruction.Length == 0)
            throw new LabelingValidationException("labeling.promptRequired");
        if (instruction.Length > ActivityStructureSettings.MaxInstructionLength)
            throw new LabelingValidationException("labeling.promptTooLong");

        var active = await ActivityPromptStore.GetActiveVersionAsync(db, type, ct);
        var activeSettings = active is null
            ? ActivityStructureSettings.Default(type)
            : new ActivityPromptSettings(type, active.Version, active.Instruction);
        if (new ActivityPromptSettings(type, 0, instruction).HasSameContent(activeSettings))
            return active is null ? ToDto(activeSettings) : ToDto(active);

        for (var attempt = 0; ; attempt++)
        {
            var latest = await db.LabelingActivityPromptVersions
                .Where(p => p.InteractionType == type)
                .MaxAsync(p => (int?)p.Version, ct) ?? 0;
            var entity = new LabelingActivityPromptVersion
            {
                InteractionType = type,
                Version = latest + 1,
                Instruction = instruction,
                CreatedByKeycloakId = actor.KeycloakId,
                CreatedByName = Truncate(actor.Name, 200),
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            };
            db.LabelingActivityPromptVersions.Add(entity);
            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("[Labeling] activity prompt {Type} v{Version} saved by {User}", type, entity.Version,
                    actor.KeycloakId);
                return ToDto(entity);
            }
            catch (DbUpdateException ex) when (attempt < 4 && DbUpdateExceptionClassifier.IsUniqueViolation(ex))
            {
                // Aynı sürüm numarasını başka bir kayıt aldı: sonraki numarayla tekrar dene.
                db.Entry(entity).State = EntityState.Detached;
            }
        }
    }

    // ------------------------------------------------------------------ yardımcılar

    private sealed record ValidBlock(string Key, string Type, Rectangle Rect);

    private sealed record LiveWork(string Hash, string Type, Rectangle Rect, ActivityPromptSettings Prompt);

    /// <param name="Started">Çağrı başladı mı (false: zaman aşımı / kapanış kuyrukta yakaladı → kotası iade edilir).</param>
    private sealed record LiveOutcome(
        LiveWork Work, ActivityExtraction? Extraction, LabelingAiUsage? FailedUsage, decimal Cost, bool Started);

    private static List<ValidBlock> ValidateBlocks(List<ActivityStructureBlockDto>? blocks)
    {
        if (blocks is null || blocks.Count is 0 or > ActivityStructureRequestDto.MaxBlocks)
            throw new LabelingValidationException("labeling.activity.invalidBlocks");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ValidBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            if (block is null)
                throw new LabelingValidationException("labeling.activity.invalidBlocks");
            if (string.IsNullOrWhiteSpace(block.Key) || block.Key.Length > ActivityStructureBlockDto.MaxKeyLength)
                throw new LabelingValidationException("labeling.activity.invalidBlockKey");
            if (!keys.Add(block.Key))
                throw new LabelingValidationException("labeling.activity.duplicateBlockKey");
            var type = ActivityStructureSettings.Canonical(block.InteractionType)
                       ?? throw new LabelingValidationException("labeling.activity.unknownType");
            if (!IsFinite(block.X, block.Y, block.Width, block.Height) || block.X < 0 || block.Y < 0
                || block.Width < 1 || block.Height < 1 || block.X + block.Width > LabelingImage.MaxEdge
                || block.Y + block.Height > LabelingImage.MaxEdge)
                throw new LabelingValidationException("labeling.invalidRegion");

            var x = (int)Math.Round(block.X);
            var y = (int)Math.Round(block.Y);
            var rect = Rectangle.FromLTRB(x, y, (int)Math.Round(block.X + block.Width), (int)Math.Round(block.Y + block.Height));
            result.Add(new ValidBlock(block.Key, type, rect));
        }
        return result;
    }

    private static bool IsFinite(params double[] values) => values.All(double.IsFinite);

    /// <summary>
    /// Blok kırpmalarının SHA-256'sı (#577 <see cref="QuestionCropHash"/>): bankaya kayıttaki yolla — sayfa bir kez JPEG'e
    /// kodlanır (<c>LabelingImage.MaxEdge</c>, küçültme yok), her blok o JPEG'den kırpılır. Aynı blok ileride bankaya
    /// kaydedilirse <c>QuestionSource.CropSha256</c> ile aynı değer çıkar.
    /// </summary>
    private IReadOnlyList<string> CropHashes(LabelingImage image, IReadOnlyList<Rectangle> rects) =>
        ActivityCropHash.Compute(imageHelper, image, rects);

    private async Task<LiveOutcome> RunLiveAsync(
        LiveWork work, LabelingImage image, LabelingAiModelOption model, SemaphoreSlim gate, CancellationToken ct)
    {
        try
        {
            await gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Sunucu zaman aşımı / kapanış: çağrı hiç başlamadı (kotası iade edilir).
            return new LiveOutcome(work, null, null, 0m, Started: false);
        }
        if (ct.IsCancellationRequested)
        {
            // İptal edilmiş çağrının bıraktığı yeri aldı ama süre zaten doldu: başlatılmaz.
            gate.Release();
            return new LiveOutcome(work, null, null, 0m, Started: false);
        }

        try
        {
            var extraction = await extractor.ExtractAsync(image, work.Rect, work.Prompt, model, ct);
            var cost = LabelingAiQuota.Cost(model, extraction.InputTokens, extraction.OutputTokens);
            logger.LogInformation(
                "[Labeling] Claude {Model} activity {Type} prompt v{Prompt} crop {Hash}: {In}/{Out} tokens, ~${Cost:F4}",
                model.Id, work.Type, work.Prompt.Version, work.Hash[..16], extraction.InputTokens, extraction.OutputTokens, cost);
            return new LiveOutcome(work, extraction, null, cost, Started: true);
        }
        catch (LabelingAiException ex)
        {
            // Yalnız bu blok başarısız: diğer bloklar döner. Yanıt geldiyse token ödendi, bütçeye sayılır.
            var usage = ex.Usage;
            var cost = usage is { } u ? LabelingAiQuota.Cost(model, u.Input, u.Output) : 0m;
            logger.LogWarning(ex, "[Labeling] Claude {Model} activity {Type} crop {Hash} failed (~${Cost:F4} charged)",
                model.Id, work.Type, work.Hash[..16], cost);
            return new LiveOutcome(work, null, usage, cost, Started: true);
        }
        catch (Exception ex)
        {
            // Zaman aşımı ya da beklenmeyen hata: yalnız bu blok hata; kullanım bilinmiyor.
            logger.LogWarning(ex, "[Labeling] Claude {Model} activity {Type} crop {Hash} did not complete",
                model.Id, work.Type, work.Hash[..16]);
            return new LiveOutcome(work, null, null, 0m, Started: true);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Canlı sonucu önbelleğe yazar (iptal edilemez). Yazılamazsa loglanır ve kaydedilmemiş satır döner: yanıt yine oluşur.
    /// </summary>
    private async Task<LabelingActivityResult> UpsertAsync(LiveOutcome outcome, LabelingAiModelOption model, LabelingActor actor)
    {
        var work = outcome.Work;
        var extraction = outcome.Extraction!;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        void Apply(LabelingActivityResult row)
        {
            row.PromptHash = work.Prompt.Fingerprint;
            row.PromptVersion = work.Prompt.Version;
            row.PlanJson = extraction.PlanJson;
            row.Instruction = extraction.Instruction;
            row.InputTokens = extraction.InputTokens;
            row.OutputTokens = extraction.OutputTokens;
            row.EstimatedCostUsd = Math.Round(outcome.Cost, 6);
            row.CreatedByKeycloakId = actor.KeycloakId;
            row.CreatedByName = Truncate(actor.Name, 200);
            row.UpdatedAt = now;
        }

        LabelingActivityResult? row = null;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                row = await db.LabelingActivityResults.AsTracking().FirstOrDefaultAsync(
                    r => r.CropSha256 == work.Hash && r.InteractionType == work.Type && r.ModelName == model.Id,
                    CancellationToken.None);
                if (row is null)
                {
                    row = new LabelingActivityResult
                    {
                        CropSha256 = work.Hash, InteractionType = work.Type, ModelName = model.Id, CreatedAt = now,
                    };
                    db.LabelingActivityResults.Add(row);
                }
                Apply(row);

                try
                {
                    await db.SaveChangesAsync(CancellationToken.None);
                    return row;
                }
                catch (DbUpdateException ex) when (attempt == 0 && DbUpdateExceptionClassifier.IsUniqueViolation(ex))
                {
                    // Eşzamanlı ilk kayıt: bizimkini bırak, diğerinin üzerine yaz.
                    db.Entry(row).State = EntityState.Detached;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Labeling] activity result for crop {Hash} ({Type}) could not be cached", work.Hash[..16], work.Type);
            // Sonraki blokların kaydı bu satırı tekrar denemesin.
            if (row is not null)
                db.Entry(row).State = EntityState.Detached;
            var unsaved = new LabelingActivityResult
            {
                CropSha256 = work.Hash, InteractionType = work.Type, ModelName = model.Id, CreatedAt = now,
            };
            Apply(unsaved);
            return unsaved;
        }
    }

    private ActivityStructureResultDto ToResult(ValidBlock block, LabelingActivityResult row, string origin)
    {
        // Doğrulama her dönüşte yeniden yapılır (kayıtlı taslak da güncel kurala göre işaretlenir).
        var valid = graders.Resolve(block.Type).ValidateDefinition(row.PlanJson, null);
        var errors = valid ? (IReadOnlyList<string>)[] : ActivityPlanDiagnostics.Explain(block.Type, row.PlanJson);
        JsonElement? plan;
        try
        {
            using var doc = JsonDocument.Parse(row.PlanJson);
            plan = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            plan = null;
        }

        return new ActivityStructureResultDto(block.Key, block.Type, StatusOk, origin, plan, row.Instruction, valid, errors,
            row.PromptVersion, row.InputTokens, row.OutputTokens, row.EstimatedCostUsd, null, row.CropSha256);
    }

    private Task<ActivityPromptSettings> GetActivePromptAsync(string type, CancellationToken ct) =>
        ActivityPromptStore.GetActiveAsync(db, type, ct);

    private static ActivityPromptVersionDto ToDto(LabelingActivityPromptVersion p) =>
        new(p.Version, p.Instruction, p.CreatedByName, p.CreatedAt);

    private static ActivityPromptVersionDto ToDto(ActivityPromptSettings p) => new(p.Version, p.Instruction, null, null);

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
