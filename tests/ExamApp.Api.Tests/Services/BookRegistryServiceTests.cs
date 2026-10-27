using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #577: kitap kaydı servisi — oluşturma / güncelleme (kümeler bütün olarak değişir), içindekiler taksonomi
/// doğrulaması, ad tekilliği, asıl kayıt kuralları, sayfa sorguları ve kullanımdaki kitabın silinememesi.
/// </summary>
public class BookRegistryServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly Tax _tax;

    private sealed record Tax(int Grade5, int Grade6, int Math, int Turkish, int Topic5, int Topic6, int SubTopic5, int OtherTopic5);

    public BookRegistryServiceTests()
    {
        using var ctx = _db.NewContext();
        var g5 = new Grade { Name = "5. Sınıf" };
        var g6 = new Grade { Name = "6. Sınıf" };
        var math = new Subject { Name = "Matematik" };
        var tr = new Subject { Name = "Türkçe" };
        ctx.AddRange(g5, g6, math, tr);
        ctx.SaveChanges();
        var t5 = new Topic { Name = "Kesirler", SubjectId = math.Id, GradeId = g5.Id };
        var t6 = new Topic { Name = "Kesirler", SubjectId = math.Id, GradeId = g6.Id };
        var other5 = new Topic { Name = "Doğal Sayılar", SubjectId = math.Id, GradeId = g5.Id };
        ctx.Topics.AddRange(t5, t6, other5);
        ctx.SaveChanges();
        var st5 = new SubTopic { Name = "Basit kesirler", TopicId = t5.Id };
        ctx.SubTopics.Add(st5);
        ctx.SaveChanges();
        _tax = new Tax(g5.Id, g6.Id, math.Id, tr.Id, t5.Id, t6.Id, st5.Id, other5.Id);
    }

    public void Dispose() => _db.Dispose();

    private BookRegistryService NewService(AppDbContext ctx) => new(ctx, _clock);

    private const int TrainerUserId = 11;
    private static readonly BookEditor Trainer = new(TrainerUserId, IsAdmin: false);
    private static readonly BookEditor Admin = new(1, IsAdmin: true);

    private LabelingBookSaveRequestDto MatLab(string name = "TT MatLab 5") => new()
    {
        Name = name,
        Notes = "Pilot",
        Offsets =
        [
            new() { FileFrom = 51, FileTo = 100, Offset = -50 },
            new() { FileFrom = 3, FileTo = 50, Offset = -2 },
        ],
        AnswerKeyRanges = [new() { FileFrom = 101, FileTo = 110 }],
        Toc =
        [
            new() { FileFrom = 3, FileTo = 20, GradeId = _tax.Grade5, SubjectId = _tax.Math, TopicId = _tax.Topic5, SubTopicId = _tax.SubTopic5 },
            new() { FileFrom = 21, FileTo = 50, GradeId = _tax.Grade5, SubjectId = _tax.Math, TopicId = _tax.OtherTopic5 },
            new() { FileFrom = 51, FileTo = 100, GradeId = _tax.Grade5, SubjectId = _tax.Math },
        ],
    };

    private async Task<LabelingBookDto> CreateAsync(LabelingBookSaveRequestDto request)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateAsync(request);
    }

    private async Task<Exception> CreateFailsAsync(LabelingBookSaveRequestDto request)
    {
        await using var ctx = _db.NewContext();
        return await Should.ThrowAsync<Exception>(() => NewService(ctx).CreateAsync(request));
    }

    [Fact]
    public async Task Create_ReturnsTheBookWithSortedSetsAndTaxonomyNames()
    {
        var book = await CreateAsync(MatLab());

        book.Name.ShouldBe("TT MatLab 5");
        book.Notes.ShouldBe("Pilot");
        book.Offsets.Select(o => (o.FileFrom, o.Offset)).ShouldBe([(3, -2), (51, -50)]);
        book.AnswerKeyRanges.ShouldHaveSingleItem().FileFrom.ShouldBe(101);
        book.Toc.Count.ShouldBe(3);
        var first = book.Toc[0];
        (first.PrintedFrom, first.PrintedTo).ShouldBe((1, 18));
        (first.GradeName, first.SubjectName, first.TopicName, first.SubTopicName)
            .ShouldBe(("5. Sınıf", "Matematik", "Kesirler", "Basit kesirler"));
        book.Toc[2].TopicId.ShouldBeNull();

        await using var ctx = _db.NewContext();
        (await NewService(ctx).ListAsync()).ShouldHaveSingleItem().Id.ShouldBe(book.Id);
    }

    [Fact]
    public async Task Update_ReplacesEverySetAtomically()
    {
        var book = await CreateAsync(MatLab());
        var update = MatLab("TT MatLab 5 (2. baskı)");
        update.Offsets = [new() { FileFrom = 1, FileTo = 10, Offset = 1 }];
        update.AnswerKeyRanges = [];
        update.Toc = [new() { FileFrom = 1, FileTo = 10, GradeId = _tax.Grade6, SubjectId = _tax.Math, TopicId = _tax.Topic6 }];

        LabelingBookDto? updated;
        await using (var ctx = _db.NewContext())
            updated = await NewService(ctx).UpdateAsync(book.Id, update, Trainer);

        updated.ShouldNotBeNull().Name.ShouldBe("TT MatLab 5 (2. baskı)");
        updated.Offsets.ShouldHaveSingleItem().Offset.ShouldBe(1);
        updated.AnswerKeyRanges.ShouldBeEmpty();
        updated.Toc.ShouldHaveSingleItem().GradeId.ShouldBe(_tax.Grade6);
        await using var read = _db.NewContext();
        (await read.BookPageOffsets.CountAsync()).ShouldBe(1);
        (await read.BookTocEntries.CountAsync()).ShouldBe(1);
        (await read.Books.SingleAsync()).UpdateTime.ShouldBe(_clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Update_UnknownBook_ReturnsNull()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).UpdateAsync(999, MatLab(), Trainer)).ShouldBeNull();
    }

    [Fact]
    public async Task Update_InvalidInput_LeavesTheStoredSetsUntouched()
    {
        var book = await CreateAsync(MatLab());
        var bad = MatLab();
        bad.Offsets.Add(new() { FileFrom = 40, FileTo = 60, Offset = 0 });

        await using (var ctx = _db.NewContext())
            (await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).UpdateAsync(book.Id, bad, Trainer)))
                .Message.ShouldBe("labeling.books.offsetOverlap");

        await using var read = _db.NewContext();
        (await read.BookPageOffsets.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Toc_TopicOfAnotherGrade_IsRejected()
    {
        var request = MatLab();
        request.Toc = [new() { FileFrom = 3, FileTo = 5, GradeId = _tax.Grade5, SubjectId = _tax.Math, TopicId = _tax.Topic6 }];

        (await CreateFailsAsync(request)).Message.ShouldBe("labeling.books.topicMismatch");
    }

    [Fact]
    public async Task Toc_TopicOfAnotherSubject_IsRejected()
    {
        var request = MatLab();
        request.Toc = [new() { FileFrom = 3, FileTo = 5, GradeId = _tax.Grade5, SubjectId = _tax.Turkish, TopicId = _tax.Topic5 }];

        (await CreateFailsAsync(request)).Message.ShouldBe("labeling.books.topicMismatch");
    }

    [Fact]
    public async Task Toc_SubTopicOfAnotherTopic_OrWithoutTopic_IsRejected()
    {
        var request = MatLab();
        request.Toc = [new() { FileFrom = 3, FileTo = 5, GradeId = _tax.Grade5, SubjectId = _tax.Math, TopicId = _tax.OtherTopic5, SubTopicId = _tax.SubTopic5 }];
        (await CreateFailsAsync(request)).Message.ShouldBe("labeling.books.subTopicMismatch");

        request.Toc = [new() { FileFrom = 3, FileTo = 5, GradeId = _tax.Grade5, SubjectId = _tax.Math, SubTopicId = _tax.SubTopic5 }];
        (await CreateFailsAsync(request)).Message.ShouldBe("labeling.books.subTopicMismatch");
    }

    [Fact]
    public async Task Toc_UnknownGradeOrSubject_IsRejected()
    {
        var request = MatLab();
        request.Toc = [new() { FileFrom = 3, FileTo = 5, GradeId = 9999, SubjectId = _tax.Math }];
        (await CreateFailsAsync(request)).Message.ShouldBe("labeling.books.gradeUnknown");

        request.Toc = [new() { FileFrom = 3, FileTo = 5, GradeId = _tax.Grade5, SubjectId = 9999 }];
        (await CreateFailsAsync(request)).Message.ShouldBe("labeling.books.subjectUnknown");
    }

    [Fact]
    public async Task Toc_Overlap_IsRejected()
    {
        var request = MatLab();
        request.Toc[1].FileFrom = 20;

        (await CreateFailsAsync(request)).Message.ShouldBe("labeling.books.tocOverlap");
    }

    [Fact]
    public async Task Name_IsRequiredAndUniqueIgnoringCase()
    {
        var request = MatLab();
        request.Name = "   ";
        (await CreateFailsAsync(request)).Message.ShouldBe("labeling.books.nameRequired");

        await CreateAsync(MatLab("TT MatLab 5"));
        var dup = (await CreateFailsAsync(MatLab("tt matlab 5"))).ShouldBeOfType<BookRegistryConflictException>();
        dup.ErrorCode.ShouldBe("LabelingBookNameTaken");
    }

    [Fact]
    public async Task Canonical_SingleLevelOnly()
    {
        var canonical = await CreateAsync(MatLab("Süper Combo Türkçe"));
        var edition = MatLab("Güneş Koleji Türkçe");
        edition.CanonicalBookId = canonical.Id;
        var created = await CreateAsync(edition);
        created.CanonicalBookId.ShouldBe(canonical.Id);
        created.CanonicalBookName.ShouldBe("Süper Combo Türkçe");

        // Kopyaya bağlanmak (zincir) ve kendisine bağlanmak geçersiz; kopyası olan kitap başka kitaba bağlanamaz.
        var chain = MatLab("Üçüncü baskı");
        chain.CanonicalBookId = created.Id;
        (await CreateFailsAsync(chain)).Message.ShouldBe("labeling.books.canonicalInvalid");

        await using var ctx = _db.NewContext();
        var self = MatLab("Süper Combo Türkçe");
        self.CanonicalBookId = canonical.Id;
        (await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).UpdateAsync(canonical.Id, self, Trainer)))
            .Message.ShouldBe("labeling.books.canonicalInvalid");
        var other = await CreateAsync(MatLab("Başka"));
        var withCopies = MatLab("Süper Combo Türkçe");
        withCopies.CanonicalBookId = other.Id;
        (await Should.ThrowAsync<LabelingValidationException>(() => NewService(ctx).UpdateAsync(canonical.Id, withCopies, Trainer)))
            .Message.ShouldBe("labeling.books.canonicalInvalid");
    }

    [Fact]
    public async Task GetPage_ReturnsPrintedNumberAnswerKeyAndToc()
    {
        var book = await CreateAsync(MatLab());
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var page = (await service.GetPageAsync(book.Id, 12)).ShouldNotBeNull();
        page.PrintedPage.ShouldBe(10);
        page.IsAnswerKey.ShouldBeFalse();
        page.Toc.ShouldNotBeNull().SubTopicId.ShouldBe(_tax.SubTopic5);

        var answerKey = (await service.GetPageAsync(book.Id, 105)).ShouldNotBeNull();
        answerKey.IsAnswerKey.ShouldBeTrue();
        answerKey.PrintedPage.ShouldBeNull();
        answerKey.Toc.ShouldBeNull();

        (await service.GetPageAsync(999, 12)).ShouldBeNull();
        (await Should.ThrowAsync<LabelingValidationException>(() => service.GetPageAsync(book.Id, 0)))
            .Message.ShouldBe("labeling.books.invalidPage");
    }

    [Fact]
    public async Task FindFilePages_FascicleBook_ReturnsAllRankedByNear()
    {
        var book = await CreateAsync(MatLab());
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.FindFilePagesAsync(book.Id, 10, null)).ShouldNotBeNull().FilePages.ShouldBe([12, 60]);
        (await service.FindFilePagesAsync(book.Id, 10, 70)).ShouldNotBeNull().FilePages.ShouldBe([60, 12]);
        (await service.FindFilePagesAsync(999, 10, null)).ShouldBeNull();
    }

    [Fact]
    public async Task ResolvePage_ForTheBankSave()
    {
        var book = await CreateAsync(MatLab());
        await using var ctx = _db.NewContext();

        var page = (await NewService(ctx).ResolvePageAsync(book.Id, 60)).ShouldNotBeNull();

        page.BookName.ShouldBe("TT MatLab 5");
        page.PrintedPage.ShouldBe(10);
        page.Toc.ShouldNotBeNull().TopicId.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_UnusedBook_SoftDeletes_UsedBook_Conflicts()
    {
        var unused = await CreateAsync(MatLab("Kullanılmayan"));
        var withTest = await CreateAsync(MatLab("Testli"));
        var namedByQuestion = await CreateAsync(MatLab("Sorulu"));
        await using (var seed = _db.NewContext())
        {
            seed.BookTests.Add(new BookTest { Name = "Deneme 1", BookId = withTest.Id });
            seed.Questions.Add(new Question { Text = "s", BookName = "Sorulu" });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        (await service.DeleteAsync(unused.Id)).ShouldBeTrue();
        (await service.DeleteAsync(unused.Id)).ShouldBeFalse();
        (await Should.ThrowAsync<BookRegistryConflictException>(() => service.DeleteAsync(withTest.Id))).ErrorCode.ShouldBe("LabelingBookInUse");
        (await Should.ThrowAsync<BookRegistryConflictException>(() => service.DeleteAsync(namedByQuestion.Id))).ErrorCode.ShouldBe("LabelingBookInUse");

        await using var read = _db.NewContext();
        (await read.Books.IgnoreQueryFilters().SingleAsync(b => b.Id == unused.Id)).IsDeleted.ShouldBeTrue();
        // Silinen kitabın adı yeniden kullanılabilir.
        await CreateAsync(MatLab("Kullanılmayan"));
    }

    [Fact]
    public async Task Delete_BookReferencedByAQuestionSource_Conflicts()
    {
        var book = await CreateAsync(MatLab("Kaynaklı"));
        await using (var seed = _db.NewContext())
        {
            var q = new Question { Text = "s" };
            seed.Questions.Add(q);
            await seed.SaveChangesAsync();
            seed.QuestionSources.Add(new QuestionSource { QuestionId = q.Id, BookId = book.Id, FilePage = 3, CropSha256 = new string('a', 64) });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<BookRegistryConflictException>(() => NewService(ctx).DeleteAsync(book.Id));
    }

    // ---------- review: paylaşılan kitap (Security M2 / L1), eski içindekiler (CR2) ----------

    private async Task<LabelingBookDto?> UpdateAsAsync(int id, LabelingBookSaveRequestDto request, BookEditor editor)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).UpdateAsync(id, request, editor);
    }

    public static TheoryData<string> SharingKinds => new() { "bookTest", "studyItem", "otherUsersQuestion" };

    private async Task ShareAsync(int bookId, string kind)
    {
        await using var seed = _db.NewContext();
        switch (kind)
        {
            case "bookTest":
                seed.BookTests.Add(new BookTest { Name = "Deneme 1", BookId = bookId });
                break;
            case "studyItem":
                seed.StudyItems.Add(new StudyItem { Title = "s", CreatedByUserId = 99, BookId = bookId });
                break;
            default:
                seed.SetCurrentUser(99); // başka öğretmen
                seed.Questions.Add(new Question { Text = "s", BookName = "TT MatLab 5" });
                break;
        }
        await seed.SaveChangesAsync();
    }

    [Theory]
    [MemberData(nameof(SharingKinds))]
    public async Task SharedBook_RenameOrCanonicalChange_IsAdminOnly(string kind)
    {
        var other = await CreateAsync(MatLab("Asıl"));
        var book = await CreateAsync(MatLab());
        await ShareAsync(book.Id, kind);

        var rename = MatLab("Yeni ad");
        (await Should.ThrowAsync<BookRegistryForbiddenException>(() => UpdateAsAsync(book.Id, rename, Trainer)))
            .ErrorCode.ShouldBe("LabelingBookRenameForbidden");
        var relink = MatLab();
        relink.CanonicalBookId = other.Id;
        await Should.ThrowAsync<BookRegistryForbiddenException>(() => UpdateAsAsync(book.Id, relink, Trainer));

        // Aralık / içindekiler / not serbest; Admin adı değiştirebilir.
        var ranges = MatLab();
        ranges.Notes = "düzeltildi";
        ranges.Offsets = [new() { FileFrom = 1, FileTo = 10, Offset = 0 }];
        (await UpdateAsAsync(book.Id, ranges, Trainer)).ShouldNotBeNull().Notes.ShouldBe("düzeltildi");
        (await UpdateAsAsync(book.Id, rename, Admin)).ShouldNotBeNull().Name.ShouldBe("Yeni ad");

        await using var read = _db.NewContext();
        (await read.Books.SingleAsync(b => b.Id == book.Id)).Name.ShouldBe("Yeni ad");
    }

    [Fact]
    public async Task UnsharedBook_OrOnlyOwnQuestions_CanBeRenamedByTrainer()
    {
        var book = await CreateAsync(MatLab());
        await using (var seed = _db.NewContext())
        {
            seed.SetCurrentUser(TrainerUserId);
            seed.Questions.Add(new Question { Text = "kendi", BookName = "TT MatLab 5" });
            await seed.SaveChangesAsync();
        }

        (await UpdateAsAsync(book.Id, MatLab("TT MatLab 5 yeni"), Trainer)).ShouldNotBeNull().Name.ShouldBe("TT MatLab 5 yeni");
    }

    [Fact]
    public async Task Rename_ToAnExistingNameIgnoringCase_Conflicts()
    {
        await CreateAsync(MatLab("Süper Combo"));
        var book = await CreateAsync(MatLab());

        (await Should.ThrowAsync<BookRegistryConflictException>(() => UpdateAsAsync(book.Id, MatLab("SÜPER COMBO"), Trainer)))
            .ErrorCode.ShouldBe("LabelingBookNameTaken");
        // Yalnız harf büyüklüğü değişen kendi adı serbest.
        (await UpdateAsAsync(book.Id, MatLab("tt matlab 5"), Trainer)).ShouldNotBeNull().Name.ShouldBe("tt matlab 5");
    }

    [Fact]
    public async Task Delete_BookUsedByAStudyItem_Conflicts()
    {
        var book = await CreateAsync(MatLab());
        await ShareAsync(book.Id, "studyItem");

        await using var ctx = _db.NewContext();
        (await Should.ThrowAsync<BookRegistryConflictException>(() => NewService(ctx).DeleteAsync(book.Id))).ErrorCode.ShouldBe("LabelingBookInUse");
    }

    [Theory]
    [InlineData("topic")]
    [InlineData("subTopic")]
    [InlineData("subject")]
    [InlineData("grade")]
    public async Task TocPointingToDeletedTaxonomy_IsReportedStale(string deleted)
    {
        var book = await CreateAsync(MatLab());
        await using (var del = _db.NewContext())
        {
            BaseEntity row = deleted switch
            {
                "topic" => await del.Topics.AsTracking().SingleAsync(t => t.Id == _tax.Topic5),
                "subTopic" => await del.SubTopics.AsTracking().SingleAsync(t => t.Id == _tax.SubTopic5),
                "subject" => await del.Subjects.AsTracking().SingleAsync(t => t.Id == _tax.Math),
                _ => await del.Grades.AsTracking().SingleAsync(t => t.Id == _tax.Grade5),
            };
            del.Remove(row); // soft-delete
            await del.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var page = (await service.GetPageAsync(book.Id, 12)).ShouldNotBeNull();
        page.Toc.ShouldBeNull();
        page.TocStale.ShouldBeTrue();
        page.PrintedPage.ShouldBe(10);
        var resolved = (await service.ResolvePageAsync(book.Id, 12)).ShouldNotBeNull();
        resolved.Toc.ShouldBeNull();
        resolved.TocStale.ShouldBeTrue();

        (await service.GetPageAsync(book.Id, 2)).ShouldNotBeNull().TocStale.ShouldBeFalse(); // içindekiler satırı yok
    }
}
