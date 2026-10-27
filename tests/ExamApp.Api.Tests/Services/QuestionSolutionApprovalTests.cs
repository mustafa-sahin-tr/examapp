using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #580: sorunun çözüm kaynağı / onay kuralları — saf kural (<see cref="QuestionSolutionApproval"/>), soru kaydı ve toplu kayıt
/// yolları, onay servisi, soru bankası <c>solutionStatus</c> filtresi ve migration'ın veri adımı (<see cref="QuestionSolutionBackfill"/>).
/// </summary>
public class QuestionSolutionApprovalTests : IDisposable
{
    private const string MatrixType = "matrixChoice";

    private const string Plan =
        """{"version":1,"type":"matrixChoice","multiPerRow":false,"columns":[{"id":"cD","label":"D"},{"id":"cY","label":"Y"}],"rows":[{"id":"r1","box":{"x":0,"y":0,"width":10,"height":3}},{"id":"r2","box":{"x":0,"y":4,"width":10,"height":3}}]}""";

    private const string Solution = """{"r1":["cD"],"r2":["cY"]}""";
    private const string OtherSolution = """{"r1":["cY"],"r2":["cY"]}""";

    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);
    private static readonly SolutionActor Trainer = new("kc-qt", "Eğitmen", CanApprove: true);
    private static readonly SolutionActor Teacher = new("kc-t", "Öğretmen", CanApprove: false);

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    public void Dispose() => _db.Dispose();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    // ================================================================== saf kural

    [Fact]
    public void Apply_NoSolution_IsNoneApproved_AndClearsApproval()
    {
        var q = new Question { SolutionSource = SolutionSource.AI, SolutionStatus = SolutionStatus.Pending, SolutionPromptVersion = 2 };

        QuestionSolutionApproval.Apply(q, hasSolution: false, solutionChanged: true, SolutionSource.AI, true, 2, Trainer, Now);

        (q.SolutionSource, q.SolutionStatus, q.SolutionPromptVersion, q.SolutionApprovedAt)
            .ShouldBe((SolutionSource.None, SolutionStatus.Approved, (int?)null, (DateTime?)null));
    }

    [Theory]
    [InlineData("Key", false, true, "Pending", null)]
    [InlineData("AI", false, true, "Pending", null)]
    [InlineData("AI", true, false, "Pending", null)]      // onay isteği ama çağıran onaylayamaz
    [InlineData("AI", true, true, "Approved", "kc-qt")]
    [InlineData("Key", true, true, "Approved", "kc-qt")]
    [InlineData(null, false, false, "Approved", "kc-t")]  // kaynak yok = Human, onaylı (yazan)
    [InlineData("Human", false, false, "Approved", "kc-t")]
    public void Apply_ChangedSolution(string? source, bool requestApproved, bool canApprove, string status, string? approver)
    {
        QuestionSolutionApproval.TryParseSource(source, out var parsed).ShouldBeTrue();
        var actor = canApprove ? Trainer : Teacher;
        var q = new Question();

        QuestionSolutionApproval.Apply(q, hasSolution: true, solutionChanged: true, parsed, requestApproved, 4, actor, Now);

        q.SolutionSource.ShouldBe(parsed ?? SolutionSource.Human);
        q.SolutionStatus.ToString().ShouldBe(status);
        q.SolutionApprovedByKeycloakId.ShouldBe(approver);
        q.SolutionApprovedAt.ShouldBe(approver is null ? null : Now);
        q.SolutionPromptVersion.ShouldBe(q.SolutionSource == SolutionSource.Human ? null : 4);
    }

    [Fact]
    public void Apply_UnchangedSolution_KeepsFields_UnlessAnApproverApprovesIt()
    {
        var q = new Question { SolutionSource = SolutionSource.AI, SolutionStatus = SolutionStatus.Pending, SolutionPromptVersion = 1 };

        QuestionSolutionApproval.Apply(q, true, solutionChanged: false, SolutionSource.Human, true, null, Teacher, Now);
        (q.SolutionSource, q.SolutionStatus).ShouldBe((SolutionSource.AI, SolutionStatus.Pending));

        QuestionSolutionApproval.Apply(q, true, solutionChanged: false, null, true, null, Trainer, Now);
        (q.SolutionSource, q.SolutionStatus, q.SolutionApprovedByName, q.SolutionPromptVersion)
            .ShouldBe((SolutionSource.AI, SolutionStatus.Approved, "Eğitmen", (int?)1));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("robot")]
    public void TryParseSource_RejectsUnknownValues(string value) =>
        QuestionSolutionApproval.TryParseSource(value, out _).ShouldBeFalse();

    // ================================================================== soru kaydı

    private async Task<int> SeedQuestionAsync(string? solution = null, SolutionSource source = SolutionSource.None,
        SolutionStatus status = SolutionStatus.Approved)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(5);
        var q = new Question
        {
            Text = "D/Y", Point = 5, InteractionType = MatrixType, InteractionPlan = Plan, InteractionSolution = solution,
            SolutionSource = source, SolutionStatus = status,
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private static QuestionDto UpdateDto(int id, string? solution, string? source = null, bool? approved = null, int? promptVersion = null) => new()
    {
        Id = id, Text = "D/Y", Point = 5, InteractionType = MatrixType, InteractionPlan = Plan, InteractionSolution = solution,
        Answers = new List<AnswerDto>(), SolutionSource = source, SolutionApproved = approved, SolutionPromptVersion = promptVersion,
    };

    private async Task<Question> ReadAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Questions.AsNoTracking().SingleAsync(q => q.Id == id);
    }

    private async Task<QuestionSavedDto> UpdateAsync(QuestionDto dto, SolutionActor? actor)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateOrUpdateQuestion(dto, actingUserId: 5, solutionActor: actor);
    }

    [Fact]
    public async Task CreateOrUpdate_AiSolution_IsPending_EvenForAnApprover_WithoutApprovalFlag()
    {
        var id = await SeedQuestionAsync();

        (await UpdateAsync(UpdateDto(id, Solution, "AI", promptVersion: 2), Trainer)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        (q.SolutionSource, q.SolutionStatus, q.SolutionPromptVersion, q.SolutionApprovedAt)
            .ShouldBe((SolutionSource.AI, SolutionStatus.Pending, (int?)2, (DateTime?)null));
    }

    [Fact]
    public async Task CreateOrUpdate_AiSolutionApprovedByTrainer_IsApproved_TeacherCannotSelfApprove()
    {
        var id = await SeedQuestionAsync();
        (await UpdateAsync(UpdateDto(id, Solution, "AI", approved: true), Teacher)).Success.ShouldBeTrue();
        (await ReadAsync(id)).SolutionStatus.ShouldBe(SolutionStatus.Pending);

        // Aynı çözüm: onaylayan yeniden gönderip onaylayabilir.
        (await UpdateAsync(UpdateDto(id, Solution, "AI", approved: true), Trainer)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        (q.SolutionSource, q.SolutionStatus, q.SolutionApprovedByKeycloakId, q.SolutionApprovedByName)
            .ShouldBe((SolutionSource.AI, SolutionStatus.Approved, "kc-qt", "Eğitmen"));
        q.SolutionApprovedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateOrUpdate_NonApproverEditOfAPendingKeySolution_StaysPendingAndKeepsSource()
    {
        var id = await SeedQuestionAsync(Solution, SolutionSource.Key, SolutionStatus.Pending);

        (await UpdateAsync(UpdateDto(id, OtherSolution), Teacher)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        (q.SolutionSource, q.SolutionStatus, q.SolutionApprovedByKeycloakId).ShouldBe((SolutionSource.Key, SolutionStatus.Pending, (string?)null));
        q.InteractionSolution.ShouldBe(OtherSolution);
    }

    [Fact]
    public async Task CreateOrUpdate_ApproverEditOfAPendingSolution_BecomesHumanApproved()
    {
        var id = await SeedQuestionAsync(Solution, SolutionSource.AI, SolutionStatus.Pending);

        (await UpdateAsync(UpdateDto(id, OtherSolution), Trainer)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        (q.SolutionSource, q.SolutionStatus, q.SolutionApprovedByKeycloakId).ShouldBe((SolutionSource.Human, SolutionStatus.Approved, "kc-qt"));
    }

    // Review C1: kutu oynatmak (yalnız plan değişir, çözüm aynı) onaylanmamış AI çözümünü aklamaz.
    [Fact]
    public async Task CreateOrUpdate_PlanOnlyChange_KeepsPendingAiSolution()
    {
        var id = await SeedQuestionAsync(Solution, SolutionSource.AI, SolutionStatus.Pending);
        var movedPlan = Plan.Replace("\"height\":3}}]", "\"height\":5}}]");
        movedPlan.ShouldNotBe(Plan);

        var dto = UpdateDto(id, Solution);
        dto.InteractionPlan = movedPlan;
        (await UpdateAsync(dto, Teacher)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        q.InteractionPlan.ShouldBe(movedPlan);
        (q.SolutionSource, q.SolutionStatus).ShouldBe((SolutionSource.AI, SolutionStatus.Pending));
    }

    // Review C1: tipi MCQ yapıp geri almak (çözüm silinip yeniden yazılır) onayı aklamaz.
    [Fact]
    public async Task CreateOrUpdate_McqRoundTrip_ByNonApprover_KeepsPending()
    {
        var id = await SeedQuestionAsync(Solution, SolutionSource.AI, SolutionStatus.Pending);

        var toMcq = UpdateDto(id, null);
        toMcq.InteractionType = "mcq";
        toMcq.InteractionPlan = null;
        (await UpdateAsync(toMcq, Teacher)).Success.ShouldBeTrue();
        var mid = await ReadAsync(id);
        mid.InteractionSolution.ShouldBeNull();
        mid.SolutionStatus.ShouldBe(SolutionStatus.Pending);

        (await UpdateAsync(UpdateDto(id, Solution), Teacher)).Success.ShouldBeTrue();
        var back = await ReadAsync(id);
        (back.SolutionSource, back.SolutionStatus).ShouldBe((SolutionSource.AI, SolutionStatus.Pending));
    }

    [Fact]
    public async Task CreateOrUpdate_KeyOrderOnlyDifference_IsNotAChange()
    {
        var id = await SeedQuestionAsync(Solution, SolutionSource.AI, SolutionStatus.Pending);

        (await UpdateAsync(UpdateDto(id, """{ "r2": ["cY"], "r1": ["cD"] }"""), Teacher)).Success.ShouldBeTrue();

        (await ReadAsync(id)).SolutionStatus.ShouldBe(SolutionStatus.Pending);
    }

    // ---- taslaktan (draftSolutionId) ----

    private async Task<int> SeedDraftAsync(SolutionStatus status, SolutionSource source = SolutionSource.AI, string solution = Solution,
        string type = MatrixType)
    {
        await using var ctx = _db.NewContext();
        var row = new LabelingActivitySolution
        {
            CropSha256 = new string('a', 64), InteractionType = type, PlanHash = new string('b', 64), PlanJson = Plan,
            Mode = "ai", ModelName = "m", PromptHash = "p", PromptVersion = 7, SolutionJson = solution, Source = source,
            Status = status, CreatedAt = Now, UpdatedAt = Now,
            ApprovedByKeycloakId = status == SolutionStatus.Approved ? "kc-draft" : null,
            ApprovedByName = status == SolutionStatus.Approved ? "Taslak Onaylayan" : null,
            ApprovedAt = status == SolutionStatus.Approved ? Now.AddMinutes(-5) : null,
        };
        ctx.LabelingActivitySolutions.Add(row);
        await ctx.SaveChangesAsync();
        return row.Id;
    }

    [Fact]
    public async Task Draft_Approved_CarriesSourceVersionAndApprover_IgnoringClientSource()
    {
        var id = await SeedQuestionAsync();
        var draft = await SeedDraftAsync(SolutionStatus.Approved);

        var dto = UpdateDto(id, Solution, source: "Human");
        dto.DraftSolutionId = draft;
        (await UpdateAsync(dto, Teacher)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        (q.SolutionSource, q.SolutionStatus, q.SolutionPromptVersion, q.SolutionApprovedByName, q.SolutionApprovedAt)
            .ShouldBe((SolutionSource.AI, SolutionStatus.Approved, (int?)7, "Taslak Onaylayan", (DateTime?)Now.AddMinutes(-5)));
    }

    [Fact]
    public async Task Draft_Pending_StaysPending_EvenIfClientClaimsHuman()
    {
        var id = await SeedQuestionAsync();
        var draft = await SeedDraftAsync(SolutionStatus.Pending, SolutionSource.Key);

        var dto = UpdateDto(id, Solution, source: "Human", approved: true);
        dto.DraftSolutionId = draft;
        (await UpdateAsync(dto, Teacher)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        (q.SolutionSource, q.SolutionStatus).ShouldBe((SolutionSource.Key, SolutionStatus.Pending));
    }

    [Fact]
    public async Task Draft_MismatchedSolutionOrType_Is400()
    {
        var id = await SeedQuestionAsync();
        var draft = await SeedDraftAsync(SolutionStatus.Approved);
        var slotDraft = await SeedDraftAsync(SolutionStatus.Approved, type: "slotFill");

        var dto = UpdateDto(id, OtherSolution);
        dto.DraftSolutionId = draft;
        var result = await UpdateAsync(dto, Trainer);
        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();

        var wrongType = UpdateDto(id, Solution);
        wrongType.DraftSolutionId = slotDraft;
        (await UpdateAsync(wrongType, Trainer)).InvalidInput.ShouldBeTrue();

        var missing = UpdateDto(id, Solution);
        missing.DraftSolutionId = 98765;
        (await UpdateAsync(missing, Trainer)).InvalidInput.ShouldBeTrue();
        (await ReadAsync(id)).InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task Draft_InBulkSave_DerivesState()
    {
        var approved = await SeedDraftAsync(SolutionStatus.Approved);
        var pending = await SeedDraftAsync(SolutionStatus.Pending);
        var a = Bulk("a", Solution, "Human");
        a.DraftSolutionId = approved;
        var b = Bulk("b", Solution, "Human");
        b.DraftSolutionId = pending;

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveBulkQuestion(BulkRequest(a, b), actingUserId: 5, Teacher)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var rows = await check.Questions.AsNoTracking().OrderBy(q => q.Id).Select(q => new { q.SolutionSource, q.SolutionStatus }).ToListAsync();
        rows.Select(r => (r.SolutionSource, r.SolutionStatus)).ShouldBe([
            (SolutionSource.AI, SolutionStatus.Approved), (SolutionSource.AI, SolutionStatus.Pending)]);

        var bad = Bulk("c", OtherSolution);
        bad.DraftSolutionId = approved;
        await using var ctx2 = _db.NewContext();
        (await NewService(ctx2).SaveBulkQuestion(BulkRequest(bad), actingUserId: 5, Teacher)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task ApproveService_PendingWithoutSolution_IsClearedByApprover()
    {
        var id = await SeedQuestionAsync(null, SolutionSource.AI, SolutionStatus.Pending);
        await using var ctx = _db.NewContext();

        var (outcome, state) = await new QuestionSolutionApprovalService(ctx, new FixedTimeProvider(new DateTimeOffset(Now))).ApproveAsync(id, Trainer);

        outcome.ShouldBe(QuestionSolutionApproveOutcome.Approved);
        (state!.SolutionSource, state.SolutionStatus).ShouldBe(("None", "approved"));
    }

    // ---- içe aktarım (review W4) ----

    [Theory]
    [InlineData("""{"solutionSource":"AI","solutionStatus":"pending","solutionPromptVersion":4}""", true, "{}", SolutionSource.AI, SolutionStatus.Pending, 4)]
    [InlineData("""{"solutionSource":"Key"}""", true, "{}", SolutionSource.Key, SolutionStatus.Pending, null)]
    [InlineData("""{"solutionSource":"AI","solutionStatus":"approved"}""", true, "{}", SolutionSource.AI, SolutionStatus.Approved, null)]
    [InlineData("""{}""", true, "{}", SolutionSource.Human, SolutionStatus.Approved, null)]
    [InlineData("""{"solutionSource":"Human"}""", true, "{}", SolutionSource.Human, SolutionStatus.Approved, null)]
    [InlineData("""{"solutionSource":"AI","solutionStatus":"pending"}""", false, "{}", SolutionSource.None, SolutionStatus.Approved, null)]
    [InlineData("""{"solutionSource":"AI","solutionStatus":"pending"}""", true, null, SolutionSource.None, SolutionStatus.Approved, null)]
    public void TransferImport_SolutionState(string manifest, bool interactive, string? solution, SolutionSource source,
        SolutionStatus status, int? promptVersion)
    {
        var q = new Question { InteractionSolution = solution };
        using var doc = System.Text.Json.JsonDocument.Parse(manifest);

        ExamApp.Api.Services.QuestionTransfer.QuestionTransferJobRunner.ApplyImportedSolutionState(q, doc.RootElement, interactive);

        (q.SolutionSource, q.SolutionStatus, q.SolutionPromptVersion).ShouldBe((source, status, promptVersion));
    }

    [Fact]
    public void MigrationBackfillSql_MatchesTheTestedConstant() =>
        ExamApp.Api.Migrations.AddAnswerKeyAndSolutionApproval.BackfillSql.ShouldBe(QuestionSolutionBackfill.Sql);

    [Fact]
    public async Task CreateOrUpdate_UnchangedSolution_KeepsPendingAndSource()
    {
        var id = await SeedQuestionAsync(Solution, SolutionSource.AI, SolutionStatus.Pending);

        (await UpdateAsync(UpdateDto(id, Solution), Teacher)).Success.ShouldBeTrue();

        var q = await ReadAsync(id);
        (q.SolutionSource, q.SolutionStatus).ShouldBe((SolutionSource.AI, SolutionStatus.Pending));
    }

    [Fact]
    public async Task CreateOrUpdate_InvalidSource_400_AndNothingWritten()
    {
        var id = await SeedQuestionAsync();

        var result = await UpdateAsync(UpdateDto(id, Solution, "robot"), Trainer);

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        (await ReadAsync(id)).InteractionSolution.ShouldBeNull();
    }

    // ================================================================== toplu kayıt

    private static BulkQuestionDto Bulk(string name, string? solution, string? source = null, bool? approved = null) => new()
    {
        Name = name, InteractionType = MatrixType, InteractionPlan = Plan, InteractionSolution = solution,
        Answers = new List<BulkAnswerDto>(), X = 0, Y = 0, Width = 10, Height = 10,
        SolutionSource = source, SolutionApproved = approved, SolutionPromptVersion = source is null ? null : 1,
    };

    private static BulkQuestionCreateDto BulkRequest(params BulkQuestionDto[] questions) => new()
    {
        ImageData = "data:image/png;base64," + Convert.ToBase64String(TestImages.Png(100, 100)),
        Passages = new List<BulkPassageDto>(),
        Questions = questions.ToList(),
        Header = new HeaderInfo(),
    };

    [Fact]
    public async Task SaveBulk_AppliesTheRulesPerQuestion()
    {
        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).SaveBulkQuestion(BulkRequest(
                Bulk("ai", Solution, "AI"),
                Bulk("key-approved", Solution, "Key", approved: true),
                Bulk("human", Solution),
                Bulk("draft", null)), actingUserId: 5, Trainer)).Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var rows = await check.Questions.AsNoTracking().OrderBy(q => q.Id)
            .Select(q => new { q.SolutionSource, q.SolutionStatus, q.SolutionApprovedByKeycloakId }).ToListAsync();
        rows.Select(r => (r.SolutionSource, r.SolutionStatus, r.SolutionApprovedByKeycloakId)).ShouldBe([
            (SolutionSource.AI, SolutionStatus.Pending, null),
            (SolutionSource.Key, SolutionStatus.Approved, "kc-qt"),
            (SolutionSource.Human, SolutionStatus.Approved, "kc-qt"),
            (SolutionSource.None, SolutionStatus.Approved, null),
        ]);
    }

    [Fact]
    public async Task SaveBulk_InvalidSource_FailsBeforeWriting()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).SaveBulkQuestion(BulkRequest(Bulk("x", Solution, "robot")), actingUserId: 5, Trainer))
            .Success.ShouldBeFalse();
        (await ctx.Questions.CountAsync()).ShouldBe(0);
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!);
    }

    // ================================================================== onay servisi

    [Fact]
    public async Task ApproveService_RecordsApprover_IsIdempotent_AndRejectsQuestionsWithoutSolution()
    {
        var pending = await SeedQuestionAsync(Solution, SolutionSource.AI, SolutionStatus.Pending);
        var none = await SeedQuestionAsync();
        var clock = new FixedTimeProvider(new DateTimeOffset(Now));

        await using (var ctx = _db.NewContext())
        {
            var service = new QuestionSolutionApprovalService(ctx, clock);
            var (outcome, state) = await service.ApproveAsync(pending, Trainer);
            outcome.ShouldBe(QuestionSolutionApproveOutcome.Approved);
            state!.SolutionStatus.ShouldBe("approved");
            state.SolutionSource.ShouldBe("AI");
            state.SolutionApprovedAt.ShouldBe(Now);

            clock.Now = clock.Now.AddHours(1);
            (await service.ApproveAsync(pending, new SolutionActor("kc-other", "Diğer", true))).State!.SolutionApprovedAt.ShouldBe(Now);
            (await service.ApproveAsync(none, Trainer)).Outcome.ShouldBe(QuestionSolutionApproveOutcome.NoSolution);
            (await service.ApproveAsync(98765, Trainer)).Outcome.ShouldBe(QuestionSolutionApproveOutcome.NotFound);
        }

        (await ReadAsync(pending)).SolutionApprovedByKeycloakId.ShouldBe("kc-qt");
    }

    // ================================================================== banka filtresi

    [Fact]
    public async Task BankSearch_FiltersBySolutionStatus_AndListsTheFields()
    {
        var pending = await SeedQuestionAsync(Solution, SolutionSource.AI, SolutionStatus.Pending);
        await SeedQuestionAsync(Solution, SolutionSource.Human);
        await SeedQuestionAsync();

        await using var ctx = _db.NewContext();
        var query = new QuestionQueryService(ctx);

        var page = await query.SearchBankAsync(new QuestionBankQuery { SolutionStatus = "PENDING" }, 5, isAdmin: false);
        page.Total.ShouldBe(1);
        page.Items.Single().Id.ShouldBe(pending);
        (page.Items.Single().SolutionSource, page.Items.Single().SolutionStatus).ShouldBe(("AI", "pending"));

        (await query.SearchBankAsync(new QuestionBankQuery { SolutionStatus = "approved" }, 5, isAdmin: false)).Total.ShouldBe(2);
        new QuestionBankQuery { SolutionStatus = "maybe" }.IsValid().ShouldBeFalse();
    }

    // ================================================================== migration veri adımı

    [Fact]
    public async Task Backfill_MarksExistingInteractiveSolutionsHuman_LeavesOthersNone()
    {
        int interactive, mcq, unsolved, blank;
        await using (var ctx = _db.NewContext())
        {
            var rows = new[]
            {
                new Question { Text = "i", InteractionType = "dragPlace", InteractionSolution = """{"t1":["k1"]}""" },
                new Question { Text = "m", InteractionType = "MCQ", InteractionSolution = "{}" },
                new Question { Text = "u", InteractionType = "slotFill" },
                new Question { Text = "b", InteractionType = "match", InteractionSolution = "  " },
            };
            ctx.Questions.AddRange(rows);
            await ctx.SaveChangesAsync();
            (interactive, mcq, unsolved, blank) = (rows[0].Id, rows[1].Id, rows[2].Id, rows[3].Id);

            // Migration öncesi hali: varsayılanlar.
            await ctx.Database.ExecuteSqlRawAsync("UPDATE \"Questions\" SET \"SolutionSource\" = 'None', \"SolutionStatus\" = 'Approved'");
            await ctx.Database.ExecuteSqlRawAsync(QuestionSolutionBackfill.Sql);
        }

        (await ReadAsync(interactive)).SolutionSource.ShouldBe(SolutionSource.Human);
        (await ReadAsync(mcq)).SolutionSource.ShouldBe(SolutionSource.None);
        (await ReadAsync(unsolved)).SolutionSource.ShouldBe(SolutionSource.None);
        (await ReadAsync(blank)).SolutionSource.ShouldBe(SolutionSource.None);
        (await ReadAsync(interactive)).SolutionStatus.ShouldBe(SolutionStatus.Approved);
    }
}
