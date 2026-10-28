using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #579: blok kırpması → tipin talimatı + şeması → plan. Sahte <see cref="IChatClient"/> (ağa çıkılmaz): gönderilen
/// talimat (yer tutucular kırpma boyutuyla), şema (tipe göre), kırpma payı ve koordinatların bloğa göre dönüşümü.
/// </summary>
public class ClaudeActivityStructureExtractorTests
{
    private static readonly LabelingAiModelOption Model = new() { Id = "claude-test", DisplayName = "Test" };

    // 1000x1400 < MaxImageEdge (1568): kırpma küçültülmez, kırpma pikseli = sayfa pikseli - kırpma ofseti.
    // Blok (200, 300) 100x50 → kırpma = blok + 16 px = (184, 284) 132x82.
    private static readonly Rectangle Block = new(200, 300, 100, 50);

    private static async Task<(ActivityExtraction Result, FakeChatClient Chat)> ExtractAsync(
        string type, object response, ChatFinishReason? finish = null, string? instruction = null)
    {
        var chat = new FakeChatClient(JsonSerializer.Serialize(response), finish ?? ChatFinishReason.Stop);
        using var extractor = new ClaudeActivityStructureExtractor(
            chat, Options.Create(new LabelingAiOptions { ApiKey = "sk-ant-test", Models = [Model] }), NullLoggerFactory.Instance);
        using var image = LabelingImage.Decode(Convert.ToBase64String(TestImages.Png(1000, 1400)));
        var prompt = instruction is null ? ActivityStructureSettings.Default(type) : new ActivityPromptSettings(type, 3, instruction);
        var result = await extractor.ExtractAsync(image, Block, prompt, Model);
        return (result, chat);
    }

    private static JsonObject Plan(ActivityExtraction result) => JsonNode.Parse(result.PlanJson)!.AsObject();

    private static (int, int, int, int) Xywh(JsonNode? box) =>
        ((int)box!["x"]!, (int)box["y"]!, (int)box["width"]!, (int)box["height"]!);

    [Fact]
    public async Task MatrixChoice_SendsTypeInstructionRenderedForTheCrop_AndMapsToBlockRelativeBoxes()
    {
        var (result, chat) = await ExtractAsync(MatrixChoiceGrader.Type, new
        {
            instruction = "Doğru ise D, yanlış ise Y yazınız.",
            multiPerRow = false,
            columns = new[] { new { label = "D" }, new { label = "Y" } },
            // Kırpmada (16,16) = sayfada (200,300) = bloğun sol üstü.
            rows = new[] { new { label = "Güneş bir yıldızdır.", x1 = 16, y1 = 16, x2 = 116, y2 = 36,
                cells = new[] { new { column = 0, x1 = 80, y1 = 20, x2 = 90, y2 = 30 }, new { column = 1, x1 = 95, y1 = 20, x2 = 105, y2 = 30 } } } },
        });

        chat.Calls.ShouldBe(1);
        chat.Instruction.ShouldStartWith("This image is one activity cropped from a page of a Turkish school book (132x82 px).");
        chat.Instruction.ShouldContain("In each row the student marks");
        chat.ImageSize.ShouldBe((132, 82));
        chat.MaxOutputTokens.ShouldBe(ClaudeActivityStructureExtractor.MaxOutputTokens);
        chat.Schema.ShouldNotBeNull();
        chat.Schema.ShouldContain("multiPerRow");

        result.Instruction.ShouldBe("Doğru ise D, yanlış ise Y yazınız.");
        var plan = Plan(result);
        var row = plan["rows"]![0]!;
        Xywh(row["box"]).ShouldBe((0, 0, 100, 20));
        Xywh(row["cells"]!["c1"]).ShouldBe((64, 4, 10, 10));
        InteractionGraderRegistry.Default.Resolve(MatrixChoiceGrader.Type).ValidateDefinition(result.PlanJson, null).ShouldBeTrue();
        result.InputTokens.ShouldBe(FakeChatClient.InputTokens);
        result.OutputTokens.ShouldBe(FakeChatClient.OutputTokens);
    }

    [Fact]
    public async Task EachType_UsesItsOwnSchema()
    {
        var (_, slot) = await ExtractAsync(SlotFillGrader.Type, new { instruction = "", slots = Array.Empty<object>() });
        var (_, drag) = await ExtractAsync(DragPlaceGrader.Type,
            new { instruction = "", mode = "place", reuse = false, targets = Array.Empty<object>(), cards = Array.Empty<object>() });
        var (_, match) = await ExtractAsync(MatchGrader.Type,
            new { instruction = "", left = Array.Empty<object>(), right = Array.Empty<object>(), prelinked = Array.Empty<object>() });

        slot.Schema.ShouldContain("slots");
        slot.Schema.ShouldNotContain("multiPerRow");
        drag.Schema.ShouldContain("targets");
        drag.Schema.ShouldContain("cards");
        match.Schema.ShouldContain("prelinked");
        slot.Instruction.ShouldContain("writes short answers");
        drag.Instruction.ShouldContain("places given cards");
        match.Instruction.ShouldContain("matches items");
    }

    [Fact]
    public async Task SlotFill_MapsCropCoordinatesAndClampsPaddingAwayFromTheBlock()
    {
        var (result, _) = await ExtractAsync(SlotFillGrader.Type, new
        {
            instruction = "",
            slots = new[]
            {
                // Pay içine taşan kutu (kırpmada 0..30) → blok sol kenarına kırpılır.
                new { label = "", kind = "number", entry = "plain", unit = "", options = Array.Empty<string>(), symbols = Array.Empty<string>(), x1 = 0, y1 = 20, x2 = 30, y2 = 30 },
            },
        });

        Xywh(Plan(result)["slots"]![0]!["box"]).ShouldBe((0, 4, 14, 10));
    }

    [Fact]
    public async Task Crossword_SendsCellSchema_AndMapsWordsToCluesWithoutAnswers()
    {
        // issue #586 (dilim e): hücre koordinatı piksel değil; kırpma/blok dönüşümü uygulanmaz.
        var (result, chat) = await ExtractAsync(CrosswordGrader.Type, new
        {
            instruction = "Bulmacayı çözünüz.",
            rows = 4,
            cols = 4,
            words = new[]
            {
                new { dir = "across", row = 0, col = 0, length = 4, number = 1, clue = "Kanı pompalar" },
                new { dir = "down", row = 0, col = 2, length = 3, number = 2, clue = "Görme organı" },
            },
        });

        chat.Instruction.ShouldStartWith("This image is a crossword puzzle (çengel bulmaca) cropped from a page of a Turkish school book (132x82 px).");
        chat.Instruction.ShouldContain("Never write the answers");
        chat.Schema.ShouldNotBeNull();
        chat.Schema.ShouldContain("words");
        result.Instruction.ShouldBe("Bulmacayı çözünüz.");
        var plan = Plan(result);
        ((int)plan["rows"]!, (int)plan["cols"]!).ShouldBe((4, 4));
        var clues = plan["clues"]!.AsArray();
        clues.Select(c => ((string)c!["dir"]!, (int)c["row"]!, (int)c["col"]!, (int)c["length"]!))
            .ShouldBe([("across", 0, 0, 4), ("down", 0, 2, 3)]);
        InteractionGraderRegistry.Default.Resolve(CrosswordGrader.Type).ValidateDefinition(result.PlanJson, null).ShouldBeTrue();
    }

    [Fact]
    public async Task TextSelect_SendsLineSchema_AndTokenizesLinesIntoBlockRelativeTokens()
    {
        // issue #582: kırpmada (16,16)-(116,26) = blokta (0,0) 100x10; "Kuş dolaş-" (L=10) + "mış." → "dolaşmış." iki kutu.
        var (result, chat) = await ExtractAsync(TextSelectGrader.Type, new
        {
            instruction = "Yazım yanlışlarını bulunuz.",
            mode = "select",
            style = "circle",
            palette = Array.Empty<string>(),
            lines = new[]
            {
                new { text = "Kuş dolaş-", x1 = 16, y1 = 16, x2 = 116, y2 = 26 },
                new { text = "mış.", x1 = 16, y1 = 36, x2 = 56, y2 = 46 },
            },
        });

        chat.Schema.ShouldNotBeNull();
        chat.Schema.ShouldContain("lines");
        chat.Schema.ShouldNotContain("tokens");
        chat.Instruction.ShouldContain("do NOT correct spelling");
        result.Instruction.ShouldBe("Yazım yanlışlarını bulunuz.");
        var plan = Plan(result);
        ((string)plan["mode"]!).ShouldBe("select");
        ((string)plan["style"]!).ShouldBe("circle");
        var tokens = plan["tokens"]!.AsArray();
        tokens.Select(t => (string)t!["text"]!).ShouldBe(["Kuş", "dolaşmış."]);
        Xywh(tokens[1]!["boxes"]![0]).ShouldBe((40, 0, 60, 10));
        Xywh(tokens[1]!["boxes"]![1]).ShouldBe((0, 20, 40, 10));
        InteractionGraderRegistry.Default.Resolve(TextSelectGrader.Type).ValidateDefinition(result.PlanJson, null).ShouldBeTrue();
    }

    [Fact]
    public async Task CustomInstruction_IsRenderedAndSent()
    {
        var (_, chat) = await ExtractAsync(MatchGrader.Type,
            new { instruction = "", left = Array.Empty<object>(), right = Array.Empty<object>(), prelinked = Array.Empty<object>() },
            instruction: "Custom {width} by {height}");

        chat.Instruction.ShouldBe("Custom 132 by 82");
    }

    [Fact]
    public async Task TruncatedResponse_ThrowsWithPaidUsage()
    {
        var ex = await Should.ThrowAsync<LabelingAiException>(() => ExtractAsync(MatrixChoiceGrader.Type,
            new { instruction = "", multiPerRow = false, columns = Array.Empty<object>(), rows = Array.Empty<object>() },
            ChatFinishReason.Length));

        ex.Usage.ShouldBe(new LabelingAiUsage(FakeChatClient.InputTokens, FakeChatClient.OutputTokens));
    }

    [Fact]
    public async Task NotConfigured_IsFalseAndCallThrows()
    {
        using var extractor = new ClaudeActivityStructureExtractor(
            Options.Create(new LabelingAiOptions { ApiKey = "", Models = [Model] }), NullLoggerFactory.Instance);
        using var image = LabelingImage.Decode(Convert.ToBase64String(TestImages.Png(10, 10)));

        extractor.IsConfigured.ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(image, new Rectangle(0, 0, 5, 5), ActivityStructureSettings.Default(MatchGrader.Type), Model));
    }

    private sealed class FakeChatClient(string json, ChatFinishReason finish) : IChatClient
    {
        public const int InputTokens = 120;
        public const int OutputTokens = 40;

        public int Calls { get; private set; }

        public string? Instruction { get; private set; }

        public string? Schema { get; private set; }

        public int? MaxOutputTokens { get; private set; }

        public (int, int) ImageSize { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var user = messages.Single(m => m.Role == ChatRole.User);
            Instruction = user.Contents.OfType<TextContent>().Single().Text;
            var image = user.Contents.OfType<DataContent>().Single();
            var info = TestImages.Info(image.Data.ToArray());
            ImageSize = (info.Width, info.Height);
            Schema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema?.GetRawText();
            MaxOutputTokens = options?.MaxOutputTokens;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                FinishReason = finish,
                Usage = new UsageDetails { InputTokenCount = InputTokens, OutputTokenCount = OutputTokens },
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
