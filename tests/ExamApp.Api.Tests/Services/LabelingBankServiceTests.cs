using System.Data.Common;
using System.Security.Cryptography;
using NSubstitute.ExceptionExtensions;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// İşaretleme → soru bankası kaydı (issue #470): doğrulama, toplu kayıt DTO eşlemesi, aynı sayfa tekrarı (409 / force),
/// başarısız kayıtta sayfa talebinin geri alınması ve sayfa özetinin <see cref="LabelingService.GetPageAsync"/> ile
/// okunması.
/// </summary>
public class LabelingBankServiceTests : IDisposable
{
    private const int ActingUserId = 77;
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);
    private static readonly LabelingActor Actor = new("kc-trainer-1", "Eğitmen");

    private readonly TestDb _db = TestDb.Create();
    private readonly IQuestionService _questions = Substitute.For<IQuestionService>();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly InMemoryFixedWindowCounterStore _counters;
    private BulkQuestionCreateDto? _captured;

    public LabelingBankServiceTests()
    {
        _counters = new InMemoryFixedWindowCounterStore(_clock);

        // Varsayılan: kayıt başarılı, gönderilen DTO yakalanır.
        _questions.SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), Arg.Any<int>())
            .Returns(ci =>
            {
                _captured = ci.Arg<BulkQuestionCreateDto>();
                return Task.FromResult(new ResponseBaseDto { Success = true, Message = "ok" });
            });
    }

    public void Dispose() => _db.Dispose();

    private LabelingBankService NewService(AppDbContext ctx) =>
        new(ctx, _questions, _counters, _clock, NullLogger<LabelingBankService>.Instance);

    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static LabelingBankBoxDto Box(int x, int y, int w, int h) => new() { X = x, Y = y, Width = w, Height = h };

    /// <summary>İki şıklı / üç şıklı iki soru içeren geçerli bir istek.</summary>
    private static LabelingBankSaveRequestDto ValidRequest(byte[] png, bool force = false) => new()
    {
        ImageBase64 = Convert.ToBase64String(png),
        Force = force,
        Questions =
        [
            new LabelingBankQuestionDto
            {
                X = 10, Y = 10, Width = 100, Height = 80,
                Options = [Box(12, 12, 20, 20), Box(40, 12, 20, 20), Box(70, 12, 20, 20)],
                CorrectOptionIndex = 1,
            },
            new LabelingBankQuestionDto
            {
                X = 10, Y = 100, Width = 100, Height = 80,
                Options = [Box(12, 102, 20, 20), Box(40, 102, 20, 20)],
                CorrectOptionIndex = 0,
            },
        ],
    };

    private static LabelingBankQuestionDto OneQuestion(int correctIndex = 0, int optionCount = 2) => new()
    {
        X = 10, Y = 10, Width = 100, Height = 80,
        Options = Enumerable.Range(0, optionCount).Select(i => Box(12 + i * 25, 12, 20, 20)).ToList(),
        CorrectOptionIndex = correctIndex,
    };

    private async Task<LabelingPage> ReadPageAsync(string hash)
    {
        await using var read = _db.NewContext();
        return await read.LabelingPages.AsNoTracking().SingleAsync(p => p.ImageSha256 == hash);
    }

    private void StubBulkResult(bool success) =>
        _questions.SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), Arg.Any<int>())
            .Returns(Task.FromResult(new ResponseBaseDto { Success = success, Message = success ? "ok" : "fail" }));

    // ---------- Doğrulama ----------

    [Fact]
    public async Task SaveToBankAsync_NoQuestions_ThrowsNoQuestionsAndDoesNotCallSave()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions = [];

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.bank.noQuestions");
        await _questions.DidNotReceiveWithAnyArgs().SaveBulkQuestion(default!, default);
    }

    [Fact]
    public async Task SaveToBankAsync_MoreThanMaxQuestions_ThrowsTooManyBoxes()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions = Enumerable.Range(0, LabelingBankService.MaxQuestions + 1).Select(_ => OneQuestion()).ToList();

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.tooManyBoxes");
    }

    [Fact]
    public async Task SaveToBankAsync_QuestionWithSingleOption_ThrowsInvalidOptions()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions = [OneQuestion(correctIndex: 0, optionCount: 1)];

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.bank.invalidOptions");
    }

    [Fact]
    public async Task SaveToBankAsync_QuestionWithElevenOptions_ThrowsInvalidOptions()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions = [OneQuestion(correctIndex: 0, optionCount: LabelingBankService.MaxOptions + 1)];

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.bank.invalidOptions");
    }

    [Fact]
    public async Task SaveToBankAsync_CorrectIndexNotSet_ThrowsCorrectAnswerMissing()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions = [OneQuestion(correctIndex: -1)];

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.bank.correctAnswerMissing");
    }

    [Fact]
    public async Task SaveToBankAsync_CorrectIndexEqualsOptionCount_ThrowsCorrectAnswerMissing()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions = [OneQuestion(correctIndex: 2, optionCount: 2)];

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.bank.correctAnswerMissing");
    }

    [Fact]
    public async Task SaveToBankAsync_NegativeQuestionX_ThrowsInvalidBox()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions[0].X = -1;

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.invalidBox");
    }

    [Fact]
    public async Task SaveToBankAsync_OptionBoxSmallerThanMinimumEdge_ThrowsInvalidBox()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions[0].Options[1].Width = 3;

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.invalidBox");
    }

    [Fact]
    public async Task SaveToBankAsync_InvalidBase64_ThrowsInvalidImage()
    {
        await using var ctx = _db.NewContext();
        var request = ValidRequest(Png(320, 200));
        request.ImageBase64 = "%%% not base64 %%%";

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.invalidImage");
    }

    [Fact]
    public async Task SaveToBankAsync_ValidationFails_DoesNotCreatePageRowOrCallSave()
    {
        var png = Png(320, 200);
        await using (var ctx = _db.NewContext())
        {
            var request = ValidRequest(png);
            request.Questions[1].CorrectOptionIndex = -1;

            await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));
        }

        await using var read = _db.NewContext();
        (await read.LabelingPages.AnyAsync(p => p.ImageSha256 == Sha256Hex(png))).ShouldBeFalse();
        await _questions.DidNotReceiveWithAnyArgs().SaveBulkQuestion(default!, default);
    }

    // ---------- Eşleme ----------

    [Fact]
    public async Task SaveToBankAsync_ValidQuestions_MapsToBulkDtoWithoutTest()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();

        await NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);

        var bulk = _captured.ShouldNotBeNull();
        bulk.Header.ShouldNotBeNull();
        bulk.Header.TestId.ShouldBeNull();
        bulk.Passages.ShouldBeEmpty();
        bulk.Questions.Count.ShouldBe(2);

        var q1 = bulk.Questions[0];
        q1.Name.ShouldBe("S1");
        q1.InteractionType.ShouldBe("mcq");
        (q1.X, q1.Y, q1.Width, q1.Height).ShouldBe((10d, 10d, 100, 80));
        q1.Answers.Select(a => a.Label).ShouldBe(["A", "B", "C"]);
        q1.Answers.Select(a => a.IsCorrect).ShouldBe([false, true, false]);
        q1.Answers.Select(a => a.Order).ShouldBe([0, 1, 2]);
        q1.Answers[1].X.ShouldBe(40d);
        q1.Answers[1].Width.ShouldBe(20);

        var q2 = bulk.Questions[1];
        q2.Name.ShouldBe("S2");
        q2.Answers.Select(a => a.Label).ShouldBe(["A", "B"]);
        q2.Answers.Select(a => a.IsCorrect).ShouldBe([true, false]);
    }

    [Fact]
    public async Task SaveToBankAsync_ValidQuestions_PassesActingUserId()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();

        await NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);

        await _questions.Received(1).SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), ActingUserId);
    }

    [Fact]
    public async Task SaveToBankAsync_ValidQuestions_SendsJpegDataUrl()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();

        await NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);

        const string prefix = "data:image/jpeg;base64,";
        var imageData = _captured.ShouldNotBeNull().ImageData;
        imageData.ShouldStartWith(prefix);
        var jpeg = Convert.FromBase64String(imageData[prefix.Length..]);
        // JPEG SOI işareti.
        jpeg[0].ShouldBe((byte)0xFF);
        jpeg[1].ShouldBe((byte)0xD8);
    }

    [Fact]
    public async Task SaveToBankAsync_QuestionBoxBeyondPageEdge_ClipsQuestionToPage()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions = [new LabelingBankQuestionDto
        {
            X = 300, Y = 10, Width = 100, Height = 80,
            Options = [Box(302, 12, 10, 10), Box(310, 12, 8, 10)],
            CorrectOptionIndex = 0,
        }];

        await NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId);

        var q = _captured.ShouldNotBeNull().Questions[0];
        q.X.ShouldBe(300d);
        q.Width.ShouldBe(20);
    }

    [Fact]
    public async Task SaveToBankAsync_OptionOutsideItsQuestion_ThrowsOptionOutsideQuestion()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions[0].Options[0] = Box(500, 500, 20, 20);

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.bank.optionOutsideQuestion");
        await _questions.DidNotReceiveWithAnyArgs().SaveBulkQuestion(default!, default);
    }

    [Fact]
    public async Task SaveToBankAsync_QuestionEntirelyOffPage_ThrowsInvalidBox()
    {
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();
        var request = ValidRequest(png);
        request.Questions[0].X = 400;

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));

        ex.Message.ShouldBe("labeling.invalidBox");
    }

    [Fact]
    public async Task SaveToBankAsync_ManyCallsFromSameUserBeyondHourlyLimit_ThrowsRateLimitAndSavesNoMore()
    {
        // Sabit saat: pencere kaymaz. Her çağrı farklı görsel → yeni sayfa (duplicate kapısına takılmaz).
        var actor = new LabelingActor("kc-busy-trainer", "Yoğun");
        for (var i = 0; i < LabelingBankService.SavesPerHour; i++)
        {
            await using var ctx = _db.NewContext();
            await NewService(ctx).SaveToBankAsync(ValidRequest(Png(200 + i, 200)), actor, ActingUserId);
        }

        await using (var over = _db.NewContext())
        {
            await Should.ThrowAsync<LabelingRateLimitException>(
                () => NewService(over).SaveToBankAsync(ValidRequest(Png(900, 200)), actor, ActingUserId));
        }

        // Başka kullanıcının kotası ayrıdır.
        await using var other = _db.NewContext();
        var response = await NewService(other).SaveToBankAsync(ValidRequest(Png(901, 200)),
            new LabelingActor("kc-other-trainer", "Diğer"), ActingUserId);
        response.Bank.QuestionCount.ShouldBe(2);
        await _questions.Received(LabelingBankService.SavesPerHour + 1)
            .SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), ActingUserId);
    }

    [Fact]
    public async Task SaveToBankAsync_StaleInProgressClaim_IsTakenOverAndSaved()
    {
        var png = Png(320, 200);
        await SeedPageAsync(png, bankSaveStartedAt: Now.AddMinutes(-11).UtcDateTime);

        await using var ctx = _db.NewContext();
        await NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);

        await _questions.Received(1).SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), ActingUserId);
        var page = await ReadPageAsync(Sha256Hex(png));
        page.BankSaveStartedAt.ShouldBeNull();
        page.BankQuestionCount.ShouldBe(2);
    }

    [Fact]
    public async Task SaveToBankAsync_FreshInProgressClaim_ThrowsDuplicateAndDoesNotSave()
    {
        var png = Png(320, 200);
        await SeedPageAsync(png, bankSaveStartedAt: Now.AddMinutes(-1).UtcDateTime);

        await using var ctx = _db.NewContext();
        var ex = await Should.ThrowAsync<LabelingBankDuplicateException>(
            () => NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId));

        ex.Existing.ShouldBeNull();
        await _questions.DidNotReceiveWithAnyArgs().SaveBulkQuestion(default!, default);
    }

    [Fact]
    public async Task SaveToBankAsync_BulkSaveFailure_ClearsInProgressMarker()
    {
        StubBulkResult(success: false);
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<LabelingBankSaveException>(() => NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId));

        (await ReadPageAsync(Sha256Hex(png))).BankSaveStartedAt.ShouldBeNull();
    }

    // ---------- Aynı sayfa: 409 / force ----------

    [Fact]
    public async Task SaveToBankAsync_SameImageWithoutForce_ThrowsDuplicateWithExistingInfo()
    {
        var png = Png(320, 200);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);
        }

        await using var second = _db.NewContext();
        var ex = await Should.ThrowAsync<LabelingBankDuplicateException>(
            () => NewService(second).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId));

        ex.Existing.ShouldNotBeNull();
        ex.Existing!.SavedAt.ShouldBe(Now.UtcDateTime);
        ex.Existing.SavedByName.ShouldBe("Eğitmen");
        ex.Existing.QuestionCount.ShouldBe(2);
        await _questions.Received(1).SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), Arg.Any<int>());
    }

    [Fact]
    public async Task SaveToBankAsync_SameImageWithForce_ResavesAndUpdatesInfo()
    {
        var png = Png(320, 200);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);
        }

        _clock.Now = Later;
        var forced = ValidRequest(png, force: true);
        forced.Questions = [OneQuestion()];
        var other = new LabelingActor("kc-trainer-2", "Diğer Eğitmen");

        await using var second = _db.NewContext();
        var response = await NewService(second).SaveToBankAsync(forced, other, ActingUserId);

        response.Bank.SavedAt.ShouldBe(Later.UtcDateTime);
        response.Bank.SavedByName.ShouldBe("Diğer Eğitmen");
        response.Bank.QuestionCount.ShouldBe(1);
        await _questions.Received(2).SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), ActingUserId);

        var page = await ReadPageAsync(Sha256Hex(png));
        page.BankSavedAt.ShouldBe(Later.UtcDateTime);
        page.BankSavedByKeycloakId.ShouldBe("kc-trainer-2");
        page.BankQuestionCount.ShouldBe(1);
    }

    [Fact]
    public async Task SaveToBankAsync_ConcurrentClaimBeforeUpdate_ThrowsDuplicateWithWinnerInfoAndDoesNotSave()
    {
        var png = Png(320, 200);
        var hash = Sha256Hex(png);
        // Yarışan istek, bu isteğin koşullu UPDATE'inden hemen önce sayfayı kendi kaydıyla talep eder.
        var interceptor = new ConcurrentClaimInterceptor(async () =>
        {
            await using var winner = _db.NewContext();
            await winner.LabelingPages.Where(p => p.ImageSha256 == hash).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.BankSavedAt, Later.UtcDateTime)
                .SetProperty(p => p.BankSavedByKeycloakId, "kc-winner")
                .SetProperty(p => p.BankSavedByName, "Kazanan")
                .SetProperty(p => p.BankQuestionCount, 3));
        });

        await using var ctx = _db.NewContext(interceptor);
        var ex = await Should.ThrowAsync<LabelingBankDuplicateException>(
            () => NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId));

        ex.Existing.ShouldNotBeNull();
        ex.Existing!.SavedByName.ShouldBe("Kazanan");
        ex.Existing.QuestionCount.ShouldBe(3);
        await _questions.DidNotReceiveWithAnyArgs().SaveBulkQuestion(default!, default);
        (await ReadPageAsync(hash)).BankSavedByName.ShouldBe("Kazanan");
    }

    // ---------- Başarısız kayıt ----------

    [Fact]
    public async Task SaveToBankAsync_BulkSaveReturnsFailure_ThrowsSaveExceptionAndLeavesNoBankRecord()
    {
        StubBulkResult(success: false);
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<LabelingBankSaveException>(() => NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId));

        var page = await ReadPageAsync(Sha256Hex(png));
        page.BankSavedAt.ShouldBeNull();
        page.BankSavedByName.ShouldBeNull();
        page.BankQuestionCount.ShouldBeNull();
    }

    [Fact]
    public async Task SaveToBankAsync_BulkSaveThrows_ThrowsSaveExceptionAndLeavesNoBankRecord()
    {
        _questions.SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException("minio down"));
        var png = Png(320, 200);
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<LabelingBankSaveException>(() => NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId));

        (await ReadPageAsync(Sha256Hex(png))).BankSavedAt.ShouldBeNull();
    }

    [Fact]
    public async Task SaveToBankAsync_BulkSaveThrowsOnForcedSave_RestoresPreviousBankRecord()
    {
        var png = Png(320, 200);
        await using (var first = _db.NewContext())
        {
            await NewService(first).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);
        }

        _clock.Now = Later;
        _questions.SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        await using (var forced = _db.NewContext())
        {
            await Should.ThrowAsync<LabelingBankSaveException>(
                () => NewService(forced).SaveToBankAsync(ValidRequest(png, force: true), Actor, ActingUserId));
        }

        var page = await ReadPageAsync(Sha256Hex(png));
        page.BankSavedAt.ShouldBe(Now.UtcDateTime);
        page.BankQuestionCount.ShouldBe(2);
        page.BankSavedByName.ShouldBe("Eğitmen");
    }

    [Fact]
    public async Task SaveToBankAsync_ForcedSaveReturnsFailure_RestoresPreviousBankRecord()
    {
        var png = Png(320, 200);
        await using (var first = _db.NewContext())
        {
            await NewService(first).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);
        }

        _clock.Now = Later;
        StubBulkResult(success: false);
        await using (var forced = _db.NewContext())
        {
            await Should.ThrowAsync<LabelingBankSaveException>(
                () => NewService(forced).SaveToBankAsync(ValidRequest(png, force: true), Actor, ActingUserId));
        }

        var page = await ReadPageAsync(Sha256Hex(png));
        page.BankSavedAt.ShouldBe(Now.UtcDateTime);
        page.BankQuestionCount.ShouldBe(2);
    }

    // ---------- Sayfa özeti ----------

    [Fact]
    public async Task GetPageAsync_NeverSavedToBank_ReturnsNullBankInfo()
    {
        // Sayfa var ama bankaya hiç kaydedilmemiş (ör. yalnız manuel işaretleme).
        var png = Png(64, 64);
        await using (var write = _db.NewContext())
        {
            write.LabelingPages.Add(new LabelingPage
            {
                ImageSha256 = Sha256Hex(png), Width = 64, Height = 64, CreatedAt = Now.UtcDateTime,
            });
            await write.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var page = await NewLabelingService(check).GetPageAsync(Sha256Hex(png));

        page.ShouldNotBeNull();
        page!.Bank.ShouldBeNull();
    }

    [Fact]
    public async Task GetPageAsync_AfterBankSave_ReturnsBankInfo()
    {
        var png = Png(320, 200);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);
        }

        await using var read = _db.NewContext();
        var page = await NewLabelingService(read).GetPageAsync(Sha256Hex(png));

        page.ShouldNotBeNull();
        page!.Bank.ShouldNotBeNull();
        page.Bank!.SavedAt.ShouldBe(Now.UtcDateTime);
        page.Bank.SavedByName.ShouldBe("Eğitmen");
        page.Bank.QuestionCount.ShouldBe(2);
    }

    private LabelingService NewLabelingService(AppDbContext ctx)
    {
        var detector = Substitute.For<IClaudeLabelDetector>();
        var options = new LabelingAiOptions
        {
            LiveCallsPerHour = 60,
            Models = [new LabelingAiModelOption { Id = "claude-test", DisplayName = "Test", InputUsdPerMTok = 3m, OutputUsdPerMTok = 15m, IsDefault = true }],
            DailyBudgetUsdPerUser = 5m,
            DailyBudgetUsdTotal = 25m,
        };
        return new LabelingService(
            ctx,
            detector,
            new InMemoryFixedWindowCounterStore(_clock),
            Options.Create(options),
            _clock,
            NullLogger<LabelingService>.Instance);
    }

    /// <summary>Sayfa satırını doğrudan oluşturur (önceden var olan bir kayıt / yarışan işaret durumu için).</summary>
    private async Task SeedPageAsync(byte[] png, DateTime? bankSaveStartedAt = null, DateTime? bankSavedAt = null,
        int? bankQuestionCount = null)
    {
        await using var ctx = _db.NewContext();
        ctx.LabelingPages.Add(new LabelingPage
        {
            ImageSha256 = Sha256Hex(png),
            Width = 320,
            Height = 200,
            CreatedAt = Now.UtcDateTime,
            BankSaveStartedAt = bankSaveStartedAt,
            BankSavedAt = bankSavedAt,
            BankSavedByName = bankSavedAt is null ? null : "Önceki",
            BankQuestionCount = bankQuestionCount,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task SaveToBankAsync_ForcedSaveWhileAnotherSaveInProgress_ThrowsDuplicateAndDoesNotSave()
    {
        var png = Png(320, 200);
        await using (var first = _db.NewContext())
        {
            await NewService(first).SaveToBankAsync(ValidRequest(png), Actor, ActingUserId);
        }

        // İlk kayıt bitti (BankSavedAt dolu); ikinci, force'lu istek okuduktan sonra başka bir kayıt işareti koyar.
        var hash = Sha256Hex(png);
        var interceptor = new ConcurrentClaimInterceptor(async () =>
        {
            await using var winner = _db.NewContext();
            await winner.LabelingPages.Where(p => p.ImageSha256 == hash).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.BankSaveStartedAt, (DateTime?)Later.UtcDateTime));
        });

        _clock.Now = Later;
        await using var ctx = _db.NewContext(interceptor);
        var ex = await Should.ThrowAsync<LabelingBankDuplicateException>(
            () => NewService(ctx).SaveToBankAsync(ValidRequest(png, force: true), Actor, ActingUserId));

        ex.Existing.ShouldNotBeNull();
        ex.Existing!.QuestionCount.ShouldBe(2);
        await _questions.Received(1).SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), ActingUserId);
    }

    /// <summary>Yarış simülasyonu: koşullu UPDATE çalışmadan önce bir kez tetiklenir.</summary>
    private sealed class ConcurrentClaimInterceptor(Func<Task> beforeClaim) : DbCommandInterceptor
    {
        private bool _fired;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_fired && command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("LabelingPages", StringComparison.Ordinal))
            {
                _fired = true;
                await beforeClaim();
            }
            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
