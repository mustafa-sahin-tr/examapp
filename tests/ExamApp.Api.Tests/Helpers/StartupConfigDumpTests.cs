using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// #228: Development başlangıç dump'ı Redis/Postgres/RabbitMQ parolalarını düz yazmamalı.
/// </summary>
public class StartupConfigDumpTests
{
    private const string Secret = "S3cr3t-Pa55";

    [Theory]
    // StackExchange.Redis: ',' ayraçlı
    [InlineData("redis:6379,password=S3cr3t-Pa55", "redis:6379,password=***")]
    [InlineData("redis:6379,password=S3cr3t-Pa55,ssl=False", "redis:6379,password=***,ssl=False")]
    [InlineData("localhost:6379,PASSWORD = S3cr3t-Pa55,abortConnect=false", "localhost:6379,PASSWORD =***,abortConnect=false")]
    [InlineData("redis:6379,password=S3cr3t;Pa55,ssl=true", "redis:6379,password=***,ssl=true")]
    // Npgsql / ADO: ';' ayraçlı, parola ',' içerebilir, tırnaklı olabilir
    [InlineData("Host=db;Port=5432;Username=u;Password=S3cr3t-Pa55;Database=w", "Host=db;Port=5432;Username=u;Password=***;Database=w")]
    [InlineData("Host=db;Password=S3cr,et;Database=w", "Host=db;Password=***;Database=w")]
    [InlineData("Host=db;pwd=S3cr3t-Pa55", "Host=db;pwd=***")]
    [InlineData("Password=S3cr3t-Pa55;Host=db", "Password=***;Host=db")]
    [InlineData("Host=db;Password=\"S3c;r3t\"\"x\";Database=w", "Host=db;Password=***;Database=w")]
    // RabbitMQ / URI biçimi (Aspire ConnectionStrings:rabbitmq)
    [InlineData("amqp://rabbituser:S3cr3t-Pa55@localhost:5672", "amqp://rabbituser:***@localhost:5672")]
    [InlineData("amqps://u:S3cr@t-Pa55@mq.example.com:5671/vhost", "amqps://u:***@mq.example.com:5671/vhost")]
    [InlineData("redis://:S3cr3t-Pa55@redis:6379/0", "redis://:***@redis:6379/0")]
    // Önekli / boşluklu anahtarlar
    [InlineData("Host=db;SSL Password=S3cr3t-Pa55;Database=w", "Host=db;SSL Password=***;Database=w")]
    [InlineData("Host=db;SslPassword=S3cr3t-Pa55;Database=w", "Host=db;SslPassword=***;Database=w")]
    [InlineData("Server=db;Passwd=S3cr3t-Pa55", "Server=db;Passwd=***")]
    [InlineData("Server=db;Pass=S3cr3t-Pa55", "Server=db;Pass=***")]
    // MinIO / Azure biçimleri (AccessKey, SecretKey, AccountKey, SharedAccessKey)
    [InlineData("Endpoint=minio:9000;AccessKey=S3cr3t-Pa55;SecretKey=S3cr3t-Pa55;Bucket=b", "Endpoint=minio:9000;AccessKey=***;SecretKey=***;Bucket=b")]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=acc;AccountKey=S3cr3t-Pa55==;EndpointSuffix=core.windows.net", "DefaultEndpointsProtocol=https;AccountName=acc;AccountKey=***;EndpointSuffix=core.windows.net")]
    [InlineData("Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=S3cr3t-Pa55=", "Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=***")]
    [InlineData("Endpoint=https://x;ApiKey=S3cr3t-Pa55", "Endpoint=https://x;ApiKey=***")]
    [InlineData("Host=db;client_secret=S3cr3t-Pa55;Token=S3cr3t-Pa55", "Host=db;client_secret=***;Token=***")]
    // Query-string ('&' / '#' terminatör)
    [InlineData("https://host/cb?user=u&password=S3cr3t-Pa55&x=1", "https://host/cb?user=u&password=***&x=1")]
    [InlineData("https://host/cb?token=S3cr3t-Pa55#frag", "https://host/cb?token=***#frag")]
    [InlineData("postgres://h/db?sslmode=require&password=S3cr3t,Pa55;x&a=1", "postgres://h/db?sslmode=require&password=***&a=1")]
    // Çok satırlı (satır sonu ayraç)
    [InlineData("Host=db\nPassword=S3cr3t-Pa55\nPort=5432", "Host=db\nPassword=***\nPort=5432")]
    [InlineData("redis:6379,password=S3cr3t-Pa55\r\nabortConnect=false", "redis:6379,password=***\r\nabortConnect=false")]
    [InlineData("Host=db;Password=S3cr3t-Pa55\nPort=5432", "Host=db;Password=***\nPort=5432")]
    public void RedactSecrets_masks_password_in_connection_string_formats(string input, string expected)
    {
        var result = StartupConfigDump.RedactSecrets(input);

        result.ShouldBe(expected);
        result.ShouldNotContain("S3cr");
        result.ShouldNotContain("Pa55");
    }

    [Theory]
    [InlineData("redis:6379")]
    [InlineData("Host=db;Port=5432;Database=w")]
    [InlineData("http://localhost:8081/realms/exam")]
    [InlineData("http://user@host/path")]
    [InlineData("")]
    // Yanlış pozitif kontrolü: masum anahtarlar maskelenmemeli
    [InlineData("Host=db;Database=exam;Keepalive=30;Passfile=/x/.pgpass;Username=u;Include Error Detail=true;Keysize=2")]
    [InlineData("redis:6379,abortConnect=false,keepAlive=60,ssl=false,user=u")]
    [InlineData("https://host/list?page=2&sort=name&keyword=abc")]
    [InlineData("Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKeyName=Root")]
    public void RedactSecrets_leaves_values_without_secrets_untouched(string input)
        => StartupConfigDump.RedactSecrets(input).ShouldBe(input);

    /// <summary>
    /// Dizenin başındaki parola anahtarı belirsiz (Npgsql mi Redis mi?): değerin geri kalanında ','
    /// varsa satır sonuna kadar her şey maskelenir; yalnızca ';' varsa ';'e kadar.
    /// </summary>
    [Theory]
    [InlineData("Password=a,b3", "Password=***")]
    [InlineData("password=a;b,redis:6379", "password=***")]
    [InlineData("password=a,redis:6379,ssl=true", "password=***")]
    [InlineData("Password=a;Host=db;Database=w", "Password=***;Host=db;Database=w")]
    [InlineData("Password=abc", "Password=***")]
    [InlineData("Password=a,b3\r\nHost=db", "Password=***\r\nHost=db")]
    [InlineData("Password=\"a,b;c\";Host=db", "Password=***;Host=db")]
    public void RedactSecrets_leading_key_is_masked_conservatively(string input, string expected)
        => StartupConfigDump.RedactSecrets(input).ShouldBe(expected);

    [Theory]
    [InlineData("password", true)]
    [InlineData("SSL Password", true)]
    [InlineData("SslPassword", true)]
    [InlineData("PWD", true)]
    [InlineData("psw", true)]
    [InlineData("passwd", true)]
    [InlineData("pass", true)]
    [InlineData("AccessKey", true)]
    [InlineData("SecretKey", true)]
    [InlineData("AccountKey", true)]
    [InlineData("SharedAccessKey", true)]
    [InlineData("api_key", true)]
    [InlineData("client_secret", true)]
    [InlineData("access_token", true)]
    [InlineData("Database", false)]
    [InlineData("Keepalive", false)]
    [InlineData("Passfile", false)]
    [InlineData("Username", false)]
    [InlineData("SharedAccessKeyName", false)]
    [InlineData("Host", false)]
    [InlineData("", false)]
    public void IsSensitiveKeyName_classifies_keys(string key, bool expected)
        => StartupConfigDump.IsSensitiveKeyName(key).ShouldBe(expected);

    [Theory]
    [InlineData("RabbitMQ:Password")]
    [InlineData("Keycloak:ClientSecret")]
    [InlineData("MinioConfig:SecretKey")]
    [InlineData("Some:ApiKey")]
    [InlineData("Some:Key")]
    [InlineData("MinioConfig:AccessKey")]
    [InlineData("Storage:AccountKey")]
    public void Sanitize_masks_sensitive_keys_entirely(string key)
        => StartupConfigDump.Sanitize(key, Secret).ShouldBe("***");

    [Fact]
    public void Development_dump_masks_redis_postgres_and_rabbitmq_passwords()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:Configuration"] = $"redis:6379,password={Secret}",
                ["Redis:InstanceName"] = "exam:",
                ["ConnectionStrings:DefaultConnection"] = $"Host=db;Username=examuser;Password={Secret};Database=worksheet",
                ["ConnectionStrings:redis"] = $"localhost:6380,password={Secret}",
                ["ConnectionStrings:rabbitmq"] = $"amqp://rabbituser:{Secret}@localhost:5672",
                ["RabbitMQ:Host"] = "rabbitmq",
                ["RabbitMQ:Username"] = "rabbituser",
                ["RabbitMQ:Password"] = Secret,
            })
            .Build();

        using var writer = new StringWriter();
        StartupConfigDump.Write(writer, config, "Development", 5079);
        var output = writer.ToString();

        output.ShouldNotContain(Secret);
        output.ShouldContain("Redis:Configuration = redis:6379,password=***");
        output.ShouldContain("ConnectionStrings:DefaultConnection = Host=db;Username=examuser;Password=***;Database=worksheet");
        output.ShouldContain("ConnectionStrings:rabbitmq = amqp://rabbituser:***@localhost:5672");
        output.ShouldContain("RabbitMQ:Password = ***");
        output.ShouldContain("RabbitMQ:Host = rabbitmq");
    }

    /// <summary>
    /// StartupConfigDump beş serviste kopya (Gateway ve finance-api ExamApp.Foundation'ı referans etmiyor).
    /// Redaksiyon bloğu kopyalar arasında sapmasın diye birebir karşılaştırılır.
    /// </summary>
    [Fact]
    public void Redaction_block_is_identical_in_all_service_copies()
    {
        var root = GetRepoRoot();
        string[] copies =
        {
            "api/ExamApp.Api/StartupConfigDump.cs",
            "auth-api/StartupConfigDump.cs",
            "Services/BadgeService/StartupConfigDump.cs",
            "Services/Gateway/StartupConfigDump.cs",
            "finance-api/finance-api/StartupConfigDump.cs",
        };

        var reference = ExtractRedactionBlock(Path.Combine(root, copies[0]));
        foreach (var copy in copies.Skip(1))
        {
            ExtractRedactionBlock(Path.Combine(root, copy))
                .ShouldBe(reference, $"{copy} redaksiyon bloğu api/ExamApp.Api kopyasından farklı");
        }
    }

    private static string ExtractRedactionBlock(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var start = text.IndexOf("// REDACTION-BEGIN", StringComparison.Ordinal);
        var end = text.IndexOf("// REDACTION-END", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"{path}: REDACTION-BEGIN işareti yok");
        end.ShouldBeGreaterThan(start, $"{path}: REDACTION-END işareti yok");
        return text[start..end];
    }

    // Derleme anındaki kaynak yolu: tests/ExamApp.Api.Tests/Helpers/<bu dosya> → üç seviye yukarısı repo kökü.
    private static string GetRepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
