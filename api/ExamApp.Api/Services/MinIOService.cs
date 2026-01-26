using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using System;
using System.IO;
using System.Threading.Tasks;

public interface IMinIoService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string bucketName = null, string contentType = null);

    Task<Stream?> GetFileStreamAsync(string fileUrl);
}

public class MinIoService : IMinIoService
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly string _baseUrl;
    public MinIoService(IConfiguration configuration)
    {
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
            var objectName = GetObjectNameFromUrl(fileUrl);

            var memoryStream = new MemoryStream();
            await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream)));
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (MinioException e)
        {
            Console.WriteLine($"[MinIO Error]: {e.Message}");
            return null;
        }
    }

    // Helper method to extract object name from file URL
    private string GetObjectNameFromUrl(string fileUrl)
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
        var objectName = string.Join('/', parts, 2, parts.Length - 2);
        return objectName;
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
            if (!found)
            {
                await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
            }

            // Dosyayı MinIO'ya yükle
            var respo = await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType));

            return $"/img/{bucketName}/{fileName}";
        }
        catch (MinioException e)
        {
            Console.WriteLine($"[MinIO Error]: {e.Message}");
            throw;
        }
    }
}
