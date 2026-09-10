using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class StudyItemServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private StudyItemService NewService(AppDbContext ctx) => new(ctx, _minio);

    private static UserProfileDto Teacher(int id = 10) => new() { Id = id, Role = "Teacher", FullName = "T" };
    private static UserProfileDto Student(int id = 20) => new() { Id = id, Role = "Student", FullName = "S" };

    private async Task<int> AddPageAsync(string title, bool published, int createdBy = 10, int? subjectId = null)
    {
        await using var ctx = _db.NewContext();
        var p = new StudyItem
        {
            Title = title, Description = "d", IsPublished = published,
            CreatedByUserId = createdBy, CreatedByName = "T", CreatedByRole = "Teacher", SubjectId = subjectId,
        };
        ctx.StudyItems.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task Students_only_see_published_pages()
    {
        await AddPageAsync("Yayında", published: true);
        await AddPageAsync("Taslak", published: false);

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        var forStudent = await svc.GetPagedAsync(new StudyItemFilterDto { PageNumber = 1, PageSize = 10 }, Student());
        forStudent.Items.Select(i => i.Title).ShouldBe(new[] { "Yayında" });

        var forTeacher = await svc.GetPagedAsync(new StudyItemFilterDto { PageNumber = 1, PageSize = 10 }, Teacher());
        forTeacher.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Search_matches_title_or_description_case_insensitively()
    {
        await AddPageAsync("Kesirler", true);
        await AddPageAsync("Ondalık", true);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetPagedAsync(
            new StudyItemFilterDto { PageNumber = 1, PageSize = 10, Search = "KESIR" }, Teacher());

        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Kesirler");
    }

    [Fact]
    public async Task Filters_by_subject()
    {
        int subjectId;
        await using (var ctx = _db.NewContext())
        {
            var s = new Subject { Name = "Mat" };
            ctx.Subjects.Add(s);
            await ctx.SaveChangesAsync();
            subjectId = s.Id;
        }
        await AddPageAsync("A", true, subjectId: subjectId);
        await AddPageAsync("B", true);

        await using var ctx2 = _db.NewContext();
        var page = await NewService(ctx2).GetPagedAsync(
            new StudyItemFilterDto { PageNumber = 1, PageSize = 10, SubjectId = subjectId }, Teacher());
        page.Items.ShouldHaveSingleItem().Title.ShouldBe("A");
    }

    [Fact]
    public async Task GetById_hides_an_unpublished_page_from_a_student()
    {
        var id = await AddPageAsync("Taslak", published: false);
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        (await svc.GetByIdAsync(id, Student())).ShouldBeNull();
        (await svc.GetByIdAsync(id, Teacher())).ShouldNotBeNull();
        (await svc.GetByIdAsync(9999, Teacher())).ShouldBeNull();
    }

    [Fact]
    public async Task Create_persists_the_page_with_author_metadata()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "  Yeni  ", Description = "aç", IsPublished = true,
                ContentType = StudyItemContentType.Link, Url = "https://www.eba.gov.tr/x", Platform = StudyItemLinkPlatform.Eba
            },
            new List<IFormFile>(), Teacher(42));

        result.Error.ShouldBeNull();
        result.Item!.Title.ShouldBe("Yeni");
        result.Item.ContentType.ShouldBe(StudyItemContentType.Link);
        result.Item.Platform.ShouldBe(StudyItemLinkPlatform.Eba);

        await using var check = _db.NewContext();
        var saved = await check.StudyItems.FirstAsync();
        saved.CreatedByUserId.ShouldBe(42);
        saved.CreatedByRole.ShouldBe("Teacher");
        saved.Url.ShouldBe("https://www.eba.gov.tr/x");
    }

    [Fact]
    public async Task Create_image_type_without_any_image_is_rejected()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto { Title = "Resimsiz", ContentType = StudyItemContentType.Image },
            new List<IFormFile>(), Teacher());

        result.Item.ShouldBeNull();
        result.Error.ShouldContain("resim");
        (await ctx.StudyItems.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://x.y/z")]
    public async Task Create_link_type_requires_a_valid_http_url(string? url)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto { Title = "Link", ContentType = StudyItemContentType.Link, Url = url },
            new List<IFormFile>(), Teacher());

        result.Item.ShouldBeNull();
        result.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task Create_book_page_range_rejects_end_page_before_start_page()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "Aralık", ContentType = StudyItemContentType.BookPageRange,
                NewBookName = "Kitap", NewBookTestName = "Test 1", StartPage = 10, EndPage = 5
            },
            new List<IFormFile>(), Teacher());

        result.Item.ShouldBeNull();
        result.Error.ShouldContain("endPage");
    }

    [Fact]
    public async Task Create_book_page_range_finds_or_creates_book_and_test_by_name()
    {
        int existingBookId;
        await using (var seed = _db.NewContext())
        {
            var book = new Book { Name = "Mevcut Kitap" };
            book.BookTests.Add(new BookTest { Name = "Mevcut Test", Book = book });
            seed.Books.Add(book);
            await seed.SaveChangesAsync();
            existingBookId = book.Id;
        }

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        // Aynı isimle: yeniden oluşturmaz, var olanı bağlar.
        var reused = await svc.CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "A", ContentType = StudyItemContentType.BookPageRange,
                NewBookName = "Mevcut Kitap", NewBookTestName = "Mevcut Test", StartPage = 1, EndPage = 3
            },
            new List<IFormFile>(), Teacher());
        reused.Error.ShouldBeNull();
        reused.Item!.BookId.ShouldBe(existingBookId);
        reused.Item.BookName.ShouldBe("Mevcut Kitap");
        reused.Item.BookTestName.ShouldBe("Mevcut Test");

        // Var olan kitap + yeni test adı: sadece test oluşur.
        var newTest = await svc.CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "B", ContentType = StudyItemContentType.BookPageRange,
                BookId = existingBookId, NewBookTestName = "Yeni Test", StartPage = 4, EndPage = 4
            },
            new List<IFormFile>(), Teacher());
        newTest.Error.ShouldBeNull();
        newTest.Item!.BookTestName.ShouldBe("Yeni Test");

        await using var check = _db.NewContext();
        (await check.Books.CountAsync()).ShouldBe(1);
        (await check.BookTests.CountAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData(StudyItemLinkPlatform.YouTube)]
    [InlineData(StudyItemLinkPlatform.Other)]
    public async Task Create_link_type_persists_the_requested_platform(StudyItemLinkPlatform platform)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "Link", ContentType = StudyItemContentType.Link,
                Url = "https://example.com/x", Platform = platform
            },
            new List<IFormFile>(), Teacher());

        result.Error.ShouldBeNull();
        result.Item!.Platform.ShouldBe(platform);
    }

    [Fact]
    public async Task Create_link_type_defaults_platform_to_other_when_not_specified()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "Link", ContentType = StudyItemContentType.Link, Url = "https://example.com/x"
            },
            new List<IFormFile>(), Teacher());

        result.Error.ShouldBeNull();
        result.Item!.Platform.ShouldBe(StudyItemLinkPlatform.Other);
    }

    [Fact]
    public async Task Create_book_page_range_links_directly_to_existing_book_and_test_ids()
    {
        int bookId, bookTestId;
        await using (var seed = _db.NewContext())
        {
            var book = new Book { Name = "Var Olan Kitap" };
            var bookTest = new BookTest { Name = "Var Olan Test", Book = book };
            book.BookTests.Add(bookTest);
            seed.Books.Add(book);
            await seed.SaveChangesAsync();
            bookId = book.Id;
            bookTestId = bookTest.Id;
        }

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "Aralık", ContentType = StudyItemContentType.BookPageRange,
                BookId = bookId, BookTestId = bookTestId, StartPage = 1, EndPage = 5
            },
            new List<IFormFile>(), Teacher());

        result.Error.ShouldBeNull();
        result.Item!.BookId.ShouldBe(bookId);
        result.Item.BookTestId.ShouldBe(bookTestId);

        await using var check = _db.NewContext();
        (await check.Books.CountAsync()).ShouldBe(1);
        (await check.BookTests.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Create_book_page_range_rejects_a_book_test_id_that_does_not_belong_to_the_book()
    {
        int bookId, otherBookTestId;
        await using (var seed = _db.NewContext())
        {
            var book = new Book { Name = "Kitap A" };
            var otherBook = new Book { Name = "Kitap B" };
            var otherBookTest = new BookTest { Name = "Test B1", Book = otherBook };
            otherBook.BookTests.Add(otherBookTest);
            seed.Books.AddRange(book, otherBook);
            await seed.SaveChangesAsync();
            bookId = book.Id;
            otherBookTestId = otherBookTest.Id;
        }

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "Aralık", ContentType = StudyItemContentType.BookPageRange,
                BookId = bookId, BookTestId = otherBookTestId, StartPage = 1, EndPage = 5
            },
            new List<IFormFile>(), Teacher());

        result.Item.ShouldBeNull();
        result.Error.ShouldContain("Kitap testi");
    }

    [Fact]
    public async Task Create_image_type_imports_images_from_the_minio_images_json_payload()
    {
        var minioImages = "[{\"bookName\":\"Fen\",\"pageNumber\":1,\"minioUrl\":\"https://minio/x1.webp\"}," +
                           "{\"bookName\":\"Fen\",\"pageNumber\":2,\"minioUrl\":\"https://minio/x2.webp\"}]";

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "Resimli", ContentType = StudyItemContentType.Image, MinioImages = minioImages
            },
            new List<IFormFile>(), Teacher());

        result.Error.ShouldBeNull();
        result.Item!.Images.Count.ShouldBe(2);
        result.Item.Images.Select(i => i.ImageUrl).ShouldBe(new[] { "https://minio/x1.webp", "https://minio/x2.webp" });
        result.Item.CoverImageUrl.ShouldBe("https://minio/x1.webp");
    }

    [Fact]
    public async Task Update_changing_content_type_clears_the_fields_of_the_previous_type()
    {
        int id;
        await using (var ctx = _db.NewContext())
        {
            var created = await NewService(ctx).CreateAsync(
                new CreateStudyItemRequestDto
                {
                    Title = "Link Baslangic", ContentType = StudyItemContentType.Link,
                    Url = "https://example.com/a", Platform = StudyItemLinkPlatform.Eba
                },
                new List<IFormFile>(), Teacher());
            id = created.Item!.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var updated = await NewService(ctx).UpdateAsync(id,
                new UpdateStudyItemRequestDto
                {
                    Title = "Kitap Sayfasi", ContentType = StudyItemContentType.BookPageRange,
                    NewBookName = "Yeni Kitap", NewBookTestName = "Yeni Test", StartPage = 1, EndPage = 2
                },
                new List<IFormFile>(), Teacher());

            updated.Error.ShouldBeNull();
            updated.Item!.ContentType.ShouldBe(StudyItemContentType.BookPageRange);
            updated.Item.Url.ShouldBeNull();
            updated.Item.Platform.ShouldBeNull();
        }

        await using var check = _db.NewContext();
        var saved = await check.StudyItems.FirstAsync(p => p.Id == id);
        saved.Url.ShouldBeNull();
        saved.Platform.ShouldBeNull();
        saved.BookId.ShouldNotBeNull();
        saved.StartPage.ShouldBe(1);
    }

    [Fact]
    public async Task GetById_reads_a_pre_migration_record_whose_content_type_defaults_to_image()
    {
        // Migration öncesi kayıtlar ContentType sütunu olmadan oluşturulmuştu; migration bunlara 0 (Image) atar.
        // Burada ContentType'ı hiç set etmeden (varsayılan) bir StudyItem kaydederek bu senaryoyu simüle ediyoruz.
        int id;
        await using (var ctx = _db.NewContext())
        {
            var legacy = new StudyItem
            {
                Title = "Eski Sayfa", Description = "d", IsPublished = true,
                CreatedByUserId = 10, CreatedByName = "T", CreatedByRole = "Teacher",
            };
            ctx.StudyItems.Add(legacy);
            await ctx.SaveChangesAsync();
            id = legacy.Id;
        }

        await using var check = _db.NewContext();
        var dto = await NewService(check).GetByIdAsync(id, Teacher());

        dto.ShouldNotBeNull();
        dto!.ContentType.ShouldBe(StudyItemContentType.Image);
        dto.Url.ShouldBeNull();
        dto.BookId.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_is_refused_for_a_non_author()
    {
        var id = await AddPageAsync("Sayfa", true, createdBy: 10);
        await using var ctx = _db.NewContext();

        var other = await NewService(ctx).DeleteAsync(id, Teacher(id: 999));
        other.Success.ShouldBeFalse();
        other.Message.ShouldContain("yetkiniz yok");

        var missing = await NewService(ctx).DeleteAsync(4242, Teacher(id: 10));
        missing.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_by_the_author_soft_deletes_the_page()
    {
        var id = await AddPageAsync("Sayfa", true, createdBy: 10);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).DeleteAsync(id, Teacher(id: 10))).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.StudyItems.AnyAsync(p => p.Id == id)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_service_caller_can_delete_any_page()
    {
        var id = await AddPageAsync("Sayfa", true, createdBy: 10);
        await using var ctx = _db.NewContext();
        (await NewService(ctx).DeleteAsync(id, new UserProfileDto { Id = 0, Role = "Service" })).Success.ShouldBeTrue();
    }

    public void Dispose() => _db.Dispose();
}
