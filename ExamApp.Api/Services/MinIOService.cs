using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using System;
using System.IO;
using System.Threading.Tasks;

public class MinIoService
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

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName)
    {
        try
        {
            // Bucket varsa oluşturma, yoksa oluştur
            bool found = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucketName));
            if (!found)
            {
                await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));
            }

            // Dosyayı MinIO'ya yükle
            var respo = await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType("image/jpeg"));

            return $"{_baseUrl}/{_bucketName}/{fileName}";
        }
        catch (MinioException e)
        {
            Console.WriteLine($"[MinIO Error]: {e.Message}");
            throw;
        }
    }
}
