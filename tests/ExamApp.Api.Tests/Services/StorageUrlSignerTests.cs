using ExamApp.Api.Services.Storage;
using ExamApp.Api.Tests.Support;
using ExamApp.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Minio;
using Minio.DataModel.Args;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #365 (S2): <see cref="MinioStorageUrlSigner"/> — yalnız allowlist'teki nesneleri imzalar
/// (<c>question-transfer/*</c> asla), imza gateway'in MinIO downstream host'u için hesaplanır, aynı 15 dk dilimi içinde
/// aynı URL'yi verir (tarayıcı önbelleği), 4 saat geçerlidir ve ağ çağrısı yapmaz.
/// </summary>
public class StorageUrlSignerTests : IDisposable
{
    private const string TestSk = "unit-test-sk-0001";
    private const string TestAk = "unit-test-ak";
    private const string SignHost = "minio:9000";
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 7, 31, TimeSpan.Zero); // dilim: 12:00
    private readonly FixedTimeProvider _clock = new(T0);
    private readonly List<MinioStorageUrlSigner> _created = [];

    private static readonly StorageArea[] Question = [StorageArea.QuestionImage];
    private static readonly StorageArea[] Cover = [StorageArea.WorksheetCover];
    private static readonly StorageArea[] Study = [StorageArea.StudyPage];
    private static readonly StorageArea[] All = [StorageArea.QuestionImage, StorageArea.WorksheetCover, StorageArea.StudyPage];

    private MinioStorageUrlSigner NewSigner(string? presignEndpoint = null, string? ak = TestAk,
        string? sk = TestSk, string endpoint = SignHost, string bucket = "exam-questions")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MinioConfig:Endpoint"] = endpoint,
            ["MinioConfig:PresignEndpoint"] = presignEndpoint,
            ["MinioConfig:PresignAccessKey"] = ak,
            ["MinioConfig:PresignSecretKey"] = sk,
            ["MinioConfig:BucketName"] = bucket,
        }).Build();
        var signer = new MinioStorageUrlSigner(config, new StorageAreaPolicy(config), _clock,
            NullLogger<MinioStorageUrlSigner>.Instance);
        _created.Add(signer);
        return signer;
    }

    public void Dispose()
    {
        foreach (var s in _created) s.Dispose();
    }

    private static void ShouldBeSigned(string? url, string expectedPathPrefix)
    {
        url.ShouldNotBeNull();
        url.ShouldStartWith(expectedPathPrefix);
        url.ShouldContain("X-Amz-Signature=");
    }

    [Theory]
    [InlineData("/img/exam-questions/questions/1/abc.jpg", StorageArea.QuestionImage)]
    [InlineData("/img/exam-questions/answers/7/def.jpg", StorageArea.QuestionImage)]
    [InlineData("/img/exam-questions/passages/p.jpg", StorageArea.QuestionImage)]
    [InlineData("/img/worksheets/12-background.png", StorageArea.WorksheetCover)]
    [InlineData("/img/exams/2f1c.jpg", StorageArea.WorksheetCover)]
    [InlineData("/img/study-pages/books/b1/page_1.webp", StorageArea.StudyPage)]
    [InlineData("/img/study-pages/pages/5/x.png", StorageArea.StudyPage)]
    public void Allowlisted_object_is_signed_as_a_relative_img_url_valid_for_the_gateway_host(string stored, StorageArea area)
    {
        var signed = NewSigner().SignForBrowser(stored, [area]);

        ShouldBeSigned(signed, stored + "?"); // ASCII anahtar → yol aynen korunur
        var result = SigV4PresignVerifier.VerifyImgUrl(signed!, SignHost, TestSk, T0.UtcDateTime);
        result.Valid.ShouldBeTrue(result.Reason);
        signed!.ShouldContain("X-Amz-Credential=" + TestAk + "%2F20261006%2Fus-east-1%2Fs3%2Faws4_request");
        signed.ShouldContain("response-cache-control=private%2C%20max-age%3D900");
    }

    [Theory]
    [InlineData("/img/exam-questions/question-transfer/exports/default/index.json")]
    [InlineData("/img/exam-questions/question-transfer/exports/default/bundle-0001.zip")]
    [InlineData("/img/exam-questions/Question-Transfer/exports/default/index.json")]
    [InlineData("/img/worksheets/question-transfer/exports/default/index.json")]
    [InlineData("/img/study-pages/question-transfer/x.json")]
    public void Question_transfer_objects_are_never_signed_for_any_area(string stored)
    {
        NewSigner().SignForBrowser(stored, All).ShouldBe(stored);
    }

    [Fact]
    public void Question_transfer_is_never_signed_even_when_the_default_bucket_is_whole_bucket_allowlisted()
    {
        // Varsayılan bucket "worksheets" yapılandırılsa bile (WorksheetCover = bucket'ın tamamı) paket imzalanmaz.
        var signer = NewSigner(bucket: "worksheets");
        signer.SignForBrowser("/img/worksheets/question-transfer/exports/default/index.json", All)
            .ShouldBe("/img/worksheets/question-transfer/exports/default/index.json");
        ShouldBeSigned(signer.SignForBrowser("/img/worksheets/9-background.png", Cover), "/img/worksheets/9-background.png?");
    }

    [Theory]
    [InlineData("/img/exam-questions/questions/1/a.jpg", StorageArea.WorksheetCover)] // doğru nesne, yanlış alan
    [InlineData("/img/worksheets/1-background.png", StorageArea.QuestionImage)]
    [InlineData("/img/exam-questions/other/x.jpg", StorageArea.QuestionImage)]      // prefix dışı
    [InlineData("/img/exam-questions/img/exam-questions/questions/x.jpg", StorageArea.QuestionImage)]
    [InlineData("/img/student-avatars/1.png", StorageArea.WorksheetCover)]          // özel bucket
    [InlineData("/img/study-pages/other/x.webp", StorageArea.StudyPage)]
    [InlineData("/img/evil-bucket/questions/x.jpg", StorageArea.QuestionImage)]
    public void Object_outside_the_area_allowlist_is_emitted_unchanged(string stored, StorageArea area)
    {
        NewSigner().SignForBrowser(stored, [area]).ShouldBe(stored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://cdn.example.com/a.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("/img/exam-questions/questions/../question-transfer/index.json")]
    [InlineData("/img/exam-questions/questions/a.jpg?X-Amz-Signature=deadbeef")]
    [InlineData("/assets/badges/gold.png")]
    public void Non_storage_values_are_emitted_unchanged(string? stored)
    {
        NewSigner().SignForBrowser(stored, All).ShouldBe(stored);
    }

    [Theory]
    [InlineData("/img/study-pages/books/A&B/page_1.webp")]
    [InlineData("/img/study-pages/books/100%41/page_1.webp")]
    [InlineData("/img/study-pages/books/yüzde%/page_1.webp")]
    public void Keys_the_gateway_cannot_forward_signature_safe_are_emitted_unchanged(string stored)
    {
        NewSigner().SignForBrowser(stored, Study).ShouldBe(stored);
    }

    [Fact]
    public void Turkish_and_space_keys_are_signed_with_an_encoded_path()
    {
        const string stored = "/img/study-pages/books/Matematik Kitabı 5. Sınıf/page_1.webp";
        var signed = NewSigner().SignForBrowser(stored, Study);

        ShouldBeSigned(signed, "/img/study-pages/books/Matematik%20Kitab%C4%B1%205.%20S%C4%B1n%C4%B1f/page_1.webp?");
        SigV4PresignVerifier.VerifyImgUrl(signed!, SignHost, TestSk, T0.UtcDateTime).Valid.ShouldBeTrue();
    }

    [Fact]
    public void Same_object_gets_the_same_url_within_a_15_minute_slice_and_a_new_one_after()
    {
        var signer = NewSigner();
        const string stored = "/img/exam-questions/questions/1/a.jpg";

        var first = signer.SignForBrowser(stored, Question);
        _clock.Now = new DateTimeOffset(2026, 10, 6, 12, 14, 59, TimeSpan.Zero);
        signer.SignForBrowser(stored, Question).ShouldBe(first);

        // Önbellekten bağımsız: yeni bir imzalayıcı da aynı dilimde birebir aynı URL'yi üretir (deterministik).
        NewSigner().SignForBrowser(stored, Question).ShouldBe(first);

        _clock.Now = new DateTimeOffset(2026, 10, 6, 12, 15, 0, TimeSpan.Zero);
        var next = signer.SignForBrowser(stored, Question);
        next.ShouldNotBe(first);
        next!.ShouldContain("X-Amz-Date=20261006T121500Z");
    }

    [Fact]
    public void Signing_time_is_the_slice_start_and_url_is_valid_for_4_hours()
    {
        var signed = NewSigner().SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question)!;

        signed.ShouldContain("X-Amz-Date=20261006T120000Z");
        signed.ShouldContain("X-Amz-Expires=14400");

        var sliceStart = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var atEndOfSlice = SigV4PresignVerifier.VerifyImgUrl(signed, SignHost, TestSk, sliceStart.AddMinutes(15).AddSeconds(-1));
        atEndOfSlice.Valid.ShouldBeTrue(atEndOfSlice.Reason);
        // Dilimin sonunda verilen URL'nin bile ≥3 sa 45 dk ömrü var: uzun test/açık sayfa görseli kaybetmez.
        (atEndOfSlice.SignedAt + atEndOfSlice.Expires - sliceStart.AddMinutes(15)).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(225));
        // Eski 60 dk sınırının ötesinde hâlâ geçerli; 4 saat sonunda süresi dolmuş.
        SigV4PresignVerifier.VerifyImgUrl(signed, SignHost, TestSk, sliceStart.AddMinutes(90)).Valid.ShouldBeTrue();
        SigV4PresignVerifier.VerifyImgUrl(signed, SignHost, TestSk, sliceStart.AddHours(4).AddSeconds(1))
            .Reason.ShouldBe("expired");
    }

    [Fact]
    public void Signature_is_bound_to_the_presign_endpoint_host_not_the_api_endpoint()
    {
        // Prod: API MinIO'ya "minio:9000" ile gider, gateway ise "exam-minio:9000" ile → imza gateway host'u için.
        var signer = NewSigner(presignEndpoint: "exam-minio:9000", endpoint: "minio:9000");
        var signed = signer.SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question)!;

        SigV4PresignVerifier.VerifyImgUrl(signed, "exam-minio:9000", TestSk, T0.UtcDateTime).Valid.ShouldBeTrue();
        SigV4PresignVerifier.VerifyImgUrl(signed, "minio:9000", TestSk, T0.UtcDateTime).Reason.ShouldBe("SignatureDoesNotMatch");
    }

    [Fact]
    public void Tampered_path_or_query_does_not_verify()
    {
        var signed = NewSigner().SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question)!;

        SigV4PresignVerifier.VerifyImgUrl(signed.Replace("questions/1/a.jpg", "question-transfer/exports/default/index.json"),
            SignHost, TestSk, T0.UtcDateTime).Valid.ShouldBeFalse();
        SigV4PresignVerifier.VerifyImgUrl(signed.Replace("X-Amz-Expires=14400", "X-Amz-Expires=604800"),
            SignHost, TestSk, T0.UtcDateTime).Valid.ShouldBeFalse();
    }

    [Fact]
    public async Task Output_matches_the_minio_sdk_reference_presign_for_the_same_inputs()
    {
        // Doğrulayıcı ↔ SDK tutarlılığı ve imzalayıcının SDK'yı beklenen girdilerle çağırdığı.
        var client = new MinioClient().WithEndpoint(SignHost).WithCredentials(TestAk, TestSk).WithRegion("us-east-1").Build();
        var reference = await client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket("exam-questions").WithObject("questions/1/a.jpg").WithExpiry(14400)
            .WithRequestDate(new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc))
            .WithHeaders(new Dictionary<string, string> { ["response-cache-control"] = "private, max-age=900" }));

        var signed = NewSigner().SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question);
        signed.ShouldBe("/img" + new Uri(reference).PathAndQuery);
        SigV4PresignVerifier.VerifyImgUrl(signed!, SignHost, TestSk, T0.UtcDateTime).Valid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, TestSk)]
    [InlineData(TestAk, "")]
    public void Missing_credentials_disable_signing_without_throwing(string? ak, string sk)
    {
        NewSigner(ak: ak, sk: sk)
            .SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question)
            .ShouldBe("/img/exam-questions/questions/1/a.jpg");
    }

    // issue #402 (O1): imza yalnız presign hesabıyla — root (MinioConfig:AccessKey/SecretKey) tek başına imzalamaz,
    // presign anahtarı root ile aynıysa da imzalanmaz; imzalı URL'de görünen anahtar presign anahtarıdır.
    [Fact]
    public void Root_credentials_alone_never_sign()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MinioConfig:Endpoint"] = SignHost,
            ["MinioConfig:AccessKey"] = "root-ak",
            ["MinioConfig:SecretKey"] = "root-sk-0001",
        }).Build();
        using var signer = new MinioStorageUrlSigner(config, new StorageAreaPolicy(config), _clock,
            NullLogger<MinioStorageUrlSigner>.Instance);

        signer.SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question).ShouldBe("/img/exam-questions/questions/1/a.jpg");
    }

    [Fact]
    public void Presign_key_equal_to_root_key_is_refused()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MinioConfig:Endpoint"] = SignHost,
            ["MinioConfig:AccessKey"] = TestAk,
            ["MinioConfig:SecretKey"] = TestSk,
            ["MinioConfig:PresignAccessKey"] = TestAk,
            ["MinioConfig:PresignSecretKey"] = TestSk,
        }).Build();
        using var signer = new MinioStorageUrlSigner(config, new StorageAreaPolicy(config), _clock,
            NullLogger<MinioStorageUrlSigner>.Instance);

        signer.SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question).ShouldBe("/img/exam-questions/questions/1/a.jpg");
    }

    [Fact]
    public void Signed_url_carries_the_presign_key_not_the_root_key()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MinioConfig:Endpoint"] = SignHost,
            ["MinioConfig:AccessKey"] = "root-ak",
            ["MinioConfig:SecretKey"] = "root-sk-0001",
            ["MinioConfig:PresignAccessKey"] = TestAk,
            ["MinioConfig:PresignSecretKey"] = TestSk,
        }).Build();
        using var signer = new MinioStorageUrlSigner(config, new StorageAreaPolicy(config), _clock,
            NullLogger<MinioStorageUrlSigner>.Instance);

        var signed = signer.SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question);
        signed.ShouldNotBeNull();
        signed.ShouldContain("X-Amz-Credential=" + TestAk + "%2F");
        signed.ShouldNotContain("root-ak");
        SigV4PresignVerifier.VerifyImgUrl(signed!, SignHost, TestSk, T0.UtcDateTime).Valid.ShouldBeTrue();
    }

    [Fact]
    public void Kill_switch_disables_signing_and_emits_values_unchanged()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MinioConfig:Endpoint"] = SignHost,
            ["MinioConfig:PresignAccessKey"] = TestAk,
            ["MinioConfig:PresignSecretKey"] = TestSk,
            ["MinioConfig:PresignImageUrls"] = "false",
        }).Build();
        using var signer = new MinioStorageUrlSigner(config, new StorageAreaPolicy(config), _clock,
            NullLogger<MinioStorageUrlSigner>.Instance);

        signer.SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question).ShouldBe("/img/exam-questions/questions/1/a.jpg");
    }

    [Fact]
    public void Presign_failure_is_cached_per_slice_and_logged_at_most_once_per_slice()
    {
        var logger = new CountingLogger();
        var attempts = 0;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MinioConfig:Endpoint"] = SignHost,
            ["MinioConfig:PresignAccessKey"] = TestAk,
            ["MinioConfig:PresignSecretKey"] = TestSk,
        }).Build();
        using var signer = new MinioStorageUrlSigner(config, new StorageAreaPolicy(config), _clock, logger)
        {
            PresignOverride = (_, _, _) => { attempts++; throw new InvalidOperationException("boom"); },
        };

        // Aynı nesne aynı dilimde: tek deneme (başarısızlık işareti önbellekte), değer imzasız döner.
        for (var i = 0; i < 5; i++)
            signer.SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question).ShouldBe("/img/exam-questions/questions/1/a.jpg");
        attempts.ShouldBe(1);

        // Başka bir nesne aynı dilimde denenir ama yeniden loglanmaz.
        signer.SignForBrowser("/img/exam-questions/questions/2/b.jpg", Question).ShouldBe("/img/exam-questions/questions/2/b.jpg");
        attempts.ShouldBe(2);
        logger.Errors.ShouldBe(1);

        // Sonraki dilim: yeniden denenir ve bir kez daha loglanır.
        _clock.Now = T0.AddMinutes(15);
        signer.SignForBrowser("/img/exam-questions/questions/1/a.jpg", Question).ShouldBe("/img/exam-questions/questions/1/a.jpg");
        attempts.ShouldBe(3);
        logger.Errors.ShouldBe(2);
    }

    private sealed class CountingLogger : Microsoft.Extensions.Logging.ILogger<MinioStorageUrlSigner>
    {
        public int Errors { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Error)
                Errors++;
        }
    }

    [Fact]
    public void Slice_start_rounds_down_to_15_minutes_in_utc()
    {
        MinioStorageUrlSigner.SliceStart(new DateTime(2026, 1, 1, 10, 44, 59, DateTimeKind.Utc))
            .ShouldBe(new DateTime(2026, 1, 1, 10, 30, 0, DateTimeKind.Utc));
        MinioStorageUrlSigner.SliceStart(new DateTime(2026, 1, 1, 10, 45, 0, DateTimeKind.Utc))
            .ShouldBe(new DateTime(2026, 1, 1, 10, 45, 0, DateTimeKind.Utc));
    }
}
