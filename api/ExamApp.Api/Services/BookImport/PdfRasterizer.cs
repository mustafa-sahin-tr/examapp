using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;

namespace ExamApp.Api.Services.BookImport;

public enum PdfRasterizeFailure
{
    /// <summary>Parola korumalı ya da desteklenmeyen şifreleme.</summary>
    Encrypted,

    /// <summary>Açılamayan / bozuk dosya.</summary>
    Corrupt,
}

public sealed class PdfRasterizeException(PdfRasterizeFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public PdfRasterizeFailure Failure { get; } = failure;
}

/// <summary>Bir sayfanın kodlanmış görseli.</summary>
public sealed record RasterizedPage(int FilePage, byte[] Image, int Width, int Height);

/// <summary>Issue #1020: PDF → sayfa görselleri. Sayfalar tek tek üretilir; bellek sayfa sayısıyla büyümez.</summary>
public interface IPdfRasterizer
{
    /// <exception cref="PdfRasterizeException">Şifreli ya da bozuk PDF.</exception>
    int GetPageCount(byte[] pdf);

    /// <summary><paramref name="fromFilePage"/>'den (1 tabanlı) sona kadar sayfaları sırayla verir (devam için).</summary>
    /// <exception cref="PdfRasterizeException">Şifreli ya da bozuk PDF.</exception>
    IEnumerable<RasterizedPage> Render(byte[] pdf, int fromFilePage, int pageCount);
}

/// <summary>
/// PDFium (BSD-3) üzerinde PDFtoImage (MIT). Sayfa genişliği el ile aktarımdaki görsellerle aynı (~2363 px), çıktı WebP.
/// PDFium iş parçacığı güvenli değildir; PDFtoImage çağrıları kendi içinde sıraya koyar.
/// </summary>
public sealed class PdfiumRasterizer(int pageWidth = PdfiumRasterizer.DefaultPageWidth, int webpQuality = PdfiumRasterizer.DefaultWebpQuality)
    : IPdfRasterizer
{
    public const int DefaultPageWidth = 2363;
    public const int DefaultWebpQuality = 82;

    public int GetPageCount(byte[] pdf) => Guard(() => Conversion.GetPageCount(pdf));

    public IEnumerable<RasterizedPage> Render(byte[] pdf, int fromFilePage, int pageCount)
    {
        if (fromFilePage > pageCount)
            yield break;

        var options = new RenderOptions(Width: pageWidth, WithAspectRatio: true, WithAnnotations: true, BackgroundColor: SKColors.White);
        var pages = Enumerable.Range(fromFilePage - 1, pageCount - fromFilePage + 1);
        using var enumerator = Guard(() => Conversion.ToImages(pdf, pages, options: options).GetEnumerator());
        var filePage = fromFilePage;
        while (Guard(enumerator.MoveNext))
        {
            using var bitmap = enumerator.Current;
            using var data = bitmap.Encode(SKEncodedImageFormat.Webp, webpQuality)
                             ?? throw new InvalidOperationException($"Page {filePage} could not be encoded.");
            yield return new RasterizedPage(filePage, data.ToArray(), bitmap.Width, bitmap.Height);
            filePage++;
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is PdfPasswordProtectedException or PdfUnsupportedSecuritySchemeException)
        {
            throw new PdfRasterizeException(PdfRasterizeFailure.Encrypted, "The PDF is password protected or encrypted.", ex);
        }
        catch (Exception ex) when (ex is PdfException)
        {
            throw new PdfRasterizeException(PdfRasterizeFailure.Corrupt, "The PDF could not be opened (damaged or not a valid PDF).", ex);
        }
    }
}
