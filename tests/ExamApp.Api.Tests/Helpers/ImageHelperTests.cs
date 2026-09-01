using ExamApp.Api.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ExamApp.Api.Tests.Helpers;

public class ImageHelperTests
{
    private readonly ImageHelper _sut = new();

    private static byte[] Png(int w, int h)
    {
        using var image = new Image<Rgba32>(w, h);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", true)]
    [InlineData("data:image/jpeg;base64,/9j/4AAQSkZJRg==", true)]
    [InlineData("data:text/plain;base64,aGVsbG8=", false)]
    [InlineData("iVBORw0KGgo=", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBase64String(string? input, bool expected)
        => _sut.IsBase64String(input!).ShouldBe(expected);

    [Theory]
    [InlineData("https://cdn.example.com/a/b/c.png", true)]
    [InlineData("http://x.io/pic.JPG?v=2", true)]
    [InlineData("https://example.com/no-extension", false)]
    [InlineData("ftp://example.com/pic.png", false)]
    [InlineData("", false)]
    public void IsValidImageUrl(string input, bool expected)
        => _sut.IsValidImageUrl(input).ShouldBe(expected);

    [Fact]
    public void CropImage_returns_a_region_of_the_requested_size()
    {
        var cropped = _sut.CropImage(Png(100, 80), x: 10, y: 5, width: 40, height: 20);

        using var image = Image.Load(cropped);
        image.Width.ShouldBe(40);
        image.Height.ShouldBe(20);
    }

    [Fact]
    public async Task ResizeImage_scales_both_dimensions()
    {
        await using var input = new MemoryStream(Png(200, 100));
        await using var result = await _sut.ResizeImageAsync(input, 0.5);

        result.ShouldNotBeNull();
        using var image = await Image.LoadAsync(result!);
        image.Width.ShouldBe(100);
        image.Height.ShouldBe(50);
    }
}
