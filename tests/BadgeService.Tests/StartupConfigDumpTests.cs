using Microsoft.Extensions.Configuration;

namespace BadgeService.Tests;

/// <summary>
/// #228: BadgeService'in StartupConfigDump kopyası Redis/Postgres/RabbitMQ parolalarını maskelemeli.
/// Kapsamlı biçim testleri ExamApp.Api.Tests'te; kopyaların birebir aynı olduğu da orada doğrulanır.
/// </summary>
public class StartupConfigDumpTests
{
    private const string Secret = "S3cr3t-Pa55";

    [Fact]
    public void Development_dump_masks_redis_postgres_and_rabbitmq_passwords()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:Configuration"] = $"redis:6379,password={Secret}",
                ["ConnectionStrings:DefaultConnection"] = $"Host=db;Username=u;Password={Secret};Database=worksheet",
                ["ConnectionStrings:rabbitmq"] = $"amqp://rabbituser:{Secret}@localhost:5672",
            })
            .Build();

        using var writer = new StringWriter();
        StartupConfigDump.Write(writer, config, "Development");
        var output = writer.ToString();

        output.ShouldNotContain(Secret);
        output.ShouldContain("Redis:Configuration = redis:6379,password=***");
        output.ShouldContain("Password=***;Database=worksheet");
        output.ShouldContain("amqp://rabbituser:***@localhost:5672");
    }
}
