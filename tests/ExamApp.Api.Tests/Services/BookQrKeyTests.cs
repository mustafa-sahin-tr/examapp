using System.Net;
using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using ZXing;
using ZXing.QrCode;

namespace ExamApp.Api.Tests.Services;

/// <summary>#1219: QR'dan cevap anahtarı — sayfa QR'ı, Fernus çözüm listesi, harf çıkarma, bağlantı güvenliği, "QR'dan al".</summary>
public class BookQrKeyTests : IDisposable
{
    private const string Qr = "http://fi.frns.in/fen501-001";
    private const string SolutionPage = "https://fideocozum.com/video.php?qr=fen501-001";

    private readonly TestDb _db = TestDb.Create();
    private readonly IAudioTranscriber _transcriber = Substitute.For<IAudioTranscriber>();

    public BookQrKeyTests()
    {
        _transcriber.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        // Ses dosyasının içeriği, öğretmenin kaydın sonunda söylediği.
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Encoding.UTF8.GetString(ci.ArgAt<byte[]>(0)));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Reads_the_keys_in_the_background_and_writes_them_as_qr_keys()
    {
        var id = await SeedJobAsync();
        var http = Publisher(SolutionPage);
        var jobs = Substitute.For<IBackgroundJobClient>();

        var start = await Service(http, jobs).StartAsync(id, null, default);

        start.Error.ShouldBe(QrKeyError.None);
        start.Run!.Orders.ShouldBe([1]); // 2. testin sayfasında QR yok, 3. testin anahtarı var
        jobs.Received(1).Create(Arg.Is<Hangfire.Common.Job>(j => j.Method.Name == nameof(BookQrKeyService.RunAsync)), Arg.Any<IState>());
        http.Requests.ShouldBeEmpty(); // okuma arka planda

        await Service(http).RunAsync(id, default);

        await using var check = _db.NewContext();
        var row = await check.BookImportJobTests.SingleAsync(t => t.JobId == id && t.Order == 1);
        // 3. çözüm YouTube: ses yok, harf tahmin edilmez.
        (row.AnswerKey, row.KeySource, row.KeyAnswers).ShouldBe(("BD?", BookKeySources.Qr, 2));
        (await check.BookImportJobTests.SingleAsync(t => t.JobId == id && t.Order == 3)).AnswerKey.ShouldBe("ABCD");
        var run = BookSetupSummary.Parse((await check.BookImportJobs.SingleAsync(j => j.Id == id)).SetupJson)!.QrRun!;
        (run.Done, run.Failed, run.Finished).ShouldBe((1, 0, true));
        http.Requests.Select(r => r.RequestUri!.Host).Distinct().ShouldBe(["fi.frns.in", "fideocozum.com"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_running_read_blocks_a_second_start_and_the_confirmation()
    {
        var id = await SeedJobAsync();
        (await Service(Publisher(SolutionPage)).StartAsync(id, 1, default)).Error.ShouldBe(QrKeyError.None);

        (await Service(Publisher(SolutionPage)).StartAsync(id, null, default)).Error.ShouldBe(QrKeyError.Running);
        await using var check = _db.NewContext();
        BookQrKeyService.IsActive(BookSetupSummary.Parse((await check.BookImportJobs.SingleAsync()).SetupJson)!.QrRun!).ShouldBeTrue();
    }

    [Fact]
    public async Task A_redirect_outside_the_publisher_is_not_followed()
    {
        var id = await SeedJobAsync();
        var http = Publisher("http://169.254.169.254/latest/meta-data");
        await Service(http).StartAsync(id, null, default);

        await Service(http).RunAsync(id, default);

        http.Requests.ShouldAllBe(r => r.RequestUri!.Host == "fi.frns.in");
        await using var check = _db.NewContext();
        (await check.BookImportJobTests.SingleAsync(t => t.JobId == id && t.Order == 1)).AnswerKey.ShouldBeNull();
        BookSetupSummary.Parse((await check.BookImportJobs.SingleAsync()).SetupJson)!.QrRun!.Failed.ShouldBe(1);
    }

    [Fact]
    public async Task Without_the_transcriber_nothing_is_started()
    {
        var id = await SeedJobAsync();
        _transcriber.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);

        (await Service(Publisher(SolutionPage)).StartAsync(id, null, default)).Error.ShouldBe(QrKeyError.NotAvailable);
    }

    [Fact]
    public async Task A_test_without_a_qr_or_a_job_past_the_setup_has_no_qr_read()
    {
        var id = await SeedJobAsync();
        (await Service(Publisher(SolutionPage)).StartAsync(id, 2, default)).Error.ShouldBe(QrKeyError.NoQr);
        (await Service(Publisher(SolutionPage)).StartAsync(id, 9, default)).Error.ShouldBe(QrKeyError.NotFound);

        var running = await SeedJobAsync(BookImportJobStatus.Running);
        (await Service(Publisher(SolutionPage)).StartAsync(running, null, default)).Error.ShouldBe(QrKeyError.InvalidState);
    }

    private BookQrKeyService Service(StubHttp http, IBackgroundJobClient? jobs = null) =>
        new(_db.NewContext(), new QrSolutionClient(http), _transcriber, [new FernusQrKeyReader()],
            jobs ?? Substitute.For<IBackgroundJobClient>(), NullLogger<BookQrKeyService>.Instance);

    /// <summary>QR kısa bağlantısı <paramref name="redirect"/>'e yönlendirir; çözüm sayfası 3 çözüm, ses dosyaları öğretmenin sözleri.</summary>
    private static StubHttp Publisher(string redirect) => new(request => request.RequestUri!.AbsoluteUri switch
    {
        Qr => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(redirect) } },
        SolutionPage => Ok("""
            <script>var solved_list = '[{"solved_type":"fernus","audio":"https:\/\/fi.frns.in\/uploads\/1.mp3","order":"1"},{"solved_type":"fernus","audio":"https:\/\/fi.frns.in\/uploads\/2.mp3","order":"2"},{"solved_type":"youtube","video":"https:\/\/youtu.be\/x","order":"3"}]';</script>
            """),
        "https://fi.frns.in/uploads/1.mp3" => Ok("Güneşin şekli küreseldir. B seçeneğini işaretleyebiliriz."),
        "https://fi.frns.in/uploads/2.mp3" => Ok("Özel filtreli teleskop kullanmalıyız. Bu da D seçeneğinde mevcuttur."),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    });

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    /// <summary>Onay bekleyen iş: 1. test (s.10–11) sayfasında QR var, 2. test (s.12–13) yok, 3. testin (s.14) anahtarı kitaptan.</summary>
    private async Task<Guid> SeedJobAsync(BookImportJobStatus status = BookImportJobStatus.AwaitingConfirmation)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var job = new BookImportJob
        {
            Id = Guid.NewGuid(), BookName = "Define Fen", GradeId = grade.Id, FileUrl = "x", FileName = "page_1.webp",
            Status = status, Stage = BookImportStage.SetupConfirmation,
            SetupJson = new BookSetupSummary { QrLinks = [new(10, Qr), new(14, Qr)] }.Serialize(),
        };
        ctx.SetCurrentUser(1);
        ctx.BookImportJobs.Add(job);
        ctx.BookImportJobTests.AddRange(
            new BookImportJobTest { JobId = job.Id, Order = 1, Unit = 1, TestNo = 1, FileFrom = 10, FileTo = 11, ExpectedQuestions = 3 },
            new BookImportJobTest { JobId = job.Id, Order = 2, Unit = 1, TestNo = 2, FileFrom = 12, FileTo = 13 },
            new BookImportJobTest
            {
                JobId = job.Id, Order = 3, Unit = 1, TestNo = 3, FileFrom = 14, FileTo = 14, AnswerKey = "ABCD", KeySource = BookKeySources.Book,
            });
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public void Reads_the_link_of_a_qr_on_a_page()
    {
        PageQrReader.Read(PageWithQr("http://fi.frns.in/fen501-001")).ShouldBe("http://fi.frns.in/fen501-001");
    }

    [Fact]
    public void A_page_without_a_qr_or_with_a_non_link_qr_has_no_link()
    {
        PageQrReader.Read(PageWithQr(null)).ShouldBeNull();
        PageQrReader.Read(PageWithQr("WIFI:S:okul;T:WPA;P:1234;;")).ShouldBeNull();
        PageQrReader.Read([1, 2, 3]).ShouldBeNull();
    }

    [Theory]
    [InlineData("Fakat buz hokeyi küresel olmadığı için benzemez. B seçeneğini işaretleyebiliriz.", 'B')]
    [InlineData("Bu da D seçeneğinde mevcuttur. İşaretleyebiliriz.", 'D')]
    [InlineData("O halde A şıkkını işaretleyebiliriz.", 'A')]
    [InlineData("Bu yüzden cevabımız C seçeneğidir.", 'C')]
    [InlineData("A seçeneği yanlış, B de yanlış; doğru cevap E.", 'E')]
    [InlineData("Doğru cevap 1-2C seçenidir.", 'C')]
    [InlineData("Cevabımız de seçeneğidir.", '?')]
    [InlineData("Güneşin kendi ekseni etrafında dönmesini keşfetmiştir. işaretleyebiliriz.", '?')]
    [InlineData("", '?')]
    public void Takes_the_last_answer_letter_the_teacher_says(string transcript, char expected)
    {
        QrAnswerLetter.From(transcript).ShouldBe(expected);
    }

    [Fact]
    public void Parses_the_fernus_solution_list_in_question_order()
    {
        const string html = """
            <script>
            var _data = [];
                    var solved_list = '[{"id":"2","nm":"2. Soru","solved_type":"fernus","audio":"https:\/\/fi.frns.in\/uploads\/b.mp3","order":"2"},{"id":"1","nm":"1. Soru","solved_type":"fernus","audio":"https:\/\/fi.frns.in\/uploads\/a.mp3","order":"1"},{"id":"3","nm":"Kenan\'ın sorusu","solved_type":"youtube","video":"https:\/\/youtu.be\/x","order":"3"},{"id":"4","solved_type":"fernus","audio":"https:\/\/evil.example\/c.mp3","order":"4"}]';
            </script>
            """;

        var solutions = FernusQrKeyReader.Parse(html);

        solutions.Select(s => s.Order).ShouldBe([1, 2, 3, 4]);
        solutions.Select(s => s.Audio?.AbsoluteUri).ShouldBe(
            ["https://fi.frns.in/uploads/a.mp3", "https://fi.frns.in/uploads/b.mp3", null, null]);
        FernusQrKeyReader.Parse("<html>yok</html>").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("fi.frns.in", true)]
    [InlineData("kxk.frns.in", true)]
    [InlineData("fideocozum.com", true)]
    [InlineData("frns.in.evil.example", false)]
    [InlineData("notfrns.in", false)]
    public void Only_fernus_hosts_are_read(string host, bool handled)
    {
        new FernusQrKeyReader().Handles(new Uri($"https://{host}/x")).ShouldBe(handled);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    public void Connects_only_to_public_addresses(string address, bool allowed)
    {
        QrSolutionClient.IsPublic(IPAddress.Parse(address)).ShouldBe(allowed);
    }

    [Fact]
    public async Task Does_not_fetch_a_host_outside_the_allow_list()
    {
        var client = new QrSolutionClient(Substitute.For<IHttpClientFactory>());

        await Should.ThrowAsync<QrFetchException>(() =>
            client.GetAsync(new Uri("http://169.254.169.254/latest"), u => new FernusQrKeyReader().Handles(u), 1024, default));
    }

    [Fact]
    public void A_test_takes_the_first_qr_inside_its_pages()
    {
        var summary = new BookSetupSummary { QrLinks = [new(12, "http://fi.frns.in/2"), new(10, "http://fi.frns.in/1")] };

        summary.QrLinkFor(10, 11).ShouldBe("http://fi.frns.in/1");
        summary.QrLinkFor(11, 13).ShouldBe("http://fi.frns.in/2");
        summary.QrLinkFor(14, 15).ShouldBeNull();
        summary.QrLinkFor(null, 15).ShouldBeNull();
    }

    /// <summary>Beyaz A4 benzeri sayfa; içerik verilirse sağ üstte QR (kitap sayfasındaki gibi).</summary>
    private static byte[] PageWithQr(string? content)
    {
        using var page = new SKBitmap(1000, 1400);
        using (var canvas = new SKCanvas(page))
        {
            canvas.Clear(SKColors.White);
            if (content is not null)
            {
                var matrix = new QRCodeWriter().encode(content, BarcodeFormat.QR_CODE, 120, 120);
                using var paint = new SKPaint { Color = SKColors.Black };
                for (var y = 0; y < matrix.Height; y++)
                    for (var x = 0; x < matrix.Width; x++)
                        if (matrix[x, y])
                            canvas.DrawRect(840 + x, 80 + y, 1, 1, paint);
            }
        }

        using var image = SKImage.FromBitmap(page);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }
}
