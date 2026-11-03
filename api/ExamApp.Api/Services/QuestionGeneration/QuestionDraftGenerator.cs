using ExamApp.Api.Services.Labeling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.QuestionGeneration;

public interface IQuestionDraftGenerator
{
    bool IsConfigured { get; }

    Task<(GeneratedQuestionSet Result, LabelingAiUsage Usage)> GenerateAsync(QuestionDraftPrompt prompt, LabelingAiModelOption model, CancellationToken ct);
}

public sealed record QuestionDraftPrompt(string OutcomeCode, string OutcomeDescription, string GradeName, string? SubjectName, int Count, int OptionCount, int DifficultyLevel);

public sealed record GeneratedQuestion(string Text, List<string> Options, int CorrectOptionIndex, string Explanation);

public sealed record GeneratedQuestionSet(List<GeneratedQuestion> Questions);

/// <summary>Kazanımdan görselsiz çoktan seçmeli soru taslağı. Taslak kaydedilmez: öğretmen ya da admin inceleyip onaylar.</summary>
public sealed class ClaudeQuestionDraftGenerator : IQuestionDraftGenerator, IDisposable
{
    internal const int MaxOutputTokens = 8000;

    /// <summary>Bütçe ayırmak için üst tahmin: sistem mesajı + kazanım.</summary>
    internal const int WorstCaseInputTokens = 2000;

    private const string SystemPrompt = """
        Türkiye'de MEB müfredatına uygun çoktan seçmeli soru yazıyorsun. Sorular öğretmen incelemesinden geçtikten sonra öğrenciye gider.

        Her soru için:
        - Verilen kazanımı doğrudan ölçsün, sınıf düzeyinin dilinde ve bilgisinde olsun.
        - Tek başına anlaşılsın: görsel, şekil, tablo, grafik, harita ya da dış kaynak gerektirmesin ve bunlara atıf yapmasın.
        - Tam olarak istenen sayıda şık olsun ve yalnız biri savunulabilir biçimde doğru olsun.
        - Şıklar benzer uzunlukta ve inandırıcı olsun; çeldiriciler öğrencilerin gerçek yanılgılarından gelsin. "Hepsi", "hiçbiri" gibi şık kullanma.
        - Şık metnine harf ya da numara ekleme; düz metin yaz, HTML kullanma.
        - Sorular birbirini tekrar etmesin.
        - Doğruluğundan emin olmadığın bilgiye dayanan soru yazma: bu durumda daha az soru üret.

        Soruyu kazanımın dilinde yaz: yabancı dil dersinde hedef dilde, diğer derslerde Türkçe. Gerekçeyi her zaman Türkçe yaz, öğretmen okuyacak.
        """;

    private readonly IChatClient? _chat;
    private readonly LabelingAiOptions _options;
    private readonly ILogger<ClaudeQuestionDraftGenerator> _logger;

    /// <summary>Test dikişi: ağa çıkmayan sahte <see cref="IChatClient"/> ile (DI yalnız public kurucuyu görür).</summary>
    internal ClaudeQuestionDraftGenerator(IChatClient chat, IOptions<LabelingAiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeQuestionDraftGenerator>();
        _chat = chat;
    }

    public ClaudeQuestionDraftGenerator(IOptions<LabelingAiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<ClaudeQuestionDraftGenerator>();
        _chat = LabelingClaude.CreateClient(_options, loggerFactory);
    }

    public bool IsConfigured => _chat is not null;

    public Task<(GeneratedQuestionSet Result, LabelingAiUsage Usage)> GenerateAsync(QuestionDraftPrompt prompt, LabelingAiModelOption model, CancellationToken ct)
    {
        if (_chat is null)
            throw new InvalidOperationException("Anthropic API key is not configured.");

        return LabelingClaude.AskTextAsync<GeneratedQuestionSet>(_chat, _options, _logger, model, MaxOutputTokens, SystemPrompt, Instruction(prompt), ct);
    }

    internal static string Instruction(QuestionDraftPrompt prompt) => $"""
        Sınıf: {prompt.GradeName}
        Ders: {prompt.SubjectName ?? "belirtilmedi"}
        Kazanım kodu: {prompt.OutcomeCode}
        Kazanım: {prompt.OutcomeDescription}
        Zorluk (1 kolay, 10 çok zor): {prompt.DifficultyLevel}

        {prompt.Count} soru yaz, her soruda {prompt.OptionCount} şık olsun. correctOptionIndex doğru şıkkın 0'dan başlayan sırasıdır.
        """;

    public void Dispose() => _chat?.Dispose();
}
