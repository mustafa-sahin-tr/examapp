using System;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Services.Interactions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Labeling;

/// <summary>Issue #579: tek etkinlik bloğu → taslak InteractionPlan v1 (Claude).</summary>
public interface IActivityStructureExtractor
{
    /// <summary>API anahtarı yapılandırılmış mı; değilse canlı çağrı gereken istek 503 döner.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// <paramref name="block"/> (sayfa pikseli, sayfanın içinde) kırpılıp <paramref name="prompt"/>'un tipinin talimatı + şemasıyla
    /// Claude'a gönderilir. Yanıt kullanılamazsa <see cref="LabelingAiException"/> (ödenmiş token <c>Usage</c>'da).
    /// </summary>
    Task<ActivityExtraction> ExtractAsync(
        LabelingImage image, Rectangle block, ActivityPromptSettings prompt, LabelingAiModelOption model,
        CancellationToken ct = default);
}

/// <summary>Plan (JSON, kutular bloğa göre göreli), basılı yönerge ve çağrının token'ı.</summary>
public sealed record ActivityExtraction(string PlanJson, string? Instruction, int InputTokens, int OutputTokens);

/// <summary>
/// Issue #579: blok başına bir Claude çağrısı; talimat ve yanıt şeması bloğun tipinden seçilir (<see cref="ActivityDrafts"/>).
/// Kırpma = blok kutusu + <see cref="CropPadding"/> (sayfaya kırpılır), uzun kenarı <c>Anthropic:MaxImageEdge</c>'e küçültülür;
/// dönen koordinatlar sayfa uzayına çevrilip blok kutusuna göre göreli yapılır. Sistem mesajı ve kırpma payı kodda sabit:
/// değişirse <see cref="ActivityStructureSettings.SchemaRevision"/>'ı artırın.
/// </summary>
public sealed class ClaudeActivityStructureExtractor : IActivityStructureExtractor, IDisposable
{
    // Düşünme token'ları da dahil; 60 satırlık matris (satır başına birkaç hücre kutusu) sığsın.
    internal const int MaxOutputTokens = 8000;

    // Blok kutusuna eklenen kenar payı (orijinal piksel): kutu sınırındaki boşluk/kart kesilmesin. Pay dışına düşen kutular
    // blok kutusuna kırpılır (plan blok uzayında kalır).
    internal const int CropPadding = 16;

    // Bütçe rezervasyonu (review M2): talimat (≤ 4000 karakter), sistem mesajı ve şema için girdi token payı.
    internal const int PromptTokenAllowance = 3000;

    /// <summary>
    /// Bir blok çağrısının en kötü durum girdi token'ı: gönderilecek kırpmanın görsel token'ı (Claude: genişlik × yükseklik / 750,
    /// uzun kenar <paramref name="maxImageEdge"/>'e küçültülmüş) + <see cref="PromptTokenAllowance"/>. Çıktı üst sınırı
    /// <see cref="MaxOutputTokens"/>.
    /// </summary>
    internal static long WorstCaseInputTokens(Rectangle block, Size page, int maxImageEdge)
    {
        var crop = Rectangle.Intersect(Rectangle.Inflate(block, CropPadding, CropPadding), new Rectangle(Point.Empty, page));
        var longEdge = Math.Max(crop.Width, crop.Height);
        var scale = longEdge > maxImageEdge ? (double)maxImageEdge / longEdge : 1d;
        var pixels = Math.Ceiling(crop.Width * scale) * Math.Ceiling(crop.Height * scale);
        return (long)Math.Ceiling(pixels / 750d) + PromptTokenAllowance;
    }

    private const string SystemPrompt =
        "You read the structure of interactive exercises on scanned pages of Turkish school books, so that a teacher " +
        "can turn them into digital activities. Coordinates are integer pixels of the image you are given, origin at " +
        "the top-left corner; (x1, y1) is the top-left and (x2, y2) the bottom-right corner of a box. Boxes are tight " +
        "around what they enclose. Report only what is visible; never invent items, and never answer the exercise.";

    private readonly IChatClient? _chat;
    private readonly LabelingAiOptions _options;
    private readonly ILogger<ClaudeActivityStructureExtractor> _logger;

    /// <summary>Test dikişi: ağa çıkmayan sahte <see cref="IChatClient"/> ile (DI yalnız public kurucuyu görür).</summary>
    internal ClaudeActivityStructureExtractor(
        IChatClient chat, IOptions<LabelingAiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeActivityStructureExtractor>();
        _chat = chat;
    }

    public ClaudeActivityStructureExtractor(IOptions<LabelingAiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeActivityStructureExtractor>();
        _chat = LabelingClaude.CreateClient(_options, loggerFactory);
    }

    public bool IsConfigured => _chat is not null;

    public async Task<ActivityExtraction> ExtractAsync(
        LabelingImage image, Rectangle block, ActivityPromptSettings prompt, LabelingAiModelOption model,
        CancellationToken ct = default)
    {
        if (_chat is null)
            throw new InvalidOperationException("Anthropic API key is not configured.");

        var page = new Rectangle(0, 0, image.Width, image.Height);
        var crop = image.Encode(_options.MaxImageEdge, Rectangle.Intersect(Rectangle.Inflate(block, CropPadding, CropPadding), page));
        var frame = new ActivityDrafts.BlockFrame(crop, Rectangle.Intersect(block, page));
        var instruction = LabelingPromptSettings.Render(prompt.Instruction, crop.Width, crop.Height);

        ActivityDrafts.Mapped mapped;
        LabelingAiUsage usage;
        switch (prompt.InteractionType)
        {
            case MatrixChoiceGrader.Type:
            {
                var (draft, u) = await AskAsync<ActivityDrafts.MatrixDraft>(model, crop, instruction, ct);
                (mapped, usage) = (ActivityDrafts.Map(draft, frame), u);
                break;
            }
            case SlotFillGrader.Type:
            {
                var (draft, u) = await AskAsync<ActivityDrafts.SlotDraft>(model, crop, instruction, ct);
                (mapped, usage) = (ActivityDrafts.Map(draft, frame), u);
                break;
            }
            case DragPlaceGrader.Type:
            {
                var (draft, u) = await AskAsync<ActivityDrafts.DragDraft>(model, crop, instruction, ct);
                (mapped, usage) = (ActivityDrafts.Map(draft, frame), u);
                break;
            }
            case MatchGrader.Type:
            {
                var (draft, u) = await AskAsync<ActivityDrafts.MatchDraft>(model, crop, instruction, ct);
                (mapped, usage) = (ActivityDrafts.Map(draft, frame), u);
                break;
            }
            case TextSelectGrader.Type: // issue #582: satır düzeyi yanıt; token'lar sunucuda
            {
                var (draft, u) = await AskAsync<ActivityDrafts.TextDraft>(model, crop, instruction, ct);
                (mapped, usage) = (ActivityDrafts.Map(draft, frame), u);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(prompt), prompt.InteractionType, "Unsupported activity type.");
        }

        return new ActivityExtraction(
            mapped.Plan.ToJsonString(),
            mapped.Instruction,
            (int)Math.Min(usage.Input, int.MaxValue),
            (int)Math.Min(usage.Output, int.MaxValue));
    }

    private Task<(T Result, LabelingAiUsage Usage)> AskAsync<T>(
        LabelingAiModelOption model, EncodedRegion region, string instruction, CancellationToken ct) =>
        LabelingClaude.AskAsync<T>(_chat!, _options, _logger, model, MaxOutputTokens, SystemPrompt, region, instruction, ct);

    public void Dispose() => _chat?.Dispose();
}
