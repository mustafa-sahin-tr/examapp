using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// İşaretleme ekranının Claude çağrıları için ortak parçalar: <see cref="IChatClient"/> kurulumu ve tek görsel + talimat →
/// yapılandırılmış (JSON şemalı) yanıt. <see cref="ClaudeLabelDetector"/> (#578 sayfa/şık) ve
/// <see cref="ClaudeActivityStructureExtractor"/> (#579 etkinlik yapısı) kullanır.
/// </summary>
internal static class LabelingClaude
{
    /// <summary>API anahtarı boşsa null (AI uçları 503 döner).</summary>
    public static IChatClient? CreateClient(LabelingAiOptions options, ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return null;

        // AsIChatClient: Anthropic SDK'nın MEAI köprüsü. Model sabitlenmez, istek başına ChatOptions.ModelId'den gelir.
        // UseLogging/UseOpenTelemetry: Aspire dashboard'unda istek/yanıt izleri (gen_ai.* span'leri).
        // MaxRetries (#1025): geçici hatalar SDK içinde üstel beklemeyle yeniden denenir.
        return new AnthropicClient { ApiKey = options.ApiKey, MaxRetries = options.MaxRetries }
            .AsIChatClient()
            .AsBuilder()
            .UseLogging(loggerFactory)
            .UseOpenTelemetry()
            .Build();
    }

    /// <summary>
    /// Görsel + talimat → <typeparamref name="T"/> (şema <c>GetResponseAsync&lt;T&gt;</c> ile). Yanıt kullanılamazsa
    /// <see cref="LabelingAiException"/>; yanıt geldiyse harcanan token istisnayla taşınır.
    /// </summary>
    public static async Task<(T Result, LabelingAiUsage Usage)> AskAsync<T>(
        IChatClient chat, LabelingAiOptions options, ILogger logger, LabelingAiModelOption model, int maxOutputTokens,
        string systemPrompt, EncodedRegion region, string instruction, CancellationToken ct)
    {
        // Görsel metinden önce: Claude görsel + talimat sırasında daha isabetli.
        List<ChatMessage> messages =
        [
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, [new DataContent(region.Jpeg, "image/jpeg"), new TextContent(instruction)]),
        ];
        return await CompleteAsync<T>(chat, options, logger, model, maxOutputTokens, messages, ct);
    }

    /// <summary>
    /// Birden çok görsel ve metin parçası → <typeparamref name="T"/> (#1021 kitap kurulumu: sayfa toplu taraması, döşemeli cevap
    /// anahtarı). Hata davranışı <see cref="AskAsync{T}"/> ile aynıdır.
    /// </summary>
    public static Task<(T Result, LabelingAiUsage Usage)> AskContentAsync<T>(
        IChatClient chat, LabelingAiOptions options, ILogger logger, LabelingAiModelOption model, int maxOutputTokens,
        string systemPrompt, IList<AIContent> content, CancellationToken ct) =>
        CompleteAsync<T>(chat, options, logger, model, maxOutputTokens,
            [new(ChatRole.System, systemPrompt), new(ChatRole.User, content)], ct);

    /// <summary>Görselsiz çağrı: talimat → <typeparamref name="T"/>. Hata davranışı <see cref="AskAsync{T}"/> ile aynıdır.</summary>
    public static Task<(T Result, LabelingAiUsage Usage)> AskTextAsync<T>(
        IChatClient chat, LabelingAiOptions options, ILogger logger, LabelingAiModelOption model, int maxOutputTokens,
        string systemPrompt, string instruction, CancellationToken ct) =>
        CompleteAsync<T>(chat, options, logger, model, maxOutputTokens,
            [new(ChatRole.System, systemPrompt), new(ChatRole.User, instruction)], ct);

    private static async Task<(T Result, LabelingAiUsage Usage)> CompleteAsync<T>(
        IChatClient chat, LabelingAiOptions options, ILogger logger, LabelingAiModelOption model, int maxOutputTokens,
        List<ChatMessage> messages, CancellationToken ct)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = model.Id,
            MaxOutputTokens = maxOutputTokens,
            Reasoning = ParseEffort(options.Effort) is { } effort ? new ReasoningOptions { Effort = effort } : null,
        };

        ChatResponse<T> response;
        try
        {
            response = await chat.GetResponseAsync<T>(messages, chatOptions, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[Labeling] Claude call failed (model {Model})", model.Id);
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
}
