using System.Text.Json;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ExamApp.Api.Tests.Support;

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

        await Should.ThrowAsync<InvalidOperationException>(() => detector.DetectAsync(image, model, LabelingPromptSettings.Default));
    }

    private static byte[] Png(int width, int height) => TestImages.Png(width, height);

    // ---------- Issue #578: blok türleri (sahte IChatClient; ağa çıkılmaz) ----------

    private static readonly LabelingAiModelOption TestModel = new() { Id = "claude-test", DisplayName = "Test" };

    private static ClaudeLabelDetector Detector(FakeChatClient chat, int maxBlocks = 40) => new(
        chat,
        Options.Create(new LabelingAiOptions { ApiKey = "sk-ant-test", Models = [TestModel], MaxQuestionsPerPage = maxBlocks }),
        NullLoggerFactory.Instance);

    private static object Block(string kind, int x1, int y1, int x2, int y2, string component = "none",
        string interactionType = "none") =>
        new { kind, component, interactionType, x1, y1, x2, y2 };

    private static async Task<(ClaudeDetection Detection, FakeChatClient Chat)> DetectAsync(
        object[] blocks, LabelingPromptSettings? prompt = null, int maxBlocks = 40, int optionCount = 2,
        CropMode cropMode = CropMode.Ok)
    {
        var chat = new FakeChatClient(JsonSerializer.Serialize(new { blocks }), optionCount, cropMode);
        using var detector = Detector(chat, maxBlocks);
        // 1000x1400 < MaxImageEdge (1568): Claude'un gördüğü uzay = sayfa uzayı, koordinatlar ölçeklenmez.
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(1000, 1400)));
        var detection = await detector.DetectAsync(image, TestModel, prompt ?? LabelingPromptSettings.Default);
        return (detection, chat);
    }

    [Fact]
    public async Task DetectAsync_MixedPage_OnePageCallAndOptionCallsOnlyForMcq()
    {
        var (detection, chat) = await DetectAsync(
        [
            Block("info_box", 50, 50, 950, 200),
            Block("mcq_question", 60, 220, 900, 500),
            Block("activity", 60, 520, 900, 700, "A", "matrixChoice"),
            Block("activity", 60, 720, 900, 900, "B", "other"),
            Block("activity", 60, 920, 900, 1100, "C", "match"),
            Block("activity", 60, 1110, 500, 1200, "G", "other"),
            Block("answer_key", 520, 1110, 900, 1200),
            Block("noise", 800, 1250, 950, 1390),
            Block("mcq_question", 60, 1210, 500, 1390),
        ]);

        // Sayfa başına TEK 1. aşama çağrısı; şık aşaması yalnız iki mcq_question için.
        chat.PageCalls.ShouldBe(1);
        chat.CropCalls.ShouldBe(2);

        var p = detection.Predictions.Predictions;
        p.Select(x => x.Kind).ShouldBe(
        [
            "info_box", "mcq_question", "activity", "activity", "activity", "activity", "answer_key", "noise", "mcq_question",
        ]);
        p.Select(x => x.Component).ShouldBe([null, null, "A", "B", "C", "G", null, null, null]);
        p.Select(x => x.InteractionType).ShouldBe([null, null, "matrixChoice", "slotFill", "match", null, null, null, null]);

        // Şıklar yalnız MCQ'da; diğer bloklar şıksız.
        p.Where(x => x.Kind != LabelingBlocks.McqQuestion).ShouldAllBe(x => x.Subpredictions.Count == 0);
        p.Where(x => x.Kind == LabelingBlocks.McqQuestion).ShouldAllBe(x => x.Subpredictions.Count == 2);
        p.SelectMany(x => x.Subpredictions).ShouldAllBe(o => o.Kind == null && o.Component == null && o.InteractionType == null);

        // Token'lar: sayfa + iki kırpma.
        detection.InputTokens.ShouldBe(3 * FakeChatClient.InputTokensPerCall);
    }

    [Fact]
    public async Task DetectAsync_NonMcqBlock_GetsQuestionPaddingAndPageClampButNoOptionExpansion()
    {
        var prompt = LabelingPromptSettings.Default with { QuestionPadding = 10, ExpandQuestionToOptions = true };

        var (detection, chat) = await DetectAsync([Block("info_box", 5, 100, 990, 300)], prompt);

        chat.CropCalls.ShouldBe(0);
        var box = detection.Predictions.Predictions.Single();
        box.X.ShouldBe(0);            // 5 - 10 → sayfa sınırına kırpılır
        box.Y.ShouldBe(90);
        box.Width.ShouldBe(1000);     // 0..1000 (990 + 10 = 1000 = sayfa genişliği)
        box.Height.ShouldBe(220);
    }

    [Fact]
    public async Task DetectAsync_McqBlock_KeepsExistingQuestionAndOptionBehaviour()
    {
        var prompt = LabelingPromptSettings.Default with { QuestionPadding = 6, OptionPadding = 4, ExpandQuestionToOptions = true };

        var (detection, _) = await DetectAsync([Block("mcq_question", 100, 100, 300, 200)], prompt);

        var box = detection.Predictions.Predictions.Single();
        box.Kind.ShouldBe(LabelingBlocks.McqQuestion);
        box.Subpredictions.Count.ShouldBe(2);
        // Soru kutusu = pay + şıkların birleşimi (şıklar kırpma içinde sahte koordinatlarla dönüyor).
        box.Subpredictions.ShouldAllBe(o =>
            o.X >= box.X && o.Y >= box.Y && o.X + o.Width <= box.X + box.Width && o.Y + o.Height <= box.Y + box.Height);
    }

    [Fact]
    public async Task DetectAsync_UnknownKind_IsReturnedAsNoiseWithoutOptionCall()
    {
        var (detection, chat) = await DetectAsync([Block("qr_code", 100, 100, 300, 300, "A", "matrixChoice")]);

        chat.CropCalls.ShouldBe(0);
        var box = detection.Predictions.Predictions.Single();
        box.Kind.ShouldBe(LabelingBlocks.Noise);
        box.Component.ShouldBeNull();
        box.InteractionType.ShouldBeNull();
    }

    [Fact]
    public async Task DetectAsync_NoiseAndInfoBoxes_DoNotPushMcqsPastMaxQuestionsPerPage()
    {
        // Review W3: başlık/altlık/anlatım soru sınırına sayılmaz; 2 soru sınırında iki MCQ de kalır.
        var (detection, chat) = await DetectAsync(
        [
            Block("noise", 10, 10, 200, 60),
            Block("info_box", 10, 100, 900, 200),
            Block("noise", 10, 210, 200, 260),
            Block("mcq_question", 10, 300, 900, 400),
            Block("mcq_question", 10, 500, 900, 600),
        ], maxBlocks: 2);

        detection.Predictions.Predictions.Select(p => p.Kind)
            .ShouldBe(["noise", "info_box", "noise", "mcq_question", "mcq_question"]);
        chat.CropCalls.ShouldBe(2);
    }

    [Fact]
    public async Task DetectAsync_OptionCallCandidates_AreCappedAtMaxQuestionsPerPageButOtherKindsContinue()
    {
        // MCQ + bileşensiz activity birlikte MaxQuestionsPerPage (2) ile sınırlı → sayfa başına şık çağrısı ≤ 2;
        // sınırı aşan aday atlanır, sonraki diğer türler eklenir. Bileşenli etkinlik adaya sayılmaz.
        var (detection, chat) = await DetectAsync(
        [
            Block("mcq_question", 10, 10, 900, 100),
            Block("activity", 10, 110, 900, 200),
            Block("mcq_question", 10, 210, 900, 300),
            Block("activity", 10, 310, 900, 400, "A", "matrixChoice"),
            Block("info_box", 10, 410, 900, 500),
        ], maxBlocks: 2, optionCount: 4);

        chat.CropCalls.ShouldBe(2);
        var p = detection.Predictions.Predictions;
        p.Select(x => (x.Kind, x.Component)).ShouldBe(
        [
            ("mcq_question", null), ("mcq_question", null), ("activity", "A"), ("info_box", null),
        ]);
        p[1].Y.ShouldBeLessThan(200); // ikinci kutu yeniden sınıflanan bileşensiz etkinlik, atlanan üçüncü MCQ değil
    }

    [Fact]
    public async Task DetectAsync_TotalBlocks_AreCappedAtThreeTimesMaxQuestionsPerPage()
    {
        var blocks = Enumerable.Range(0, 10).Select(i => Block("noise", 10, 10 + i * 100, 200, 60 + i * 100)).ToArray();

        var (detection, _) = await DetectAsync(blocks, maxBlocks: 2);

        detection.Predictions.Predictions.Count.ShouldBe(2 * ClaudeLabelDetector.TotalBlockFactor);
    }

    [Fact]
    public async Task DetectAsync_McqOptionCallTruncated_CountsItsTokensAndKeepsQuestionWithoutOptions()
    {
        var (detection, chat) = await DetectAsync([Block("mcq_question", 100, 100, 400, 300)], cropMode: CropMode.Truncate);

        chat.CropCalls.ShouldBe(1);
        var box = detection.Predictions.Predictions.Single();
        box.Kind.ShouldBe(LabelingBlocks.McqQuestion);
        box.Subpredictions.ShouldBeEmpty();
        detection.InputTokens.ShouldBe(2 * FakeChatClient.InputTokensPerCall);
        detection.OutputTokens.ShouldBe(2 * FakeChatClient.OutputTokensPerCall);
    }

    [Fact]
    public async Task DetectAsync_ProbeOptionCallTruncated_CountsItsTokensAndStaysActivity()
    {
        var (detection, _) = await DetectAsync([Block("activity", 100, 100, 400, 300)], cropMode: CropMode.Truncate);

        detection.Predictions.Predictions.Single().Kind.ShouldBe(LabelingBlocks.Activity);
        detection.InputTokens.ShouldBe(2 * FakeChatClient.InputTokensPerCall);
        detection.OutputTokens.ShouldBe(2 * FakeChatClient.OutputTokensPerCall);
    }

    [Fact]
    public async Task DetectAsync_OptionCallWithoutResponse_CountsNoExtraTokens()
    {
        var (detection, _) = await DetectAsync([Block("mcq_question", 100, 100, 400, 300)], cropMode: CropMode.Throw);

        detection.InputTokens.ShouldBe(FakeChatClient.InputTokensPerCall);
    }

    [Fact]
    public async Task DetectAsync_PageCallTruncated_ThrowsWithPageUsage()
    {
        var chat = new FakeChatClient(JsonSerializer.Serialize(new { blocks = Array.Empty<object>() }), truncatePage: true);
        using var detector = Detector(chat);
        using var image = LabelingImage.Decode(Convert.ToBase64String(Png(200, 200)));

        var ex = await Should.ThrowAsync<LabelingAiException>(() =>
            detector.DetectAsync(image, TestModel, LabelingPromptSettings.Default));

        ex.Usage.ShouldBe(new LabelingAiUsage(FakeChatClient.InputTokensPerCall, FakeChatClient.OutputTokensPerCall));
        chat.CropCalls.ShouldBe(0);
    }

    [Fact]
    public async Task DetectAsync_PageCall_UsesTwelveThousandOutputTokens()
    {
        var (_, chat) = await DetectAsync([]);

        chat.PageMaxOutputTokens.ShouldBe(12000);
    }

    [Fact]
    public async Task DetectAsync_TinyBlocks_AreDroppedBeforeTheCap()
    {
        var (detection, _) = await DetectAsync(
        [
            Block("noise", 10, 10, 12, 12),
            Block("info_box", 10, 100, 900, 200),
        ], maxBlocks: 1);

        detection.Predictions.Predictions.Single().Kind.ShouldBe(LabelingBlocks.InfoBox);
    }

    [Fact]
    public async Task DetectAsync_PageSchema_CarriesKindComponentAndInteractionType()
    {
        var (_, chat) = await DetectAsync([]);

        var schema = chat.PageSchema.ShouldNotBeNull();
        schema.ShouldContain("\"kind\"");
        schema.ShouldContain("\"component\"");
        schema.ShouldContain("\"interactionType\"");
        schema.ShouldContain("mcq_question, activity, info_box, answer_key, noise");
    }

    [Fact]
    public async Task DetectAsync_PredictionsJson_OmitsNullBlockFieldsAndUsesSnakeCase()
    {
        var (detection, _) = await DetectAsync(
        [
            Block("activity", 60, 520, 900, 700, "C", "dragPlace"),
            Block("info_box", 60, 720, 900, 900),
        ]);

        var json = JsonSerializer.Serialize(detection.Predictions);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("predictions");
        items[0].GetProperty("kind").GetString().ShouldBe("activity");
        items[0].GetProperty("component").GetString().ShouldBe("C");
        items[0].GetProperty("interaction_type").GetString().ShouldBe("dragPlace");
        items[1].GetProperty("kind").GetString().ShouldBe("info_box");
        items[1].TryGetProperty("component", out _).ShouldBeFalse();
        items[1].TryGetProperty("interaction_type", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task DetectAsync_ComponentlessActivityWithFourOptions_IsReclassifiedAsMcqWithOptions()
    {
        var prompt = LabelingPromptSettings.Default with { QuestionPadding = 6, ExpandQuestionToOptions = true };

        var (detection, chat) = await DetectAsync(
            [Block("activity", 100, 100, 400, 300, "none", "other")], prompt, optionCount: 4);

        chat.CropCalls.ShouldBe(1);
        var box = detection.Predictions.Predictions.Single();
        box.Kind.ShouldBe(LabelingBlocks.McqQuestion);
        box.Component.ShouldBeNull();
        box.InteractionType.ShouldBeNull();
        box.Subpredictions.Count.ShouldBe(4);
        box.Subpredictions.ShouldAllBe(o =>
            o.X >= box.X && o.Y >= box.Y && o.X + o.Width <= box.X + box.Width && o.Y + o.Height <= box.Y + box.Height);
        detection.InputTokens.ShouldBe(2 * FakeChatClient.InputTokensPerCall);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task DetectAsync_ComponentlessActivityWithFewerThanTwoOptions_StaysActivityWithoutOptions(int optionCount)
    {
        var prompt = LabelingPromptSettings.Default with { QuestionPadding = 6 };

        var (detection, chat) = await DetectAsync(
            [Block("activity", 100, 100, 400, 300)], prompt, optionCount: optionCount);

        chat.CropCalls.ShouldBe(1);
        var box = detection.Predictions.Predictions.Single();
        box.Kind.ShouldBe(LabelingBlocks.Activity);
        box.Component.ShouldBeNull();
        box.Subpredictions.ShouldBeEmpty();
        // Yalnız soru payı (şıklara genişleme yok); probe token'ı yine sayılır.
        (box.X, box.Y, box.Width, box.Height).ShouldBe((94, 94, 312, 212));
        detection.InputTokens.ShouldBe(2 * FakeChatClient.InputTokensPerCall);
    }

    [Fact]
    public async Task DetectAsync_ComponentlessActivityOptionCallFails_StaysActivity()
    {
        var (detection, chat) = await DetectAsync([Block("activity", 100, 100, 400, 300)], cropMode: CropMode.Throw);

        chat.CropCalls.ShouldBe(1);
        var box = detection.Predictions.Predictions.Single();
        box.Kind.ShouldBe(LabelingBlocks.Activity);
        box.Subpredictions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("A", "matrixChoice")]
    [InlineData("C", "match")]
    [InlineData("H", "other")]
    public async Task DetectAsync_ActivityWithComponent_GetsNoOptionCall(string component, string interactionType)
    {
        var (detection, chat) = await DetectAsync(
            [Block("activity", 100, 100, 400, 300, component, interactionType)], optionCount: 4);

        chat.CropCalls.ShouldBe(0);
        var box = detection.Predictions.Predictions.Single();
        box.Kind.ShouldBe(LabelingBlocks.Activity);
        box.Component.ShouldBe(component);
        box.Subpredictions.ShouldBeEmpty();
    }

    /// <summary>
    public enum CropMode
    {
        Ok,
        /// <summary>İstek yanıt almadan düşer (usage yok).</summary>
        Throw,
        /// <summary>Yanıt kesik (FinishReason Length) ama token ödendi.</summary>
        Truncate,
    }

    /// 1. aşama (MaxOutputTokens > 4000) sabit blok listesi, 2. aşama (kırpma) iki şık döner. Kırpma çağrıları eşzamanlı
    /// olabilir: sayaçlar Interlocked.
    /// </summary>
    private sealed class FakeChatClient(
        string pageJson, int optionCount = 2, CropMode cropMode = CropMode.Ok, bool truncatePage = false) : IChatClient
    {
        public const int InputTokensPerCall = 10;
        public const int OutputTokensPerCall = 5;

        public int? PageMaxOutputTokens { get; private set; }
        private int _pageCalls;
        private int _cropCalls;

        public int PageCalls => _pageCalls;

        public int CropCalls => _cropCalls;

        public string? PageSchema { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            string json;
            var finish = ChatFinishReason.Stop;
            if (options?.MaxOutputTokens > 4000)
            {
                PageMaxOutputTokens = options.MaxOutputTokens;
                if (truncatePage)
                    finish = ChatFinishReason.Length;
                Interlocked.Increment(ref _pageCalls);
                PageSchema = (options.ResponseFormat as ChatResponseFormatJson)?.Schema?.GetRawText();
                json = pageJson;
            }
            else
            {
                Interlocked.Increment(ref _cropCalls);
                if (cropMode == CropMode.Throw)
                    throw new HttpRequestException("simulated Claude failure");
                if (cropMode == CropMode.Truncate)
                    finish = ChatFinishReason.Length;
                // Seçenekler tek satırda (A) B) C) D) yan yana): 20 px aralıklı, kırpma içinde.
                json = JsonSerializer.Serialize(new
                {
                    options = Enumerable.Range(0, optionCount)
                        .Select(i => new { label = ((char)('A' + i)).ToString(), x1 = 20 + i * 30, y1 = 20, x2 = 40 + i * 30, y2 = 40 })
                        .ToArray(),
                });
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                FinishReason = finish,
                Usage = new UsageDetails { InputTokenCount = InputTokensPerCall, OutputTokenCount = OutputTokensPerCall },
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
