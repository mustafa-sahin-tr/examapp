using NSubstitute.ExceptionExtensions;
using System.Security.Cryptography;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

public class LabelingServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly LabelingActor Actor = new("kc-trainer-1", "Eğitmen");

    private readonly TestDb _db = TestDb.Create();
    private readonly IClaudeLabelDetector _detector = Substitute.For<IClaudeLabelDetector>();
    private readonly FixedTimeProvider _clock = new(Now);

    public LabelingServiceTests()
    {
        _detector.IsConfigured.Returns(true);
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection());
        _counters = new InMemoryFixedWindowCounterStore(_clock);
    }

    public void Dispose() => _db.Dispose();

    private static LabelingAiModelOption Model(string id = "claude-test") => new()
    {
        Id = id,
        DisplayName = "Test",
        InputUsdPerMTok = 3m,
        OutputUsdPerMTok = 15m,
        IsDefault = true,
    };

    // Kota ve bütçe sayaçları test başına tek mağazada tutulur (her NewService çağrısı aynı durumu görür).
    private readonly InMemoryFixedWindowCounterStore _counters;

    private LabelingService NewService(AppDbContext ctx, int liveCallsPerHour = 60, LabelingAiModelOption[]? models = null,
        decimal? dailyBudgetUsdPerUser = null, decimal? dailyBudgetUsdTotal = null)
    {
        var options = new LabelingAiOptions
        {
            LiveCallsPerHour = liveCallsPerHour,
            Models = models is { Length: > 0 } ? models.ToList() : [Model()],
            DailyBudgetUsdPerUser = dailyBudgetUsdPerUser ?? 5m,
            DailyBudgetUsdTotal = dailyBudgetUsdTotal ?? 25m,
        };
        return new LabelingService(
            ctx,
            _detector,
            _counters,
            Options.Create(options),
            _clock,
            NullLogger<LabelingService>.Instance);
    }

    private static byte[] Png(int width, int height) => TestImages.Png(width, height);

    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes);

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static PredictionListDto Boxes(params (int X, int Y, int W, int H)[] questions) => new()
    {
        Success = true,
        Predictions = questions.Select(q => new PredictionDto
        {
            ClassId = 0, X = q.X, Y = q.Y, Width = q.W, Height = q.H,
            Subpredictions = [new PredictionDto { ClassId = 0, X = q.X + 1, Y = q.Y + 1, Width = 5, Height = 5 }],
        }).ToList(),
    };

    private static ClaudeDetection Detection(int input = 100, int output = 50) =>
        new(Boxes((10, 10, 100, 80)), input, output);

    private LabelingSaveManualRequestDto ManualRequest(byte[] png, PredictionListDto? predictions = null,
        string? baseSource = null, string? baseModelName = null, PredictionListDto? snapshot = null) => new()
    {
        ImageBase64 = Base64(png),
        Predictions = predictions ?? Boxes((1, 2, 30, 40)),
        BaseSource = baseSource,
        BaseModelName = baseModelName,
        ModelSnapshot = snapshot,
    };

    // ---------- GetPageAsync ----------

    [Fact]
    public async Task GetPageAsync_InvalidHash_ThrowsValidation()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => service.GetPageAsync("NOT-A-HASH"));
        ex.Message.ShouldBe("labeling.invalidHash");
    }

    [Fact]
    public async Task GetPageAsync_UppercaseHash_IsRejected()
    {
        await using var ctx = _db.NewContext();
        var hash = Sha256Hex(Png(4, 4)).ToUpperInvariant();

        await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).GetPageAsync(hash));
    }

    [Fact]
    public async Task GetPageAsync_UnknownValidHash_ReturnsNull()
    {
        await using var ctx = _db.NewContext();
        var hash = Sha256Hex(Png(4, 4));

        (await NewService(ctx).GetPageAsync(hash)).ShouldBeNull();
    }

    [Fact]
    public async Task SaveManualAsync_ThenGetPageAsync_ReturnsPageWithSha256OfRawBytes()
    {
        var png = Png(320, 200);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SaveManualAsync(ManualRequest(png), Actor);
        }

        await using var read = _db.NewContext();
        var page = await NewService(read).GetPageAsync(Sha256Hex(png));

        page.ShouldNotBeNull();
        page.ImageSha256.ShouldBe(Sha256Hex(png));
        page.Width.ShouldBe(320);
        page.Height.ShouldBe(200);
    }

    // ---------- DetectWithAiAsync ----------

    [Fact]
    public async Task DetectWithAiAsync_UnknownModel_ThrowsValidationBeforeCallingClaude()
    {
        await using var ctx = _db.NewContext();
        var request = new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(8, 8)), Model = "gpt-nope" };

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).DetectWithAiAsync(request, Actor));

        ex.Message.ShouldBe("labeling.unknownModel");
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, default!, default!);
    }

    [Fact]
    public async Task DetectWithAiAsync_InvalidImage_ThrowsValidation()
    {
        await using var ctx = _db.NewContext();
        var request = new LabelingAiDetectRequestDto { ImageBase64 = Base64("not an image"u8.ToArray()), Model = "claude-test" };

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).DetectWithAiAsync(request, Actor));

        ex.Message.ShouldBe("labeling.invalidImage");
    }

    [Fact]
    public async Task DetectWithAiAsync_DetectorNotConfigured_ThrowsUnavailableAndConsumesNoQuota()
    {
        _detector.IsConfigured.Returns(false);
        await using var ctx = _db.NewContext();
        var service = NewService(ctx, liveCallsPerHour: 1);
        var request = new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(8, 8)), Model = "claude-test" };

        await Should.ThrowAsync<LabelingAiUnavailableException>(() => service.DetectWithAiAsync(request, Actor));

        // Anahtar sonradan eklendiğinde kota hâlâ boş olmalı: 503 çağrı sayılmaz.
        _detector.IsConfigured.Returns(true);
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection());
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection());
        var result = await service.DetectWithAiAsync(request, Actor);
        result.Origin.ShouldBe("live");
    }

    [Fact]
    public async Task DetectWithAiAsync_NotConfiguredButCachedResult_ReturnsCachedWithoutError()
    {
        var png = Png(8, 8);
        var request = new LabelingAiDetectRequestDto { ImageBase64 = Base64(png), Model = "claude-test" };
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).DetectWithAiAsync(request, Actor);
        }

        _detector.IsConfigured.Returns(false);
        await using var second = _db.NewContext();
        var result = await NewService(second).DetectWithAiAsync(request, Actor);

        result.Origin.ShouldBe("cached");
    }

    [Fact]
    public async Task DetectWithAiAsync_LiveCall_PersistsAiResultWithTokensAndCost()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection(input: 1000, output: 2000));
        var png = Png(64, 64);
        var request = new LabelingAiDetectRequestDto { ImageBase64 = Base64(png), Model = "claude-test" };

        await using var ctx = _db.NewContext();
        var response = await NewService(ctx).DetectWithAiAsync(request, Actor);

        response.Origin.ShouldBe("live");
        response.ImageSha256.ShouldBe(Sha256Hex(png));
        response.Result.Source.ShouldBe("Ai");
        response.Result.ModelName.ShouldBe("claude-test");
        response.Result.InputTokens.ShouldBe(1000);
        response.Result.OutputTokens.ShouldBe(2000);
        // (1000 * 3 + 2000 * 15) / 1e6 = 0.033
        response.Result.EstimatedCostUsd.ShouldBe(0.033m);
        response.Result.CreatedByName.ShouldBe("Eğitmen");
        response.Result.Predictions.Predictions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DetectWithAiAsync_SameImageTwice_SecondCallIsCachedAndDoesNotCallClaudeAgain()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection());
        var request = new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(16, 16)), Model = "claude-test" };

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var first = await service.DetectWithAiAsync(request, Actor);
        var second = await service.DetectWithAiAsync(request, Actor);

        first.Origin.ShouldBe("live");
        second.Origin.ShouldBe("cached");
        second.Result.Predictions.Predictions.Count.ShouldBe(first.Result.Predictions.Predictions.Count);
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, default!, default!);
    }

    [Fact]
    public async Task DetectWithAiAsync_CachedResultForSameImageAndModel_DoesNotConsumeRateLimit()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection());
        var request = new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(16, 16)), Model = "claude-test" };

        await using var ctx = _db.NewContext();
        var service = NewService(ctx, liveCallsPerHour: 1);
        await service.DetectWithAiAsync(request, Actor);

        // Kota (1/saat) doldu; önbellekten dönüş 429 vermemeli.
        var again = await service.DetectWithAiAsync(request, Actor);
        again.Origin.ShouldBe("cached");
    }

    [Fact]
    public async Task DetectWithAiAsync_ForceTrue_CallsClaudeEvenWhenCachedAndOverwritesSingleRow()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection(input: 10, output: 10));
        var png = Png(16, 16);
        await using var ctx = _db.NewContext();
        var service = NewService(ctx, liveCallsPerHour: 10);
        await service.DetectWithAiAsync(new LabelingAiDetectRequestDto { ImageBase64 = Base64(png), Model = "claude-test" }, Actor);

        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection(input: 77, output: 88));
        var forced = await service.DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(png), Model = "claude-test", Force = true }, Actor);

        forced.Origin.ShouldBe("live");
        forced.Result.InputTokens.ShouldBe(77);
        await _detector.ReceivedWithAnyArgs(2).DetectAsync(default!, default!, default!);

        await using var read = _db.NewContext();
        var page = await NewService(read).GetPageAsync(Sha256Hex(png));
        page!.Results.Count(r => r.Source == "Ai").ShouldBe(1);
        page.Results.Single(r => r.Source == "Ai").InputTokens.ShouldBe(77);
    }

    [Fact]
    public async Task DetectWithAiAsync_SameImageDifferentModels_KeepsOneAiRowPerModel()
    {
        var png = Png(16, 16);
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection());
        var models = new[] { Model("claude-a"), Model("claude-b") };

        await using var ctx = _db.NewContext();
        var service = NewService(ctx, 60, models);
        await service.DetectWithAiAsync(new LabelingAiDetectRequestDto { ImageBase64 = Base64(png), Model = "claude-a" }, Actor);
        await service.DetectWithAiAsync(new LabelingAiDetectRequestDto { ImageBase64 = Base64(png), Model = "claude-b" }, Actor);

        await using var read = _db.NewContext();
        var page = await NewService(read, 60, models).GetPageAsync(Sha256Hex(png));
        page!.Results.Where(r => r.Source == "Ai").Select(r => r.ModelName).OrderBy(n => n)
            .ShouldBe(["claude-a", "claude-b"]);
    }

    [Fact]
    public async Task DetectWithAiAsync_RateLimitExceeded_ThrowsWithRetryAfterAndSkipsClaude()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection());
        await using var ctx = _db.NewContext();
        var service = NewService(ctx, liveCallsPerHour: 1);

        await service.DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(16, 16)), Model = "claude-test" }, Actor);

        var ex = await Should.ThrowAsync<LabelingRateLimitException>(() => service.DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(17, 17)), Model = "claude-test" }, Actor));

        ex.RetryAfter.ShouldBe(TimeSpan.FromHours(1));
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, default!, default!);
    }

    [Fact]
    public async Task DetectWithAiAsync_RateLimitIsPerUser_OtherUserIsNotBlocked()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection());
        await using var ctx = _db.NewContext();
        var service = NewService(ctx, liveCallsPerHour: 1);

        await service.DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(16, 16)), Model = "claude-test" }, Actor);

        var other = await service.DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(18, 18)), Model = "claude-test" },
            new LabelingActor("kc-trainer-2", "Diğer"));

        other.Origin.ShouldBe("live");
    }

    [Fact]
    public async Task DetectWithAiAsync_ClaudeFails_PropagatesAndSavesNothing()
    {
        _detector.DetectAsync(default!, default!, default!)
            .ThrowsAsyncForAnyArgs(new LabelingAiException("Claude response was truncated."));
        var png = Png(16, 16);

        await using (var ctx = _db.NewContext())
        {
            await Should.ThrowAsync<LabelingAiException>(() => NewService(ctx).DetectWithAiAsync(
                new LabelingAiDetectRequestDto { ImageBase64 = Base64(png), Model = "claude-test" }, Actor));
        }

        await using var read = _db.NewContext();
        (await NewService(read).GetPageAsync(Sha256Hex(png))).ShouldBeNull();
    }

    [Fact]
    public async Task DetectWithAiAsync_ClaudeFailsAfterPaidResponse_ChargesBudgetAndRethrows()
    {
        // Security D1: kesik/ret/şema dışı yanıtın token'ı ödendi → bütçeye sayılır (1M girdi * $3 + 1M çıktı * $15).
        _detector.DetectAsync(default!, default!, default!).ThrowsAsyncForAnyArgs(
            new LabelingAiException("Claude response was truncated.", usage: new LabelingAiUsage(1_000_000, 1_000_000)));
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<LabelingAiException>(() => NewService(ctx).DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(16, 16)), Model = "claude-test" }, Actor));

        (await _counters.PeekAsync($"labeling-ai-usd:{Actor.KeycloakId}")).Count.ShouldBe(18_000_000);
        (await _counters.PeekAsync("labeling-ai-usd:all")).Count.ShouldBe(18_000_000);
    }

    [Fact]
    public async Task DetectWithAiAsync_ClaudeFailsWithoutResponse_ChargesNothing()
    {
        _detector.DetectAsync(default!, default!, default!)
            .ThrowsAsyncForAnyArgs(new LabelingAiException("Claude request failed."));
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<LabelingAiException>(() => NewService(ctx).DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(16, 16)), Model = "claude-test" }, Actor));

        (await _counters.PeekAsync($"labeling-ai-usd:{Actor.KeycloakId}")).Count.ShouldBe(0);
    }

    // ---------- SaveManualAsync ----------

    [Fact]
    public async Task SaveManualAsync_WithSnapshot_StoresManualAndModelRows()
    {
        var png = Png(100, 100);
        var snapshot = Boxes((5, 5, 20, 20));
        var manual = Boxes((1, 2, 30, 40), (50, 50, 20, 20));

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).SaveManualAsync(
            ManualRequest(png, predictions: manual, baseSource: "Model", snapshot: snapshot), Actor);

        page.Results.Select(r => r.Source).ShouldBe(["Model", "Manual"]);
        var manualRow = page.Results.Single(r => r.Source == "Manual");
        manualRow.BaseSource.ShouldBe("Model");
        manualRow.BaseModelName.ShouldBeNull();
        manualRow.Predictions.Predictions.Count.ShouldBe(2);
        page.Results.Single(r => r.Source == "Model").Predictions.Predictions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SaveManualAsync_WithoutSnapshot_DoesNotCreateModelRow()
    {
        var png = Png(100, 100);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).SaveManualAsync(ManualRequest(png), Actor);

        page.Results.Select(r => r.Source).ShouldBe(["Manual"]);
    }

    [Fact]
    public async Task SaveManualAsync_CalledTwiceWithSameImage_UpsertsSingleRowPerSource()
    {
        var png = Png(100, 100);
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        await service.SaveManualAsync(ManualRequest(png, Boxes((1, 1, 10, 10)), snapshot: Boxes((2, 2, 10, 10))), Actor);
        var page = await service.SaveManualAsync(
            ManualRequest(png, Boxes((1, 1, 10, 10), (30, 30, 10, 10)), snapshot: Boxes((2, 2, 10, 10))), Actor);

        page.Results.Count.ShouldBe(2);
        page.Results.Single(r => r.Source == "Manual").Predictions.Predictions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SaveManualAsync_BaseSourceIsCaseInsensitive()
    {
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).SaveManualAsync(ManualRequest(Png(10, 10), baseSource: "ai", baseModelName: "claude-test"), Actor);

        var manual = page.Results.Single(r => r.Source == "Manual");
        manual.BaseSource.ShouldBe("Ai");
        manual.BaseModelName.ShouldBe("claude-test");
    }

    [Fact]
    public async Task SaveManualAsync_BaseSourceModel_DropsBaseModelName()
    {
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).SaveManualAsync(ManualRequest(Png(10, 10), baseSource: "Model", baseModelName: "claude-test"), Actor);

        page.Results.Single(r => r.Source == "Manual").BaseModelName.ShouldBeNull();
    }

    [Fact]
    public async Task SaveManualAsync_BaseSourceAi_TruncatesBaseModelNameTo64()
    {
        await using var ctx = _db.NewContext();
        var longName = new string('m', 80);
        var page = await NewService(ctx).SaveManualAsync(ManualRequest(Png(10, 10), baseSource: "Ai", baseModelName: longName), Actor);

        page.Results.Single(r => r.Source == "Manual").BaseModelName!.Length.ShouldBe(64);
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("Ai2")]
    [InlineData("5")]
    public async Task SaveManualAsync_BaseSourceOutsideModelOrAi_ThrowsValidation(string baseSource)
    {
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SaveManualAsync(ManualRequest(Png(10, 10), baseSource: baseSource), Actor));

        ex.Message.ShouldBe("labeling.invalidBaseSource");
    }

    [Fact]
    public async Task SaveManualAsync_NegativeQuestionCoordinate_ThrowsInvalidBox()
    {
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SaveManualAsync(ManualRequest(Png(10, 10), predictions: Boxes((-1, 0, 5, 5))), Actor));

        ex.Message.ShouldBe("labeling.invalidBox");
    }

    [Fact]
    public async Task SaveManualAsync_NegativeOptionCoordinateInSnapshot_ThrowsInvalidBox()
    {
        await using var ctx = _db.NewContext();
        var snapshot = Boxes((1, 1, 5, 5));
        snapshot.Predictions[0].Subpredictions[0].Height = -3;

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SaveManualAsync(ManualRequest(Png(10, 10), snapshot: snapshot), Actor));

        ex.Message.ShouldBe("labeling.invalidBox");
    }

    [Fact]
    public async Task SaveManualAsync_MoreThan300Questions_ThrowsTooManyBoxes()
    {
        await using var ctx = _db.NewContext();
        var predictions = new PredictionListDto
        {
            Predictions = Enumerable.Range(0, 301).Select(i => new PredictionDto { X = i, Y = 0, Width = 1, Height = 1 }).ToList(),
        };

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SaveManualAsync(ManualRequest(Png(10, 10), predictions: predictions), Actor));

        ex.Message.ShouldBe("labeling.tooManyBoxes");
    }

    [Fact]
    public async Task SaveManualAsync_MoreThan26OptionsForOneQuestion_ThrowsTooManyBoxes()
    {
        await using var ctx = _db.NewContext();
        var predictions = Boxes((1, 1, 50, 50));
        predictions.Predictions[0].Subpredictions = Enumerable.Range(0, 27)
            .Select(i => new PredictionDto { X = i, Y = 0, Width = 1, Height = 1 }).ToList();

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SaveManualAsync(ManualRequest(Png(10, 10), predictions: predictions), Actor));

        ex.Message.ShouldBe("labeling.tooManyBoxes");
    }

    [Fact]
    public async Task SaveManualAsync_InvalidBase64_ThrowsInvalidImage()
    {
        await using var ctx = _db.NewContext();
        var request = new LabelingSaveManualRequestDto { ImageBase64 = "%%%not-base64%%%", Predictions = Boxes() };

        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveManualAsync(request, Actor));

        ex.Message.ShouldBe("labeling.invalidImage");
    }

    [Fact]
    public async Task SaveManualAsync_CreatedByNameLongerThan200_IsTruncated()
    {
        await using var ctx = _db.NewContext();
        var actor = new LabelingActor("kc-1", new string('a', 250));

        var page = await NewService(ctx).SaveManualAsync(ManualRequest(Png(10, 10)), actor);

        page.Results.Single().CreatedByName!.Length.ShouldBe(200);
    }

    [Fact]
    public async Task DetectWithAiAsync_AfterManualSave_IsNotServedFromManualRow()
    {
        var png = Png(40, 40);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SaveManualAsync(ManualRequest(png), Actor);
        }

        await using var read = _db.NewContext();
        var response = await NewService(read).DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(png), Model = "claude-test" }, Actor);

        // Manuel kayıt Ai önbelleği değildir: bu sayfa için Claude'a gerçekten sorulmalı.
        response.Origin.ShouldBe("live");
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, default!, default!);
    }

    [Fact]
    public async Task GetAiModels_DetectorNotConfigured_ReturnsEmptyList()
    {
        _detector.IsConfigured.Returns(false);
        await using var ctx = _db.NewContext();

        NewService(ctx).GetAiModels().ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAiModels_DetectorConfigured_ReturnsConfiguredModels()
    {
        await using var ctx = _db.NewContext();

        var models = NewService(ctx, models: [Model("claude-a"), Model("claude-b")]).GetAiModels();

        models.Select(m => m.Id).ShouldBe(["claude-a", "claude-b"]);
    }

    [Fact]
    public async Task DetectWithAiAsync_UserDailyBudgetExhausted_ThrowsBudgetAndSkipsClaude()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection(input: 1000, output: 2000)); // 0.033 USD
        await using var ctx = _db.NewContext();
        var service = NewService(ctx, liveCallsPerHour: 100, dailyBudgetUsdPerUser: 0.01m);
        await service.DetectWithAiAsync(new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(16, 16)), Model = "claude-test" }, Actor);

        var ex = await Should.ThrowAsync<LabelingRateLimitException>(() => service.DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(17, 17)), Model = "claude-test" }, Actor));

        ex.Budget.ShouldBeTrue();
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, default!, default!);
    }

    [Fact]
    public async Task DetectWithAiAsync_CachedResultWhileBudgetExhausted_StillReturnsCached()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection(input: 1000, output: 2000));
        var png = Png(16, 16);
        var request = new LabelingAiDetectRequestDto { ImageBase64 = Base64(png), Model = "claude-test" };
        await using var ctx = _db.NewContext();
        var service = NewService(ctx, liveCallsPerHour: 100, dailyBudgetUsdPerUser: 0.01m);
        await service.DetectWithAiAsync(request, Actor);

        var again = await service.DetectWithAiAsync(request, Actor);

        again.Origin.ShouldBe("cached");
    }

    [Fact]
    public async Task DetectWithAiAsync_TotalDailyBudgetExhaustedByAnotherUser_BlocksOtherUsers()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection(input: 1000, output: 2000));
        await using var ctx = _db.NewContext();
        var service = NewService(ctx, liveCallsPerHour: 100, dailyBudgetUsdTotal: 0.01m);
        await service.DetectWithAiAsync(new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(16, 16)), Model = "claude-test" }, Actor);

        var ex = await Should.ThrowAsync<LabelingRateLimitException>(() => service.DetectWithAiAsync(
            new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(17, 17)), Model = "claude-test" },
            new LabelingActor("kc-trainer-2", "Diğer")));

        ex.Budget.ShouldBeTrue();
    }

    [Fact]
    public async Task DetectWithAiAsync_ZeroCostCall_DoesNotChargeBudget()
    {
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(Detection(input: 0, output: 0));
        await using var ctx = _db.NewContext();
        var service = NewService(ctx, liveCallsPerHour: 100, dailyBudgetUsdPerUser: 0.000001m);

        await service.DetectWithAiAsync(new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(16, 16)), Model = "claude-test" }, Actor);
        var second = await service.DetectWithAiAsync(new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(17, 17)), Model = "claude-test" }, Actor);

        second.Origin.ShouldBe("live");
    }

    [Fact]
    public async Task SaveManualAsync_BoxesExtendingPastPage_AreClampedToPageSize()
    {
        await using var ctx = _db.NewContext();

        var page = await NewService(ctx).SaveManualAsync(
            ManualRequest(Png(100, 100), predictions: Boxes((90, 90, 50, 50))), Actor);

        var box = page.Results.Single().Predictions.Predictions.Single();
        box.X.ShouldBe(90);
        box.Y.ShouldBe(90);
        box.Width.ShouldBe(10);
        box.Height.ShouldBe(10);
    }

    [Fact]
    public async Task SaveManualAsync_NonZeroClassId_ThrowsInvalidBox()
    {
        await using var ctx = _db.NewContext();
        var predictions = Boxes((1, 1, 10, 10));
        predictions.Predictions[0].ClassId = 3;

        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SaveManualAsync(ManualRequest(Png(100, 100), predictions: predictions), Actor));

        ex.Message.ShouldBe("labeling.invalidBox");
    }

    // ---------- Issue #578: blok türleri ----------

    private static PredictionListDto Blocks(params (string? Kind, string? Component, string? InteractionType)[] blocks) => new()
    {
        Success = true,
        Predictions = blocks.Select((b, i) => new PredictionDto
        {
            ClassId = 0, X = 1, Y = 1 + i * 20, Width = 30, Height = 15,
            Kind = b.Kind, Component = b.Component, InteractionType = b.InteractionType,
        }).ToList(),
    };

    [Fact]
    public async Task SaveManualAsync_BlockKinds_RoundTripThroughStoredResult()
    {
        var png = Png(100, 200);
        var predictions = Blocks(
            (null, null, null),
            ("mcq_question", null, null),
            ("activity", "C", "match"),
            ("activity", "F", null),
            ("info_box", null, null),
            ("answer_key", null, null),
            ("noise", null, null));
        predictions.Predictions[1].Subpredictions = [new PredictionDto { X = 2, Y = 22, Width = 5, Height = 5 }];
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SaveManualAsync(ManualRequest(png, predictions), Actor);
        }

        await using var read = _db.NewContext();
        var page = await NewService(read).GetPageAsync(Sha256Hex(png));

        var stored = page!.Results.Single(r => r.Source == "Manual").Predictions.Predictions;
        stored.Select(p => p.Kind).ShouldBe([null, "mcq_question", "activity", "activity", "info_box", "answer_key", "noise"]);
        stored.Select(p => p.Component).ShouldBe([null, null, "C", "F", null, null, null]);
        stored.Select(p => p.InteractionType).ShouldBe([null, null, "match", null, null, null, null]);
        stored[1].Subpredictions.Single().Kind.ShouldBeNull();
    }

    [Fact]
    public async Task SaveManualAsync_OptionCarryingBlockFields_AreNotStored()
    {
        var png = Png(100, 100);
        var predictions = Blocks(("mcq_question", null, null));
        predictions.Predictions[0].Subpredictions =
            [new PredictionDto { X = 2, Y = 2, Width = 5, Height = 5, Kind = "activity", Component = "A" }];
        await using var ctx = _db.NewContext();

        var page = await NewService(ctx).SaveManualAsync(ManualRequest(png, predictions), Actor);

        var option = page.Results.Single().Predictions.Predictions.Single().Subpredictions.Single();
        option.Kind.ShouldBeNull();
        option.Component.ShouldBeNull();
    }

    [Theory]
    [InlineData("question", null, null)]
    [InlineData("info_box", "A", null)]
    [InlineData("activity", "J", null)]
    [InlineData("activity", "A", "slotFill")]
    [InlineData("activity", null, "dragPlace")]
    [InlineData(null, null, "match")]
    public async Task SaveManualAsync_InvalidBlockKind_ThrowsAndSavesNothing(string? kind, string? component, string? interactionType)
    {
        var png = Png(100, 100);
        await using (var ctx = _db.NewContext())
        {
            var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
                NewService(ctx).SaveManualAsync(ManualRequest(png, Blocks((kind, component, interactionType))), Actor));
            ex.Message.ShouldBe("labeling.invalidBlockKind");
        }

        await using var read = _db.NewContext();
        (await NewService(read).GetPageAsync(Sha256Hex(png)))?.Results.ShouldBeEmpty();
    }

    [Fact]
    public async Task DetectWithAiAsync_BlockKinds_ArePersistedAndServedFromCache()
    {
        var detection = new ClaudeDetection(Blocks(("activity", "B", "slotFill"), ("noise", null, null)), 10, 5);
        _detector.DetectAsync(default!, default!, default!).ReturnsForAnyArgs(detection);
        var request = new LabelingAiDetectRequestDto { ImageBase64 = Base64(Png(50, 60)), Model = "claude-test" };
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).DetectWithAiAsync(request, Actor);
        }

        await using var read = _db.NewContext();
        var cached = await NewService(read).DetectWithAiAsync(request, Actor);

        cached.Origin.ShouldBe("cached");
        var p = cached.Result.Predictions.Predictions;
        p.Select(x => x.Kind).ShouldBe(["activity", "noise"]);
        p[0].Component.ShouldBe("B");
        p[0].InteractionType.ShouldBe("slotFill");
    }

    [Fact]
    public async Task GetPageAsync_LegacyStoredJsonWithoutKind_ReadsAsNullKind()
    {
        var png = Png(40, 40);
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).SaveManualAsync(ManualRequest(png), Actor);
            var row = ctx.LabelingResults.Single();
            // Revizyon 2 öncesi kayıt: kind alanları hiç yok.
            row.BoxesJson = """{"success":true,"predictions":[{"class_id":0,"x":1,"y":2,"width":3,"height":4,"subpredictions":[]}]}""";
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var box = (await NewService(read).GetPageAsync(Sha256Hex(png)))!.Results.Single().Predictions.Predictions.Single();

        box.Kind.ShouldBeNull();
        box.Component.ShouldBeNull();
        box.InteractionType.ShouldBeNull();
    }

    // ---------- issue #580: etiketler manuel kayıtta ----------

    [Fact]
    public async Task SaveManualAsync_RoundTripsTrimmedLabels()
    {
        var png = Png(100, 100);
        var boxes = Boxes((1, 2, 30, 40), (50, 50, 20, 20));
        boxes.Predictions[0].Label = "  ETKİNLİK 3 ";
        boxes.Predictions[1].Label = "   ";
        boxes.SectionLabel = " TEST 01 ";

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).SaveManualAsync(ManualRequest(png, boxes), Actor);

        var manual = page.Results.Single(r => r.Source == "Manual").Predictions;
        manual.SectionLabel.ShouldBe("TEST 01");
        manual.Predictions.Select(p => p.Label).ShouldBe(["ETKİNLİK 3", null]);
    }

    [Theory]
    [InlineData(65, false)]
    [InlineData(10, true)]
    public async Task SaveManualAsync_InvalidLabel_Is400(int length, bool control)
    {
        var boxes = Boxes((1, 2, 30, 40));
        boxes.Predictions[0].Label = control ? "ETK\u0001" : new string('a', length);

        await using var ctx = _db.NewContext();
        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveManualAsync(ManualRequest(Png(100, 100), boxes), Actor));
        ex.Message.ShouldBe("labeling.invalidLabel");
    }
}
