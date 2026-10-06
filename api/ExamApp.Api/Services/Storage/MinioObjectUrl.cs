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
}
