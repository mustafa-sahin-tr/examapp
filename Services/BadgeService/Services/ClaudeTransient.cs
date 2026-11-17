using System;
using System.Net;
using Anthropic.Exceptions;

namespace BadgeService.Services;

/// <summary>
/// Claude'un geçici hatası: hız sınırı (429), aşırı yük (529) ve diğer 5xx, bağlantı hatası (exam API'deki
/// <c>ClaudeTransient</c> ile aynı kural). İstisna zinciri gezilir.
/// </summary>
public static class ClaudeTransient
{
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
