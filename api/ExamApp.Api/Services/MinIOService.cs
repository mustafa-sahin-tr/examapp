using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

public interface IMinIoService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string bucketName = null, string contentType = null);

    Task<Stream?> GetFileStreamAsync(string fileUrl);

    Task<bool> DeleteFileByUrlAsync(string fileUrl);
}

public class MinIoService : IMinIoService
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly string _baseUrl;
    private readonly ILogger<MinIoService> _logger;
    public MinIoService(IConfiguration configuration, ILogger<MinIoService> logger)
    {
        _logger = logger;
        var minioConfig = configuration.GetSection("MinioConfig");
        _bucketName = minioConfig["BucketName"];
        _baseUrl = minioConfig["BaseUrl"];

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
        // Example: "/img/bucketName/objectName" or full URL
        if (string.IsNullOrEmpty(fileUrl))
            throw new ArgumentException("fileUrl cannot be null or empty", nameof(fileUrl));

        // Remove base URL if present
        var url = fileUrl;
        if (!string.IsNullOrEmpty(_baseUrl) && fileUrl.StartsWith(_baseUrl))
        {
            url = fileUrl.Substring(_baseUrl.Length);
        }

        // Remove leading slashes
        url = url.TrimStart('/');

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
                await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
                _logger.LogInformation("[MinIO] Bucket created: {Bucket}", bucketName);

                // Buckets are private by default; images are served via
                // direct/unsigned URLs (e.g. the Ocelot /img/* route), which
                // needs anonymous read access. Equivalent to
                // `mc anonymous set download` on the bucket.
                var publicReadPolicy = $$"""
                {
                  "Version": "2012-10-17",
                  "Statement": [
                    {
                      "Effect": "Allow",
                      "Principal": { "AWS": ["*"] },
                      "Action": ["s3:GetObject"],
                      "Resource": ["arn:aws:s3:::{{bucketName}}/*"]
                    }
                  ]
                }
                """;
                await _minioClient.SetPolicyAsync(new SetPolicyArgs()
                    .WithBucket(bucketName)
                    .WithPolicy(publicReadPolicy));
                _logger.LogInformation("[MinIO] Public read policy applied to bucket: {Bucket}", bucketName);
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
}
