using System;
using System.Text.Json;
using ExamApp.Api.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class OutboxPublisher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;

    public OutboxPublisher(IServiceProvider sp)
    {
        _serviceProvider = sp;        
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

            var messages = await context.OutboxMessages
                .Where(x => x.ProcessedAt == null)
                .OrderBy(x => x.CreatedAt)
                .Take(20)
                .ToListAsync();

            foreach (var message in messages)
            {
                var eventType = Type.GetType($"ExamApp.Api.Models.Dtos.Events.{message.Type}");
                if (eventType == null) continue;

                var @event = JsonSerializer.Deserialize(message.Content, eventType);
                if (@event == null) continue;

                await publishEndpoint.Publish(@event, eventType, stoppingToken);

                message.ProcessedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();
            await Task.Delay(3000, stoppingToken);
        }
    }
}

