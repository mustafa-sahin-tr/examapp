using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #583 (review 4, D3): açık uçlu madde teste giremez \u2014 havuzdan ekle, tekli / toplu soru kaydı, test kopyalama ve Activity →
/// Test tür değişimi <c>OpenTextNotAllowedInTest</c> ile reddedilir, hiçbir şey yazılmaz; etkinliğe eklemek serbesttir. Ayrıca
/// skor kuralı (açık uçlu satır toplamdan düşer) ve genişletilmiş düz metin süzgeci (bidi / C1).
/// </summary>
public class OpenTextTestGuardTests : IDisposable
{
    private const int Owner = 58350;
    private const string Plan = """{"version":1,"type":"openText","maxWords":50}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int GradeId, int BookId, int TestBookTestId, int ActivityBookTestId, int TestId, int ActivityId,
        int OpenQuestionId, int McqQuestionId);

    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var book = new Book { Name = "Kitap", BookTests = { new BookTest { Name = "T1" }, new BookTest { Name = "T2" } } };
        ctx.AddRange(grade, book);
        await ctx.SaveChangesAsync();
        ctx.SetCurrentUser(Owner);
        var test = new Worksheet { Name = "Test", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, Kind = WorksheetKind.Test,
            BookTestId = book.BookTests.First().Id };
        var activity = new Worksheet { Name = "Etkinlik", Description = "", GradeId = grade.Id, Kind = WorksheetKind.Activity,
            BookTestId = book.BookTests.Last().Id };
        var open = new Question { Text = "Açıkla", Point = 1, InteractionType = OpenTextGrader.Type, InteractionPlan = Plan };
        var mcq = new Question { Text = "Seç", Point = 1 };
        ctx.AddRange(test, activity, open, mcq);
        await ctx.SaveChangesAsync();
        return new Seed(grade.Id, book.Id, book.BookTests.First().Id, book.BookTests.Last().Id, test.Id, activity.Id, open.Id, mcq.Id);
    }

    private async Task<int> LinkCountAsync(int worksheetId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestQuestions.CountAsync(wq => wq.TestId == worksheetId);
    }

    private async Task LinkDirectAsync(int worksheetId, int questionId)
    {
        await using var ctx = _db.NewContext();
        ctx.TestQuestions.Add(new WorksheetQuestion { TestId = worksheetId, QuestionId = questionId, Order = 1 });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task BankLink_OpenTextIntoTest_IsRejected_IntoActivity_IsAllowed()
    {
        var s = await SeedAsync();

        await using (var ctx = _db.NewContext())
        {
            var rejected = await new QuestionTestLinkService(ctx).LinkAsync(s.TestId, [s.McqQuestionId, s.OpenQuestionId], Owner, isAdmin: false);
            rejected!.ErrorCode.ShouldBe(OpenTextTestGuard.ErrorCode);
        }
        (await LinkCountAsync(s.TestId)).ShouldBe(0);

        await using (var ctx = _db.NewContext())
            (await new QuestionTestLinkService(ctx).LinkAsync(s.ActivityId, [s.OpenQuestionId], Owner, isAdmin: false))!
                .AddedQuestionIds.ShouldBe([s.OpenQuestionId]);
    }

    [Fact]
    public async Task SingleSave_OpenTextIntoTest_OrTestQuestionTurnedOpenText_IsRejected()
    {
        var s = await SeedAsync();
        await LinkDirectAsync(s.TestId, s.McqQuestionId);

        await using (var ctx = _db.NewContext())
        {
            var create = await new QuestionService(ctx, new ImageHelper(), _minio).CreateOrUpdateQuestion(new QuestionDto
            {
                Text = "Yeni", Point = 1, TestId = s.TestId, InteractionType = "OpenText", InteractionPlan = Plan, Answers = new(),
            }, Owner);
            (create.InvalidInput, create.ErrorCode).ShouldBe((true, OpenTextTestGuard.ErrorCode));

            var update = await new QuestionService(ctx, new ImageHelper(), _minio).CreateOrUpdateQuestion(new QuestionDto
            {
                Id = s.McqQuestionId, Text = "Seç", Point = 1, InteractionType = OpenTextGrader.Type, InteractionPlan = Plan, Answers = new(),
            }, Owner);
            (update.InvalidInput, update.ErrorCode).ShouldBe((true, OpenTextTestGuard.ErrorCode));
        }

        await using var check = _db.NewContext();
        (await check.Questions.AsNoTracking().SingleAsync(q => q.Id == s.McqQuestionId)).InteractionType.ShouldBeNull();
        (await check.Questions.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task BulkSave_OpenTextIntoTest_IsRejectedBeforeUpload()
    {
        var s = await SeedAsync();

        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await new QuestionService(ctx, new ImageHelper(), _minio).SaveBulkQuestion(new BulkQuestionCreateDto
            {
                ImageData = "data:image/png;base64," + Convert.ToBase64String(TestImages.Png(100, 100)),
                Passages = new List<BulkPassageDto>(),
                Header = new HeaderInfo { TestId = s.TestId },
                Questions =
                [
                    new BulkQuestionDto
                    {
                        Name = "açık", InteractionType = OpenTextGrader.Type, InteractionPlan = Plan, Answers = new(),
                        X = 0, Y = 0, Width = 10, Height = 10,
                    },
                ],
            }, Owner);

        result.ShouldBeOfType<QuestionSavedDto>().ErrorCode.ShouldBe(OpenTextTestGuard.ErrorCode);
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default!, default!);
        (await LinkCountAsync(s.TestId)).ShouldBe(0);
    }

    [Fact]
    public async Task Copy_TestWithLegacyOpenText_IsRejected_ActivityCopyIsAllowed()
    {
        var s = await SeedAsync();
        await LinkDirectAsync(s.TestId, s.OpenQuestionId);
        await LinkDirectAsync(s.ActivityId, s.OpenQuestionId);

        await using var ctx = _db.NewContext();
        var authoring = new WorksheetAuthoringService(ctx, new ImageHelper(), _minio);
        var rejected = await authoring.CopyWorksheetAsync(s.TestId, Owner, isAdmin: false);
        (rejected.Success, rejected.ErrorCode).ShouldBe((false, OpenTextTestGuard.ErrorCode));
        (await authoring.CopyWorksheetAsync(s.ActivityId, Owner, isAdmin: false)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task KindChange_ActivityWithOpenTextToTest_IsConflict()
    {
        var s = await SeedAsync();
        await LinkDirectAsync(s.ActivityId, s.OpenQuestionId);

        await using (var ctx = _db.NewContext())
        {
            var result = await new WorksheetAuthoringService(ctx, new ImageHelper(), _minio).CreateOrUpdateAsync(new ExamDto
            {
                Id = s.ActivityId, Name = "Etkinlik", Description = "d", GradeId = s.GradeId, BookId = s.BookId,
                BookTestId = s.ActivityBookTestId, Kind = WorksheetKind.Test, MaxDurationSeconds = 600,
            }, Owner, isAdmin: false);
            (result.Conflict, result.ErrorCode).ShouldBe((true, WorksheetAuthoringErrorCodes.OpenTextNotAllowedInTest));
        }

        await using var check = _db.NewContext();
        (await check.Worksheets.AsNoTracking().SingleAsync(w => w.Id == s.ActivityId)).Kind.ShouldBe(WorksheetKind.Activity);
    }

    [Fact]
    public void Tally_SkipsExcludedRows_AndTotalCountsOnlyScoredRows()
    {
        var facts = new[]
        {
            new WorksheetScoring.AnswerFacts(1, false, false, 1),                       // doğru
            new WorksheetScoring.AnswerFacts(null, true, false, null, Excluded: true),  // açık uçlu (cevaplı)
            new WorksheetScoring.AnswerFacts(null, false, false, null, Excluded: true), // açık uçlu (boş)
            new WorksheetScoring.AnswerFacts(null, false, false, 2),                    // boş
        };

        WorksheetScoring.Tally(facts, f => f).ShouldBe((1, 0, 1));
        WorksheetScoring.Total(facts, f => f).ShouldBe(2);
    }

    [Theory]
    [InlineData("\u202A")]
    [InlineData("\u202E")]
    [InlineData("\u2066")]
    [InlineData("\u2069")]
    [InlineData("\u0080")]
    [InlineData("\u009F")]
    [InlineData("\u007F")]
    [InlineData("\u0001")]
    public void PlainText_RejectsBidiControlsAndC1(string ch)
    {
        PlainTextAttribute.ContainsControlCharacters("a" + ch + "b").ShouldBeTrue();
        new PlainTextAttribute().IsValid("x" + ch).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Satır 1\nSatır 2\r\n\tGirinti")]
    [InlineData("Çağrı ÖĞÜŞİı \u2014 \u201Ctırnak\u201D \U0001F600 \u200B \u00A0")]
    [InlineData("\u2065-öncesi \u202F \u206A-sonrası \u2029 \u00A1 \u2029")]
    public void PlainText_AllowsOrdinaryText(string text)
        => PlainTextAttribute.ContainsControlCharacters(text).ShouldBeFalse();
}
