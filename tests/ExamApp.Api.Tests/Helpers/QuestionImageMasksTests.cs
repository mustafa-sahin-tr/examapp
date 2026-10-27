using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using SkiaSharp;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>Issue #581: maske boyama (opak beyaz, kırpmayla kesişim, dışa yuvarlama) ve doğrulama.</summary>
public class QuestionImageMasksTests
{
    private static readonly SKColor Ink = new(10, 20, 30, 255);

    private static SKBitmap Filled(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(Ink);
        return bitmap;
    }

    private static RegionBoxDto Box(double x, double y, double w, double h) => new() { X = x, Y = y, Width = w, Height = h };

    [Fact]
    public void Apply_PaintsInsideOpaqueWhite_AndLeavesOutsideUnchanged()
    {
        using var bitmap = Filled(20, 10);

        QuestionImageMasks.Apply(bitmap, [Box(5, 2, 4, 3)]).ShouldBe(1);

        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 20; x++)
        {
            var inside = x is >= 5 and < 9 && y is >= 2 and < 5;
            bitmap.GetPixel(x, y).ShouldBe(inside ? SKColors.White : Ink, $"({x},{y})");
        }
    }

    [Fact]
    public void Apply_ClipsToTheBitmap_AndSkipsMasksOutsideIt()
    {
        using var bitmap = Filled(10, 10);

        var painted = QuestionImageMasks.Apply(bitmap, [Box(7, 8, 50, 50), Box(10, 0, 5, 5), Box(0, 30, 5, 5)]);

        painted.ShouldBe(1);
        bitmap.GetPixel(9, 9).ShouldBe(SKColors.White);
        bitmap.GetPixel(7, 8).ShouldBe(SKColors.White);
        bitmap.GetPixel(6, 9).ShouldBe(Ink);
        bitmap.GetPixel(9, 7).ShouldBe(Ink);
        bitmap.GetPixel(0, 0).ShouldBe(Ink);
    }

    [Fact]
    public void Apply_RoundsFractionalEdgesOutward()
    {
        using var bitmap = Filled(10, 10);

        QuestionImageMasks.Apply(bitmap, [Box(2.6, 3.2, 1.1, 1.0)]);

        // [2.6, 3.7) x [3.2, 4.2) → piksel 2..3 x 3..4
        bitmap.GetPixel(2, 3).ShouldBe(SKColors.White);
        bitmap.GetPixel(3, 4).ShouldBe(SKColors.White);
        bitmap.GetPixel(4, 3).ShouldBe(Ink);
        bitmap.GetPixel(2, 5).ShouldBe(Ink);
        bitmap.GetPixel(1, 3).ShouldBe(Ink);
    }

    [Fact]
    public void Apply_OnTransparentPixels_ProducesOpaqueWhite()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);

        QuestionImageMasks.Apply(bitmap, [Box(0, 0, 2, 2)]);

        bitmap.GetPixel(1, 1).ShouldBe(SKColors.White);
        bitmap.GetPixel(3, 3).Alpha.ShouldBe((byte)0);
    }

    [Fact]
    public void Apply_NullOrEmpty_DoesNothing()
    {
        using var bitmap = Filled(3, 3);
        QuestionImageMasks.Apply(bitmap, null).ShouldBe(0);
        QuestionImageMasks.Apply(bitmap, []).ShouldBe(0);
        bitmap.GetPixel(1, 1).ShouldBe(Ink);
    }

    [Fact]
    public void IsValid_AcceptsNullEmptyAndUpToTheLimit()
    {
        QuestionImageMasks.IsValid(null).ShouldBeTrue();
        QuestionImageMasks.IsValid([]).ShouldBeTrue();
        QuestionImageMasks.IsValid(Enumerable.Range(0, QuestionImageMasks.MaxMasks).Select(i => Box(i, 0, 1, 1)).ToList())
            .ShouldBeTrue();
        // bölgeden taşan ama kesişen maske geçerli (boyamada kırpılır)
        QuestionImageMasks.IsValid([Box(55, 35, 50, 50)], 60, 40).ShouldBeTrue();
        QuestionImageMasks.IsValid([Box(59.5, 39.5, 1, 1)], 60, 40).ShouldBeTrue();
    }

    [Theory]
    [InlineData(60, 0)]
    [InlineData(0, 40)]
    [InlineData(500, 500)]
    public void IsValid_RejectsMasksThatDoNotIntersectTheRegion(double x, double y) =>
        QuestionImageMasks.IsValid([Box(x, y, 5, 5)], 60, 40).ShouldBeFalse();

    [Fact]
    public void ApplyToPage_OffsetsByTheRegion_AndClampsToIt()
    {
        using var page = Filled(30, 20);

        // bölge (10, 5) 8x6; maske (bölgeye göre) (4, 2) 20x20 → sayfada (14, 7)..(18, 11)
        QuestionImageMasks.ApplyToPage(page, 10, 5, 8, 6, [Box(4, 2, 20, 20)]).ShouldBe(1);

        for (var y = 0; y < 20; y++)
        for (var x = 0; x < 30; x++)
            page.GetPixel(x, y).ShouldBe(x is >= 14 and < 18 && y is >= 7 and < 11 ? SKColors.White : Ink, $"({x},{y})");
    }

    public static TheoryData<double, double, double, double> InvalidBoxes => new()
    {
        { -1, 0, 5, 5 },
        { 0, -0.5, 5, 5 },
        { 0, 0, 0, 5 },
        { 0, 0, 5, 0.5 },
        { double.NaN, 0, 5, 5 },
        { 0, 0, double.PositiveInfinity, 5 },
    };

    [Theory]
    [MemberData(nameof(InvalidBoxes))]
    public void IsValid_RejectsNegativeTinyOrNonFiniteBoxes(double x, double y, double w, double h) =>
        QuestionImageMasks.IsValid([Box(x, y, w, h)]).ShouldBeFalse();

    [Fact]
    public void IsValid_RejectsTooManyOrNullEntries()
    {
        QuestionImageMasks.IsValid(Enumerable.Range(0, QuestionImageMasks.MaxMasks + 1).Select(_ => Box(0, 0, 1, 1)).ToList())
            .ShouldBeFalse();
        QuestionImageMasks.IsValid(new List<RegionBoxDto?> { null }).ShouldBeFalse();
    }
}
