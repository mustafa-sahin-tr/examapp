using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ExamApp.Api.Services.Storage;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #402 (O1): ayrı GetObject-only MinIO presign hesabı — açılış guard'ı ve presign politikasının
/// (<c>deploy/scripts/minio-presign-init.sh</c> + k8s ConfigMap kopyası) <see cref="StorageAreaPolicy"/> allowlist'iyle
/// birebir aynı kalması (drift).
/// </summary>
public class MinioPresignAccountTests
{
    private static IConfigurationSection Section(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>("MinioConfig:" + v.Key, v.Value)))
            .Build()
            .GetSection("MinioConfig");

    [Fact]
    public void Guard_is_a_no_op_in_development_and_when_signing_is_disabled()
    {
        Should.NotThrow(() => MinioPresignCredentialGuard.EnsureConfigured(true, Section()));
        Should.NotThrow(() => MinioPresignCredentialGuard.EnsureConfigured(false, Section(("PresignImageUrls", "false"))));
    }

    [Fact]
    public void Guard_accepts_a_dedicated_account_outside_development()
    {
        Should.NotThrow(() => MinioPresignCredentialGuard.EnsureConfigured(false, Section(
            ("AccessKey", "root"), ("PresignAccessKey", "exam-presign"), ("PresignSecretKey", "a-real-secret-value"))));
    }

    [Theory]
    [InlineData(null, "a-real-secret-value", "PresignAccessKey must be set")]
    [InlineData("exam-presign", null, "PresignSecretKey must be set")]
    [InlineData("exam-presign", "devOnlyMinioPresignSecretChangeMe123", "PresignSecretKey must be set")]
    [InlineData("root", "a-real-secret-value", "must not be the MinIO root access key")]
    public void Guard_fails_fast_outside_development(string? accessKey, string? secretKey, string expected)
    {
        var ex = Should.Throw<InvalidOperationException>(() => MinioPresignCredentialGuard.EnsureConfigured(false, Section(
            ("AccessKey", "root"), ("PresignAccessKey", accessKey), ("PresignSecretKey", secretKey))));
        ex.Message.ShouldContain(expected);
        if (secretKey != null)
            ex.Message.ShouldNotContain(secretKey); // secret değeri mesaja sızmaz
    }

    private static readonly Regex Arn = new(@"arn:aws:s3:::([^""\s]+)", RegexOptions.Compiled);

    private static HashSet<string> ExpectedResources()
    {
        var policy = new StorageAreaPolicy(MinioBucketPolicies.DefaultBucket);
        return policy.AllowedPrefixes().Select(r => $"{r.Bucket}/{r.Prefix}*").ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ResourcesIn(string text) =>
        Arn.Matches(text).Select(m => m.Groups[1].Value.Replace("${QB}", MinioBucketPolicies.DefaultBucket))
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Presign_policy_in_init_script_matches_the_signing_allowlist()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "deploy", "scripts", "minio-presign-init.sh"));
        ResourcesIn(script).ShouldBe(ExpectedResources(), ignoreOrder: true);
        script.ShouldContain("\"Action\": [\"s3:GetObject\"]");
        ResourcesIn(script).ShouldNotContain(r => r.Contains("question-transfer"));
    }

    [Fact]
    public void K8s_configmap_script_is_identical_to_the_init_script()
    {
        // Code review #402: yalnız politika değil, betiğin TAMAMI aynı olmalı (k8s kopyası elle güncelleniyor).
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), "deploy", "gcp", "k8s", "stateful-services.yaml")).Replace("\r\n", "\n");
        const string header = "  minio-presign-init.sh: |\n";
        var start = yaml.IndexOf(header, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, "k8s minio-presign-init ConfigMap bulunamadı");
        start += header.Length;
        var end = yaml.IndexOf("\n---", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);

        var embedded = string.Join("\n", yaml[start..end].Split('\n').Select(StripBlockIndent));
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "deploy", "scripts", "minio-presign-init.sh")).Replace("\r\n", "\n");

        embedded.TrimEnd('\n').ShouldBe(script.TrimEnd('\n'));
        ResourcesIn(embedded).ShouldBe(ExpectedResources(), ignoreOrder: true);
    }

    private static string StripBlockIndent(string line)
    {
        if (line.Length == 0)
            return line;
        line.ShouldStartWith("    ", customMessage: $"ConfigMap betiğinde girintisiz satır: '{line}'");
        return line[4..];
    }

    // Derleme anındaki kaynak yolu: tests/ExamApp.Api.Tests/Services/<bu dosya> → üç seviye yukarısı repo kökü.
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
