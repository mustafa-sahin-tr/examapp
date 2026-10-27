using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Services.Interactions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Labeling;

/// <summary>Issue #580: eşlenmiş cevap anahtarı maddesi; <see cref="Box"/> anahtar sayfasının pikseli (yalnız görsel cevapta).</summary>
public sealed record AnswerKeyReadEntry(
    int? PrintedPage, string? ActivityLabel, string? ItemLabel, string? AnswerText, bool Visual, Rectangle? Box);

/// <summary>Issue #580: sayfanın okunan maddeleri ve çağrının token'ı.</summary>
public sealed record AnswerKeyReading(IReadOnlyList<AnswerKeyReadEntry> Entries, int InputTokens, int OutputTokens);

/// <summary>Issue #580: çözüm girdisi olarak bir anahtar maddesi — metni ve (görsel cevapta) saklanan anahtar kırpması.</summary>
public sealed record AnswerKeyInput(
    int? PrintedPage, string? ActivityLabel, string? ItemLabel, string? AnswerText, byte[]? CropJpeg);

/// <summary>Issue #580: tipin çözüm biçiminde çözüm (taslak id'leri) ve çağrının token'ı.</summary>
public sealed record ActivitySolving(string SolutionJson, int InputTokens, int OutputTokens);

/// <summary>Issue #580: cevap anahtarı okuma ve etkinlik çözümü (Claude).</summary>
public interface IActivitySolutionAi
{
    /// <summary>API anahtarı yapılandırılmış mı; değilse canlı çağrı gereken istek 503 döner.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Anahtar sayfasının tamamı <paramref name="prompt"/> (<c>answerKey</c>) talimatıyla okunur. Yanıt kullanılamazsa
    /// <see cref="LabelingAiException"/> (ödenmiş token <c>Usage</c>'da).
    /// </summary>
    Task<AnswerKeyReading> ReadAnswerKeyAsync(
        LabelingImage page, ActivityPromptSettings prompt, LabelingAiModelOption model, CancellationToken ct = default);

    /// <summary>
    /// Etkinlik kırpması (+ anahtar maddeleri ve görsel kırpmaları, <c>solveFromKey</c>) → tipin çözümü. <paramref name="planJson"/>
    /// taslak plandır (taslak id'leri). Yanıt kullanılamazsa <see cref="LabelingAiException"/>.
    /// </summary>
    Task<ActivitySolving> SolveAsync(
        LabelingImage page, Rectangle block, string interactionType, string planJson, ActivityPromptSettings prompt,
        IReadOnlyList<AnswerKeyInput> key, LabelingAiModelOption model, CancellationToken ct = default);
}

/// <summary>
/// Issue #580: Claude ile cevap anahtarı okuma ve tipe göre etkinlik çözümü. Sistem mesajları, anahtar şeması, çözüm şemaları
/// (<see cref="ActivitySolutionDrafts"/>), talimata eklenen tip açıklaması ve yapı biçimi kodda sabittir: değişirse
/// <see cref="ActivityStructureSettings"/> revizyonlarını artırın.
/// </summary>
public sealed class ClaudeActivitySolutionAi : IActivitySolutionAi, IDisposable
{
    // Anahtar sayfasında yüzlerce madde olabilir (madde başına ~30 token + düşünme).
    internal const int AnswerKeyMaxOutputTokens = 16000;

    internal const int SolveMaxOutputTokens = 4000;

    // Anahtar sayfası bütçe rezervasyonu: talimat (≤ 4000 karakter), sistem mesajı ve şema için girdi token payı.
    internal const int PromptTokenAllowance = 3000;

    /// <summary>Bir çözüm çağrısına eklenebilecek en çok anahtar görseli.</summary>
    public const int MaxKeyImages = 8;

    private const string AnswerKeySystemPrompt =
        "You read the answer keys printed at the back of Turkish school books, so that each answer can be linked to its " +
        "activity. Coordinates are integer pixels of the image you are given, origin at the top-left corner; (x1, y1) is the " +
        "top-left and (x2, y2) the bottom-right corner of a box. Copy what is printed; never invent or correct answers.";

    private const string SolveSystemPrompt =
        "You fill in the solutions of interactive exercises from Turkish school books, so that a teacher can review them. " +
        "The structure of the exercise is given as JSON with ids; answer only with ids from that structure. Never invent " +
        "items that are not in the structure.";

    private readonly IChatClient? _chat;
    private readonly LabelingAiOptions _options;
    private readonly ILogger<ClaudeActivitySolutionAi> _logger;

    /// <summary>Test dikişi: ağa çıkmayan sahte <see cref="IChatClient"/> ile.</summary>
    internal ClaudeActivitySolutionAi(IChatClient chat, IOptions<LabelingAiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeActivitySolutionAi>();
        _chat = chat;
    }

    public ClaudeActivitySolutionAi(IOptions<LabelingAiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeActivitySolutionAi>();
        _chat = LabelingClaude.CreateClient(_options, loggerFactory);
    }

    public bool IsConfigured => _chat is not null;

    /// <summary>Anahtar sayfası çağrısının en kötü durum girdi token'ı (sayfa görseli + pay).</summary>
    internal static long WorstCaseAnswerKeyInputTokens(Size page, int maxImageEdge) =>
        ImageTokens(page, maxImageEdge) + PromptTokenAllowance;

    // Çözüm çağrısında metin dışı sabit pay: sistem mesajı, tip açıklaması, şema ve mesaj yapısı (token).
    internal const int SolveFixedTokenAllowance = 1500;

    /// <summary>
    /// Çözüm çağrısının en kötü durum girdi token'ı (review D-1): etkinlik kırpması + anahtar kırpmaları + metin (talimat, yapı JSON'u,
    /// anahtar metinleri; karakter / 3 — Türkçe metin ve JSON için karamsar) + sabit pay.
    /// </summary>
    internal static long WorstCaseSolveInputTokens(
        Rectangle block, Size page, int maxImageEdge, IEnumerable<Size> keyCrops, long textChars)
    {
        var crop = Rectangle.Intersect(Rectangle.Inflate(block, ClaudeActivityStructureExtractor.CropPadding,
            ClaudeActivityStructureExtractor.CropPadding), new Rectangle(Point.Empty, page));
        return ImageTokens(crop.Size, maxImageEdge) + keyCrops.Sum(s => ImageTokens(s, maxImageEdge))
               + (long)Math.Ceiling(Math.Max(0, textChars) / 3d) + SolveFixedTokenAllowance;
    }

    private static long ImageTokens(Size size, int maxImageEdge)
    {
        var longEdge = Math.Max(size.Width, size.Height);
        if (longEdge <= 0)
            return 0;
        var scale = longEdge > maxImageEdge ? (double)maxImageEdge / longEdge : 1d;
        var pixels = Math.Ceiling(size.Width * scale) * Math.Ceiling(size.Height * scale);
        return (long)Math.Ceiling(pixels / 750d);
    }

    public async Task<AnswerKeyReading> ReadAnswerKeyAsync(
        LabelingImage page, ActivityPromptSettings prompt, LabelingAiModelOption model, CancellationToken ct = default)
    {
        var chat = _chat ?? throw new InvalidOperationException("Anthropic API key is not configured.");
        var region = page.Encode(_options.MaxImageEdge);
        var instruction = LabelingPromptSettings.Render(prompt.Instruction, region.Width, region.Height);
        var (draft, usage) = await LabelingClaude.AskAsync<ActivitySolutionDrafts.AnswerKeyDraft>(
            chat, _options, _logger, model, AnswerKeyMaxOutputTokens, AnswerKeySystemPrompt, region, instruction, ct);
        var entries = ActivitySolutionDrafts.Map(draft, region, new Size(page.Width, page.Height));
        return new AnswerKeyReading(entries, Clamp(usage.Input), Clamp(usage.Output));
    }

    public async Task<ActivitySolving> SolveAsync(
        LabelingImage page, Rectangle block, string interactionType, string planJson, ActivityPromptSettings prompt,
        IReadOnlyList<AnswerKeyInput> key, LabelingAiModelOption model, CancellationToken ct = default)
    {
        var chat = _chat ?? throw new InvalidOperationException("Anthropic API key is not configured.");
        var bounds = new Rectangle(0, 0, page.Width, page.Height);
        var crop = page.Encode(_options.MaxImageEdge, Rectangle.Intersect(Rectangle.Inflate(block,
            ClaudeActivityStructureExtractor.CropPadding, ClaudeActivityStructureExtractor.CropPadding), bounds));

        var text = new StringBuilder(LabelingPromptSettings.Render(prompt.Instruction, crop.Width, crop.Height));
        text.Append("\n\n").Append(ActivitySolutionDrafts.TypeExplanation(interactionType));
        text.Append("\n\nStructure:\n").Append(ActivitySolutionDrafts.Structure(planJson));

        List<AIContent> contents = [new DataContent(crop.Jpeg, "image/jpeg")];
        if (key.Count > 0)
        {
            text.Append("\n\nAnswer key:");
            var picture = 0;
            foreach (var entry in key)
            {
                text.Append("\n- ").Append(Describe(entry));
                if (entry.CropJpeg is { Length: > 0 } jpeg)
                {
                    picture++;
                    text.Append(" [answer picture ").Append(picture).Append(']');
                    contents.Add(new DataContent(jpeg, "image/jpeg"));
                }
            }
        }
        contents.Add(new TextContent(text.ToString()));

        var plan = JsonNode.Parse(planJson) as JsonObject ?? new JsonObject();
        JsonObject solution;
        LabelingAiUsage usage;
        switch (interactionType)
        {
            case MatrixChoiceGrader.Type:
            {
                var (draft, u) = await AskAsync<ActivitySolutionDrafts.MatrixSolutionDraft>(chat, model, contents, ct);
                (solution, usage) = (ActivitySolutionDrafts.Map(draft, plan), u);
                break;
            }
            case SlotFillGrader.Type:
            {
                var (draft, u) = await AskAsync<ActivitySolutionDrafts.SlotSolutionDraft>(chat, model, contents, ct);
                (solution, usage) = (ActivitySolutionDrafts.Map(draft, plan), u);
                break;
            }
            case DragPlaceGrader.Type:
            {
                var (draft, u) = await AskAsync<ActivitySolutionDrafts.DragSolutionDraft>(chat, model, contents, ct);
                (solution, usage) = (ActivitySolutionDrafts.Map(draft, plan), u);
                break;
            }
            case MatchGrader.Type:
            {
                var (draft, u) = await AskAsync<ActivitySolutionDrafts.MatchSolutionDraft>(chat, model, contents, ct);
                (solution, usage) = (ActivitySolutionDrafts.Map(draft, plan), u);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(interactionType), interactionType, "Unsupported activity type.");
        }

        return new ActivitySolving(solution.ToJsonString(), Clamp(usage.Input), Clamp(usage.Output));
    }

    /// <summary>"page 12 · Etkinlik 3 · item a: 3/4" — anahtar maddesinin talimattaki satırı.</summary>
    internal static string Describe(AnswerKeyInput entry)
    {
        var parts = new List<string>(3);
        if (entry.PrintedPage is { } p)
            parts.Add($"page {p}");
        if (!string.IsNullOrWhiteSpace(entry.ActivityLabel))
            parts.Add(entry.ActivityLabel!);
        if (!string.IsNullOrWhiteSpace(entry.ItemLabel))
            parts.Add($"item {entry.ItemLabel}");
        var head = parts.Count > 0 ? string.Join(" · ", parts) : "answer";
        return string.IsNullOrWhiteSpace(entry.AnswerText) ? head : $"{head}: {entry.AnswerText}";
    }

    private Task<(T Result, LabelingAiUsage Usage)> AskAsync<T>(
        IChatClient chat, LabelingAiModelOption model, IList<AIContent> contents, CancellationToken ct) =>
        LabelingClaude.AskAsync<T>(chat, _options, _logger, model, SolveMaxOutputTokens, SolveSystemPrompt, contents, ct);

    private static int Clamp(long value) => (int)Math.Min(value, int.MaxValue);

    public void Dispose() => _chat?.Dispose();
}
