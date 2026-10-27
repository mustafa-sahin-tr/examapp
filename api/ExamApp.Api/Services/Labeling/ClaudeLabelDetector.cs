using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using ExamApp.Api.Models.Dtos.Labeling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Drawing;

namespace ExamApp.Api.Services.Labeling;

public interface IClaudeLabelDetector
{
    /// <summary>API anahtarı yapılandırılmış mı; değilse AI ucu 503 döner.</summary>
    bool IsConfigured { get; }

    Task<ClaudeDetection> DetectAsync(
        LabelingImage image, LabelingAiModelOption model, LabelingPromptSettings prompt, CancellationToken ct = default);
}

/// <summary>Claude'un bulduğu kutular (orijinal piksel, question-detector şekli) ve iki aşamanın toplam token'ı.</summary>
public sealed record ClaudeDetection(PredictionListDto Predictions, int InputTokens, int OutputTokens);

/// <summary>
/// Claude yanıtı kullanılamadı (ret, eksik/bozuk JSON, API hatası) → 502. <see cref="Usage"/>: yanıt geldiyse harcanan token
/// (ödenmiş başarısız çağrı da bütçeye/toplama sayılır); istek hiç yanıt almadıysa null.
/// </summary>
public sealed class LabelingAiException(string message, Exception? inner = null, LabelingAiUsage? usage = null)
    : Exception(message, inner)
{
    public LabelingAiUsage? Usage { get; } = usage;
}

/// <summary>Bir ya da birkaç Claude çağrısının girdi/çıktı token'ı.</summary>
public readonly record struct LabelingAiUsage(long Input, long Output);

/// <summary>
/// Microsoft.Extensions.AI <see cref="IChatClient"/> üzerinden Claude ile blok/şık kutusu tespiti. İki aşama:
/// (1) tüm sayfa → TEK çağrıda bütün bloklar, her biri türüyle (issue #578: <c>mcq_question</c> | <c>activity</c> +
/// bileşen ailesi | <c>info_box</c> | <c>answer_key</c> | <c>noise</c>, bkz. <see cref="LabelingBlocks"/>),
/// (2) <c>mcq_question</c> kırpmaları → şık kutuları (en fazla <c>MaxParallelCrops</c> eşzamanlı); bileşensiz
/// <c>activity</c> da denenir, >= 2 şık bulunursa <c>mcq_question</c> olur (güvenlik ağı, SchemaRevision 3).
/// Görseller küçültülmüş uzayda gider; dönen koordinatlar <see cref="EncodedRegion.ToPage"/> ile orijinale çevrilir.
/// Model istek başına <see cref="ChatOptions.ModelId"/> ile seçilir; yanıt şeması <c>GetResponseAsync&lt;T&gt;</c> ile
/// JSON schema olarak gönderilir. Aşama talimatları ve kutu payları kullanıcı tarafından düzenlenir
/// (<see cref="LabelingPromptSettings"/>); sistem mesajı (koordinat kuralı) ve şema burada sabittir.
/// </summary>
public sealed class ClaudeLabelDetector : IClaudeLabelDetector, IDisposable
{
    // Düşünme token'ları da bu sınıra dahil; yanıt küçük bir JSON. Sınır istek başına maliyeti de sınırlar.
    // 12000 (#578): her blok artık kind/component/interactionType dizgileri de taşıyor ve toplam blok sınırı
    // MaxQuestionsPerPage * TotalBlockFactor; 8000 yoğun sayfada kesilme (Length → 502) payı bırakmıyordu.
    private const int PageMaxOutputTokens = 12000;
    private const int CropMaxOutputTokens = 4000;

    // Soru kırpmasına eklenen kenar payı (orijinal piksel): kutu sınırdaki şık harfini kesmesin.
    private const int CropPadding = 12;

    // Sayfadaki tüm blokların (gürültü, anlatım dahil) üst sınırı = MaxQuestionsPerPage * bu çarpan. Şık çağrısı alabilecek
    // bloklar (mcq_question + bileşensiz activity) ayrıca MaxQuestionsPerPage ile sınırlı: başlık/altlık son soruları itmez.
    internal const int TotalBlockFactor = 3;

    // Bileşensiz "activity" bloğunda bu kadar şık bulunursa blok mcq_question'a çevrilir (SchemaRevision 3).
    private const int MinProbeOptions = 2;

    private static readonly LabelingBlocks.Classification McqClassification =
        new(LabelingBlocks.McqQuestion, null, null, false);

    // Bu sınıftaki sabitler (sistem mesajı, şemalar, CropPadding, MaxImageEdge kullanımı) değişirse
    // LabelingPromptSettings.SchemaRevision'ı artırın; yoksa eski Claude sonuçları kayıttan dönmeye devam eder.
    private const string SystemPrompt =
        "You locate regions on scanned pages of Turkish school test books for an object-detection training set. " +
        "Coordinates are integer pixels of the image you are given, origin at the top-left corner; " +
        "(x1, y1) is the top-left and (x2, y2) the bottom-right corner of a box. Boxes are tight around the ink " +
        "they enclose. Report only what is visible; never invent regions.";

    private readonly IChatClient? _chat;
    private readonly LabelingAiOptions _options;
    private readonly ILogger<ClaudeLabelDetector> _logger;

    /// <summary>Test dikişi: ağa çıkmayan sahte <see cref="IChatClient"/> ile (DI yalnız public kurucuyu görür).</summary>
    internal ClaudeLabelDetector(IChatClient chat, IOptions<LabelingAiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeLabelDetector>();
        _chat = chat;
    }

    public ClaudeLabelDetector(IOptions<LabelingAiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeLabelDetector>();
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return;

        // AsIChatClient: Anthropic SDK'nın MEAI köprüsü. Model sabitlenmez, istek başına ChatOptions.ModelId'den gelir.
        // UseLogging/UseOpenTelemetry: Aspire dashboard'unda istek/yanıt izleri (gen_ai.* span'leri).
        _chat = new AnthropicClient { ApiKey = _options.ApiKey }
            .AsIChatClient()
            .AsBuilder()
            .UseLogging(loggerFactory)
            .UseOpenTelemetry()
            .Build();
    }

    public bool IsConfigured => _chat is not null;

    public async Task<ClaudeDetection> DetectAsync(
        LabelingImage image, LabelingAiModelOption model, LabelingPromptSettings prompt, CancellationToken ct = default)
    {
        if (_chat is null)
            throw new InvalidOperationException("Anthropic API key is not configured.");

        var page = image.Encode(_options.MaxImageEdge);
        var (detected, pageUsage) = await AskAsync<PageBlocks>(model, PageMaxOutputTokens,
            page,
            LabelingPromptSettings.Render(prompt.QuestionInstruction, page.Width, page.Height),
            ct);

        var blocks = NormalizeBlocks(detected.Blocks, page, _options.MaxQuestionsPerPage, model.Id);

        var pageBounds = new Rectangle(0, 0, image.Width, image.Height);
        long inputTokens = pageUsage.Input;
        long outputTokens = pageUsage.Output;

        void AddUsage(LabelingAiUsage? usage)
        {
            if (usage is not { } u)
                return;
            Interlocked.Add(ref inputTokens, u.Input);
            Interlocked.Add(ref outputTokens, u.Output);
        }
        using var gate = new SemaphoreSlim(_options.MaxParallelCrops);

        var predictions = await Task.WhenAll(blocks.Select(async block =>
        {
            var rect = block.Rect;
            var padded = Rectangle.Intersect(
                Rectangle.Inflate(rect, prompt.QuestionPadding, prompt.QuestionPadding), pageBounds);
            // Güvenlik ağı: bileşensiz "activity" çoğu zaman yanlış sınıflanmış MCQ'dur (ör. kökünde "Örnek:" satırı olan
            // soru). Ona da şık aşaması sorulur; >= 2 şık bulunursa mcq_question olur. Bileşenli etkinlik hiç sorulmaz.
            var probe = block.Kind.Kind == LabelingBlocks.Activity;
            if (!NeedsOptionCall(block.Kind))
            {
                // Etkinlik / anlatım / cevap anahtarı / gürültü: şık aranmaz (2. aşama yok), yalnız pay + sayfa kırpması.
                return ToPrediction(padded, block.Kind, []);
            }

            await gate.WaitAsync(ct);
            try
            {
                var crop = image.Encode(_options.MaxImageEdge, Rectangle.Inflate(rect, CropPadding, CropPadding));
                OptionBoxes options;
                LabelingAiUsage usage;
                try
                {
                    (options, usage) = await AskAsync<OptionBoxes>(model, CropMaxOutputTokens,
                    crop,
                    LabelingPromptSettings.Render(prompt.OptionInstruction, crop.Width, crop.Height),
                    ct);
                }
                catch (LabelingAiException ex) when (probe)
                {
                    // Bileşensiz etkinliğin şık denemesi başarısız: etkinlik olarak kalır. Ödenmiş token yine sayılır.
                    _logger.LogWarning(ex, "[Labeling] option probe failed for a component-less activity (model {Model})", model.Id);
                    AddUsage(ex.Usage);
                    return ToPrediction(padded, block.Kind, []);
                }
                catch (LabelingAiException ex)
                {
                    // Tek soru yüzünden ödenmiş sayfa çöpe gitmesin: soru şıksız kalır, kullanıcı şıkları elle çizer.
                    _logger.LogWarning(ex, "[Labeling] option detection failed for one question (model {Model})", model.Id);
                    options = new OptionBoxes([]);
                    usage = ex.Usage ?? default;
                }
                AddUsage(usage);

                var optionRects = (options.Options ?? [])
                    .Select(o => crop.ToPage(o.X1, o.Y1, o.X2, o.Y2))
                    .Where(r => r.Width > 2 && r.Height > 2)
                    .Select(r => Rectangle.Intersect(Rectangle.Inflate(r, prompt.OptionPadding, prompt.OptionPadding), pageBounds))
                    .ToList();
                if (probe && optionRects.Count < MinProbeOptions)
                    return ToPrediction(padded, block.Kind, []);
                var kind = probe ? McqClassification : block.Kind;
                var question = padded;
                // Eğitim verisinde şıklar sorunun içinde olmalı: istenirse soru kutusu şıkların birleşimi kadar genişler.
                if (prompt.ExpandQuestionToOptions)
                    question = optionRects.Aggregate(question, Rectangle.Union);

                return ToPrediction(question, kind, optionRects
                    .Select(r => new PredictionDto { ClassId = 0, X = r.X, Y = r.Y, Width = r.Width, Height = r.Height })
                    .ToList());
            }
            finally
            {
                gate.Release();
            }
        }));

        return new ClaudeDetection(
            new PredictionListDto { Success = true, Predictions = predictions.ToList() },
            (int)Math.Min(inputTokens, int.MaxValue),
            (int)Math.Min(outputTokens, int.MaxValue));
    }

    private async Task<(T Result, LabelingAiUsage Usage)> AskAsync<T>(
        LabelingAiModelOption model, int maxOutputTokens, EncodedRegion region, string instruction, CancellationToken ct)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = model.Id,
            MaxOutputTokens = maxOutputTokens,
            Reasoning = ParseEffort(_options.Effort) is { } effort ? new ReasoningOptions { Effort = effort } : null,
        };

        // Görsel metinden önce: Claude görsel + talimat sırasında daha isabetli.
        List<ChatMessage> messages =
        [
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, [new DataContent(region.Jpeg, "image/jpeg"), new TextContent(instruction)]),
        ];

        ChatResponse<T> response;
        try
        {
            response = await _chat!.GetResponseAsync<T>(messages, chatOptions, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[Labeling] Claude call failed (model {Model})", model.Id);
            throw new LabelingAiException("Claude request failed.", ex);
        }

        var usage = new LabelingAiUsage(response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
        // Yanıt geldi = token ödendi: başarısızlıkta da kullanım istisnayla taşınır (bütçe/toplam).
        if (response.FinishReason == ChatFinishReason.ContentFilter)
            throw new LabelingAiException("Claude declined the request.", usage: usage);
        if (response.FinishReason == ChatFinishReason.Length)
            throw new LabelingAiException("Claude response was truncated.", usage: usage);
        if (!response.TryGetResult(out var result) || result is null)
            throw new LabelingAiException("Claude response did not match the schema.", usage: usage);

        return (result, usage);
    }

    private static ReasoningEffort? ParseEffort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => ReasoningEffort.Low,
        "medium" => ReasoningEffort.Medium,
        "high" => ReasoningEffort.High,
        "xhigh" => ReasoningEffort.ExtraHigh,
        _ => null,
    };

    public void Dispose() => _chat?.Dispose();

    /// <summary>1. aşama bloğunun sayfa uzayındaki dikdörtgeni ve normalleştirilmiş türü.</summary>
    internal sealed record DetectedBlock(Rectangle Rect, LabelingBlocks.Classification Kind);

    /// <summary>
    /// 1. aşama yanıtı → sayfa uzayı: tür normalleştirilir (bilinmeyen → noise, loglanır), çok küçük kutular atılır.
    /// Şık çağrısı alabilecek bloklar (<c>mcq_question</c> + bileşensiz <c>activity</c>) <paramref name="maxQuestions"/> ile,
    /// tüm bloklar <paramref name="maxQuestions"/> * <see cref="TotalBlockFactor"/> ile sınırlanır; sınırı aşan aday atlanır,
    /// diğer türler toplam sınıra kadar eklenmeye devam eder (okunma sırası korunur). Sayfa başına şık çağrısı ≤ maxQuestions.
    /// </summary>
    internal IReadOnlyList<DetectedBlock> NormalizeBlocks(
        IEnumerable<Block>? blocks, EncodedRegion page, int maxQuestions, string modelId)
    {
        var maxBlocks = maxQuestions * TotalBlockFactor;
        var candidates = 0;
        var result = new List<DetectedBlock>();
        foreach (var b in blocks ?? [])
        {
            var rect = page.ToPage(b.X1, b.Y1, b.X2, b.Y2);
            if (rect.Width <= 4 || rect.Height <= 4)
                continue;
            var kind = LabelingBlocks.Normalize(b.Kind, b.Component, b.InteractionType);
            if (kind.UnknownKind)
                _logger.LogWarning("[Labeling] Claude returned unknown block kind {Kind} (model {Model}); treated as noise",
                    Truncate(b.Kind, 40), modelId);
            if (NeedsOptionCall(kind))
            {
                if (candidates >= maxQuestions)
                    continue;
                candidates++;
            }
            result.Add(new DetectedBlock(rect, kind));
            if (result.Count >= maxBlocks)
                break;
        }
        return result;
    }

    /// <summary>Şık aşamasına girer mi: <c>mcq_question</c> ya da (güvenlik ağı) bileşensiz <c>activity</c>.</summary>
    private static bool NeedsOptionCall(LabelingBlocks.Classification kind) =>
        kind.Kind == LabelingBlocks.McqQuestion || (kind.Kind == LabelingBlocks.Activity && kind.Component is null);

    private static PredictionDto ToPrediction(Rectangle box, LabelingBlocks.Classification kind, List<PredictionDto> options) => new()
    {
        ClassId = 0,
        X = box.X,
        Y = box.Y,
        Width = box.Width,
        Height = box.Height,
        Subpredictions = options,
        Kind = kind.Kind,
        Component = kind.Component,
        InteractionType = kind.InteractionType,
    };

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    // Yanıt şemaları — GetResponseAsync<T> bunlardan JSON schema üretir. Açıklamalar şemaya girer (kodda sabit; kullanıcı
    // talimatı eski/özel bir sürüm olsa da izinli değerler modele ulaşır). Tür önce, koordinatlar sonra: model bloğu
    // sınıflandırıp sonra çerçeveler.
    internal sealed record PageBlocks(
        [property: Description("Every block on the page in reading order (top to bottom; left column before right column).")]
        List<Block> Blocks);

    internal sealed record Block(
        [property: Description("One of: mcq_question, activity, info_box, answer_key, noise.")]
        string Kind,
        [property: Description("Activity component family letter A-I when kind is activity; otherwise \"none\".")]
        string Component,
        [property: Description("When kind is activity: matrixChoice, slotFill, dragPlace, match or other; otherwise \"none\".")]
        string InteractionType,
        int X1, int Y1, int X2, int Y2);

    private sealed record OptionBoxes(List<OptionBox> Options);

    private sealed record OptionBox(string Label, int X1, int Y1, int X2, int Y2);
}
