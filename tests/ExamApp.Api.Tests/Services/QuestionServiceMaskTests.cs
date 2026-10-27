using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #581: <see cref="QuestionService.SaveBulkQuestion"/> soru kırpmasındaki maskeleri (basılı cevap) yüklemeden ve
/// kırpma hash'inden önce opak beyazla boyar; geçersiz maske hiçbir şey yazılmadan reddedilir.
/// </summary>
public class QuestionServiceMaskTests : IDisposable
{
    private const int ActingUserId = 7;
    private const int PageWidth = 120;
    private const int PageHeight = 80;

    // Soru bölgesi: sayfada (20, 10), 60x40. Maske (bölgeye göre) (30, 5) 10x8 + bölgeden taşan (55, 35) 20x20.
    private const int QX = 20, QY = 10, QW = 60, QH = 40;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly Dictionary<string, byte[]> _uploads = new();

    public QuestionServiceMaskTests()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                using var ms = new MemoryStream();
                ci.Arg<Stream>().CopyTo(ms);
                _uploads[ci.ArgAt<string>(1)] = ms.ToArray();
                return Task.FromResult("/img/b/" + ci.ArgAt<string>(1));
            });
    }

    public void Dispose() => _db.Dispose();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    /// <summary>Her pikseli farklı, beyaz içermeyen sayfa (PNG → kayıpsız, pikseller birebir karşılaştırılır).</summary>
    private static byte[] PagePng()
    {
        using var bitmap = new SKBitmap(PageWidth, PageHeight);
        for (var y = 0; y < PageHeight; y++)
        for (var x = 0; x < PageWidth; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)(x * 2), (byte)(y * 3), (byte)((x + y) % 200), 255));
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static SKColor PagePixel(int x, int y) => new((byte)(x * 2), (byte)(y * 3), (byte)((x + y) % 200), 255);

    private static List<RegionBoxDto> Masks() =>
    [
        new() { X = 30, Y = 5, Width = 10, Height = 8 },
        new() { X = 55, Y = 35, Width = 20, Height = 20 }, // sağ alt köşeden taşar → kırpılır
    ];

    private static bool InsideMask(int x, int y) =>
        (x is >= 30 and < 40 && y is >= 5 and < 13) || (x >= 55 && y >= 35);

    private static BulkQuestionDto Question(List<RegionBoxDto>? masks, string type = "dragDropLabeling") => new()
    {
        Name = "S1",
        X = QX,
        Y = QY,
        Width = QW,
        Height = QH,
        InteractionType = type,
        Masks = masks,
        Answers = type == "mcq"
            ?
            [
                new() { Label = "A", X = QX + 2, Y = QY + 30, Width = 20, Height = 8, IsCorrect = true },
                new() { Label = "B", X = QX + 32, Y = QY + 30, Width = 26, Height = 8 },
            ]
            : [],
    };

    private static BulkQuestionCreateDto Bulk(params BulkQuestionDto[] questions) => new()
    {
        ImageData = "data:image/png;base64," + Convert.ToBase64String(PagePng()),
        Passages = [],
        Questions = questions.ToList(),
        Header = new HeaderInfo { TestId = null },
    };

    private static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes).ShouldNotBeNull();

    [Fact]
    public async Task BulkSave_PaintsMasksWhiteInTheUploadedQuestionImage_AndLeavesTheRestUnchanged()
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(Bulk(Question(Masks())), ActingUserId);
        result.Success.ShouldBeTrue(result.Message);

        var questionJpg = _uploads.Single(u => u.Key.EndsWith("/question.jpg")).Value;
        using var stored = Decode(questionJpg);
        (stored.Width, stored.Height).ShouldBe((QW, QH));
        for (var y = 0; y < QH; y++)
        for (var x = 0; x < QW; x++)
            stored.GetPixel(x, y).ShouldBe(InsideMask(x, y) ? SKColors.White : PagePixel(QX + x, QY + y), $"({x},{y})");

        // question-v2 (etkileşimli tipte tam boy) da maskeli
        using var v2 = Decode(_uploads.Single(u => u.Key.EndsWith("/question-v2.jpg")).Value);
        v2.GetPixel(35, 8).ShouldBe(SKColors.White);
    }

    [Fact]
    public async Task BulkSave_McqOptionCropsAreCutFromTheMaskedImage()
    {
        // B şıkkı (bölgeye göre 32..58 x 30..38) maskenin (55.., 35..) köşesine biner.
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Bulk(Question(Masks(), "mcq")), ActingUserId)).Success.ShouldBeTrue();

        var answer = await ReadAnswerAsync("B");
        using var b = Decode(_uploads.Single(u => u.Key.EndsWith($"/{answer.Id}.jpg")).Value);
        b.GetPixel(55 - 32, 35 - 30).ShouldBe(SKColors.White);
        b.GetPixel(0, 0).ShouldBe(PagePixel(QX + 32, QY + 30));
    }

    private async Task<Answer> ReadAnswerAsync(string label)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Answers.AsNoTracking().SingleAsync(a => a.Text == label);
    }

    [Fact]
    public async Task BulkSave_WithoutMasks_StoresTheCropUnchanged()
    {
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Bulk(Question(null)), ActingUserId)).Success.ShouldBeTrue();

        using var stored = Decode(_uploads.Single(u => u.Key.EndsWith("/question.jpg")).Value);
        stored.GetPixel(35, 8).ShouldBe(PagePixel(QX + 35, QY + 8));
    }

    [Fact]
    public async Task BulkSave_CropHashIsComputedFromTheMaskedImage()
    {
        int bookId;
        await using (var ctx = _db.NewContext())
        {
            bookId = (await new BookRegistryService(ctx, TimeProvider.System)
                .CreateAsync(new LabelingBookSaveRequestDto { Name = "Maske Kitabı" })).Id;
        }

        var question = Question(Masks());
        question.Source = new BulkQuestionSourceInfo(bookId, 3, null, null, null);
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(Bulk(question), ActingUserId)).Success.ShouldBeTrue();

        await using var read = _db.NewContext();
        var source = await read.QuestionSources.AsNoTracking().SingleAsync();
        var uploaded = _uploads.Single(u => u.Key.EndsWith("/question.jpg")).Value;
        source.CropSha256.ShouldBe(QuestionCropHash.Of(uploaded));

        // Kayıt öncesi tekrar kontrolü (QuestionCropHash.Compute) aynı maskelerle aynı hash'i üretir; maskesiz farklıdır.
        var boxes = new List<(int, int, int, int)> { (QX, QY, QW, QH) };
        var page = PagePng();
        QuestionCropHash.Compute(new ImageHelper(), page, boxes, [Masks()]).Single().ShouldBe(source.CropSha256);
        QuestionCropHash.Compute(new ImageHelper(), page, boxes).Single().ShouldNotBe(source.CropSha256);
    }

    public static TheoryData<double, double, double, double> InvalidMasks => new()
    {
        { -1, 0, 5, 5 },
        { 0, 0, 0, 5 },
        { 0, 0, 5, 0 },
        { QW, 0, 5, 5 },        // bölgenin sağında, kesişmez
        { 0, QH + 3, 5, 5 },    // bölgenin altında, kesişmez
    };

    [Theory]
    [MemberData(nameof(InvalidMasks))]
    public async Task BulkSave_InvalidMask_IsRejectedAsInvalidInput_BeforeAnythingIsWritten(double x, double y, double w, double h)
    {
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(
                Bulk(Question([new() { X = x, Y = y, Width = w, Height = h }])), ActingUserId);

        AssertRejected(result);
    }

    [Fact]
    public async Task BulkSave_MoreThanTwentyMasks_IsRejected()
    {
        var masks = Enumerable.Range(0, QuestionImageMasks.MaxMasks + 1)
            .Select(i => new RegionBoxDto { X = i, Y = 0, Width = 1, Height = 1 }).ToList();
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).SaveBulkQuestion(Bulk(Question(masks)), ActingUserId);

        AssertRejected(result);
    }

    private void AssertRejected(ResponseBaseDto result)
    {
        result.Success.ShouldBeFalse();
        result.ShouldBeOfType<QuestionSavedDto>().InvalidInput.ShouldBeTrue();
        result.Message.ShouldContain("S1");
        result.Message.ShouldContain("maske"); // tr kaynağı (questions.invalidMask), anahtar değil
        _uploads.ShouldBeEmpty();
        using var ctx = _db.NewContext();
        ctx.Questions.Count().ShouldBe(0);
    }

    [Fact]
    public async Task BulkSave_MaskIsPaintedOnThePage_SoOverlappingPassageAndOtherQuestionAreMaskedToo()
    {
        // Kapsam (passage) sayfada (0, 0) 100x60 — soruyu içine alır; ikinci soru (50, 20) 40x30 birinciyle örtüşür.
        // Birinci sorunun maskesi (bölgeye göre (30, 5) 10x8 → sayfada (50, 15)..(60, 23)) üçüne de biner.
        var first = Question([new() { X = 30, Y = 5, Width = 10, Height = 8 }]);
        first.PassageId = "p1";
        var second = new BulkQuestionDto
        {
            Name = "S2", X = 50, Y = 20, Width = 40, Height = 30, InteractionType = "dragDropLabeling", Answers = [],
        };
        var bulk = Bulk(first, second);
        bulk.Passages = [new BulkPassageDto { Id = "p1", X = 0, Y = 0, Width = 100, Height = 60 }];

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(bulk, ActingUserId)).Success.ShouldBeTrue();

        using var passage = Decode(_uploads.Single(u => u.Key.StartsWith("passages/")).Value);
        passage.GetPixel(55, 18).ShouldBe(SKColors.White);
        passage.GetPixel(45, 18).ShouldBe(PagePixel(45, 18));

        var questionImages = _uploads.Where(u => u.Key.EndsWith("/question.jpg")).Select(u => Decode(u.Value)).ToList();
        try
        {
            questionImages.Count.ShouldBe(2);
            var firstImage = questionImages.Single(b => b.Width == QW);
            var secondImage = questionImages.Single(b => b.Width == 40);
            firstImage.GetPixel(55 - QX, 18 - QY).ShouldBe(SKColors.White);
            secondImage.GetPixel(55 - 50, 21 - 20).ShouldBe(SKColors.White); // sayfada (55, 21)
            secondImage.GetPixel(15, 10).ShouldBe(PagePixel(65, 30));        // maske dışı
        }
        finally
        {
            questionImages.ForEach(b => b.Dispose());
        }
    }

    [Fact]
    public async Task BulkSave_MaskExtendingBeyondItsQuestion_IsClampedToTheQuestionBox()
    {
        // Maske (bölgeye göre) (50, 30) 40x40 → sayfada bölge dışına taşar; sayfa (QX+QW, ...) etkilenmez.
        var first = Question([new() { X = 50, Y = 30, Width = 40, Height = 40 }]);
        first.PassageId = "p1";
        var bulk = Bulk(first);
        bulk.Passages = [new BulkPassageDto { Id = "p1", X = 0, Y = 0, Width = PageWidth, Height = PageHeight }];

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(bulk, ActingUserId)).Success.ShouldBeTrue();

        using var passage = Decode(_uploads.Single(u => u.Key.StartsWith("passages/")).Value);
        passage.GetPixel(QX + QW - 1, QY + QH - 1).ShouldBe(SKColors.White);
        passage.GetPixel(QX + QW, QY + QH - 1).ShouldBe(PagePixel(QX + QW, QY + QH - 1));
        passage.GetPixel(QX + QW - 1, QY + QH).ShouldBe(PagePixel(QX + QW - 1, QY + QH));
    }

    [Fact]
    public void Masks_BindFromClientJson()
    {
        var json = """{"name":"S1","x":0,"y":0,"width":10,"height":10,"masks":[{"x":1,"y":2,"width":3,"height":4}]}""";
        var dto = System.Text.Json.JsonSerializer.Deserialize<BulkQuestionDto>(json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var mask = dto.Masks.ShouldNotBeNull().ShouldHaveSingleItem();
        (mask.X, mask.Y, mask.Width, mask.Height).ShouldBe((1d, 2d, 3d, 4d));
    }
}
