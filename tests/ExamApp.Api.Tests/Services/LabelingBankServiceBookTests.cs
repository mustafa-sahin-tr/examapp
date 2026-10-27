using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #577: işaretleme ekranından kitap sayfası kaydı — içindekilerden sınıf/ders/konu varsayılanı
/// (<c>ClassificationSource = Book</c>), kitap adı ve <c>QuestionSource</c> (bastırılmış sayfa, etiketler), aynı kırpmanın
/// kayıttan önce 409 ile uyarılması (Force aşar) ve kırpma hash'inin deterministik + toplu kayıtta saklananla aynı olması.
/// </summary>
public class LabelingBankServiceBookTests : IDisposable
{
    private const int ActingUserId = 77;
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly LabelingActor Actor = new("kc-trainer-1", "Eğitmen");

    private readonly TestDb _db = TestDb.Create();
    private readonly IQuestionService _questions = Substitute.For<IQuestionService>();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly InMemoryFixedWindowCounterStore _counters;
    private BulkQuestionCreateDto? _captured;
    private readonly Seed _seed;

    private sealed record Seed(int Grade5, int Grade6, int Math, int Topic, int SubTopic, int TopicOnly, int BookId);

    public LabelingBankServiceBookTests()
    {
        _counters = new InMemoryFixedWindowCounterStore(_clock);
        using (var ctx = _db.NewContext())
        {
            var g5 = new Grade { Name = "5. Sınıf" };
            var g6 = new Grade { Name = "6. Sınıf" };
            var math = new Subject { Name = "Matematik" };
            ctx.AddRange(g5, g6, math);
            ctx.SaveChanges();
            var topic = new Topic { Name = "Kesirler", SubjectId = math.Id, GradeId = g5.Id };
            var topicOnly = new Topic { Name = "Doğal Sayılar", SubjectId = math.Id, GradeId = g5.Id };
            ctx.Topics.AddRange(topic, topicOnly);
            ctx.SaveChanges();
            var sub = new SubTopic { Name = "Basit kesirler", TopicId = topic.Id };
            ctx.SubTopics.Add(sub);
            ctx.SaveChanges();
            var book = new BookRegistryService(ctx, _clock).CreateAsync(new LabelingBookSaveRequestDto
            {
                Name = "TT MatLab 5",
                Offsets =
                [
                    new() { FileFrom = 3, FileTo = 50, Offset = -2 },
                    new() { FileFrom = 51, FileTo = 100, Offset = -50 },
                ],
                AnswerKeyRanges = [new() { FileFrom = 151, FileTo = 160 }],
                Toc =
                [
                    new() { FileFrom = 3, FileTo = 50, GradeId = g5.Id, SubjectId = math.Id, TopicId = topic.Id, SubTopicId = sub.Id },
                    new() { FileFrom = 51, FileTo = 80, GradeId = g5.Id, SubjectId = math.Id, TopicId = topicOnly.Id },
                    new() { FileFrom = 81, FileTo = 100, GradeId = g5.Id, SubjectId = math.Id },
                ],
            }).GetAwaiter().GetResult();
            _seed = new Seed(g5.Id, g6.Id, math.Id, topic.Id, sub.Id, topicOnly.Id, book.Id);
        }

        _questions.SaveBulkQuestion(Arg.Any<BulkQuestionCreateDto>(), Arg.Any<int>())
            .Returns(ci =>
            {
                _captured = ci.Arg<BulkQuestionCreateDto>();
                return Task.FromResult(new ResponseBaseDto { Success = true, Message = "ok" });
            });
    }

    public void Dispose() => _db.Dispose();

    private LabelingBankService NewService(AppDbContext ctx) =>
        new(ctx, _questions, _counters, _clock, NullLogger<LabelingBankService>.Instance,
            new BookRegistryService(ctx, _clock), new ImageHelper());

    /// <summary>Gradyanlı sayfa: farklı kutuların kırpmaları farklı baytlar üretir (düz renkte aynı boyut = aynı hash).</summary>
    private static byte[] GradientPng(int width = 320, int height = 200, int seed = 0)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)((x + seed) % 256), (byte)(y % 256), (byte)((x * y + seed) % 256)));
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static LabelingBankBoxDto Box(int x, int y, int w, int h) => new() { X = x, Y = y, Width = w, Height = h };

    private static LabelingBankSaveRequestDto Request(byte[] png, int? bookId, int? filePage, int? gradeId = null, bool force = false) => new()
    {
        ImageBase64 = Convert.ToBase64String(png),
        GradeId = gradeId,
        BookId = bookId,
        FilePage = filePage,
        Force = force,
        Questions =
        [
            new LabelingBankQuestionDto
            {
                X = 10, Y = 10, Width = 100, Height = 80,
                Options = [Box(12, 12, 20, 20), Box(40, 12, 20, 20)],
                CorrectOptionIndex = 1,
                ActivityLabel = "  Etkinlik 9 ",
                ItemLabel = "3",
            },
            new LabelingBankQuestionDto
            {
                X = 150, Y = 100, Width = 120, Height = 80,
                Options = [Box(152, 102, 20, 20), Box(180, 102, 20, 20)],
                CorrectOptionIndex = 0,
            },
        ],
    };

    /// <summary>Servisin toplu kayda vereceği JPEG'den (aynı yol) kutu başına kırpma hash'i.</summary>
    private static IReadOnlyList<string> ExpectedCropHashes(byte[] png, LabelingBankSaveRequestDto request)
    {
        byte[] jpeg;
        using (var image = LabelingImage.Decode(Convert.ToBase64String(png)))
            jpeg = image.Encode(LabelingImage.MaxEdge).Jpeg;
        return QuestionCropHash.Compute(new ImageHelper(), jpeg, request.Questions.Select(q => (q.X, q.Y, q.Width, q.Height)).ToList());
    }

    private async Task SeedExistingSourceAsync(string cropSha, int filePage = 12, string? itemLabel = "7", int ownerId = ActingUserId)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(ownerId);
        var q = new Question { Text = "eski" };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        ctx.QuestionSources.Add(new QuestionSource
        {
            QuestionId = q.Id, BookId = _seed.BookId, FilePage = filePage, PrintedPage = 10, ItemLabel = itemLabel, CropSha256 = cropSha,
        });
        await ctx.SaveChangesAsync();
    }

    // ---------- İçindekiler varsayılanı ----------

    [Fact]
    public async Task BookPage_WithSubTopicToc_DefaultsGradeSubjectTopicSubTopic_AsBookSource()
    {
        await using var ctx = _db.NewContext();
        await NewService(ctx).SaveToBankAsync(Request(GradientPng(), _seed.BookId, 12), Actor, ActingUserId);

        var bulk = _captured.ShouldNotBeNull();
        bulk.GradeId.ShouldBe(_seed.Grade5);
        bulk.Header.SubjectId.ShouldBe(_seed.Math);
        bulk.Header.TopicId.ShouldBe(_seed.Topic);
        bulk.Header.Subtopics.ShouldBe([_seed.SubTopic]);
        bulk.Header.ClassificationSource.ShouldBe("Book");
        bulk.Questions.ShouldAllBe(q => q.BookName == "TT MatLab 5");
        var source = bulk.Questions[0].Source.ShouldNotBeNull();
        (source.BookId, source.FilePage, source.PrintedPage).ShouldBe((_seed.BookId, 12, 10));
        (source.ActivityLabel, source.ItemLabel).ShouldBe(("Etkinlik 9", "3"));
        bulk.Questions[1].Source.ShouldNotBeNull().ActivityLabel.ShouldBeNull();
    }

    [Fact]
    public async Task BookPage_FascicleTwo_UsesItsOffsetAndTopicOnlyToc()
    {
        await using var ctx = _db.NewContext();
        await NewService(ctx).SaveToBankAsync(Request(GradientPng(), _seed.BookId, 60), Actor, ActingUserId);

        var bulk = _captured.ShouldNotBeNull();
        bulk.Header.TopicId.ShouldBe(_seed.TopicOnly);
        bulk.Header.Subtopics.ShouldBeEmpty();
        bulk.Header.ClassificationSource.ShouldBe("Book");
        bulk.Questions[0].Source!.PrintedPage.ShouldBe(10);
    }

    [Fact]
    public async Task BookPage_SubjectOnlyToc_KeepsHumanSource()
    {
        await using var ctx = _db.NewContext();
        await NewService(ctx).SaveToBankAsync(Request(GradientPng(), _seed.BookId, 90), Actor, ActingUserId);

        var bulk = _captured.ShouldNotBeNull();
        bulk.Header.SubjectId.ShouldBe(_seed.Math);
        bulk.Header.TopicId.ShouldBeNull();
        bulk.Header.ClassificationSource.ShouldBeNull();
    }

    [Fact]
    public async Task BookPage_RequestGradeWins_AndOtherGradeDropsTocTaxonomy()
    {
        await using var ctx = _db.NewContext();
        await NewService(ctx).SaveToBankAsync(Request(GradientPng(), _seed.BookId, 12, gradeId: _seed.Grade6), Actor, ActingUserId);

        var bulk = _captured.ShouldNotBeNull();
        bulk.GradeId.ShouldBe(_seed.Grade6);
        bulk.Header.TopicId.ShouldBeNull();
        bulk.Header.ClassificationSource.ShouldBeNull();
        bulk.Questions[0].BookName.ShouldBe("TT MatLab 5"); // kaynak yine yazılır
        bulk.Questions[0].Source.ShouldNotBeNull();
    }

    [Fact]
    public async Task BookPage_OutsideTocAndNoGrade_ThrowsGradeRequired()
    {
        await using var ctx = _db.NewContext();
        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SaveToBankAsync(Request(GradientPng(), _seed.BookId, 2), Actor, ActingUserId));
        ex.Message.ShouldBe("labeling.bank.gradeRequired");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BookWithoutPage_OrPageWithoutBook_Throws(bool withBook, bool withPage)
    {
        await using var ctx = _db.NewContext();
        var ex = await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).SaveToBankAsync(
            Request(GradientPng(), withBook ? _seed.BookId : null, withPage ? 12 : null, gradeId: _seed.Grade5), Actor, ActingUserId));
        ex.Message.ShouldBe("labeling.bank.bookPageRequired");
    }

    [Fact]
    public async Task UnknownBook_Throws()
    {
        await using var ctx = _db.NewContext();
        var ex = await Should.ThrowAsync<LabelingValidationException>(() =>
            NewService(ctx).SaveToBankAsync(Request(GradientPng(), 9999, 12), Actor, ActingUserId));
        ex.Message.ShouldBe("labeling.books.notFound");
    }

    [Fact]
    public async Task WithoutBook_NoSourceIsAttached()
    {
        await using var ctx = _db.NewContext();
        await NewService(ctx).SaveToBankAsync(Request(GradientPng(), null, null, gradeId: _seed.Grade5), Actor, ActingUserId);

        _captured.ShouldNotBeNull().Questions.ShouldAllBe(q => q.Source == null && q.BookName == null);
        _captured.Header.ClassificationSource.ShouldBeNull();
    }

    // ---------- Aynı kırpma ----------

    [Fact]
    public async Task SameCropAlreadyInBank_Throws409BeforeClaimAndSave()
    {
        var png = GradientPng();
        var request = Request(png, _seed.BookId, 12);
        var hashes = ExpectedCropHashes(png, request);
        await SeedExistingSourceAsync(hashes[1]);

        await using (var ctx = _db.NewContext())
        {
            var ex = await Should.ThrowAsync<LabelingCropDuplicateException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));
            var dup = ex.Existing.ShouldHaveSingleItem();
            dup.QuestionIndex.ShouldBe(1);
            (dup.BookName, dup.FilePage, dup.PrintedPage, dup.ItemLabel).ShouldBe(("TT MatLab 5", 12, 10, "7"));
        }

        await _questions.DidNotReceiveWithAnyArgs().SaveBulkQuestion(default!, default);
        await using var read = _db.NewContext();
        (await read.LabelingPages.SingleAsync()).BankSaveStartedAt.ShouldBeNull(); // talep alınmadı
    }

    [Fact]
    public async Task SameCropAlreadyInBank_ForceSavesAnyway()
    {
        var png = GradientPng();
        var request = Request(png, _seed.BookId, 12, force: true);
        await SeedExistingSourceAsync(ExpectedCropHashes(png, request)[0]);

        await using var ctx = _db.NewContext();
        await NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId);

        _captured.ShouldNotBeNull();
    }

    [Fact]
    public async Task CropOfASoftDeletedQuestion_IsNotADuplicate()
    {
        var png = GradientPng();
        var request = Request(png, _seed.BookId, 12);
        await SeedExistingSourceAsync(ExpectedCropHashes(png, request)[0]);
        await using (var del = _db.NewContext())
        {
            var q = await del.Questions.AsTracking().SingleAsync();
            del.Questions.Remove(q); // soft-delete
            await del.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        await NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId);
        _captured.ShouldNotBeNull();
    }

    [Fact]
    public async Task DifferentCrop_IsNotADuplicate()
    {
        var png = GradientPng();
        await SeedExistingSourceAsync(ExpectedCropHashes(GradientPng(seed: 7), Request(png, _seed.BookId, 12))[0]);

        await using var ctx = _db.NewContext();
        await NewService(ctx).SaveToBankAsync(Request(png, _seed.BookId, 12), Actor, ActingUserId);
        _captured.ShouldNotBeNull();
    }

    // ---------- Hash: deterministik + toplu kayıtta saklananla aynı ----------

    [Fact]
    public void CropHash_IsDeterministic()
    {
        var png = GradientPng();
        var request = Request(png, _seed.BookId, 12);

        var first = ExpectedCropHashes(png, request);
        var second = ExpectedCropHashes(GradientPng(), request);

        second.ShouldBe(first);
        first.Distinct().Count().ShouldBe(2);
        first.ShouldAllBe(h => LabelingImage.IsValidSha256(h));
    }

    [Fact]
    public async Task BulkSave_StoresQuestionSourceWithTheHashOfTheUploadedCrop_AndPublishesSubTopics()
    {
        var png = GradientPng();
        var request = Request(png, _seed.BookId, 12);
        var expected = ExpectedCropHashes(png, request);

        // 1) işaretleme servisi toplu kayıt DTO'sunu üretir (yakalanır)
        await using (var ctx = _db.NewContext())
            await NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId);
        var bulk = _captured.ShouldNotBeNull();

        // 2) gerçek toplu kayıt: yüklenen question.jpg baytları yakalanır
        var minio = Substitute.For<IMinIoService>();
        var uploads = new Dictionary<string, byte[]>();
        minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                using var ms = new MemoryStream();
                ci.Arg<Stream>().CopyTo(ms);
                uploads[ci.ArgAt<string>(1)] = ms.ToArray();
                return Task.FromResult("/img/b/" + ci.ArgAt<string>(1));
            });
        ResponseBaseDto result;
        await using (var ctx = _db.NewContext())
            result = await new QuestionService(ctx, new ImageHelper(), minio).SaveBulkQuestion(bulk, ActingUserId);
        result.Success.ShouldBeTrue(result.Message);

        await using var read = _db.NewContext();
        var sources = await read.QuestionSources.AsNoTracking().OrderBy(s => s.QuestionId).ToListAsync();
        sources.Select(s => s.CropSha256).ShouldBe(expected);
        sources.ShouldAllBe(s => s.BookId == _seed.BookId && s.FilePage == 12 && s.PrintedPage == 10);
        (sources[0].ActivityLabel, sources[0].ItemLabel).ShouldBe(("Etkinlik 9", "3"));
        var questionJpgs = uploads.Where(u => u.Key.EndsWith("/question.jpg")).Select(u => QuestionCropHash.Of(u.Value)).ToList();
        questionJpgs.OrderBy(h => h).ShouldBe(expected.OrderBy(h => h));

        var questions = await read.Questions.AsNoTracking().OrderBy(q => q.Id).ToListAsync();
        questions.ShouldAllBe(q => q.ClassificationSource == ClassificationSource.Book && q.BookName == "TT MatLab 5"
                                   && q.TopicId == _seed.Topic && q.SubjectId == _seed.Math);
        var events = (await read.OutboxMessages.AsNoTracking().ToListAsync())
            .Select(m => System.Text.Json.JsonSerializer.Deserialize<QuestionCreatedEvent>(m.Content)!).ToList();
        events.Count.ShouldBe(2);
        events.ShouldAllBe(e => e.ClassificationSource == "Book" && e.GradeId == _seed.Grade5 && e.TopicId == _seed.Topic);
        events.ShouldAllBe(e => e.SubTopicIds!.SequenceEqual(new[] { _seed.SubTopic }));

        // 3) aynı pikseller başka bir dosya olarak (IEND sonrası ek bayt → farklı sayfa SHA'sı, sayfa tekrarı yok) →
        //    aynı kırpmalar: 409 LabelingCropAlreadyInBank, mevcut kayıtlar 1) ve 2)'de oluşan sorular.
        var reencoded = png.Concat(new byte[] { 0, 1, 2, 3 }).ToArray();
        await using (var ctx = _db.NewContext())
        {
            var ex = await Should.ThrowAsync<LabelingCropDuplicateException>(() =>
                NewService(ctx).SaveToBankAsync(Request(reencoded, _seed.BookId, 12), Actor, ActingUserId));
            ex.Existing.Select(e => (e.QuestionIndex, e.QuestionId)).ShouldBe(
                [(0, questions[0].Id), (1, questions[1].Id)]);
            ex.Existing.ShouldAllBe(e => e.BookName == "TT MatLab 5" && e.PrintedPage == 10);
        }
    }

    [Fact]
    public void Source_IsNotBoundFromClientJson()
    {
        var json = """{"name":"S1","x":0,"y":0,"width":10,"height":10,"source":{"bookId":1,"filePage":2}}""";
        var dto = System.Text.Json.JsonSerializer.Deserialize<BulkQuestionDto>(json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        dto.Source.ShouldBeNull();
    }

    // ---------- review follow-ups ----------

    private LabelingBankService NewService(AppDbContext ctx, bool admin, out LabelingActor actor)
    {
        actor = Actor with { IsAdmin = admin };
        return NewService(ctx);
    }

    [Theory]
    [InlineData(false, true)]  // başka öğretmenin sorusu → yalnız sıra + otherOwner
    [InlineData(true, false)]  // Admin hepsini görür
    public async Task CropDuplicateOfAnotherOwner_HidesDetailsUnlessAdmin(bool admin, bool expectHidden)
    {
        var png = GradientPng();
        var request = Request(png, _seed.BookId, 12);
        await SeedExistingSourceAsync(ExpectedCropHashes(png, request)[0], ownerId: 999);

        await using var ctx = _db.NewContext();
        var service = NewService(ctx, admin, out var actor);
        var ex = await Should.ThrowAsync<LabelingCropDuplicateException>(() => service.SaveToBankAsync(request, actor, ActingUserId));

        var dup = ex.Existing.ShouldHaveSingleItem();
        dup.QuestionIndex.ShouldBe(0);
        dup.OtherOwner.ShouldBe(expectHidden);
        if (expectHidden)
            dup.ToResponse().ShouldBeOfType<LabelingCropDuplicateHiddenDto>().QuestionIndex.ShouldBe(0);
        else
            dup.ToResponse().ShouldBeSameAs(dup);
    }

    [Fact]
    public async Task CropDuplicate409s_CountTowardsTheHourlyLimit()
    {
        var png = GradientPng();
        var request = Request(png, _seed.BookId, 12);
        await SeedExistingSourceAsync(ExpectedCropHashes(png, request)[0]);

        for (var i = 0; i < LabelingBankService.SavesPerHour; i++)
        {
            await using var ctx = _db.NewContext();
            await Should.ThrowAsync<LabelingCropDuplicateException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));
        }

        await using (var ctx = _db.NewContext())
            await Should.ThrowAsync<LabelingRateLimitException>(() => NewService(ctx).SaveToBankAsync(request, Actor, ActingUserId));
    }

    [Fact]
    public async Task AnswerKeyPage_Throws409UnlessForce()
    {
        await using (var ctx = _db.NewContext())
            await Should.ThrowAsync<LabelingAnswerKeyPageException>(() =>
                NewService(ctx).SaveToBankAsync(Request(GradientPng(), _seed.BookId, 155, gradeId: _seed.Grade5), Actor, ActingUserId));
        await _questions.DidNotReceiveWithAnyArgs().SaveBulkQuestion(default!, default);

        await using (var ctx = _db.NewContext())
            await NewService(ctx).SaveToBankAsync(Request(GradientPng(), _seed.BookId, 155, gradeId: _seed.Grade5, force: true), Actor, ActingUserId);
        _captured.ShouldNotBeNull().Questions[0].Source!.FilePage.ShouldBe(155);
    }

    [Fact]
    public async Task Response_TocApplied_TrueOnlyWhenTocTopicWasApplied()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.SaveToBankAsync(Request(GradientPng(seed: 1), _seed.BookId, 12), Actor, ActingUserId)).TocApplied.ShouldBeTrue();
        (await service.SaveToBankAsync(Request(GradientPng(seed: 2), _seed.BookId, 90), Actor, ActingUserId)).TocApplied.ShouldBeFalse(); // yalnız ders
        (await service.SaveToBankAsync(Request(GradientPng(seed: 3), _seed.BookId, 12, gradeId: _seed.Grade6), Actor, ActingUserId)).TocApplied.ShouldBeFalse();
        (await service.SaveToBankAsync(Request(GradientPng(seed: 4), null, null, gradeId: _seed.Grade5), Actor, ActingUserId)).TocApplied.ShouldBeFalse();
    }

    [Fact]
    public async Task TocPointingToADeletedTopic_IsNotApplied_AndDoesNotFail()
    {
        await using (var del = _db.NewContext())
        {
            del.Remove(await del.Topics.AsTracking().SingleAsync(t => t.Id == _seed.Topic)); // soft-delete
            await del.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var response = await NewService(ctx).SaveToBankAsync(Request(GradientPng(), _seed.BookId, 12, gradeId: _seed.Grade5), Actor, ActingUserId);

        response.TocApplied.ShouldBeFalse();
        var bulk = _captured.ShouldNotBeNull();
        bulk.Header.TopicId.ShouldBeNull();
        bulk.Header.ClassificationSource.ShouldBeNull();
        bulk.Questions[0].Source.ShouldNotBeNull().PrintedPage.ShouldBe(10);
    }
}
