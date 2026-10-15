using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ExamApp.Api.Services.Parents;

/// <summary>Issue #440: yeniden gönderim kararı.</summary>
public sealed record ParentResendDecision(bool Allowed, DateTime? NextResendAt, int ResendsLeft);

/// <summary>
/// Issue #440: davet gönderim / e-posta değiştirme sınırlarının saf hesapları (servis, durum DTO'su ve testler aynı kuralı kullanır).
/// <list type="bullet">
/// <item>Aynı öğrenci + e-posta çiftine: iki gönderim arasında en az 24 saat; ilk gönderimden sonra en fazla 5 yeniden gönderim (kaç davet
/// satırına bölünmüş olursa olsun — A→B→A döngüsü sınırı aşamaz).</item>
/// <item>Son 1 saatte 3 farklı e-posta denendiyse 4. farklı e-posta kilitli; kilit, en eski denemelerin pencereden düşüp 2 farklı e-posta
/// kalacağı ana kadar sürer. Daha önce denenmiş e-postayı yeniden göndermek kilide takılmaz.</item>
/// </list>
/// </summary>
public static class ParentInvitationLimits
{
    /// <param name="totalSends">Çifte yapılmış toplam gönderim (0 = hiç).</param>
    public static ParentResendDecision Resend(int totalSends, DateTime? lastSentAt, DateTime now)
    {
        if (totalSends <= 0 || lastSentAt == null)
            return new ParentResendDecision(true, null, ParentInvitationRules.MaxResends);

        var resendsLeft = Math.Max(0, ParentInvitationRules.MaxResends - (totalSends - 1));
        var next = DateTime.SpecifyKind(lastSentAt.Value + ParentInvitationRules.ResendInterval, DateTimeKind.Utc);
        if (resendsLeft == 0)
            return new ParentResendDecision(false, null, 0);
        return new ParentResendDecision(now >= next, next, resendsLeft);
    }

    /// <summary>
    /// Yeni e-posta (<paramref name="emailHash"/>) için kilit bitişi; kilit yoksa null. <paramref name="emailHash"/> null ise "yeni
    /// herhangi bir e-posta kilitli mi" (öğrenci paneli gösterimi).
    /// </summary>
    public static DateTime? EmailChangeLockedUntil(IEnumerable<ParentEmailAttemptFact> attempts, string? emailHash, DateTime now)
    {
        var windowStart = now - ParentInvitationRules.EmailChangeWindow;
        var latestByEmail = attempts
            .Where(a => a.At > windowStart)
            .GroupBy(a => a.EmailHash, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(a => a.At), StringComparer.Ordinal);

        if (emailHash != null && latestByEmail.ContainsKey(emailHash))
            return null;
        if (latestByEmail.Count < ParentInvitationRules.MaxDistinctEmails)
            return null;

        var pivot = latestByEmail.Values.OrderByDescending(t => t).ElementAt(ParentInvitationRules.MaxDistinctEmails - 1);
        return DateTime.SpecifyKind(pivot + ParentInvitationRules.EmailChangeWindow, DateTimeKind.Utc);
    }

    /// <summary>Normalize edilmiş e-postanın SHA-256 hex hash'i (deneme kaydı — düz e-posta saklanmaz).</summary>
    public static string EmailHash(string normalizedEmail)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail))).ToLowerInvariant();
}

/// <summary>Issue #440: aktivasyon token'ı — 32 rastgele bayt (base64url); yalnız SHA-256 hash'i saklanır.</summary>
public static class ParentActivationTokens
{
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(ParentInvitationRules.TokenBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Token → SHA-256 hex. Biçimsiz/çok uzun girdi null (sorgu yapılmaz).</summary>
    public static string? Hash(string? token)
    {
        var value = token?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length < 20 || value.Length > 128)
            return null;
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return null;
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value))).ToLowerInvariant();
    }
}
