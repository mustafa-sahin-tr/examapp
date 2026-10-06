using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #365 (S3): çalışma etkinliği editörünün kitap sayfası sorgusu. Bucket'lar özel olduğu için tarayıcı
/// <c>/img/study-pages/books/...</c> nesnesini artık yoklayamaz; sunucu StatObject yapar. Kitap adı tek yol parçası olarak
/// sıkı allowlist'ten geçer (traversal, yol ayırıcı, gateway imzasını bozan &amp;/%, kontrol karakteri yok).
/// </summary>
public class StudyItemBookPageLookupTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private StudyItemService NewService() => new(_db.NewContext(), _minio);

    private static StudyBookPageRefDto Page(string book, int page) => new() { Book = book, PageNumber = page };

    [Fact]
    public async Task Existing_pages_get_the_stored_path_and_missing_pages_get_nothing()
    {
        _minio.ObjectExistsAsync("study-pages", "books/Fen Bilimleri Kitabı/page_3.webp", Arg.Any<CancellationToken>()).Returns(true);

        var result = await NewService().LookupBookPagesAsync([Page("Fen Bilimleri Kitabı", 3), Page("Fen Bilimleri Kitabı", 4)]);

        result.Error.ShouldBeNull();
        result.Items!.Count.ShouldBe(2);
        var found = result.Items[0];
        found.Exists.ShouldBeTrue();
        found.Book.ShouldBe("Fen Bilimleri Kitabı");
        found.PageNumber.ShouldBe(3);
        found.MinioUrl.ShouldBe("/img/study-pages/books/Fen Bilimleri Kitabı/page_3.webp");
        found.PreviewUrl.ShouldBe(found.MinioUrl); // MVC JSON çıktısında [StorageUrl] ile imzalanır

        var missing = result.Items[1];
        missing.Exists.ShouldBeFalse();
        missing.MinioUrl.ShouldBeNull();
        missing.PreviewUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Duplicate_pages_are_looked_up_once()
    {
        var result = await NewService().LookupBookPagesAsync([Page("Mat 5", 1), Page("Mat 5", 1), Page("Mat 5", 2)]);

        result.Items!.Select(i => i.PageNumber).ShouldBe(new[] { 1, 2 });
        await _minio.Received(1).ObjectExistsAsync("study-pages", "books/Mat 5/page_1.webp", Arg.Any<CancellationToken>());
        await _minio.Received(2).ObjectExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Fen Bilimleri Kitabı")]
    [InlineData("matematik-5_a")]
    [InlineData("Yayın (2024), cilt 1")]
    [InlineData("Ali'nin+Kitabı v1.2")]
    [InlineData("ÇĞİÖŞÜçğıöşü")]
    public async Task Allowed_book_names_reach_storage(string book)
    {
        var result = await NewService().LookupBookPagesAsync([Page(book, 1)]);

        result.Error.ShouldBeNull();
        await _minio.Received(1).ObjectExistsAsync("study-pages", $"books/{book}/page_1.webp", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" lead")]
    [InlineData("trail ")]
    [InlineData("..")]
    [InlineData("../question-transfer")]
    [InlineData("a..b")]
    [InlineData(".hidden")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("a%2Fb")]
    [InlineData("a&b")]
    [InlineData("a?b")]
    [InlineData("a#b")]
    [InlineData("a\tb")]
    [InlineData("a\u0000b")]
    [InlineData("a<script>")]
    [InlineData("a*")]
    public async Task Invalid_book_names_are_rejected_before_any_storage_call(string book)
    {
        var result = await NewService().LookupBookPagesAsync([Page("Mat 5", 1), Page(book, 1)]);

        result.Items.ShouldBeNull();
        result.StorageUnavailable.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
        await _minio.DidNotReceiveWithAnyArgs().ObjectExistsAsync(default!, default!, default);
    }

    [Fact]
    public async Task Too_long_book_name_is_rejected()
    {
        var result = await NewService().LookupBookPagesAsync([Page(new string('a', StudyBookPageRefDto.MaxBookLength + 1), 1)]);

        result.Error.ShouldNotBeNull();
        await _minio.DidNotReceiveWithAnyArgs().ObjectExistsAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(StudyBookPageRefDto.MaxPageNumber + 1)]
    public async Task Out_of_range_page_numbers_are_rejected(int page)
    {
        var result = await NewService().LookupBookPagesAsync([Page("Mat 5", page)]);

        result.Error.ShouldNotBeNull();
        await _minio.DidNotReceiveWithAnyArgs().ObjectExistsAsync(default!, default!, default);
    }

    [Fact]
    public async Task Empty_and_oversized_requests_are_rejected()
    {
        (await NewService().LookupBookPagesAsync([])).Error.ShouldNotBeNull();

        var tooMany = Enumerable.Range(1, StudyBookPageLookupRequestDto.MaxPages + 1).Select(n => Page("Mat 5", n)).ToList();
        (await NewService().LookupBookPagesAsync(tooMany)).Error.ShouldNotBeNull();
        await _minio.DidNotReceiveWithAnyArgs().ObjectExistsAsync(default!, default!, default);
    }

    [Fact]
    public async Task Storage_failure_reports_unavailable_without_partial_results()
    {
        _minio.ObjectExistsAsync("study-pages", "books/Mat 5/page_2.webp", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new HttpRequestException("minio down")));

        var result = await NewService().LookupBookPagesAsync([Page("Mat 5", 1), Page("Mat 5", 2)]);

        result.StorageUnavailable.ShouldBeTrue();
        result.Items.ShouldBeNull();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task First_storage_failure_cancels_the_remaining_lookups()
    {
        var cancelled = 0;
        _minio.ObjectExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var key = call.ArgAt<string>(1);
                var token = call.ArgAt<CancellationToken>(2);
                if (key.EndsWith("page_1.webp"))
                {
                    await Task.Delay(200); // diğer sorgular bu arada yolda
                    throw new HttpRequestException("minio down");
                }
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token); // yavaş MinIO
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelled);
                    throw;
                }
                return true;
            });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await NewService().LookupBookPagesAsync(Enumerable.Range(1, 5).Select(n => Page("Mat 5", n)).ToList());

        result.StorageUnavailable.ShouldBeTrue();
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        cancelled.ShouldBe(4); // yoldaki 4 StatObject iptal edildi
    }

    [Fact]
    public async Task Concurrency_is_bounded()
    {
        var inFlight = 0;
        var peak = 0;
        _minio.ObjectExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref peak, now);
                await Task.Delay(5);
                Interlocked.Decrement(ref inFlight);
                return false;
            });

        var pages = Enumerable.Range(1, 60).Select(n => Page("Mat 5", n)).ToList();
        var result = await NewService().LookupBookPagesAsync(pages);

        result.Items!.Count.ShouldBe(60);
        peak.ShouldBeLessThanOrEqualTo(StudyItemService.BookPageLookupConcurrency);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    public void Dispose() => _db.Dispose();
}
