using System;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace ExamApp.Api.Helpers;

public class ImageHelper
{
    public bool IsBase64String(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        // Base64 formatını tespit etmek için regex
        string base64Pattern = @"^data:image\/(png|jpeg|jpg|gif|bmp|webp);base64,[A-Za-z0-9+/=]+$";
        return Regex.IsMatch(input, base64Pattern);
    }

    public bool IsValidImageUrl(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        // URL olup olmadığını anlamak için regex
        string urlPattern = @"^(http|https):\/\/.*\.(jpeg|jpg|png|gif|bmp|webp)(\?.*)?$";
        return Regex.IsMatch(input, urlPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>ImageSharp'ın varsayılan JPEG kalitesi (#559 öncesi saklanan soru görselleriyle aynı boyut/kalite).</summary>
    public const int JpegQuality = 75;

    /// <summary>
    /// Görseli bir kez çözer (EXIF yönü uygulanmış); aynı sayfadan çok kırpma yapan çağıran bunu kullanır ve bitmap'i
    /// dispose eder. Sınır aşımı / bozuk dosya <see cref="ImageDecodeException"/>.
    /// </summary>
    public SKBitmap LoadImage(byte[] imageBytes, out SKEncodedImageFormat format)
        => SkiaImaging.DecodeOriented(imageBytes, out format);

    /// <summary>Çözülmüş görselden birebir kırpma; dikdörtgen görselin içinde olmalı.</summary>
    public SKBitmap CropBitmap(SKBitmap image, int x, int y, int width, int height)
    {
        var rect = SKRectI.Create(x, y, width, height);
        if (width <= 0 || height <= 0 || !SKRectI.Create(0, 0, image.Width, image.Height).Contains(rect))
            throw new ArgumentOutOfRangeException(nameof(x), "Crop rectangle must lie within the image.");

        return SkiaImaging.CropResize(image, rect, width, height);
    }

    /// <summary>Kaynak formatında yazar (JPEG/PNG/WebP; GIF/BMP → PNG).</summary>
    public byte[] EncodeImage(SKBitmap image, SKEncodedImageFormat format)
        => SkiaImaging.Encode(image, format, JpegQuality);

    // CropImage: Crops the given image bytes using the provided rectangle and returns the cropped image bytes.
    public byte[] CropImage(byte[] imageBytes, int x, int y, int width, int height)
    {
        using var image = LoadImage(imageBytes, out var format);
        using var cropped = CropBitmap(image, x, y, width, height);
        return EncodeImage(cropped, format);
    }

    public async Task<Stream?> ResizeImageAsync(Stream imageStream, double scale)
    {
        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer);
        using var image = LoadImage(buffer.ToArray(), out var format);
        int newWidth = Math.Max(1, (int)(image.Width * scale));
        int newHeight = Math.Max(1, (int)(image.Height * scale));

        // Hedef boyut da SkiaImaging sınırlarından geçer (büyük scale → ImageTooLargeException).
        using var resized = SkiaImaging.CropResize(image, SKRectI.Create(0, 0, image.Width, image.Height), newWidth, newHeight);
        return new MemoryStream(EncodeImage(resized, format));
    }
}
