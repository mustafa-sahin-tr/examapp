using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using ExamApp.Api.Models.Dtos.Labeling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

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

/// <summary>Claude yanıtı kullanılamadı (ret, eksik/bozuk JSON, API hatası) → 502.</summary>
public sealed class LabelingAiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Microsoft.Extensions.AI <see cref="IChatClient"/> üzerinden Claude ile soru/şık kutusu tespiti. İki aşama:
/// (1) tüm sayfa → soru kutuları, (2) her soru kırpması → şık kutuları (en fazla <c>MaxParallelCrops</c> eşzamanlı).
/// Görseller küçültülmüş uzayda gider; dönen koordinatlar <see cref="EncodedRegion.ToPage"/> ile orijinale çevrilir.
/// Model istek başına <see cref="ChatOptions.ModelId"/> ile seçilir; yanıt şeması <c>GetResponseAsync&lt;T&gt;</c> ile
/// JSON schema olarak gönderilir. Aşama talimatları ve kutu payları kullanıcı tarafından düzenlenir
/// (<see cref="LabelingPromptSettings"/>); sistem mesajı (koordinat kuralı) ve şema burada sabittir.
/// </summary>
public sealed class ClaudeLabelDetector : IClaudeLabelDetector, IDisposable
{
    // Düşünme token'ları da bu sınıra dahil; yanıt küçük bir JSON. Sınır istek başına maliyeti de sınırlar.
    private const int PageMaxOutputTokens = 8000;
    private const int CropMaxOutputTokens = 4000;

    // Soru kırpmasına eklenen kenar payı (orijinal piksel): kutu sınırdaki şık harfini kesmesin.
    private const int CropPadding = 12;

    private const string SystemPrompt =
        "You locate regions on scanned pages of Turkish school test books for an object-detection training set. " +
        "Coordinates are integer pixels of the image you are given, origin at the top-left corner; " +
        "(x1, y1) is the top-left and (x2, y2) the bottom-right corner of a box. Boxes are tight around the ink " +
        "they enclose. Report only what is visible; never invent regions.";

    private readonly IChatClient? _chat;
    private readonly LabelingAiOptions _options;
    private readonly ILogger<ClaudeLabelDetector> _logger;

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
        var (questions, pageUsage) = await AskAsync<QuestionBoxes>(model, PageMaxOutputTokens,
            page,
            LabelingPromptSettings.Render(prompt.QuestionInstruction, page.Width, page.Height),
            ct);

        var questionRects = (questions.Questions ?? [])
            .Select(q => page.ToPage(q.X1, q.Y1, q.X2, q.Y2))
            .Where(r => r.Width > 4 && r.Height > 4)
            .Take(_options.MaxQuestionsPerPage)
            .ToList();

        var pageBounds = new Rectangle(0, 0, image.Width, image.Height);
        long inputTokens = pageUsage.Input;
        long outputTokens = pageUsage.Output;
        using var gate = new SemaphoreSlim(_options.MaxParallelCrops);

        var predictions = await Task.WhenAll(questionRects.Select(async rect =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var padded = Rectangle.Inflate(rect, CropPadding, CropPadding);
                var crop = image.Encode(_options.MaxImageEdge, padded);
                OptionBoxes options;
                (long Input, long Output) usage;
                try
                {
                    (options, usage) = await AskAsync<OptionBoxes>(model, CropMaxOutputTokens,
                    crop,
                    LabelingPromptSettings.Render(prompt.OptionInstruction, crop.Width, crop.Height),
                    ct);
                }
                catch (LabelingAiException ex)
                {
                    // Tek soru yüzünden ödenmiş sayfa çöpe gitmesin: soru şıksız kalır, kullanıcı şıkları elle çizer.
                    _logger.LogWarning(ex, "[Labeling] option detection failed for one question (model {Model})", model.Id);
                    options = new OptionBoxes([]);
                    usage = (0, 0);
                }
                Interlocked.Add(ref inputTokens, usage.Input);
                Interlocked.Add(ref outputTokens, usage.Output);

                var optionRects = (options.Options ?? [])
                    .Select(o => crop.ToPage(o.X1, o.Y1, o.X2, o.Y2))
                    .Where(r => r.Width > 2 && r.Height > 2)
                    .Select(r => Rectangle.Intersect(Rectangle.Inflate(r, prompt.OptionPadding, prompt.OptionPadding), pageBounds))
                    .ToList();
                var question = Rectangle.Intersect(
                    Rectangle.Inflate(rect, prompt.QuestionPadding, prompt.QuestionPadding), pageBounds);
                // Eğitim verisinde şıklar sorunun içinde olmalı: istenirse soru kutusu şıkların birleşimi kadar genişler.
                if (prompt.ExpandQuestionToOptions)
                    question = optionRects.Aggregate(question, Rectangle.Union);

                return new PredictionDto
                {
                    ClassId = 0,
                    X = question.X,
                    Y = question.Y,
                    Width = question.Width,
                    Height = question.Height,
                    Subpredictions = optionRects
                        .Select(r => new PredictionDto { ClassId = 0, X = r.X, Y = r.Y, Width = r.Width, Height = r.Height })
                        .ToList(),
                };
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

    private async Task<(T Result, (long Input, long Output) Usage)> AskAsync<T>(
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

        var usage = (response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
        if (response.FinishReason == ChatFinishReason.ContentFilter)
            throw new LabelingAiException("Claude declined the request.");
        if (response.FinishReason == ChatFinishReason.Length)
            throw new LabelingAiException("Claude response was truncated.");
        if (!response.TryGetResult(out var result) || result is null)
            throw new LabelingAiException("Claude response did not match the schema.");

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

    // Yanıt şemaları — GetResponseAsync<T> bunlardan JSON schema üretir.
    private sealed record QuestionBoxes(List<Box> Questions);

    private sealed record Box(int X1, int Y1, int X2, int Y2);

    private sealed record OptionBoxes(List<OptionBox> Options);

    private sealed record OptionBox(string Label, int X1, int Y1, int X2, int Y2);
}
