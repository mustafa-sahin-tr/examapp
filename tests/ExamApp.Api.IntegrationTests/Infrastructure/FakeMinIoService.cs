using System.Collections.Concurrent;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>
/// No-op object storage: uploads return a fake URL, reads return nothing unless a test seeded that exact URL via
/// <see cref="Put"/> (issue #365: export download authorization tests need a real 200 body).
/// </summary>
public sealed class FakeMinIoService : IMinIoService
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public void Put(string fileUrl, byte[] content) => _objects[fileUrl] = content;

    public Task<string> UploadFileAsync(Stream fileStream, string fileName, string? bucketName = null, string? contentType = null)
        => Task.FromResult($"http://fake-minio/{bucketName ?? "bucket"}/{fileName}");

    public Task<Stream?> GetFileStreamAsync(string fileUrl) =>
        Task.FromResult<Stream?>(_objects.TryGetValue(fileUrl, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task<bool> DeleteFileByUrlAsync(string fileUrl) => Task.FromResult(true);

    // issue #365: MinioBucketBootstrapper runs in the test host too; no MinIO here.
    public Task EnsureBucketAsync(string bucketName, string? anonymousReadPolicyJson, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListBucketNamesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> GetBucketPolicyAsync(string bucketName, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    /// <summary>issue #365 (S3): only objects seeded via <see cref="Put"/> (as <c>/img/{bucket}/{key}</c>) exist.</summary>
    public Task<bool> ObjectExistsAsync(string bucketName, string objectName, CancellationToken ct = default)
        => Task.FromResult(_objects.ContainsKey($"/img/{bucketName}/{objectName}"));
}
