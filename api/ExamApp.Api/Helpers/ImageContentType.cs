namespace ExamApp.Api.Helpers;

/// <summary>
/// Görsel içerik türünü imza baytlarından belirler (#559). Soru/şık görsel anahtarları tarihsel olarak <c>.jpg</c>
/// uzantılı ama kaynak formatında (PNG/WebP/JPEG) yazılıyor; MinIO içerik türü uzantıdan değil içerikten gelsin.
/// Tanınmayan içerik için null — çağıran güvenli varsayılanı (image/jpeg) kullanır, asla text/html türetilmez.
/// </summary>
public static class ImageContentType
{
    public static string? Sniff(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        var position = stream.Position;
        Span<byte> head = stackalloc byte[12];
        stream.Position = 0;
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = position;
        return Sniff(head[..read]);
    }

    public static string? Sniff(ReadOnlySpan<byte> head) => head switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x42, 0x4D, ..] => "image/bmp",
        _ => null,
    };
}
