using System.Security.Cryptography;
using System.Text.Json;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Keycloak'ın <c>pbkdf2-sha512</c> sağlayıcısıyla birebir uyumlu parola hash'i üretir (issue #217).
/// Realm partial import'ta <c>CredentialRepresentation.secretData/credentialData</c> olarak verilir;
/// Keycloak hash'i olduğu gibi saklar, kullanıcı başına yeniden hesaplamaz. Aynı hash tüm seed
/// kullanıcılarına verilebilir (test verisi; parola zaten tek ve ortak).
///
/// <para>Format (Keycloak <c>Pbkdf2PasswordHashProvider</c> / <c>PasswordCredentialModel</c>):
/// secretData = {"value":base64(hash),"salt":base64(salt),"additionalParameters":{}},
/// credentialData = {"hashIterations":N,"algorithm":"pbkdf2-sha512","additionalParameters":{}}.
/// Türetilen anahtar 512 bit, HMAC-SHA512 — Java <c>PBKDF2WithHmacSHA512</c> ile aynı sonuç.</para>
/// </summary>
public static class KeycloakPasswordHasher
{
    public const string Algorithm = "pbkdf2-sha512";

    /// <summary>Keycloak 24+ varsayılanı (pbkdf2-sha512 için 210.000).</summary>
    public const int DefaultIterations = 210_000;

    private const int SaltSizeBytes = 16;
    private const int DerivedKeySizeBytes = 64; // 512 bit

    public static KeycloakHashedCredential HashPbkdf2Sha512(string rawPassword, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawPassword);
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(rawPassword, salt, iterations, HashAlgorithmName.SHA512, DerivedKeySizeBytes);

        var secretData = JsonSerializer.Serialize(new
        {
            value = Convert.ToBase64String(hash),
            salt = Convert.ToBase64String(salt),
            additionalParameters = new { }
        });
        var credentialData = JsonSerializer.Serialize(new
        {
            hashIterations = iterations,
            algorithm = Algorithm,
            additionalParameters = new { }
        });

        return new KeycloakHashedCredential(secretData, credentialData);
    }
}
