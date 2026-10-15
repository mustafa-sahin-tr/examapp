using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Foundation.Security;

/// <summary>
/// Issues #440/#441 review: veli aktivasyon bağlantısındaki tek kullanımlık token outbox satırında (OutboxMessages.Content),
/// RabbitMQ mesajında ve error kuyruğunda DÜZ METİN kalmasın diye exam API (Protect) ve BadgeService (Unprotect) arasında
/// paylaşılan simetrik şifreleme. AES-256-GCM; biçim <c>v1.&lt;nonce&gt;.&lt;ciphertext+tag&gt;</c> (base64url). Anahtar:
/// <c>ParentInvitations:LinkEncryptionKey</c> (base64, tam 32 bayt); yalnız exam API ve BadgeService tutar.
/// Bozulmuş/yanlış anahtarlı girdi <see cref="CryptographicException"/> fırlatır (sessiz kabul yok).
/// </summary>
public sealed class ActivationLinkProtector
{
    public const string ConfigKey = "ParentInvitations:LinkEncryptionKey";
    private const string Version = "v1";
    private const string DevOnlyPrefix = "devOnly";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>Development dev anahtarı (base64 of ASCII "devOnlyParentInvitationLinkKey!!"); Development dışında reddedilir.</summary>
    public const string DevKeyBase64 = "ZGV2T25seVBhcmVudEludml0YXRpb25MaW5rS2V5ISE=";

    private readonly byte[] _key;

    public ActivationLinkProtector(byte[] key)
    {
        if (key is null || key.Length != KeySize)
            throw new ArgumentException($"Activation link key must be exactly {KeySize} bytes.", nameof(key));
        _key = (byte[])key.Clone();
    }

    /// <summary>
    /// Anahtarı çözer ve doğrular. Development'ta boşsa dev anahtarına düşer; Development dışında boş, base64 olmayan, 32 bayt olmayan
    /// ya da <c>devOnly</c> önekli (çözülmüş bayt ya da ham metin) anahtar <see cref="InvalidOperationException"/> ile açılışı durdurur.
    /// </summary>
    public static byte[] ResolveKey(string? configured, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (isDevelopment)
                return Convert.FromBase64String(DevKeyBase64);
            throw new InvalidOperationException(
                $"{ConfigKey} ayarlanmamış. Development dışında 32 baytlık base64 bir secret zorunludur (ParentInvitations__LinkEncryptionKey).");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(configured.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{ConfigKey} geçerli base64 değil.");
        }

        if (bytes.Length != KeySize)
            throw new InvalidOperationException($"{ConfigKey} base64 çözüldüğünde tam {KeySize} bayt olmalı (şu an {bytes.Length}).");

        if (!isDevelopment && (configured.TrimStart().StartsWith(DevOnlyPrefix, StringComparison.Ordinal)
            || Encoding.ASCII.GetString(bytes).StartsWith(DevOnlyPrefix, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"{ConfigKey} hâlâ dev-only değer. Development dışındaki ortamlarda gerçek bir secret ayarlanmalıdır.");

        return bytes;
    }

    /// <summary>Yapılandırmadan koruyucuyu kurar (kayıt sırasında çağrılır → fail-fast).</summary>
    public static ActivationLinkProtector FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
        => new(ResolveKey(configuration[ConfigKey], environment.IsDevelopment()));

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using (var gcm = new AesGcm(_key, TagSize))
            gcm.Encrypt(nonce, data, cipher, tag, Encoding.ASCII.GetBytes(Version));

        var blob = new byte[cipher.Length + TagSize];
        Buffer.BlockCopy(cipher, 0, blob, 0, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, cipher.Length, TagSize);
        return $"{Version}.{ToBase64Url(nonce)}.{ToBase64Url(blob)}";
    }

    /// <exception cref="CryptographicException">Biçim, anahtar ya da içerik geçersiz/bozulmuş.</exception>
    public string Unprotect(string protectedValue)
    {
        try
        {
            var parts = (protectedValue ?? string.Empty).Split('.');
            if (parts.Length != 3 || parts[0] != Version)
                throw new CryptographicException("Unsupported activation link format.");

            var nonce = FromBase64Url(parts[1]);
            var blob = FromBase64Url(parts[2]);
            if (nonce.Length != NonceSize || blob.Length < TagSize)
                throw new CryptographicException("Malformed activation link payload.");

            var cipher = blob.AsSpan(0, blob.Length - TagSize);
            var tag = blob.AsSpan(blob.Length - TagSize);
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(_key, TagSize);
            gcm.Decrypt(nonce, cipher, tag, plain, Encoding.ASCII.GetBytes(Version));
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Malformed activation link payload.", ex);
        }
    }

    private static string ToBase64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
        return Convert.FromBase64String(b);
    }
}
