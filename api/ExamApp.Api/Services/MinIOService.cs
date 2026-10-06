using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IMinIoService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string bucketName = null, string contentType = null);

    Task<Stream?> GetFileStreamAsync(string fileUrl);

    Task<bool> DeleteFileByUrlAsync(string fileUrl);

    /// <summary>
    /// issue #365: bucket'ı yoksa (politikasız, özel) oluşturur ve anonim okuma politikasını verilen JSON'a eşitler.
    /// <paramref name="anonymousReadPolicyJson"/> null ise bucket'taki politika kaldırılır (tamamen özel).
    /// İdempotenttir; yalnız <see cref="ExamApp.Api.Services.Storage.MinioBucketBootstrapper"/> çağırır.
    /// </summary>
    Task EnsureBucketAsync(string bucketName, string? anonymousReadPolicyJson, CancellationToken ct = default);

    /// <summary>issue #365 (security L1): sunucudaki tüm bucket adları (bilinmeyen bucket'taki politikayı raporlamak için).</summary>
    Task<IReadOnlyList<string>> ListBucketNamesAsync(CancellationToken ct = default);

    /// <summary>issue #365: bucket politikası JSON'u; politika yoksa null.</summary>
    Task<string?> GetBucketPolicyAsync(string bucketName, CancellationToken ct = default);

    /// <summary>
    /// issue #365 (S3): nesne var mı (sunucu tarafı StatObject; bucket'lar özel olduğu için tarayıcı artık yoklayamaz).
    /// Nesne ya da bucket yoksa false; MinIO'ya ulaşılamaması gibi altyapı hataları exception olarak yükselir.
    /// </summary>
    Task<bool> ObjectExistsAsync(string bucketName, string objectName, CancellationToken ct = default);
}

public class MinIoService : IMinIoService
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly ILogger<MinIoService> _logger;
    public MinIoService(IConfiguration configuration, ILogger<MinIoService> logger)
    {
        _logger = logger;
        var minioConfig = configuration.GetSection("MinioConfig");
        _bucketName = minioConfig["BucketName"];

        _minioClient = new MinioClient()
            .WithEndpoint(minioConfig["Endpoint"])
            .WithCredentials(minioConfig["AccessKey"], minioConfig["SecretKey"])
            .Build();
    }

    // write a function to get file from minio
    public async Task<Stream?> GetFileStreamAsync(string fileUrl)
    {
        try
        {
            var (bucketName, objectName) = GetBucketAndObjectNameFromUrl(fileUrl);

            var memoryStream = new MemoryStream();
            await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream)));
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (MinioException e)
        {
            _logger.LogError(e, "[MinIO] operation failed");
            return null;
        }
    }

    private (string BucketName, string ObjectName) GetBucketAndObjectNameFromUrl(string fileUrl)
    {
        // Example: "/img/bucketName/objectName" (UploadFileAsync dönüşü; issue #365: eski MinioConfig:BaseUrl öneki
        // hiçbir zaman saklanmadı, kaldırıldı)
        if (string.IsNullOrEmpty(fileUrl))
            throw new ArgumentException("fileUrl cannot be null or empty", nameof(fileUrl));

        // Remove leading slashes
        var url = fileUrl.TrimStart('/');

        // Find the first slash after bucket name
        var parts = url.Split('/');
        if (parts.Length < 3)
            throw new ArgumentException("Invalid fileUrl format", nameof(fileUrl));

        // parts[0] = "img", parts[1] = bucketName, parts[2..] = objectName
        var bucketName = parts[1];
        var objectName = string.Join('/', parts, 2, parts.Length - 2);
        return (bucketName, objectName);
    }

    public async Task<bool> DeleteFileByUrlAsync(string fileUrl)
    {
        try
        {
            var (bucketName, objectName) = GetBucketAndObjectNameFromUrl(fileUrl);
            await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName));
            return true;
        }
        catch (MinioException e)
        {
            _logger.LogError(e, "[MinIO] operation failed");
            return false;
        }
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string bucketName = null, string contentType = null)
    {
        try
        {
            if (string.IsNullOrEmpty(bucketName))
            {
                bucketName = _bucketName;
            }

            if (string.IsNullOrWhiteSpace(contentType))
            {
                var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
                contentType = ext switch
                {
                    ".zip" => "application/zip",
                    ".json" => "application/json",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => "image/jpeg",
                };
            }

            if (fileStream.CanSeek)
            {
                fileStream.Position = 0;
            }
            // Bucket varsa oluşturma, yoksa oluştur
            bool found = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
            _logger.LogDebug("[MinIO] Bucket {Bucket} exists: {Found}", bucketName, found);
            if (!found)
            {
                // issue #365: bucket özel (politikasız) oluşturulur. Anonim okuma artık bucket geneli değil; bilinen
                // bucket'ların prefix bazlı politikasını MinioBucketBootstrapper açılışta uygular/düzeltir.
                await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
                _logger.LogInformation("[MinIO] Bucket created (private): {Bucket}", bucketName);
            }

            // Dosyayı MinIO'ya yükle
            var respo = await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType));

            _logger.LogInformation("[MinIO] Uploaded {ObjectName} to {Bucket} ({Size} bytes)", respo.ObjectName, bucketName, respo.Size);
            return $"/img/{bucketName}/{fileName}";
        }
        catch (MinioException e)
        {
            _logger.LogError(e, "[MinIO] operation failed");
            throw;
        }
    }

    public async Task EnsureBucketAsync(string bucketName, string? anonymousReadPolicyJson, CancellationToken ct = default)
    {
        var found = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName), ct);
        if (!found)
        {
            await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName), ct);
            _logger.LogInformation("[MinIO] Bucket created (private): {Bucket}", bucketName);
        }

        if (anonymousReadPolicyJson is null)
        {
            try
            {
                await _minioClient.RemovePolicyAsync(new RemovePolicyArgs().WithBucket(bucketName), ct);
            }
            catch (ErrorResponseException e) when (e.Response?.Code == "NoSuchBucketPolicy")
            {
                // zaten politikasız
            }
            _logger.LogInformation("[MinIO] Bucket {Bucket} is private (no anonymous policy).", bucketName);
            return;
        }

        // SetPolicy mevcut politikanın yerine geçer (birleştirmez) — eski bucket geneli politika da böylece düşer.
        await _minioClient.SetPolicyAsync(new SetPolicyArgs().WithBucket(bucketName).WithPolicy(anonymousReadPolicyJson), ct);
        _logger.LogInformation("[MinIO] Anonymous read policy applied to bucket {Bucket}.", bucketName);
    }

    public async Task<IReadOnlyList<string>> ListBucketNamesAsync(CancellationToken ct = default)
    {
        var result = await _minioClient.ListBucketsAsync(ct);
        return result.Buckets.Select(b => b.Name).ToList();
    }

    public async Task<bool> ObjectExistsAsync(string bucketName, string objectName, CancellationToken ct = default)
    {
        try
        {
            await _minioClient.StatObjectAsync(new StatObjectArgs().WithBucket(bucketName).WithObject(objectName), ct);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
        catch (BucketNotFoundException)
        {
            return false;
        }
        catch (ErrorResponseException e) when (e.Response?.Code is "NoSuchKey" or "NoSuchBucket" or "NotFound")
        {
            return false;
        }
    }

    public async Task<string?> GetBucketPolicyAsync(string bucketName, CancellationToken ct = default)
    {
        try
        {
            var policy = await _minioClient.GetPolicyAsync(new GetPolicyArgs().WithBucket(bucketName), ct);
            return string.IsNullOrWhiteSpace(policy) ? null : policy;
        }
        catch (ErrorResponseException e) when (e.Response?.Code == "NoSuchBucketPolicy")
        {
            return null;
        }
    }
}
