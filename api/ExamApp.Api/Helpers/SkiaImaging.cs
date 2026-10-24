using SkiaSharp;

namespace ExamApp.Api.Helpers;

/// <summary>
/// SkiaSharp (MIT) üzerinde görsel okuma/yazma (#559: ImageSharp 3.x'in yamasız açıkları; 4.x Release build'de ücretli
/// lisans anahtarı istiyor). Çözme yalnız ilk kare; EXIF yönü elle uygulanır (Skia codec'i uygulamaz). Skia JPEG/PNG/WebP
/// yazar — GIF/BMP girdisi PNG olarak yazılır. Skia kodlayıcısı EXIF/XMP/IPTC yazmaz (GPS/cihaz bilgisi dışarı taşınmaz).
/// </summary>
public static class SkiaImaging
{
    /// <summary>
    /// Piksel sınırları — küçük bir dosya devasa boyut bildirip belleği tüketmesin (decompression bomb). Skia bitmap'i
    /// yönetilmeyen bellekte (RGBA, piksel başına 4 bayt) ayrıldığı için GC sınırlamaz; her çözme bu sınırdan geçer.
    /// </summary>
    public const long MaxPixels = 40_000_000;

    public const int MaxEdge = 12_000;

    // Kullanıcı girdisi yalnız bu codec'lerden geçer (Skia'nın ICO/WBMP/DNG/HEIF/AVIF vb. çözücüleri kapalı).
    private static readonly HashSet<SKEncodedImageFormat> AllowedFormats =
    [
        SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp,
        SKEncodedImageFormat.Gif, SKEncodedImageFormat.Bmp,
    ];

    /// <summary>Boyut sınırların içinde mi (EXIF yönünden bağımsız: iki kenar da <see cref="MaxEdge"/>'i aşmamalı).</summary>
    public static bool IsWithinLimits(int width, int height) =>
        width > 0 && height > 0 && (long)width * height <= MaxPixels && Math.Max(width, height) <= MaxEdge;
    /// <summary>Başlık: piksel çözmeden boyut, EXIF yönü ve kaynak format. Görsel değilse <see cref="ImageDecodeException"/>.</summary>
    public static SkiaImageHeader ReadHeader(byte[] bytes)
    {
        using var codec = CreateCodec(bytes);
        return new SkiaImageHeader(codec.Info.Width, codec.Info.Height, codec.EncodedOrigin, codec.EncodedFormat);
    }

    /// <summary>
    /// İlk kareyi RGBA olarak çözer ve EXIF yönünü uygular. Sınır aşımı <see cref="ImageTooLargeException"/>, bozuk/eksik
    /// dosya <see cref="ImageDecodeException"/> — ikisi de piksel ayrılmadan ya da ayrılan bitmap bırakılarak.
    /// </summary>
    public static SKBitmap DecodeOriented(byte[] bytes, out SKEncodedImageFormat format)
    {
        using var codec = CreateCodec(bytes);
        format = codec.EncodedFormat;
        if (!IsWithinLimits(codec.Info.Width, codec.Info.Height))
            throw new ImageTooLargeException();

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        // Kesik dosya (IncompleteInput) da reddedilir: eksik satırlar doldurulup bozuk görsel saklanmasın.
        if (result != SKCodecResult.Success)
        {
            bitmap.Dispose();
            throw new ImageDecodeException($"decode failed: {result}");
        }

        try
        {
            return ApplyOrigin(bitmap, codec.EncodedOrigin);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Kaynaktaki <paramref name="source"/> dikdörtgenini <paramref name="width"/>×<paramref name="height"/> yeni bitmap'e çizer.</summary>
    public static SKBitmap CropResize(SKBitmap bitmap, SKRectI source, int width, int height)
    {
        if (!IsWithinLimits(width, height))
            throw new ImageTooLargeException();

        bitmap.SetImmutable(); // SKImage.FromBitmap pikselleri kopyalamadan paylaşsın.
        var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(target);
        using var image = SKImage.FromBitmap(bitmap);
        // Aynı boyut (kırpma): birebir piksel kopyası — yeniden örnekleme yazıyı yumuşatırdı (code review K1).
        // Küçültme: mipmap'li doğrusal (aliasing yok). Büyütme: Catmull-Rom (interpolasyonlu kübik).
        var sampling = width == source.Width && height == source.Height
            ? new SKSamplingOptions(SKFilterMode.Nearest)
            : width < source.Width || height < source.Height
                ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                : new SKSamplingOptions(SKCubicResampler.CatmullRom);
        canvas.DrawImage(image, SKRect.Create(source.Left, source.Top, source.Width, source.Height),
            SKRect.Create(0, 0, width, height), sampling, null);
        return target;
    }

    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 90)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitmap), "Image has no pixels.");

        var writable = format is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp ? format : SKEncodedImageFormat.Png;
        bitmap.SetImmutable();
        using var image = SKImage.FromBitmap(bitmap);
        // JPEG alfa taşımaz: saydam alan beyaz zemine oturtulur (yoksa premultiplied saydam piksel siyaha döner).
        using var flattened = writable == SKEncodedImageFormat.Jpeg && !bitmap.Info.IsOpaque ? FlattenOnWhite(image) : null;
        using var data = (flattened ?? image).Encode(writable, quality)
            ?? throw new InvalidOperationException($"encode failed: {writable}");
        return data.ToArray();
    }

    private static SKImage FlattenOnWhite(SKImage image)
    {
        using var surface = SKSurface.Create(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(image, 0, 0);
        return surface.Snapshot();
    }

    private static SKCodec CreateCodec(byte[] bytes)
    {
        // İmza önce yönetilen kodda denetlenir: tanınmayan baytlar Skia'nın gevşek algılayıcılarına (WBMP vb.) hiç
        // gitmez — 8 baytlık bir girdi SKCodec.Create'i takılı bırakabiliyordu (thread kilitleyen DoS).
        if (ImageContentType.Sniff(bytes) is null)
            throw new ImageDecodeException("unrecognized image signature");

        var codec = SKCodec.Create(new SKMemoryStream(bytes));
        if (codec is null)
            throw new ImageDecodeException("unsupported or invalid image");
        if (!AllowedFormats.Contains(codec.EncodedFormat))
        {
            codec.Dispose();
            throw new ImageDecodeException($"format not allowed: {codec.EncodedFormat}");
        }

        return codec;
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

/// <summary>Girdi çözülebilir (izin verilen formatta) bir görsel değil.</summary>
public class ImageDecodeException(string message) : Exception(message);

/// <summary>Görsel ya da hedef boyut <see cref="SkiaImaging.MaxPixels"/>/<see cref="SkiaImaging.MaxEdge"/> sınırını aşıyor.</summary>
public sealed class ImageTooLargeException() : ImageDecodeException("image exceeds pixel limits");
