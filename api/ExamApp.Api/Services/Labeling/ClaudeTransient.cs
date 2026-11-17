using System;
using System.Net;
using Anthropic.Exceptions;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Claude'un geçici hatası: hız sınırı (429), aşırı yük (529) ve diğer 5xx, bağlantı hatası. SDK bunları kendi içinde birkaç kez
/// yeniden dener (<see cref="LabelingAiOptions.MaxRetries"/>); o da tükenince çağıran işi başarısız saymak yerine bekleyip
/// sürdürebilir. İstisna zinciri gezilir (<see cref="LabelingAiException"/> SDK istisnasını içte taşır).
/// </summary>
public static class ClaudeTransient
{
    /// <summary>Geçici hatada işin yeniden deneneceği süre (SDK yanıtın Retry-After başlığını dışa vermiyor).</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    public static bool Is(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is AnthropicRateLimitException or Anthropic5xxException or AnthropicIOException)
                return true;
            if (ex is AnthropicApiException { StatusCode: HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError })
                return true;
        }

        return false;
    }
}
