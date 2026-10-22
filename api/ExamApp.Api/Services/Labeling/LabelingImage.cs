using System;
using System.IO;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// İşaretleme görselinin çözülmüş hali: SHA-256 (ham baytlar — question-detector <c>content_id</c> ile aynı girdi) ve
/// Claude'a gidecek küçültülmüş JPEG'ler. <see cref="Dispose"/> ImageSharp tamponunu bırakır.
/// </summary>
public sealed class LabelingImage : IDisposable
{
    /// <summary>Çözülmüş görsel için üst sınır (base64 öncesi bayt).</summary>
    public const int MaxBytes = 20 * 1024 * 1024;

    private readonly Image<Rgb24> _image;

    private LabelingImage(Image<Rgb24> image, string sha256)
    {
        _image = image;
        Sha256 = sha256;
    }

    public string Sha256 { get; }

    public int Width => _image.Width;

    public int Height => _image.Height;

    /// <summary>Base64 (data URL öneki olabilir) → görsel. Geçersiz/çok büyükse <see cref="LabelingValidationException"/>.</summary>
    public static LabelingImage Decode(string base64)
    {
        var bytes = DecodeBase64(base64);
        try
        {
            var image = Image.Load<Rgb24>(bytes);
            return new LabelingImage(image, ComputeSha256(bytes));
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new LabelingValidationException("labeling.invalidImage");
        }
    }

    /// <summary>Hash için görseli çözmeden yalnız baytları okur.</summary>
    public static string HashOnly(string base64) => ComputeSha256(DecodeBase64(base64));

    public static bool IsValidSha256(string? value) =>
        value is { Length: 64 } && value.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;

    /// <summary>
    /// Tüm sayfa ya da bir dikdörtgen (orijinal piksel) — uzun kenarı <paramref name="maxEdge"/>'i aşmayacak şekilde
    /// küçültülmüş JPEG. <c>Scale</c>: gönderilen görsel pikseli → orijinal piksel çarpanı.
    /// </summary>
    public EncodedRegion Encode(int maxEdge, Rectangle? region = null)
    {
        var rect = Rectangle.Intersect(region ?? new Rectangle(0, 0, Width, Height), new Rectangle(0, 0, Width, Height));
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new LabelingValidationException("labeling.invalidRegion");

        var longEdge = Math.Max(rect.Width, rect.Height);
        var scale = longEdge > maxEdge ? (double)longEdge / maxEdge : 1d;
        var targetWidth = Math.Max(1, (int)Math.Round(rect.Width / scale));
        var targetHeight = Math.Max(1, (int)Math.Round(rect.Height / scale));

        using var clone = _image.Clone(ctx =>
        {
            ctx.Crop(rect);
            if (scale > 1d)
                ctx.Resize(targetWidth, targetHeight, KnownResamplers.Lanczos3);
        });
        using var ms = new MemoryStream();
        clone.SaveAsJpeg(ms, new JpegEncoder { Quality = 90 });
        return new EncodedRegion(ms.ToArray(), clone.Width, clone.Height, rect.X, rect.Y, rect.Width / (double)clone.Width,
            rect.Height / (double)clone.Height);
    }

    public void Dispose() => _image.Dispose();

    private static byte[] DecodeBase64(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new LabelingValidationException("labeling.invalidImage");

        var comma = base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? base64.IndexOf(',') : -1;
        var payload = comma >= 0 ? base64[(comma + 1)..] : base64;
        if (payload.Length > MaxBytes / 3 * 4 + 4)
            throw new LabelingValidationException("labeling.imageTooLarge");

        try
        {
            return Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            throw new LabelingValidationException("labeling.invalidImage");
        }
    }

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>Claude'a gönderilen JPEG ve onu orijinal sayfa uzayına geri çeviren ofset/çarpan.</summary>
public sealed record EncodedRegion(byte[] Jpeg, int Width, int Height, int OffsetX, int OffsetY, double ScaleX, double ScaleY)
{
    public Rectangle ToPage(double x1, double y1, double x2, double y2)
    {
        var left = Math.Clamp(Math.Min(x1, x2), 0, Width);
        var right = Math.Clamp(Math.Max(x1, x2), 0, Width);
        var top = Math.Clamp(Math.Min(y1, y2), 0, Height);
        var bottom = Math.Clamp(Math.Max(y1, y2), 0, Height);
        var px = OffsetX + (int)Math.Round(left * ScaleX);
        var py = OffsetY + (int)Math.Round(top * ScaleY);
        return new Rectangle(px, py, (int)Math.Round((right - left) * ScaleX), (int)Math.Round((bottom - top) * ScaleY));
    }
}

/// <summary>İstemci girdisi geçersiz → 400. <see cref="Exception.Message"/> yerelleştirme anahtarıdır.</summary>
public sealed class LabelingValidationException(string messageKey) : Exception(messageKey);
