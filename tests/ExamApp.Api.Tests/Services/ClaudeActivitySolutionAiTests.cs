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
/// Issue #580: Claude çağrısının biçimi (sahte <see cref="IChatClient"/>, ağa çıkılmaz) — anahtar sayfası tek görsel + talimat →
/// sayfa pikselinde maddeler; çözüm: etkinlik kırpması + anahtar kırpmaları + talimat (tip açıklaması, kutusuz yapı, anahtar satırları),
/// tipin çözüm şeması, yanıt → tipin çözüm biçimi; kesik / şemaya uymayan yanıt <see cref="LabelingAiException"/> (kullanım taşınır).
/// </summary>
public class ClaudeActivitySolutionAiTests
{
    private static readonly LabelingAiModelOption Model = new() { Id = "claude-test", DisplayName = "Test" };

    private static ClaudeActivitySolutionAi NewAi(FakeChatClient chat) =>
        new(chat, Options.Create(new LabelingAiOptions { ApiKey = "sk-ant-test", Models = [Model] }), NullLoggerFactory.Instance);

    private static LabelingImage Page(int width = 1000, int height = 1400) =>
        LabelingImage.Decode(Convert.ToBase64String(TestImages.Png(width, height)));

    [Fact]
    public async Task ReadAnswerKey_SendsPageWithRenderedInstruction_AndMapsVisualBoxesToPagePixels()
    {
        // 3136x2000: uzun kenar 1568'e küçültülür (2 kat) — kutular sayfaya geri ölçeklenir.
        var chat = new FakeChatClient(JsonSerializer.Serialize(new
        {
            entries = new object[]
            {
                new { printedPage = 12, activityLabel = "Etkinlik 3", itemLabel = "a", answerText = "3/4", visual = false, x1 = 0, y1 = 0, x2 = 0, y2 = 0 },
                new { printedPage = 12, activityLabel = "Etkinlik 4", itemLabel = "", answerText = "", visual = true, x1 = 10, y1 = 20, x2 = 110, y2 = 70 },
            },
        }));
        using var ai = NewAi(chat);
        using var page = Page(3136, 2000);

        var reading = await ai.ReadAnswerKeyAsync(page, ActivityStructureSettings.Default("answerKey"), Model);

        chat.Instructions.Single().ShouldStartWith("This image is one page of the answer key at the back of a Turkish school book (1568x1000 px).");
        chat.ImageSizes.ShouldBe([(1568, 1000)]);
        chat.MaxOutputTokens.ShouldBe(ClaudeActivitySolutionAi.AnswerKeyMaxOutputTokens);
        chat.Schema!.ShouldContain("printedPage");
        reading.Entries.Count.ShouldBe(2);
        reading.Entries[1].Box.ShouldBe(new Rectangle(20, 40, 200, 100));
        reading.InputTokens.ShouldBe(FakeChatClient.InputTokens);
    }

    [Fact]
    public async Task Solve_FromKey_SendsActivityCropThenKeyCrops_AndKeyLinesInTheText()
    {
        var chat = new FakeChatClient(JsonSerializer.Serialize(new
        {
            rows = new[] { new { rowId = "r1", columnIds = new[] { "c1" } }, new { rowId = "r2", columnIds = new[] { "c2" } } },
        }));
        using var ai = NewAi(chat);
        using var page = Page();
        var keyCrop = TestImages.Jpeg(60, 40);

        var solving = await ai.SolveAsync(page, new Rectangle(200, 300, 100, 50), MatrixChoiceGrader.Type,
            ActivitySolutionDraftsTests.MatrixPlan, ActivityStructureSettings.Default("solveFromKey"),
            [new AnswerKeyInput(12, "Etkinlik 3", "a", "D", null), new AnswerKeyInput(12, "Etkinlik 3", null, null, keyCrop)], Model);

        // Kırpma = blok + 16 px pay; ardından anahtar görseli.
        chat.ImageSizes.ShouldBe([(132, 82), (60, 40)]);
        var text = chat.Instructions.Single();
        text.ShouldStartWith("The first image is one activity cropped from a page of a Turkish school book (132x82 px).");
        text.ShouldContain("(matrixChoice)");
        text.ShouldContain("Structure:\n{\"version\":1,\"type\":\"matrixChoice\"");
        text.ShouldNotContain("\"box\"");
        text.ShouldContain("- page 12 · Etkinlik 3 · item a: D");
        text.ShouldContain("- page 12 · Etkinlik 3 [answer picture 1]");
        chat.Schema!.ShouldContain("columnIds");
        chat.MaxOutputTokens.ShouldBe(ClaudeActivitySolutionAi.SolveMaxOutputTokens);

        solving.SolutionJson.ShouldBe("""{"r1":["c1"],"r2":["c2"]}""");
        InteractionGraderRegistry.Default.Resolve(MatrixChoiceGrader.Type)
            .ValidateDefinition(ActivitySolutionDraftsTests.MatrixPlan, solving.SolutionJson).ShouldBeTrue();
    }

    [Theory]
    [InlineData("slotFill", "slots", "alternatives")]
    [InlineData("dragPlace", "targets", "cardIds")]
    [InlineData("match", "pairs", "rightId")]
    public async Task Solve_Ai_UsesTheTypesSolutionSchema(string type, string root, string field)
    {
        var chat = new FakeChatClient(JsonSerializer.Serialize(new Dictionary<string, object> { [root] = Array.Empty<object>() }));
        using var ai = NewAi(chat);
        using var page = Page();
        var plan = type switch
        {
            SlotFillGrader.Type => ActivitySolutionDraftsTests.SlotPlan,
            DragPlaceGrader.Type => ActivitySolutionDraftsTests.DragPlan,
            _ => ActivitySolutionDraftsTests.MatchPlan,
        };

        var solving = await ai.SolveAsync(page, new Rectangle(10, 10, 200, 100), type, plan,
            new ActivityPromptSettings("solveAi", 2, "Solve {width}x{height}."), [], Model);

        chat.Schema!.ShouldContain(field);
        chat.ImageSizes.Count.ShouldBe(1);
        chat.Instructions.Single().ShouldStartWith("Solve 226x126.");
        JsonNode.Parse(solving.SolutionJson).ShouldBeOfType<JsonObject>();
    }

    [Fact]
    public async Task TruncatedResponse_ThrowsWithUsage()
    {
        var chat = new FakeChatClient("{}", ChatFinishReason.Length);
        using var ai = NewAi(chat);
        using var page = Page();

        var ex = await Should.ThrowAsync<LabelingAiException>(() => ai.SolveAsync(page, new Rectangle(10, 10, 50, 50),
            MatchGrader.Type, ActivitySolutionDraftsTests.MatchPlan, ActivityStructureSettings.Default("solveAi"), [], Model));

        ex.Usage.ShouldNotBeNull();
    }

    private sealed class FakeChatClient(string json, ChatFinishReason? finish = null) : IChatClient
    {
        public const int InputTokens = 120;
        public const int OutputTokens = 40;

        public List<string> Instructions { get; } = [];

        public List<(int, int)> ImageSizes { get; } = [];

        public string? Schema { get; private set; }

        public int? MaxOutputTokens { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var user = messages.Single(m => m.Role == ChatRole.User);
            // Görseller metinden önce.
            user.Contents.Last().ShouldBeOfType<TextContent>();
            Instructions.Add(user.Contents.OfType<TextContent>().Single().Text);
            foreach (var image in user.Contents.OfType<DataContent>())
            {
                var info = TestImages.Info(image.Data.ToArray());
                ImageSizes.Add((info.Width, info.Height));
            }
            Schema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema?.GetRawText();
            MaxOutputTokens = options?.MaxOutputTokens;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                FinishReason = finish ?? ChatFinishReason.Stop,
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
