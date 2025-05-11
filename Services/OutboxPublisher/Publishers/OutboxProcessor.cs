using MassTransit;
using Microsoft.EntityFrameworkCore;
using OutboxPublisherService.Data;
using System.Text.Json;

namespace OutboxPublisherService.Publishers;

public class OutboxProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;

    public OutboxProcessor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

            var messages = await db.OutboxMessages
                .Where(x => x.ProcessedAt == null)
                .OrderBy(x => x.CreatedAt)
                .Take(10)
                .ToListAsync(stoppingToken);

            foreach (var message in messages)
            {
                var eventType = Type.GetType(message.Type);
                if (eventType == null) continue;

                var @event = JsonSerializer.Deserialize(message.Content, eventType);
                if (@event == null) continue;

                await publisher.Publish(@event, eventType, stoppingToken);

                message.ProcessedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(stoppingToken);
            await Task.Delay(5000, stoppingToken); // 5 saniyede bir tarar
        }
    }
}
