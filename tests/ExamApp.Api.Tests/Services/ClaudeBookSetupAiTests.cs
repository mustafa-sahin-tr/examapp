using System.Text.Json;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Labeling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1196: kesik sayfa taraması (uzun içindekiler) geniş çıktı sınırıyla bir kez yeniden sorulur.</summary>
public class ClaudeBookSetupAiTests
{
    private static readonly LabelingAiModelOption Haiku = new() { Id = ClaudeBookSetupAi.DefaultSurveyModel, DisplayName = "Haiku" };

    private static ClaudeBookSetupAi Ai(FakeChatClient chat) => new(
        chat,
        Options.Create(new LabelingAiOptions { ApiKey = "sk-ant-test", Models = [Haiku] }),
        new ConfigurationBuilder().Build(),
        NullLoggerFactory.Instance);

    private static BookPageImage Page(int filePage)
    {
        using var bitmap = new SKBitmap(40, 60);
        bitmap.Erase(SKColors.White);
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 80);
        return new BookPageImage(filePage, data.ToArray());
    }

    private static readonly string SurveyJson = JsonSerializer.Serialize(new
    {
        pages = new object[]
        {
            new { filePage = 1, kind = "toc", printedPage = (int?)null, unit = (int?)null, testNo = (int?)null, title = (string?)null,
                minutes = (int?)null, maxQuestionNumber = (int?)null, series = (string?)null },
            new { filePage = 2, kind = "test", printedPage = 2, unit = 1, testNo = 1, title = "Doğal Sayılar", minutes = 20,
                maxQuestionNumber = 12, series = (string?)null },
        },
        tocRows = new[] { new { unit = 1, title = "Doğal Sayılar", printedPage = 2 } },
    });

    [Fact]
    public async Task Survey_Truncated_RetriesOnceWithTheLargeLimit_AndCountsBothCalls()
    {
        var chat = new FakeChatClient(truncateFirst: 1);
        var cost = new BookSetupCost();

        var result = await Ai(chat).SurveyAsync([Page(1), Page(2)], cost, CancellationToken.None);

        chat.Limits.ShouldBe([400 * 2 + 1000, ClaudeBookSetupAi.SurveyRetryMaxOutputTokens]);
        result.Pages.Select(p => p.FilePage).ShouldBe([1, 2]);
        result.Toc.Single().Title.ShouldBe("Doğal Sayılar");
        cost.OutputTokens.ShouldBe(2 * FakeChatClient.OutputTokensPerCall);
    }

    [Fact]
    public async Task Survey_TruncatedTwice_SurveysTheHalvesAndMerges()
    {
        // Issue #1354: a dense page set cut even with the large limit is split instead of failing the setup.
        var chat = new FakeChatClient(truncateFirst: 2);
        var cost = new BookSetupCost();

        var result = await Ai(chat).SurveyAsync([Page(1), Page(2)], cost, CancellationToken.None);

        chat.Limits.ShouldBe([400 * 2 + 1000, ClaudeBookSetupAi.SurveyRetryMaxOutputTokens, 400 + 1000, 400 + 1000]);
        result.Pages.Select(p => p.FilePage).ShouldBe([1, 2]);
        cost.OutputTokens.ShouldBe(4 * FakeChatClient.OutputTokensPerCall);
    }

    [Fact]
    public async Task Survey_OnePageTruncatedTwice_FailsTheSetup()
    {
        var chat = new FakeChatClient(truncateFirst: 2);

        var ex = await Should.ThrowAsync<BookImportFailure>(() => Ai(chat).SurveyAsync([Page(1)], new BookSetupCost(), CancellationToken.None));

        ex.Message.ShouldBe("Book setup failed: Claude response was truncated.");
        chat.Limits.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AnswerKey_Truncated_LeavesTheReadEmptyInsteadOfFailing()
    {
        var chat = new FakeChatClient(truncateFirst: 1);

        var rows = await Ai(chat).ReadAnswerKeyAsync(Page(9), tiled: false, strong: false, new BookSetupCost(), CancellationToken.None);

        rows.ShouldBeEmpty();
        chat.Limits.Count.ShouldBe(1);
    }

    /// <summary>İlk <c>truncateFirst</c> çağrı kesik (Length) döner; her çağrının çıktı sınırı kaydedilir.</summary>
    [Fact]
    public async Task AnswerKey_ExamRow_IsReadAsAnExamNotAsTestOneOfTheUnit()
    {
        // 5. sınıf Sosyal Bilgiler: "1. Sınav" satırı 2. ünite tablosunun altında; eskiden "2-01" sahipsiz anahtar oluyordu.
        var reply = JsonSerializer.Serialize(new
        {
            rows = new object[]
            {
                new { unit = 2, testNo = 18, answers = new[] { new { number = 1, answer = "A" } }, series = (string?)null, exam = (int?)null },
                new { unit = 2, testNo = 0, answers = new[] { new { number = 1, answer = "B" }, new { number = 2, answer = "A" } },
                    series = (string?)null, exam = (int?)1 },
            },
        });

        var rows = await Ai(new FakeChatClient(0, reply))
            .ReadAnswerKeyAsync(Page(223), tiled: false, strong: false, new BookSetupCost(), CancellationToken.None);

        rows.Select(r => (r.Unit, r.TestNo, r.ExamNo)).ShouldBe([(2, 18, (int?)null), (0, 0, 1)]);
        rows[1].Answers.ShouldBe(new Dictionary<int, char> { [1] = 'B', [2] = 'A' });
    }

    private sealed class FakeChatClient(int truncateFirst, string? reply = null) : IChatClient
    {
        public const int OutputTokensPerCall = 7;

        public List<int?> Limits { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Limits.Add(options?.MaxOutputTokens);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply ?? SurveyJson))
            {
                FinishReason = Limits.Count <= truncateFirst ? ChatFinishReason.Length : ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = OutputTokensPerCall },
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
