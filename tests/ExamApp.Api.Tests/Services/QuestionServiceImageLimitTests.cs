using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #597: sayfa görseli <see cref="SkiaImaging"/> sınırını aşınca toplu kayıt genel "kaydedilemedi" yerine
/// <see cref="QuestionService.ImageTooLargeErrorCode"/> ile geçersiz girdi döner (controller 400'e eşler); hiçbir şey yazılmaz.
/// </summary>
public class QuestionServiceImageLimitTests : IDisposable
{
    private const int ActingUserId = 7;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    public QuestionServiceImageLimitTests()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => Task.FromResult("/img/b/" + ci.ArgAt<string>(1)));
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Uzun kenarı <see cref="SkiaImaging.MaxEdge"/>'i aşan, piksel sayısı küçük (hafif) PNG.</summary>
    private static byte[] OversizedPng()
    {
        using var bitmap = new SKBitmap(SkiaImaging.MaxEdge + 1, 2);
        bitmap.Erase(SKColors.Gray);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public async Task BulkSave_PageImageOverPixelLimits_IsRejectedWithImageTooLarge_AndNothingIsWritten()
    {
        var bulk = new BulkQuestionCreateDto
        {
            ImageData = "data:image/png;base64," + Convert.ToBase64String(OversizedPng()),
            Passages = [],
            Questions =
            [
                new()
                {
                    Name = "S1", X = 0, Y = 0, Width = 20, Height = 2,
                    Answers =
                    [
                        new() { Label = "A", X = 0, Y = 0, Width = 5, Height = 1, IsCorrect = true },
                        new() { Label = "B", X = 6, Y = 0, Width = 5, Height = 1 },
                    ],
                },
            ],
            Header = new HeaderInfo { TestId = null },
        };

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await new QuestionService(ctx, new ImageHelper(), _minio).SaveBulkQuestion(bulk, ActingUserId);

        result.Success.ShouldBeFalse();
        var saved = result.ShouldBeOfType<QuestionSavedDto>();
        saved.InvalidInput.ShouldBeTrue();
        saved.ErrorCode.ShouldBe(QuestionService.ImageTooLargeErrorCode);
        saved.Message.ShouldNotBeNullOrWhiteSpace();

        await using var read = _db.NewContext();
        (await read.Questions.CountAsync()).ShouldBe(0);
    }
}
