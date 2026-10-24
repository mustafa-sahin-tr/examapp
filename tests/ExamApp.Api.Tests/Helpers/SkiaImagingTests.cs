using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using ExamApp.Api.Tests.Support;
using SkiaSharp;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>#559 review bulguları: birebir kırpma, saydamlık, format/boyut sınırları, içerik türü, ölçek doğrulaması.</summary>
public class SkiaImagingTests
{
    private static byte[] Checkerboard(int size)
    {
        using var bitmap = new SKBitmap(size, size);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                bitmap.SetPixel(x, y, (x + y) % 2 == 0 ? SKColors.Black : SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void CropImage_same_size_is_a_pixel_exact_copy()
    {
        var png = Checkerboard(20);

        var cropped = new ImageHelper().CropImage(png, 3, 3, 10, 10);

        using var source = SKBitmap.Decode(png);
        using var result = SKBitmap.Decode(cropped);
        for (var y = 0; y < 10; y++)
            for (var x = 0; x < 10; x++)
                result.GetPixel(x, y).ShouldBe(source.GetPixel(x + 3, y + 3));
    }

    [Fact]
    public void Encode_jpeg_flattens_transparent_pixels_on_white()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);

        var jpeg = SkiaImaging.Encode(bitmap, SKEncodedImageFormat.Jpeg);

        using var decoded = SKBitmap.Decode(jpeg);
        var pixel = decoded.GetPixel(1, 1);
        pixel.Red.ShouldBeGreaterThan((byte)240);
        pixel.Green.ShouldBeGreaterThan((byte)240);
        pixel.Blue.ShouldBeGreaterThan((byte)240);
    }

    [Fact]
    public void Decode_rejects_formats_outside_the_allowlist()
        // Skia'nın gevşek WBMP algılayıcısı bu baytları görsel sanıyordu (code review U2).
        => Should.Throw<ImageDecodeException>(() => SkiaImaging.DecodeOriented([0, 0, 4, 4, 0, 0, 0, 0], out _));

    [Fact]
    public void Decode_rejects_truncated_images()
    {
        var png = TestImages.Png(300, 300, SKColors.Red);
        Should.Throw<ImageDecodeException>(() => SkiaImaging.DecodeOriented(png[..(png.Length / 2)], out _));
    }

    [Fact]
    public void Decode_rejects_images_beyond_pixel_limits_before_allocating()
        => Should.Throw<ImageTooLargeException>(() =>
            SkiaImaging.DecodeOriented(TestImages.Png(SkiaImaging.MaxEdge + 1, 1), out _));

    [Fact]
    public async Task ResizeImage_rejects_targets_beyond_pixel_limits()
    {
        await using var input = new MemoryStream(TestImages.Png(4000, 4000));
        await Should.ThrowAsync<ImageTooLargeException>(() => new ImageHelper().ResizeImageAsync(input, 4));
    }

    [Fact]
    public async Task ResizeImage_tiny_scale_keeps_at_least_one_pixel()
    {
        await using var input = new MemoryStream(TestImages.Png(10, 10));
        await using var result = await new ImageHelper().ResizeImageAsync(input, 0.01);
        var info = TestImages.Info(result!);
        (info.Width, info.Height).ShouldBe((1, 1));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    public void ContentType_is_sniffed_from_image_bytes(SKEncodedImageFormat format, string expected)
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 80);
        using var stream = new MemoryStream(data.ToArray());
        stream.Position = 1;

        ImageContentType.Sniff(stream).ShouldBe(expected);
        stream.Position.ShouldBe(1);
    }

    [Fact]
    public void ContentType_of_non_image_is_null()
        => ImageContentType.Sniff("<html><script>"u8).ShouldBeNull();

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(0.05, true)]
    [InlineData(4, true)]
    [InlineData(0.01, false)]
    [InlineData(5, false)]
    [InlineData(-1, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void Image_scale_is_validated(double scale, bool valid)
        => QuestionsController.IsValidImageScale(scale).ShouldBe(valid);
}
