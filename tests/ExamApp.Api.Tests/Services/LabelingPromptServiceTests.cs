using NSubstitute.ExceptionExtensions;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Claude prompt sürümleri: durum okuma, kaydetme/doğrulama, sürüm numaralama ve prompt değişince önbellek geçersizliği.
/// Detektör mock'lanır; gerçek Anthropic çağrısı yapılmaz.
/// </summary>
public class LabelingPromptServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly LabelingActor Actor = new("kc-trainer-7", "Eğitmen");

    private readonly TestDb _db = TestDb.Create();
    private readonly IClaudeLabelDetector _detector = Substitute.For<IClaudeLabelDetector>();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly InMemoryFixedWindowCounterStore _counters;

    public LabelingPromptServiceTests()
    {
        _detector.IsConfigured.Returns(true);
        _detector.DetectAsync(default!, default!, default!, default).ReturnsForAnyArgs(Detection());
        _counters = new InMemoryFixedWindowCounterStore(_clock);
    }

    public void Dispose() => _db.Dispose();

    private LabelingService NewService(AppDbContext ctx) => new(
        ctx,
        _detector,
        _counters,
        Options.Create(new LabelingAiOptions
        {
            LiveCallsPerHour = 60,
            Models = [Model()],
            DailyBudgetUsdPerUser = 5m,
            DailyBudgetUsdTotal = 25m,
        }),
        _clock,
        NullLogger<LabelingService>.Instance);

    private static LabelingAiModelOption Model() => new()
    {
        Id = "claude-test",
        DisplayName = "Test",
        InputUsdPerMTok = 3m,
        OutputUsdPerMTok = 15m,
        IsDefault = true,
    };

    private static LabelingSavePromptRequestDto Prompt(
        string question = "Q {width}x{height}", string option = "O {width}x{height}",
        int questionPadding = 6, int optionPadding = 4, bool expand = true) => new()
    {
        QuestionInstruction = question,
        OptionInstruction = option,
        QuestionPadding = questionPadding,
        OptionPadding = optionPadding,
        ExpandQuestionToOptions = expand,
    };

    private static ClaudeDetection Detection(int input = 100, int output = 50) => new(
        new PredictionListDto
        {
            Success = true,
            Predictions = [new PredictionDto { ClassId = 0, X = 10, Y = 10, Width = 100, Height = 80 }],
        },
        input,
        output);

    private static byte[] Png(int width, int height) => TestImages.Png(width, height);

    private static LabelingAiDetectRequestDto DetectRequest(byte[] png, bool force = false) => new()
    {
        ImageBase64 = Convert.ToBase64String(png),
        Model = "claude-test",
        Force = force,
    };

    // ---------- GetPromptStateAsync ----------

    [Fact]
    public async Task GetPromptStateAsync_NoVersionsSaved_ActiveIsCodeDefaultsAndHistoryEmpty()
    {
        await using var ctx = _db.NewContext();

        var state = await NewService(ctx).GetPromptStateAsync();

        state.Active.Version.ShouldBe(0);
        state.Active.QuestionInstruction.ShouldBe(LabelingPromptSettings.DefaultQuestionInstruction);
        state.Active.OptionInstruction.ShouldBe(LabelingPromptSettings.DefaultOptionInstruction);
        state.Defaults.ShouldBe(state.Active);
        state.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPromptStateAsync_AfterSave_ActiveIsLatestAndDefaultsStayVersionZero()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.SavePromptAsync(Prompt(question: "first {width}"), Actor);

        var state = await service.SavePromptAsync(Prompt(question: "second {height}"), Actor);

        state.Active.Version.ShouldBe(2);
        state.Active.QuestionInstruction.ShouldBe("second {height}");
        state.Defaults.Version.ShouldBe(0);
        state.Defaults.QuestionInstruction.ShouldBe(LabelingPromptSettings.DefaultQuestionInstruction);
    }

    [Fact]
    public async Task GetPromptStateAsync_MoreThan20Versions_HistoryIsCappedNewestFirst()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        for (var i = 1; i <= 22; i++)
            await service.SavePromptAsync(Prompt(question: $"v{i} {{width}}"), Actor);

        var state = await service.GetPromptStateAsync();

        state.History.Count.ShouldBe(20);
        state.History.Select(h => h.Version).ShouldBe(Enumerable.Range(3, 20).Reverse());
        state.Active.Version.ShouldBe(22);
    }

    // ---------- SavePromptAsync: doğrulama ----------

    [Theory]
    [InlineData("", "ok")]
    [InlineData("   ", "ok")]
    [InlineData("ok", "")]
    [InlineData("ok", "\t\n ")]
    public async Task SavePromptAsync_BlankInstruction_ThrowsPromptRequired(string question, string option)
    {
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SavePromptAsync(Prompt(question, option), Actor));

        ex.Message.ShouldBe("labeling.promptRequired");
    }

    [Fact]
    public async Task SavePromptAsync_InstructionOver4000Characters_ThrowsPromptTooLong()
    {
        await using var ctx = _db.NewContext();
        var tooLong = new string('x', LabelingPromptSettings.MaxInstructionLength + 1);

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SavePromptAsync(Prompt(question: tooLong), Actor));

        ex.Message.ShouldBe("labeling.promptTooLong");
    }

    [Fact]
    public async Task SavePromptAsync_OptionInstructionOver4000Characters_ThrowsPromptTooLong()
    {
        await using var ctx = _db.NewContext();
        var tooLong = new string('x', LabelingPromptSettings.MaxInstructionLength + 1);

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SavePromptAsync(Prompt(option: tooLong), Actor));

        ex.Message.ShouldBe("labeling.promptTooLong");
    }

    [Fact]
    public async Task SavePromptAsync_InstructionExactly4000Characters_IsAccepted()
    {
        await using var ctx = _db.NewContext();
        var exactly = new string('x', LabelingPromptSettings.MaxInstructionLength);

        var state = await NewService(ctx).SavePromptAsync(Prompt(question: exactly), Actor);

        state.Active.QuestionInstruction.Length.ShouldBe(LabelingPromptSettings.MaxInstructionLength);
    }

    [Fact]
    public async Task SavePromptAsync_LongInstructionWithSurroundingWhitespace_LengthIsCheckedAfterTrim()
    {
        // Baştaki/sondaki boşluk sayılmaz: 4000 karakter + boşluklar kabul edilir.
        await using var ctx = _db.NewContext();
        var padded = "   " + new string('x', LabelingPromptSettings.MaxInstructionLength) + "   ";

        var state = await NewService(ctx).SavePromptAsync(Prompt(question: padded), Actor);

        state.Active.QuestionInstruction.ShouldBe(new string('x', LabelingPromptSettings.MaxInstructionLength));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(51, 0)]
    [InlineData(0, 51)]
    public async Task SavePromptAsync_PaddingOutsideZeroToFifty_ThrowsInvalidPadding(int questionPadding, int optionPadding)
    {
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SavePromptAsync(Prompt(questionPadding: questionPadding, optionPadding: optionPadding), Actor));

        ex.Message.ShouldBe("labeling.invalidPadding");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    public async Task SavePromptAsync_PaddingAtBoundaries_IsAccepted(int questionPadding, int optionPadding)
    {
        await using var ctx = _db.NewContext();

        var state = await NewService(ctx).SavePromptAsync(Prompt(questionPadding: questionPadding, optionPadding: optionPadding), Actor);

        state.Active.QuestionPadding.ShouldBe(questionPadding);
        state.Active.OptionPadding.ShouldBe(optionPadding);
    }

    [Fact]
    public async Task SavePromptAsync_ValidationFailure_DoesNotCreateVersion()
    {
        await using (var ctx = _db.NewContext())
        {
            await Should.ThrowAsync<LabelingValidationException>(() =>
                NewService(ctx).SavePromptAsync(Prompt(question: " "), Actor));
        }

        await using var read = _db.NewContext();
        (await read.LabelingPromptVersions.CountAsync()).ShouldBe(0);
    }

    // ---------- SavePromptAsync: sürümleme ve kayıt ----------

    [Fact]
    public async Task SavePromptAsync_FirstSave_CreatesVersionOne()
    {
        await using var ctx = _db.NewContext();

        var state = await NewService(ctx).SavePromptAsync(Prompt(), Actor);

        state.Active.Version.ShouldBe(1);
        state.History.Single().Version.ShouldBe(1);
    }

    [Fact]
    public async Task SavePromptAsync_Save_KeepsPreviousVersionsUnchanged()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.SavePromptAsync(Prompt(question: "alpha {width}", questionPadding: 3), Actor);

        await service.SavePromptAsync(Prompt(question: "beta {width}", questionPadding: 9), Actor);

        await using var read = _db.NewContext();
        var rows = await read.LabelingPromptVersions.OrderBy(p => p.Version).ToListAsync();
        rows.Count.ShouldBe(2);
        rows[0].QuestionInstruction.ShouldBe("alpha {width}");
        rows[0].QuestionPadding.ShouldBe(3);
        rows[1].QuestionInstruction.ShouldBe("beta {width}");
        rows[1].QuestionPadding.ShouldBe(9);
    }

    [Fact]
    public async Task SavePromptAsync_InstructionsWithWhitespace_AreStoredTrimmed()
    {
        await using var ctx = _db.NewContext();

        var state = await NewService(ctx).SavePromptAsync(Prompt(question: "  Q  ", option: "\nO\n"), Actor);

        state.Active.QuestionInstruction.ShouldBe("Q");
        state.Active.OptionInstruction.ShouldBe("O");
    }

    [Fact]
    public async Task SavePromptAsync_ExpandFlagFalse_IsStored()
    {
        await using var ctx = _db.NewContext();

        var state = await NewService(ctx).SavePromptAsync(Prompt(expand: false), Actor);

        state.Active.ExpandQuestionToOptions.ShouldBeFalse();
    }

    [Fact]
    public async Task SavePromptAsync_Save_RecordsActorAndClockTimestamp()
    {
        await using var ctx = _db.NewContext();

        var state = await NewService(ctx).SavePromptAsync(Prompt(), Actor);

        state.Active.CreatedByName.ShouldBe("Eğitmen");
        state.Active.CreatedAt.ShouldBe(Now.UtcDateTime);
    }

    [Fact]
    public async Task SavePromptAsync_ActorNameLongerThan200_IsTruncated()
    {
        await using var ctx = _db.NewContext();

        var state = await NewService(ctx).SavePromptAsync(Prompt(), new LabelingActor("kc-1", new string('a', 250)));

        state.Active.CreatedByName!.Length.ShouldBe(200);
    }

    [Fact]
    public async Task SavePromptAsync_ConcurrentWriterTakesNextVersion_RetriesOnceWithFreshNumber()
    {
        // Kaydetmeden hemen önce başka bir yazıcı 1 numaralı sürümü alır: UNIQUE çakışır, servis bir kez
        // yeniden numaralandırıp 2 olarak kaydetmeli.
        using var shared = SharedMemoryDb.Create();
        var interceptor = new ConcurrentVersionWriter(() => shared.Open());
        await using var ctx = shared.Open(interceptor);

        var state = await NewService(ctx).SavePromptAsync(Prompt(question: "mine {width}"), Actor);

        interceptor.Fired.ShouldBe(1);
        state.Active.Version.ShouldBe(2);
        state.Active.QuestionInstruction.ShouldBe("mine {width}");
        await using var read = shared.Open();
        (await read.LabelingPromptVersions.OrderBy(p => p.Version).Select(p => p.Version).ToListAsync())
            .ShouldBe([1, 2]);
    }

    // ---------- DetectWithAiAsync: aktif prompt ve önbellek ----------

    [Fact]
    public async Task DetectWithAiAsync_NoPromptSaved_SendsDefaultsToDetectorAndStoresVersionZero()
    {
        var png = Png(16, 16);
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);

        await _detector.Received(1).DetectAsync(
            Arg.Any<LabelingImage>(),
            Arg.Any<LabelingAiModelOption>(),
            Arg.Is<LabelingPromptSettings>(p => p.Version == 0 && p.QuestionPadding == 6 && p.OptionPadding == 4),
            Arg.Any<CancellationToken>());
        response.Result.PromptVersion.ShouldBe(0);
    }

    [Fact]
    public async Task DetectWithAiAsync_ActivePromptSaved_SendsSavedSettingsToDetector()
    {
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "Qx {width}", option: "Ox {height}",
                questionPadding: 11, optionPadding: 2, expand: false), Actor);
        }

        await using var call = _db.NewContext();
        await NewService(call).DetectWithAiAsync(DetectRequest(Png(16, 16)), Actor);

        await _detector.Received(1).DetectAsync(
            Arg.Any<LabelingImage>(),
            Arg.Any<LabelingAiModelOption>(),
            Arg.Is<LabelingPromptSettings>(p =>
                p.Version == 1 && p.QuestionInstruction == "Qx {width}" && p.OptionInstruction == "Ox {height}" &&
                p.QuestionPadding == 11 && p.OptionPadding == 2 && !p.ExpandQuestionToOptions),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DetectWithAiAsync_SameImageSamePromptVersion_SecondCallIsCached()
    {
        var png = Png(16, 16);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(), Actor);
        }

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor)).Origin.ShouldBe("live");
        }
        await using (var ctx = _db.NewContext())
        {
            var second = await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);

            second.Origin.ShouldBe("cached");
            second.Result.PromptVersion.ShouldBe(1);
        }

        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task DetectWithAiAsync_PromptChangedAfterCachedResult_InvalidatesCacheAndCallsClaudeAgain()
    {
        var png = Png(16, 16);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "v1 {width}"), Actor);
            await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);
        }

        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "v2 {width}"), Actor);
        }

        await using var after = _db.NewContext();
        var response = await NewService(after).DetectWithAiAsync(DetectRequest(png), Actor);

        response.Origin.ShouldBe("live");
        response.Result.PromptVersion.ShouldBe(2);
        await _detector.Received(2).DetectAsync(
            Arg.Any<LabelingImage>(), Arg.Any<LabelingAiModelOption>(), Arg.Any<LabelingPromptSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DetectWithAiAsync_PromptChanged_RerunOverwritesSingleAiRowWithNewVersion()
    {
        var png = Png(16, 16);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "v1 {width}"), Actor);
            await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);
        }
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "v2 {width}"), Actor);
            await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);
        }

        await using var read = _db.NewContext();
        var aiRows = await read.LabelingResults
            .Where(r => r.Source == LabelingSource.Ai && r.ModelName == "claude-test")
            .Select(r => r.PromptVersion)
            .ToListAsync();
        aiRows.ShouldBe([2]);
    }

    [Fact]
    public async Task DetectWithAiAsync_ForceTrueWithSameVersion_CallsClaudeAndStoresActiveVersion()
    {
        var png = Png(16, 16);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(), Actor);
            await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);
        }

        await using var forced = _db.NewContext();
        var response = await NewService(forced).DetectWithAiAsync(DetectRequest(png, force: true), Actor);

        response.Origin.ShouldBe("live");
        response.Result.PromptVersion.ShouldBe(1);
        await _detector.ReceivedWithAnyArgs(2).DetectAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task DetectWithAiAsync_LiveCallWithPrompt_ResultCarriesPromptVersionInDto()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.SavePromptAsync(Prompt(), Actor);

        var response = await service.DetectWithAiAsync(DetectRequest(Png(16, 16)), Actor);

        response.Result.PromptVersion.ShouldBe(1);
        response.Result.Source.ShouldBe("Ai");
    }

    [Fact]
    public async Task DetectWithAiAsync_ClaudeFailsAfterPromptSave_KeepsPriorCacheAndVersion()
    {
        var png = Png(16, 16);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "v1 {width}"), Actor);
            await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);
        }
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "v2 {width}"), Actor);
        }
        _detector.DetectAsync(default!, default!, default!, default)
            .ThrowsAsyncForAnyArgs(new LabelingAiException("boom"));

        await using var failing = _db.NewContext();
        await Should.ThrowAsync<LabelingAiException>(() =>
            NewService(failing).DetectWithAiAsync(DetectRequest(png, force: true), Actor));

        await using var read = _db.NewContext();
        (await read.LabelingResults.Where(r => r.Source == LabelingSource.Ai).Select(r => r.PromptVersion).ToListAsync())
            .ShouldBe([1]);
    }

    [Fact]
    public async Task SavePromptAsync_FourConsecutiveConflicts_StillSavesOnFifthAttempt()
    {
        using var shared = SharedMemoryDb.Create();
        var interceptor = new ConcurrentVersionWriter(() => shared.Open(), conflicts: 4);
        await using var ctx = shared.Open(interceptor);

        var state = await NewService(ctx).SavePromptAsync(Prompt(question: "retry {width}"), Actor);

        interceptor.Fired.ShouldBe(4);
        state.Active.Version.ShouldBe(5);
        state.Active.QuestionInstruction.ShouldBe("retry {width}");
    }

    [Fact]
    public async Task SavePromptAsync_FiveConsecutiveConflicts_ThrowsAndSavesNothingOfOurs()
    {
        using var shared = SharedMemoryDb.Create();
        var interceptor = new ConcurrentVersionWriter(() => shared.Open(), conflicts: 5);
        await using var ctx = shared.Open(interceptor);

        await Should.ThrowAsync<DbUpdateException>(() =>
            NewService(ctx).SavePromptAsync(Prompt(question: "never {width}"), Actor));

        interceptor.Fired.ShouldBe(5);
        await using var read = shared.Open();
        (await read.LabelingPromptVersions.Where(p => p.QuestionInstruction == "never {width}").CountAsync()).ShouldBe(0);
    }

    // ---------- SavePromptAsync: değişmeyen içerik (no-op) ----------

    [Fact]
    public async Task SavePromptAsync_SameContentAsCodeDefaultsWithNoVersions_DoesNotInsertVersion()
    {
        await using var ctx = _db.NewContext();

        var state = await NewService(ctx).SavePromptAsync(
            Prompt(LabelingPromptSettings.DefaultQuestionInstruction, LabelingPromptSettings.DefaultOptionInstruction), Actor);

        state.Active.Version.ShouldBe(0);
        state.History.ShouldBeEmpty();
        await using var read = _db.NewContext();
        (await read.LabelingPromptVersions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SavePromptAsync_SameContentAsActiveVersion_ReturnsCurrentStateWithoutNewVersion()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.SavePromptAsync(Prompt(question: "same {width}"), Actor);

        var state = await service.SavePromptAsync(Prompt(question: "same {width}"), Actor);

        state.Active.Version.ShouldBe(1);
        state.History.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SavePromptAsync_EditedCodeDefault_OpensNewVersionAndSameContentAgainDoesNot()
    {
        // Issue #578: ekrandaki varsayılan (blok türü talimatı) düzenlenip kaydedilince v1; aynı içerikle Kaydet v2 açmaz.
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var edited = LabelingPromptSettings.DefaultQuestionInstruction + " Treat \"Etkinlik\" headings as activities.";

        var first = await service.SavePromptAsync(Prompt(edited, LabelingPromptSettings.DefaultOptionInstruction), Actor);
        var second = await service.SavePromptAsync(Prompt(edited, LabelingPromptSettings.DefaultOptionInstruction), Actor);

        first.Active.Version.ShouldBe(1);
        first.Active.QuestionInstruction.ShouldBe(edited);
        first.Defaults.QuestionInstruction.ShouldBe(LabelingPromptSettings.DefaultQuestionInstruction);
        second.Active.Version.ShouldBe(1);
        second.History.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SavePromptAsync_WhitespaceOnlyDifference_IsTreatedAsSameContent()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.SavePromptAsync(Prompt(question: "Q {width}"), Actor);

        var state = await service.SavePromptAsync(Prompt(question: "  Q {width}  \n"), Actor);

        state.Active.Version.ShouldBe(1);
    }

    [Fact]
    public async Task SavePromptAsync_ContentEqualToOlderButNotActiveVersion_CreatesNewVersion()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.SavePromptAsync(Prompt(question: "A {width}"), Actor);
        await service.SavePromptAsync(Prompt(question: "B {width}"), Actor);

        var state = await service.SavePromptAsync(Prompt(question: "A {width}"), Actor);

        state.Active.Version.ShouldBe(3);
        state.Active.QuestionInstruction.ShouldBe("A {width}");
    }

    [Fact]
    public async Task SavePromptAsync_ExpandFlagOnlyChanged_CreatesNewVersion()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.SavePromptAsync(Prompt(expand: true), Actor);

        var state = await service.SavePromptAsync(Prompt(expand: false), Actor);

        state.Active.Version.ShouldBe(2);
    }

    // ---------- DetectWithAiAsync: içerik parmak izi önbelleği ----------

    [Fact]
    public async Task DetectWithAiAsync_LiveResult_StoresPromptHashOfActiveContent()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.SavePromptAsync(Prompt(question: "hash {width}"), Actor);

        var response = await service.DetectWithAiAsync(DetectRequest(Png(16, 16)), Actor);

        response.Result.PromptVersion.ShouldBe(1);
        await using var read = _db.NewContext();
        var stored = await read.LabelingResults.SingleAsync(r => r.Source == LabelingSource.Ai);
        stored.PromptHash.ShouldBe(Settings("hash {width}").Fingerprint);
        stored.PromptHash.ShouldNotBe(LabelingPromptSettings.Default.Fingerprint);
        stored.PromptVersion.ShouldBe(1);
    }

    [Fact]
    public async Task DetectWithAiAsync_NoOpSaveDoesNotInvalidateCache()
    {
        var png = Png(16, 16);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "noop {width}"), Actor);
            await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);
        }
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "noop {width}"), Actor);
        }

        await using var after = _db.NewContext();
        var response = await NewService(after).DetectWithAiAsync(DetectRequest(png), Actor);

        response.Origin.ShouldBe("cached");
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task DetectWithAiAsync_PromptRevertedToEarlierContent_ReusesCacheFromThatContent()
    {
        // Cache anahtarı sürüm numarası değil içerik parmak izi: A → B → A döndüğünde A'nın sonucu geçerlidir.
        var png = Png(16, 16);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "A {width}"), Actor);
            await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);
        }
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "B {width}"), Actor);
        }
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SavePromptAsync(Prompt(question: "A {width}"), Actor);
        }

        await using var after = _db.NewContext();
        var response = await NewService(after).DetectWithAiAsync(DetectRequest(png), Actor);

        response.Origin.ShouldBe("cached");
        response.Result.PromptVersion.ShouldBe(1);
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task DetectWithAiAsync_LegacyRowWithoutPromptHash_IsNeverServedFromCache()
    {
        var png = Png(16, 16);
        await SeedLegacyAiRowWithoutHashAsync(png);

        await using var ctx = _db.NewContext();
        var response = await NewService(ctx).DetectWithAiAsync(DetectRequest(png), Actor);

        response.Origin.ShouldBe("live");
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, default!, default!, default);
    }

    // ---------- SaveManualAsync: BasePromptVersion ----------

    [Fact]
    public async Task SaveManualAsync_BaseSourceAiWithBasePromptVersion_StoresVersionOnManualRow()
    {
        var png = Png(16, 16);
        await using var ctx = _db.NewContext();

        var page = await NewService(ctx).SaveManualAsync(ManualRequest(png, "Ai", "claude-test", 3), Actor);

        var manual = page.Results.Single(r => r.Source == "Manual");
        manual.BaseSource.ShouldBe("Ai");
        manual.BasePromptVersion.ShouldBe(3);
    }

    [Fact]
    public async Task SaveManualAsync_BasePromptVersionZero_IsKept()
    {
        var png = Png(16, 16);
        await using var ctx = _db.NewContext();

        var page = await NewService(ctx).SaveManualAsync(ManualRequest(png, "Ai", "claude-test", 0), Actor);

        page.Results.Single(r => r.Source == "Manual").BasePromptVersion.ShouldBe(0);
    }

    [Fact]
    public async Task SaveManualAsync_NegativeBasePromptVersion_IsDropped()
    {
        var png = Png(16, 16);
        await using var ctx = _db.NewContext();

        var page = await NewService(ctx).SaveManualAsync(ManualRequest(png, "Ai", "claude-test", -1), Actor);

        page.Results.Single(r => r.Source == "Manual").BasePromptVersion.ShouldBeNull();
    }

    [Fact]
    public async Task SaveManualAsync_BaseSourceModelWithBasePromptVersion_IsDropped()
    {
        var png = Png(16, 16);
        await using var ctx = _db.NewContext();

        var page = await NewService(ctx).SaveManualAsync(ManualRequest(png, "Model", null, 3), Actor);

        page.Results.Single(r => r.Source == "Manual").BasePromptVersion.ShouldBeNull();
    }

    [Fact]
    public async Task SaveManualAsync_BaseSourceAiWithoutBasePromptVersion_IsNull()
    {
        var png = Png(16, 16);
        await using var ctx = _db.NewContext();

        var page = await NewService(ctx).SaveManualAsync(ManualRequest(png, "Ai", "claude-test", null), Actor);

        page.Results.Single(r => r.Source == "Manual").BasePromptVersion.ShouldBeNull();
    }

    [Fact]
    public async Task GetPageAsync_AfterManualSaveWithBasePromptVersion_ReturnsItOnResultDto()
    {
        var png = Png(16, 16);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SaveManualAsync(ManualRequest(png, "Ai", "claude-test", 2), Actor);
        }

        await using var read = _db.NewContext();
        var page = await NewService(read).GetPageAsync(Sha256Hex(png));

        page!.Results.Single(r => r.Source == "Manual").BasePromptVersion.ShouldBe(2);
    }

    /// <summary>
    /// Kaydetmeden hemen önce (sürüm okuması bitti) başka bir bağlantının aynı sürüm numarasını yazdığını simüle eder.
    /// Ayrı bağlantı (paylaşımlı bellek DB) kullanılır; böylece gerçek bir eşzamanlı yazıcıya yakın davranır.
    /// <paramref name="conflicts"/> kez tetiklenir, sonra susar.
    /// </summary>
    private sealed class ConcurrentVersionWriter(Func<AppDbContext> openOtherContext, int conflicts = 1) : SaveChangesInterceptor
    {
        public int Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fired < conflicts && eventData.Context is AppDbContext && eventData.Context.ChangeTracker.Entries<LabelingPromptVersion>()
                    .Any(e => e.State == EntityState.Added))
            {
                Fired++;
                using var other = openOtherContext();
                var taken = (other.LabelingPromptVersions.Max(p => (int?)p.Version) ?? 0) + 1;
                other.LabelingPromptVersions.Add(new LabelingPromptVersion
                {
                    Version = taken,
                    QuestionInstruction = "other",
                    OptionInstruction = "other",
                    CreatedAt = Now.UtcDateTime,
                });
                other.SaveChanges();
            }
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Paylaşımlı bellek SQLite: her bağlam kendi bağlantısını açar; bu nesne veriyi canlı tutar.</summary>
    private sealed class SharedMemoryDb : IDisposable
    {
        private readonly SqliteConnection _keepAlive;
        private readonly string _connectionString;

        private SharedMemoryDb(SqliteConnection keepAlive, string connectionString)
        {
            _keepAlive = keepAlive;
            _connectionString = connectionString;
        }

        public static SharedMemoryDb Create()
        {
            var connectionString = $"Data Source=labelprompt-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keepAlive = new SqliteConnection(connectionString);
            keepAlive.Open();
            using (var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options))
                ctx.Database.EnsureCreated();
            return new SharedMemoryDb(keepAlive, connectionString);
        }

        public AppDbContext Open(params IInterceptor[] interceptors) => new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).AddInterceptors(interceptors).Options);

        public void Dispose() => _keepAlive.Dispose();
    }

    private static LabelingSaveManualRequestDto ManualRequest(byte[] png, string? baseSource, string? baseModelName = null,
        int? basePromptVersion = null) => new()
    {
        ImageBase64 = Convert.ToBase64String(png),
        Predictions = new PredictionListDto { Success = true, Predictions = [] },
        BaseSource = baseSource,
        BaseModelName = baseModelName,
        BasePromptVersion = basePromptVersion,
    };

    private static LabelingPromptSettings Settings(string question = "Q {width}", string option = "O {width}x{height}") =>
        new(0, question, option, 6, 4, true);

    private async Task SeedLegacyAiRowWithoutHashAsync(byte[] png)
    {
        await using var ctx = _db.NewContext();
        var page = new LabelingPage
        {
            ImageSha256 = Sha256Hex(png), Width = 16, Height = 16, CreatedAt = Now.UtcDateTime,
        };
        ctx.LabelingPages.Add(page);
        ctx.LabelingResults.Add(new LabelingResult
        {
            Page = page,
            Source = LabelingSource.Ai,
            ModelName = "claude-test",
            BoxesJson = "{\"success\":true,\"predictions\":[]}",
            PromptVersion = null,
            PromptHash = null,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        });
        await ctx.SaveChangesAsync();
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}
