using System;
using System.Diagnostics.CodeAnalysis;

namespace ExamApp.Api.Services.Storage;

/// <summary>
/// issue #365: DB'de saklanan MinIO adresi biçimi <c>/img/{bucket}/{key}</c> (<see cref="IMinIoService.UploadFileAsync"/>
/// dönüşü). Ayrıştırma sıkıdır: tam URL, boş bucket/key, <c>..</c> / ters bölü / çift bölü içeren key reddedilir.
/// </summary>
public static class MinioObjectUrl
{
    private const string Prefix = "/img/";

    public static string Build(string bucket, string key) => $"{Prefix}{bucket}/{key}";

    public static bool TryParse(string? url, [NotNullWhen(true)] out string? bucket, [NotNullWhen(true)] out string? key)
    {
        bucket = key = null;
        if (string.IsNullOrEmpty(url) || !url.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var rest = url.AsSpan(Prefix.Length);
        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1)
            return false;

        var b = rest[..slash].ToString();
        var k = rest[(slash + 1)..].ToString();
        if (k.StartsWith('/') || k.Contains("//", StringComparison.Ordinal) || k.Contains('\\') ||
            k.Contains("..", StringComparison.Ordinal) || k.Contains('?') || k.Contains('#'))
            return false;

        bucket = b;
        key = k;
        return true;
    }

    /// <summary>
    /// issue #365 (S2): anahtar gateway'den (Ocelot <c>/img/{everything}</c>) imzası bozulmadan geçebilir mi? Ocelot yer
    /// tutucuyu bir kez decode edip yeniden iletir. Canlı doğrulamada (2026-10-06) boşluk, Türkçe karakter,
    /// <c>+ ( ) ' ! , = ; @ $ ~</c> geçti; <c>&amp;</c> ve geçerli bir <c>%XX</c> dizisi imzayı bozdu
    /// (SignatureDoesNotMatch). Bu yüzden <c>&amp;</c>/<c>%</c> içeren anahtar ne imzalanır ne istemciden kabul edilir.
    /// </summary>
    public static bool IsGatewaySafeKey(string key)
    {
        foreach (var c in key)
        {
            if (c is '&' or '%' or '?' or '#' or '\\' || char.IsControl(c))
                return false;
        }
        return true;
    }
}
