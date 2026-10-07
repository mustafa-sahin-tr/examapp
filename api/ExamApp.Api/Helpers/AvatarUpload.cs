using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Helpers;

/// <summary>Avatar yüklemesinin doğrulama sonucu.</summary>
public enum AvatarUploadError
{
    None = 0,
    Missing = 1,
    TooLarge = 2,
    UnsupportedType = 3
}

/// <summary>
/// Issue #424 (security review): <c>POST api/student/update-avatar</c> doğrulaması. Yalnızca PNG / JPEG / WebP, en fazla
/// <see cref="MaxBytes"/>. Bildirilen <c>Content-Type</c> TEK BAŞINA güvenilmez: dosyanın ilk baytları (imza) da aynı türü
/// göstermeli. Depoya yazılacak uzantı istemcinin dosya adından değil, tespit edilen türden gelir.
/// </summary>
public static class AvatarUpload
{
    public const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>Multipart gövde üst sınırı (dosya + form alanı/başlık payı).</summary>
    public const long MaxRequestBytes = MaxBytes + 64 * 1024;

    /// <summary>Dosyayı doğrular; geçerliyse depoda kullanılacak uzantıyı (".png", ".jpg", ".webp") döner.</summary>
    public static async Task<(AvatarUploadError Error, string? Extension)> ValidateAsync(IFormFile? file, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return (AvatarUploadError.Missing, null);
        if (file.Length > MaxBytes)
            return (AvatarUploadError.TooLarge, null);

        var declared = DeclaredType(file.ContentType);
        if (declared == null)
            return (AvatarUploadError.UnsupportedType, null);

        var header = new byte[12];
        int read;
        await using (var stream = file.OpenReadStream())
            read = await ReadAtLeastAsync(stream, header, ct);

        var detected = DetectType(header.AsSpan(0, read));
        if (detected == null || detected != declared)
            return (AvatarUploadError.UnsupportedType, null);

        return (AvatarUploadError.None, detected switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            _ => ".webp"
        });
    }

    private static string? DeclaredType(string? contentType)
    {
        var mediaType = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return mediaType switch
        {
            "image/png" => "image/png",
            "image/jpeg" or "image/jpg" or "image/pjpeg" => "image/jpeg",
            "image/webp" => "image/webp",
            _ => null
        };
    }

    /// <summary>Dosya imzası: PNG <c>89 50 4E 47 0D 0A 1A 0A</c>, JPEG <c>FF D8 FF</c>, WebP <c>RIFF....WEBP</c>.</summary>
    internal static string? DetectType(ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (header.StartsWith(png))
            return "image/png";
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "image/jpeg";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }

    private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (n == 0)
                break;
            total += n;
        }
        return total;
    }
}
