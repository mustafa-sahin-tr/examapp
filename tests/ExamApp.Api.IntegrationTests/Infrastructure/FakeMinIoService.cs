namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>No-op object storage: uploads return a fake URL, reads return nothing.</summary>
public sealed class FakeMinIoService : IMinIoService
{
    public Task<string> UploadFileAsync(Stream fileStream, string fileName, string? bucketName = null, string? contentType = null)
        => Task.FromResult($"http://fake-minio/{bucketName ?? "bucket"}/{fileName}");

    public Task<Stream?> GetFileStreamAsync(string fileUrl) => Task.FromResult<Stream?>(null);

    public Task<bool> DeleteFileByUrlAsync(string fileUrl) => Task.FromResult(true);
}
