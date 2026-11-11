using ExamApp.Api.Services.Labeling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Hints;

/// <summary>Issue #1113: Claude'a giden soru. Görsel varsa JPEG; metin her zaman (sınıflandırma metni: kapsam, yönerge, kök, şıklar).</summary>
public sealed record HintPrompt(EncodedRegion? Image, string? Text, string CorrectLetter, string? CorrectText, string? GradeName, string? SubjectName);

/// <summary><c>CannotHelp</c>: soru okunamadı ya da ipucu cevabı vermeden yazılamıyor.</summary>
public sealed record GeneratedHints(List<string> Steps, bool CannotHelp);

public interface IHintGenerator
{
    bool IsConfigured { get; }

    Task<(GeneratedHints Result, LabelingAiUsage Usage)> GenerateAsync(HintPrompt prompt, LabelingAiModelOption model, CancellationToken ct);
}

/// <summary>
/// Issue #1113: adım adım ipucu. Doğru şık modele verilir (ipucu doğru yola götürsün) ama yazılması yasaktır; sunucu ayrıca
/// <see cref="HintLeakGuard"/> ile denetler.
/// </summary>
public sealed class ClaudeHintGenerator : IHintGenerator, IDisposable
{
    /// <summary>Sistem mesajı ya da şema değişince artır: eski ipuçları yeniden üretilir (#578 kuralı).</summary>
    public const int SchemaRevision = 1;

    internal const int MaxOutputTokens = 2000;

    /// <summary>Bütçe ayırmak için üst tahmin: sistem mesajı + görsel + metin.</summary>
    internal const int WorstCaseInputTokens = 4000;

    internal const int MaxImageEdge = 1568;

    private const string SystemPrompt = """
        Türkiye'de bir öğrenciye, yanlış yaptığı ya da boş bıraktığı çoktan seçmeli soruda adım adım ipucu veriyorsun.

        Kurallar:
        - 2 ya da 3 adım yaz. Her adım bir öncekinden biraz daha fazla yol göstersin; ilk adım yalnız nereden başlanacağını söylesin.
        - Doğru şıkkı, harfini, doğru şıkkın metnini ya da sonucun kendisini (son sayı, son kelime) ASLA yazma. Hiçbir şıkkı ele de yazma.
        - Son adımda bile öğrenciye son işlemi kendisi yaptır.
        - Her adım en çok iki kısa cümle olsun, sınıf düzeyinin dilinde, Türkçe (yabancı dil sorusunda açıklama yine Türkçe).
        - Soru okunamıyorsa, eksikse ya da cevabı vermeden ipucu yazılamıyorsa cannotHelp true ve steps boş olsun.
        - Düz metin yaz; HTML, Markdown, LaTeX kullanma.
        """;

    private readonly IChatClient? _chat;
    private readonly LabelingAiOptions _options;
    private readonly ILogger<ClaudeHintGenerator> _logger;

    public ClaudeHintGenerator(IOptions<LabelingAiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeHintGenerator>();
        _chat = LabelingClaude.CreateClient(_options, loggerFactory);
    }

    public bool IsConfigured => _chat is not null;

    public Task<(GeneratedHints Result, LabelingAiUsage Usage)> GenerateAsync(HintPrompt prompt, LabelingAiModelOption model, CancellationToken ct)
    {
        if (_chat is null)
            throw new InvalidOperationException("Anthropic API key is not configured.");

        var instruction = Instruction(prompt);
        return prompt.Image is { } image
            ? LabelingClaude.AskAsync<GeneratedHints>(_chat, _options, _logger, model, MaxOutputTokens, SystemPrompt, image, instruction, ct)
            : LabelingClaude.AskTextAsync<GeneratedHints>(_chat, _options, _logger, model, MaxOutputTokens, SystemPrompt, instruction, ct);
    }

    internal static string Instruction(HintPrompt prompt) => $"""
        Sınıf: {prompt.GradeName ?? "belirtilmedi"}
        Ders: {prompt.SubjectName ?? "belirtilmedi"}
        {(prompt.Image is null ? "" : "Soru görseldedir.")}
        {(string.IsNullOrWhiteSpace(prompt.Text) ? "" : "Soru metni:\n" + prompt.Text)}

        Doğru şık (yalnız senin bilmen için, yazma): {prompt.CorrectLetter}{(prompt.CorrectText is { Length: > 0 } t ? " — " + t : "")}
        """;

    public void Dispose() => _chat?.Dispose();
}
