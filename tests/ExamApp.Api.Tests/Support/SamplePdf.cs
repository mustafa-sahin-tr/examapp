using SkiaSharp;

namespace ExamApp.Api.Tests.Support;

/// <summary>Issue #1020: testler için SkiaSharp ile gerçek, çok sayfalı PDF üretir (A4, her sayfada numarası yazılı).</summary>
public static class SamplePdf
{
    public static byte[] Create(int pages)
    {
        using var stream = new MemoryStream();
        using (var document = SKDocument.CreatePdf(stream))
        {
            using var font = new SKFont(SKTypeface.Default, 48);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            for (var i = 1; i <= pages; i++)
            {
                var canvas = document.BeginPage(595, 842);
                canvas.DrawText($"Page {i}", 72, 120, font, paint);
                canvas.DrawRect(72, 200, 451, 300, paint);
                document.EndPage();
            }

            document.Close();
        }

        return stream.ToArray();
    }
}
