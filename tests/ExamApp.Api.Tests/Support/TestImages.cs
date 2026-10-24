using SkiaSharp;

namespace ExamApp.Api.Tests.Support;

/// <summary>Testlerde görsel üretip okumak için SkiaSharp yardımcıları (#559: ImageSharp kaldırıldı).</summary>
public static class TestImages
{
    /// <summary>Verilen boyutta düz renkli PNG.</summary>
    public static byte[] Png(int width, int height, SKColor? color = null)
        => Encode(width, height, SKEncodedImageFormat.Png, color);

    public static byte[] Jpeg(int width, int height, SKColor? color = null)
        => Encode(width, height, SKEncodedImageFormat.Jpeg, color);

    /// <summary>Görselin (EXIF yönü uygulanmamış) boyutu ve formatı.</summary>
    public static (int Width, int Height, SKEncodedImageFormat Format) Info(byte[] bytes)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(bytes)) ?? throw new InvalidOperationException("not an image");
        return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
    }

    public static (int Width, int Height, SKEncodedImageFormat Format) Info(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Info(ms.ToArray());
    }

    /// <summary>
    /// JPEG'e EXIF Orientation etiketi taşıyan APP1 segmenti ekler (Skia kodlayıcısı EXIF yazmaz). Orientation 1-8.
    /// </summary>
    public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        // TIFF başlığı (little-endian) + tek IFD girdisi: tag 0x0112 SHORT count 1 value=orientation.
        byte[] tiff =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // "II", 42, IFD0 ofseti 8
            0x01, 0x00,                                     // 1 girdi
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, // Orientation, SHORT, count 1
            (byte)orientation, 0x00, 0x00, 0x00,            // değer
            0x00, 0x00, 0x00, 0x00,                         // sonraki IFD yok
        ];
        byte[] exifHeader = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00]; // "Exif\0\0"
        var length = 2 + exifHeader.Length + tiff.Length;
        using var ms = new MemoryStream();
        ms.Write(jpeg, 0, 2); // SOI
        ms.Write([0xFF, 0xE1, (byte)(length >> 8), (byte)length]);
        ms.Write(exifHeader);
        ms.Write(tiff);
        ms.Write(jpeg, 2, jpeg.Length - 2);
        return ms.ToArray();
    }

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format, SKColor? color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color ?? SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
