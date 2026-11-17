using System;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// #1219: sayfa görselindeki QR'ı okur (kurulum taramasında, yapay zekâ yok). Yalnız http(s) bağlantısı döner; okunamazsa,
/// QR yoksa ya da içerik bağlantı değilse null. Sayfada birden çok QR varsa ilki.
/// </summary>
public static class PageQrReader
{
    private static readonly DecodingOptions Options = new()
    {
        PossibleFormats = [BarcodeFormat.QR_CODE],
        TryHarder = true,
    };

    public static string? Read(byte[] image)
    {
        try
        {
            using var decoded = SKBitmap.Decode(image);
            if (decoded is null)
                return null;
            using var gray = new SKBitmap(decoded.Width, decoded.Height, SKColorType.Gray8, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(gray))
                canvas.DrawBitmap(decoded, 0, 0);
            var source = new PlanarYUVLuminanceSource(gray.Bytes, gray.RowBytes, gray.Height, 0, 0, gray.Width, gray.Height, false);
            var reader = new BarcodeReaderGeneric { Options = Options, AutoRotate = false };
            return AsLink(reader.Decode(source)?.Text);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    internal static string? AsLink(string? text)
    {
        text = text?.Trim();
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && text!.Length <= MaxLinkLength
            ? uri.AbsoluteUri
            : null;
    }

    public const int MaxLinkLength = 500;
}
