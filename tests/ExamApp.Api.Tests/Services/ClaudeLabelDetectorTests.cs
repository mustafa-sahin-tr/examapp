using ExamApp.Api.Services.Labeling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Yalnız ağa çıkmadan erişilebilen yüzey: yapılandırma bayrağı ve anahtar yokken çağrı reddi. Gerçek Anthropic
/// çağrısı bu testlerde yapılmaz (IChatClient'a erişim private; davranış <see cref="LabelingServiceTests"/> içinde mock'lanır).
/// </summary>
public class ClaudeLabelDetectorTests
{
    private static ClaudeLabelDetector Detector(string? apiKey) => new(
        Options.Create(new LabelingAiOptions { ApiKey = apiKey, Models = [] }),
        NullLoggerFactory.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsConfigured_BlankApiKey_IsFalse(string? apiKey)
    {
        using var detector = Detector(apiKey);

        detector.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void IsConfigured_ApiKeySet_IsTrue()
    {
        // Anthropic istemcisi kurulumu ağ çağrısı yapmaz; yalnız anahtarın varlığı kontrol edilir.
        using var detector = Detector("sk-ant-test-key");

        detector.IsConfigured.ShouldBeTrue();
    }

    [Fact]
    public async Task DetectAsync_NotConfigured_ThrowsInvalidOperation()
    {
        using var detector = Detector(null);
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(10, 10)));
        var model = new LabelingAiModelOption { Id = "claude-test", DisplayName = "Test" };

        await Should.ThrowAsync<InvalidOperationException>(() => detector.DetectAsync(image, model));
    }

    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
