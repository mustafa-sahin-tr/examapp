using System.Text.Json;
using ExamApp.Api.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #365 (S1): bucket'lar artık bucket geneli anonim <c>s3:GetObject *</c> almıyor. Varsayılan bucket'ta yalnız
/// görsel prefix'leri (questions/answers/passages) anonim okunur; <c>question-transfer/*</c> asla; student-avatars
/// tamamen özel. Bootstrapper idempotent ve MinIO kesintisinde API'yi düşürmeden yeniden dener.
/// </summary>
public class MinioBucketBootstrapperTests
{
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private MinioBucketBootstrapper NewBootstrapper(string? defaultBucket = "exam-questions", TimeProvider? clock = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MinioConfig:BucketName"] = defaultBucket })
            .Build();
        return new MinioBucketBootstrapper(_minio, config, clock ?? TimeProvider.System,
            NullLogger<MinioBucketBootstrapper>.Instance);
    }

    private static (string[] Principal, string[] Actions, string[] Resources) Parse(string policyJson)
    {
        using var doc = JsonDocument.Parse(policyJson);
        doc.RootElement.GetProperty("Version").GetString().ShouldBe("2012-10-17");
        var statements = doc.RootElement.GetProperty("Statement").EnumerateArray().ToList();
        statements.Count.ShouldBe(1);
        var st = statements[0];
        st.GetProperty("Effect").GetString().ShouldBe("Allow");
        static string[] Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();
        return (Strings(st.GetProperty("Principal").GetProperty("AWS")), Strings(st.GetProperty("Action")),
            Strings(st.GetProperty("Resource")));
    }

    private static MinioBucketSpec Spec(string name) =>
        MinioBucketPolicies.KnownBuckets("exam-questions").Single(b => b.Name == name);

    [Fact]
    public void Known_buckets_cover_every_bucket_the_api_writes_to()
    {
        MinioBucketPolicies.KnownBuckets("exam-questions").Select(b => b.Name).Order()
            .ShouldBe(new[] { "exam-questions", "exams", "student-avatars", "study-pages", "worksheets" });
    }

    [Fact]
    public void Default_bucket_policy_only_exposes_image_prefixes_never_question_transfer()
    {
        var (principal, actions, resources) = Parse(MinioBucketPolicies.BuildAnonymousReadPolicy(Spec("exam-questions"))!);

        principal.ShouldBe(new[] { "*" });
        actions.ShouldBe(new[] { "s3:GetObject" });
        resources.ShouldBe(new[]
        {
            "arn:aws:s3:::exam-questions/questions/*",
            "arn:aws:s3:::exam-questions/answers/*",
            "arn:aws:s3:::exam-questions/passages/*",
        });
        resources.ShouldNotContain("arn:aws:s3:::exam-questions/*");
        resources.ShouldAllBe(r => !r.Contains("question-transfer"));
    }

    [Theory]
    [InlineData("worksheets")]
    [InlineData("exams")]
    [InlineData("study-pages")]
    public void Image_only_buckets_stay_publicly_readable_for_now(string bucket)
    {
        var (_, actions, resources) = Parse(MinioBucketPolicies.BuildAnonymousReadPolicy(Spec(bucket))!);

        actions.ShouldBe(new[] { "s3:GetObject" }); // never s3:ListBucket
        resources.ShouldBe(new[] { $"arn:aws:s3:::{bucket}/*" });
    }

    [Fact]
    public void Student_avatars_bucket_has_no_anonymous_policy()
    {
        MinioBucketPolicies.BuildAnonymousReadPolicy(Spec("student-avatars")).ShouldBeNull();
    }

    [Fact]
    public void Configured_default_bucket_name_gets_the_image_prefix_policy()
    {
        var spec = MinioBucketPolicies.KnownBuckets("custom-bank").Single(b => b.Name == "custom-bank");

        Parse(MinioBucketPolicies.BuildAnonymousReadPolicy(spec)!).Resources
            .ShouldBe(new[]
            {
                "arn:aws:s3:::custom-bank/questions/*",
                "arn:aws:s3:::custom-bank/answers/*",
                "arn:aws:s3:::custom-bank/passages/*",
            });
        MinioBucketPolicies.KnownBuckets("custom-bank").ShouldNotContain(b => b.Name == "exam-questions");
    }

    [Fact]
    public void Wildcards_in_prefixes_are_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            MinioBucketPolicies.BuildAnonymousReadPolicy(new MinioBucketSpec("b", ["questions/*"])));
    }

    [Fact]
    public async Task Ensure_applies_the_same_policy_on_every_run_idempotent()
    {
        var sut = NewBootstrapper();

        (await sut.EnsureBucketsAsync(sut.Buckets)).ShouldBeEmpty();
        var first = _minio.ReceivedCalls().Select(c => (c.GetArguments()[0], c.GetArguments()[1])).ToList();
        _minio.ClearReceivedCalls();
        (await sut.EnsureBucketsAsync(sut.Buckets)).ShouldBeEmpty();
        var second = _minio.ReceivedCalls().Select(c => (c.GetArguments()[0], c.GetArguments()[1])).ToList();

        first.Count.ShouldBe(5);
        second.ShouldBe(first);
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default, default);
        await _minio.Received(1).EnsureBucketAsync("student-avatars", null, Arg.Any<CancellationToken>());
        await _minio.Received(1).EnsureBucketAsync("exam-questions",
            Arg.Is<string>(p => p.Contains("exam-questions/questions/*") && !p.Contains("exam-questions/*\"")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_bucket_is_reported_and_does_not_stop_the_others()
    {
        _minio.EnsureBucketAsync("exam-questions", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("minio down")));
        var sut = NewBootstrapper();

        var failed = await sut.EnsureBucketsAsync(sut.Buckets);

        failed.Select(b => b.Name).ShouldBe(new[] { "exam-questions" });
        await _minio.Received(1).EnsureBucketAsync("worksheets", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _minio.Received(1).EnsureBucketAsync("student-avatars", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Background_run_retries_only_failed_buckets_until_they_succeed()
    {
        var calls = 0;
        var done = new TaskCompletionSource();
        _minio.EnsureBucketAsync("exam-questions", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) < 3)
                    return Task.FromException(new HttpRequestException("minio down"));
                done.TrySetResult();
                return Task.CompletedTask;
            });
        var sut = NewBootstrapper(clock: new ImmediateTimeProvider());

        await sut.StartAsync(CancellationToken.None); // must not throw although MinIO is down
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);

        calls.ShouldBe(3);
        await _minio.Received(1).EnsureBucketAsync("worksheets", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>issue #365 (security L1): bilinmeyen bucket'taki politika uyarılır ama asla değiştirilmez.</summary>
    [Fact]
    public async Task Unknown_bucket_with_a_policy_is_flagged_but_never_modified()
    {
        _minio.ListBucketNamesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { "exam-questions", "legacy-public", "legacy-private" });
        _minio.GetBucketPolicyAsync("legacy-public", Arg.Any<CancellationToken>()).Returns("{\"Statement\":[]}");
        _minio.GetBucketPolicyAsync("legacy-private", Arg.Any<CancellationToken>()).Returns((string?)null);
        var sut = NewBootstrapper();

        var flagged = await sut.WarnAboutUnknownBucketPoliciesAsync();

        flagged.ShouldBe(["legacy-public"]);
        await _minio.DidNotReceive().GetBucketPolicyAsync("exam-questions", Arg.Any<CancellationToken>());
        await _minio.DidNotReceiveWithAnyArgs().EnsureBucketAsync(default!, default, default);
    }

    [Fact]
    public async Task Unknown_bucket_audit_failure_is_swallowed()
    {
        _minio.ListBucketNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<string>>(new HttpRequestException("down")));

        (await NewBootstrapper().WarnAboutUnknownBucketPoliciesAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/img/exam-questions/questions/7/x.jpg", "exam-questions", "questions/7/x.jpg")]
    [InlineData("/img/study-pages/a b/ç.jpg", "study-pages", "a b/ç.jpg")]
    public void MinioObjectUrl_parses_stored_urls(string url, string bucket, string key)
    {
        MinioObjectUrl.TryParse(url, out var b, out var k).ShouldBeTrue();
        b.ShouldBe(bucket);
        k.ShouldBe(key);
        MinioObjectUrl.Build(b, k).ShouldBe(url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("questions/x.jpg")]
    [InlineData("http://host/img/exam-questions/questions/x.jpg")]
    [InlineData("/img/exam-questions")]
    [InlineData("/img/exam-questions/")]
    [InlineData("/img//questions/x.jpg")]
    [InlineData("/img/exam-questions//img/exam-questions/questions/x.jpg")]
    [InlineData("/img/exam-questions/questions/../question-transfer/index.json")]
    [InlineData(@"/img/exam-questions/questions\x.jpg")]
    [InlineData("/img/exam-questions/questions/x.jpg?X-Amz-Signature=1")]
    public void MinioObjectUrl_rejects_malformed_urls(string? url)
    {
        MinioObjectUrl.TryParse(url, out _, out _).ShouldBeFalse();
    }

    /// <summary>Retry gecikmelerini sıfırlar (Task.Delay(TimeSpan, TimeProvider) CreateTimer üzerinden çalışır).</summary>
    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            base.CreateTimer(callback, state, TimeSpan.Zero, period);
    }
}
