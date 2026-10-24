using SkiaSharp;

namespace ExamApp.Api.Helpers;

/// <summary>
/// SkiaSharp (MIT) üzerinde görsel okuma/yazma (#559: ImageSharp 3.x'in yamasız açıkları; 4.x Release build'de ücretli
/// lisans anahtarı istiyor). Çözme yalnız ilk kare; EXIF yönü elle uygulanır (Skia codec'i uygulamaz). Skia JPEG/PNG/WebP
/// yazar — GIF/BMP girdisi PNG olarak yazılır. Skia kodlayıcısı EXIF/XMP/IPTC yazmaz (GPS/cihaz bilgisi dışarı taşınmaz).
/// </summary>
public static class SkiaImaging
{
    /// <summary>Başlık: piksel çözmeden boyut, EXIF yönü ve kaynak format. Görsel değilse <see cref="ImageDecodeException"/>.</summary>
    public static SkiaImageHeader ReadHeader(byte[] bytes)
    {
        using var codec = CreateCodec(bytes);
        return new SkiaImageHeader(codec.Info.Width, codec.Info.Height, codec.EncodedOrigin, codec.EncodedFormat);
    }

    /// <summary>İlk kareyi RGBA olarak çözer ve EXIF yönünü uygular.</summary>
    public static SKBitmap DecodeOriented(byte[] bytes, out SKEncodedImageFormat format)
    {
        using var codec = CreateCodec(bytes);
        format = codec.EncodedFormat;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new ImageDecodeException($"decode failed: {result}");
        }

        return ApplyOrigin(bitmap, codec.EncodedOrigin);
    }

    /// <summary>Kaynaktaki <paramref name="source"/> dikdörtgenini <paramref name="width"/>×<paramref name="height"/> yeni bitmap'e çizer.</summary>
    public static SKBitmap CropResize(SKBitmap bitmap, SKRectI source, int width, int height)
    {
        var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(target);
        using var image = SKImage.FromBitmap(bitmap);
        // Küçültmede mipmap'li doğrusal örnekleme (aliasing yok), büyütmede kübik.
        var sampling = width < source.Width || height < source.Height
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
        canvas.DrawImage(image, SKRect.Create(source.Left, source.Top, source.Width, source.Height),
            SKRect.Create(0, 0, width, height), sampling, null);
        return target;
    }

    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 90)
    {
        var writable = format is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp ? format : SKEncodedImageFormat.Png;
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(writable, quality)
            ?? throw new InvalidOperationException($"encode failed: {writable}");
        return data.ToArray();
    }

    private static SKCodec CreateCodec(byte[] bytes)
    {
        var codec = bytes.Length == 0 ? null : SKCodec.Create(new SKMemoryStream(bytes));
        return codec ?? throw new ImageDecodeException("unsupported or invalid image");
    }

    /// <summary>EXIF yönünü piksellere uygular (Skia'nın SkEncodedOriginToMatrix'iyle aynı dönüşümler).</summary>
    private static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return source;
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int w = swap ? source.Height : source.Width, h = swap ? source.Width : source.Height;
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var oriented = new SKBitmap(new SKImageInfo(w, h, source.ColorType, source.AlphaType));
        using (var canvas = new SKCanvas(oriented))
        {
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(source, 0, 0);
        }

        source.Dispose();
        return oriented;
    }
}

/// <summary>Piksel çözmeden okunan başlık; <see cref="Width"/>/<see cref="Height"/> EXIF yönü uygulanmamış ham boyut.</summary>
public sealed record SkiaImageHeader(int Width, int Height, SKEncodedOrigin Origin, SKEncodedImageFormat Format)
{
    /// <summary>EXIF 5-8: görsel 90° döndürülerek gösterilir, genişlik/yükseklik yer değiştirir.</summary>
    public bool IsTransposed => Origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
        or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
}

/// <summary>Girdi çözülebilir bir görsel değil.</summary>
public sealed class ImageDecodeException(string message) : Exception(message);
