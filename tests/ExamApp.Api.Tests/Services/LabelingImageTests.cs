using System.Security.Cryptography;
using ExamApp.Api.Services.Labeling;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ExamApp.Api.Tests.Services;

public class LabelingImageTests
{
    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Fact]
    public void Decode_PlainBase64_HashesRawBytesAndReadsDimensions()
    {
        var png = Png(30, 20);

        using var image = LabelingImage.Decode(Convert.ToBase64String(png));

        image.Sha256.ShouldBe(Sha256Hex(png));
        image.Width.ShouldBe(30);
        image.Height.ShouldBe(20);
    }

    [Fact]
    public void Decode_DataUrlPrefix_ProducesSameHashAsPlainBase64()
    {
        var png = Png(12, 12);
        var plain = Convert.ToBase64String(png);

        using var fromDataUrl = LabelingImage.Decode("data:image/png;base64," + plain);
        using var fromPlain = LabelingImage.Decode(plain);

        fromDataUrl.Sha256.ShouldBe(fromPlain.Sha256);
    }

    [Fact]
    public void ReadHeader_MatchesDecodedImageHashAndSize()
    {
        var png = Png(9, 7);

        var header = LabelingImage.ReadHeader("data:image/png;base64," + Convert.ToBase64String(png));

        header.Sha256.ShouldBe(Sha256Hex(png));
        header.Width.ShouldBe(9);
        header.Height.ShouldBe(7);
    }

    [Fact]
    public void ReadHeader_NonImageBytes_ThrowsInvalidImage()
    {
        var ex = Should.Throw<LabelingValidationException>(() =>
            LabelingImage.ReadHeader(Convert.ToBase64String("plain text"u8.ToArray())));

        ex.Message.ShouldBe("labeling.invalidImage");
    }

    [Fact]
    public void Decode_WidthBeyondMaxEdge_ThrowsImageTooLarge()
    {
        var ex = Should.Throw<LabelingValidationException>(() =>
            LabelingImage.Decode(Convert.ToBase64String(Png(LabelingImage.MaxEdge + 1, 1))));

        ex.Message.ShouldBe("labeling.imageTooLarge");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decode_BlankInput_ThrowsInvalidImage(string? input)
    {
        var ex = Should.Throw<LabelingValidationException>(() => LabelingImage.Decode(input!));

        ex.Message.ShouldBe("labeling.invalidImage");
    }

    [Fact]
    public void Decode_NotBase64_ThrowsInvalidImage()
    {
        var ex = Should.Throw<LabelingValidationException>(() => LabelingImage.Decode("%%%"));

        ex.Message.ShouldBe("labeling.invalidImage");
    }

    [Fact]
    public void Decode_Base64OfNonImageBytes_ThrowsInvalidImage()
    {
        var ex = Should.Throw<LabelingValidationException>(() =>
            LabelingImage.Decode(Convert.ToBase64String("definitely not an image"u8.ToArray())));

        ex.Message.ShouldBe("labeling.invalidImage");
    }

    [Fact]
    public void Decode_PayloadBeyondSizeLimit_ThrowsImageTooLargeBeforeDecoding()
    {
        var oversized = new string('A', LabelingImage.MaxBytes / 3 * 4 + 8);

        var ex = Should.Throw<LabelingValidationException>(() => LabelingImage.Decode(oversized));

        ex.Message.ShouldBe("labeling.imageTooLarge");
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef", false)]
    [InlineData("0123456789abcdef", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidSha256_AcceptsOnlyLowercase64Hex(string? value, bool expected)
    {
        LabelingImage.IsValidSha256(value).ShouldBe(expected);
    }

    [Fact]
    public void Encode_LargeImage_DownscalesLongEdgeToMaxEdgeAndKeepsAspectRatio()
    {
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(3000, 1000)));

        var encoded = image.Encode(maxEdge: 1568);

        encoded.Width.ShouldBe(1568);
        encoded.Height.ShouldBe(523);
        encoded.OffsetX.ShouldBe(0);
        encoded.OffsetY.ShouldBe(0);
    }

    [Fact]
    public void Encode_SmallImage_IsNotResized()
    {
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(200, 100)));

        var encoded = image.Encode(maxEdge: 1568);

        encoded.Width.ShouldBe(200);
        encoded.Height.ShouldBe(100);
        encoded.ScaleX.ShouldBe(1d);
        encoded.ScaleY.ShouldBe(1d);
    }

    [Fact]
    public void Encode_ReturnsJpegBytes()
    {
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(50, 50)));

        var encoded = image.Encode(maxEdge: 1568);

        // JPEG SOI marker
        encoded.Jpeg[0].ShouldBe((byte)0xFF);
        encoded.Jpeg[1].ShouldBe((byte)0xD8);
    }

    [Fact]
    public void Encode_RegionIsCroppedAndOffsetIsRecorded()
    {
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(400, 300)));

        var encoded = image.Encode(maxEdge: 1568, region: new Rectangle(100, 50, 200, 100));

        encoded.Width.ShouldBe(200);
        encoded.Height.ShouldBe(100);
        encoded.OffsetX.ShouldBe(100);
        encoded.OffsetY.ShouldBe(50);
        encoded.ScaleX.ShouldBe(1d);
    }

    [Fact]
    public void Encode_RegionPartlyOutsideImage_IsClippedToImage()
    {
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(400, 300)));

        var encoded = image.Encode(maxEdge: 1568, region: new Rectangle(350, 250, 200, 200));

        encoded.Width.ShouldBe(50);
        encoded.Height.ShouldBe(50);
        encoded.OffsetX.ShouldBe(350);
        encoded.OffsetY.ShouldBe(250);
    }

    [Fact]
    public void Encode_RegionFullyOutsideImage_ThrowsInvalidRegion()
    {
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(400, 300)));

        var ex = Should.Throw<LabelingValidationException>(() =>
            image.Encode(maxEdge: 1568, region: new Rectangle(1000, 1000, 10, 10)));

        ex.Message.ShouldBe("labeling.invalidRegion");
    }

    [Fact]
    public void Encode_CropThenDownscale_MapsBoxesBackToOriginalPixels()
    {
        // 800x400 crop downscaled so its long edge is 500px: scale factor 1.6.
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(2000, 1000)));
        var encoded = image.Encode(maxEdge: 500, region: new Rectangle(400, 200, 800, 400));

        encoded.Width.ShouldBe(500);
        encoded.Height.ShouldBe(250);

        // Crop'un sol üst köşesi → orijinalde (400, 200); sağ alt köşe → (1200, 600).
        var whole = encoded.ToPage(0, 0, encoded.Width, encoded.Height);
        whole.X.ShouldBe(400);
        whole.Y.ShouldBe(200);
        whole.Width.ShouldBe(800);
        whole.Height.ShouldBe(400);
    }

    [Fact]
    public void ToPage_NoScale_AddsRegionOffset()
    {
        var region = new EncodedRegion(Array.Empty<byte>(), Width: 200, Height: 100, OffsetX: 100, OffsetY: 50, ScaleX: 1, ScaleY: 1);

        var rect = region.ToPage(10, 20, 30, 60);

        rect.X.ShouldBe(110);
        rect.Y.ShouldBe(70);
        rect.Width.ShouldBe(20);
        rect.Height.ShouldBe(40);
    }

    [Fact]
    public void ToPage_ScaledRegion_MultipliesByScaleAndOffset()
    {
        var region = new EncodedRegion(Array.Empty<byte>(), Width: 100, Height: 50, OffsetX: 10, OffsetY: 20, ScaleX: 2, ScaleY: 2);

        var rect = region.ToPage(5, 5, 15, 25);

        rect.X.ShouldBe(20);
        rect.Y.ShouldBe(30);
        rect.Width.ShouldBe(20);
        rect.Height.ShouldBe(40);
    }

    [Fact]
    public void ToPage_SwappedCorners_ProduceSameRectangle()
    {
        var region = new EncodedRegion(Array.Empty<byte>(), Width: 100, Height: 50, OffsetX: 0, OffsetY: 0, ScaleX: 1, ScaleY: 1);

        region.ToPage(30, 40, 10, 20).ShouldBe(region.ToPage(10, 20, 30, 40));
    }

    [Fact]
    public void ToPage_CoordinatesOutsideEncodedImage_AreClampedToIt()
    {
        var region = new EncodedRegion(Array.Empty<byte>(), Width: 100, Height: 50, OffsetX: 10, OffsetY: 20, ScaleX: 2, ScaleY: 2);

        var rect = region.ToPage(-50, -50, 500, 500);

        rect.X.ShouldBe(10);
        rect.Y.ShouldBe(20);
        rect.Width.ShouldBe(200);
        rect.Height.ShouldBe(100);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var image = LabelingImage.Decode(Convert.ToBase64String(Png(4, 4)));

        image.Dispose();
        Should.NotThrow(() => image.Dispose());
    }
}
