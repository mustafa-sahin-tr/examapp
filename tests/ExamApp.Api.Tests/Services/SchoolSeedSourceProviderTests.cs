using System.Net;
using System.Security.Cryptography;
using System.Text;
using ExamApp.Api.Services.Schools.Seed;
using ExamApp.Api.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// seed-schools indirici (issue #216): önbellek/hash/HTTP davranışı. Ağ yok — <see cref="StubHttp"/>
/// sahte handler, geçici ContentRootPath.
/// </summary>
public class SchoolSeedSourceProviderTests : IDisposable
{
    private const string Url = "https://raw.githubusercontent.com/test/repo/sha/data/meb-okullar.csv";
    private const string Csv = "il,ilce,okul_adi,kurum_kodu,okul_turu\nKars,Merkez,Test İlkokulu,1,İlkokul\n";
    private static readonly byte[] CsvBytes = Encoding.UTF8.GetBytes(Csv);
    private static readonly string CsvSha256 = Convert.ToHexStringLower(SHA256.HashData(CsvBytes));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "examapp-seed-tests", Guid.NewGuid().ToString("N"));

    private string CachePath => Path.Combine(_root, "Data", GitHubSchoolSeedSourceProvider.CacheDirectoryName, GitHubSchoolSeedSourceProvider.CacheFileName);
    private string CacheDir => Path.GetDirectoryName(CachePath)!;

    private GitHubSchoolSeedSourceProvider NewProvider(StubHttp http, string? expectedSha = null)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.ContentRootPath.Returns(_root);
        return new GitHubSchoolSeedSourceProvider(http, env, NullLogger<GitHubSchoolSeedSourceProvider>.Instance, Url, expectedSha ?? CsvSha256);
    }

    private static StubHttp Serve(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });

    [Fact]
    public async Task Downloads_verifies_hash_and_caches_when_no_cache()
    {
        var http = Serve(CsvBytes);

        var source = await NewProvider(http).GetAsync();

        http.Requests.ShouldHaveSingleItem().RequestUri!.ToString().ShouldBe(Url);
        source.FromCache.ShouldBeFalse();
        source.Sha256.ShouldBe(CsvSha256);
        source.Description.ShouldBe(Url);
        File.Exists(CachePath).ShouldBeTrue();
        File.Exists(CachePath + ".download").ShouldBeFalse();
        using var stream = source.OpenRead();
        MebSchoolCsv.Parse(stream).ShouldHaveSingleItem().Name.ShouldBe("Test İlkokulu");
    }

    [Fact]
    public async Task Valid_cache_is_used_without_any_http_call()
    {
        Directory.CreateDirectory(CacheDir);
        await File.WriteAllBytesAsync(CachePath, CsvBytes);
        var http = Serve(CsvBytes);

        var source = await NewProvider(http).GetAsync();

        http.Requests.ShouldBeEmpty();
        source.FromCache.ShouldBeTrue();
        source.Sha256.ShouldBe(CsvSha256);
        source.Description.ShouldBe(CachePath);
    }

    [Fact]
    public async Task Corrupt_cache_is_redownloaded()
    {
        Directory.CreateDirectory(CacheDir);
        await File.WriteAllTextAsync(CachePath, "bozuk içerik");
        var http = Serve(CsvBytes);

        var source = await NewProvider(http).GetAsync();

        http.Requests.Count.ShouldBe(1);
        source.FromCache.ShouldBeFalse();
        (await File.ReadAllBytesAsync(CachePath)).ShouldBe(CsvBytes);
    }

    [Fact]
    public async Task Hash_mismatch_throws_deletes_temp_and_leaves_no_cache()
    {
        var http = Serve(CsvBytes);

        var ex = await Should.ThrowAsync<SchoolSeedSourceException>(() =>
            NewProvider(http, expectedSha: new string('0', 64)).GetAsync());

        ex.Message.ShouldContain("SHA-256");
        ex.Message.ShouldContain(CsvSha256);
        File.Exists(CachePath).ShouldBeFalse();
        File.Exists(CachePath + ".download").ShouldBeFalse();
    }

    [Fact]
    public async Task Http_error_status_throws_clear_message()
    {
        var http = Serve(Array.Empty<byte>(), HttpStatusCode.NotFound);

        var ex = await Should.ThrowAsync<SchoolSeedSourceException>(() => NewProvider(http).GetAsync());

        ex.Message.ShouldContain("404");
        ex.Message.ShouldContain(Url);
        File.Exists(CachePath).ShouldBeFalse();
        File.Exists(CachePath + ".download").ShouldBeFalse();
    }

    [Fact]
    public async Task Network_failure_is_wrapped()
    {
        var http = new StubHttp(_ => throw new HttpRequestException("bağlantı reddedildi"));

        var ex = await Should.ThrowAsync<SchoolSeedSourceException>(() => NewProvider(http).GetAsync());

        ex.Message.ShouldContain("bağlantı reddedildi");
        ex.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Oversized_body_is_cut_off_and_discarded()
    {
        // Content-Length bildirmeyen, sınırın üstünde akan gövde: sayaçlı kopya kesmeli.
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new EndlessZeroStream(GitHubSchoolSeedSourceProvider.MaxDownloadBytes + 1024))
        });

        var ex = await Should.ThrowAsync<SchoolSeedSourceException>(() => NewProvider(http).GetAsync());

        ex.Message.ShouldContain("üst sınır");
        File.Exists(CachePath).ShouldBeFalse();
        File.Exists(CachePath + ".download").ShouldBeFalse();
    }

    [Fact]
    public async Task Declared_oversized_content_length_is_rejected_before_reading()
    {
        var http = new StubHttp(_ =>
        {
            var content = new ByteArrayContent(CsvBytes);
            content.Headers.ContentLength = GitHubSchoolSeedSourceProvider.MaxDownloadBytes + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var ex = await Should.ThrowAsync<SchoolSeedSourceException>(() => NewProvider(http).GetAsync());

        ex.Message.ShouldContain("Content-Length");
        File.Exists(CachePath).ShouldBeFalse();
    }

    [Fact]
    public void Production_constants_are_pinned()
    {
        GitHubSchoolSeedSourceProvider.SourceUrl.ShouldStartWith("https://raw.githubusercontent.com/dalgali/MEB-okul-listesi/");
        GitHubSchoolSeedSourceProvider.SourceUrl.ShouldContain(GitHubSchoolSeedSourceProvider.SourceCommitSha);
        GitHubSchoolSeedSourceProvider.SourceUrl.ShouldNotContain("meb.gov.tr");
        GitHubSchoolSeedSourceProvider.SourceCommitSha.Length.ShouldBe(40);
        GitHubSchoolSeedSourceProvider.ExpectedSha256.Length.ShouldBe(64);
        GitHubSchoolSeedSourceProvider.CacheFileName.ShouldContain(GitHubSchoolSeedSourceProvider.SourceCommitSha[..12]);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* en iyi çaba */ }
    }

    /// <summary>Belirtilen uzunlukta sıfır akıtan, Length bildirmeyen akış.</summary>
    private sealed class EndlessZeroStream(long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _position);
            if (n <= 0) return 0;
            Array.Clear(buffer, offset, n);
            _position += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
