using System;
using ExamApp.Api.Helpers;
using SkiaSharp;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Bir satır aralığında (<c>[x0, x1)</c>) mürekkep var mı; <see cref="BookQuestionLayout.Prepare"/> şık üst kenarını boşluğa oturtur.</summary>
public delegate bool RowInk(int y, int x0, int x1);

/// <summary>
/// Sayfa görselinin parlaklık haritası: bir satır parçasında koyu piksel var mı. Şık kutusunun yukarı payı soru kökünün son
/// satırına taşmasın diye kullanılır (şıklar kökün hemen altında başlayınca pay kökü keser).
/// </summary>
public sealed class PageInk
{
    // Açık gri arka plan / tarama gürültüsü mürekkep sayılmaz; yazı ve çizgiler bundan koyu.
    private const int DarkLuma = 150;

    private readonly byte[] _luma;

    private PageInk(byte[] luma, int width, int height)
    {
        _luma = luma;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Base64 (data URL öneki olabilir) sayfa görseli; çözülemezse null (kural uygulanmaz, eski davranış).</summary>
    public static PageInk? FromBase64(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return null;
        try
        {
            var comma = base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? base64.IndexOf(',') : -1;
            var bytes = Convert.FromBase64String(comma >= 0 ? base64[(comma + 1)..] : base64);
            using var bitmap = SkiaImaging.DecodeOriented(bytes, out _);
            return FromBitmap(bitmap);
        }
        catch (Exception ex) when (ex is FormatException or ImageDecodeException or ImageTooLargeException or NotSupportedException)
        {
            return null;
        }
    }

    public static PageInk FromBitmap(SKBitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var luma = new byte[width * height];
        if (bitmap.ColorType is SKColorType.Rgba8888 or SKColorType.Bgra8888)
        {
            // R ile B'nin yeri farklı; parlaklıkta ağırlıkları farklı ama koyu / açık ayrımı için ikisi de yeter.
            var pixels = bitmap.GetPixelSpan();
            var rowBytes = bitmap.RowBytes;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * rowBytes + x * 4;
                    luma[y * width + x] = Luma(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
                }
            }
        }
        else
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    luma[y * width + x] = Luma(c.Red, c.Green, c.Blue, c.Alpha);
                }
            }
        }
        return new PageInk(luma, width, height);
    }

    // Saydam piksel beyaz zemin sayılır.
    private static byte Luma(byte r, byte g, byte b, byte a) =>
        a < 128 ? (byte)255 : (byte)((r * 299 + g * 587 + b * 114) / 1000);

    /// <summary>Satır parçasında en az iki koyu piksel var mı (tek nokta gürültüsü sayılmaz).</summary>
    public bool RowHasInk(int y, int x0, int x1)
    {
        if (y < 0 || y >= Height)
            return false;
        x0 = Math.Max(0, x0);
        x1 = Math.Min(Width, x1);
        var dark = 0;
        var row = y * Width;
        for (var x = x0; x < x1; x++)
        {
            if (_luma[row + x] < DarkLuma && ++dark >= 2)
                return true;
        }
        return false;
    }
}
