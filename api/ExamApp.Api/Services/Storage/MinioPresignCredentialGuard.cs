using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Services.Storage;

/// <summary>
/// issue #402 (O1): presign hesabının açılış doğrulaması (<c>KeycloakSecretGuard</c> deseni; Gateway.Tests bu dosyayı
/// link'lediği için Foundation'a bağımlı değil — dev-only kuralı aynı: boş ya da <c>devOnly</c> önekli). Presigned görsel
/// URL'leri imzalayan erişim anahtarını <c>X-Amz-Credential</c>'da taşır; bu yüzden imza root kullanıcıyla değil,
/// yalnız <c>s3:GetObject</c> (izinli prefix'ler) yetkili ayrı bir MinIO hesabıyla atılır
/// (<c>deploy/scripts/minio-presign-init.sh</c> oluşturur). Development dışında, imzalama açıkken
/// (<c>MinioConfig:PresignImageUrls</c> != false) eksik/dev-only secret, eksik erişim anahtarı ya da root ile aynı
/// erişim anahtarı açılışta <see cref="InvalidOperationException"/> fırlatır.
/// </summary>
public static class MinioPresignCredentialGuard
{
    public const string AccessKeyKey = "PresignAccessKey";
    public const string SecretKeyKey = "PresignSecretKey";

    /// <summary>Presign erişim anahtarı root (<c>MinioConfig:AccessKey</c>) ile aynı mı (büyük/küçük harf duyarlı, MinIO gibi).</summary>
    public static bool IsRootKey(string? presignAccessKey, string? rootAccessKey) =>
        !string.IsNullOrWhiteSpace(presignAccessKey) && !string.IsNullOrWhiteSpace(rootAccessKey) &&
        string.Equals(presignAccessKey.Trim(), rootAccessKey.Trim(), StringComparison.Ordinal);

    /// <summary>Hatalı anahtarların listesi (boşsa yapılandırma geçerli). Development'ta ya da imzalama kapalıyken hep boş.</summary>
    public static IReadOnlyList<string> Problems(bool isDevelopment, IConfigurationSection minioConfig)
    {
        var problems = new List<string>();
        if (isDevelopment || !minioConfig.GetValue("PresignImageUrls", true))
            return problems;

        var accessKey = minioConfig[AccessKeyKey];
        if (string.IsNullOrWhiteSpace(accessKey))
            problems.Add($"MinioConfig:{AccessKeyKey} must be set (MinioConfig__{AccessKeyKey})");
        else if (IsRootKey(accessKey, minioConfig["AccessKey"]))
            problems.Add($"MinioConfig:{AccessKeyKey} must not be the MinIO root access key (MinioConfig:AccessKey)");

        if (IsMissingOrDevOnly(minioConfig[SecretKeyKey]))
            problems.Add($"MinioConfig:{SecretKeyKey} must be set (MinioConfig__{SecretKeyKey}); empty or dev-only values are not allowed outside Development");

        return problems;
    }

    private static bool IsMissingOrDevOnly(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("devOnly", StringComparison.OrdinalIgnoreCase);

    public static void EnsureConfigured(bool isDevelopment, IConfigurationSection minioConfig)
    {
        var problems = Problems(isDevelopment, minioConfig);
        if (problems.Count > 0)
            throw new InvalidOperationException(
                "MinIO presign account misconfigured (issue #402): " + string.Join("; ", problems) + ".");
    }
}
