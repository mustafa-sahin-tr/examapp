using System;
using System.Text.Json;
using BadgeService.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Services;

public class BadgeEvaluator
{
    private readonly BadgeDbContext _context;
    private readonly IHubContext<BadgeNotificationHub> _hub;

    public BadgeEvaluator(BadgeDbContext context, IHubContext<BadgeNotificationHub> hub)
    {
        _context = context;
        _hub = hub;
    }

    public async Task EvaluateAnswerSubmittedAsync(int userId, string clientId)
    {
        var allDefinitions = await _context.BadgeDefinitions.ToListAsync();

        foreach (var def in allDefinitions)
        {
            if (def.RuleType == "AnswerCount")
            {
                var config = JsonSerializer.Deserialize<AnswerCountRuleConfig>(def.RuleConfigJson)!;

                var answerCount = await _context.BadgeEarned
                    .Where(x => x.UserId == userId && x.BadgeDefinitionId == def.Id)
                    .CountAsync();

                if (answerCount == 0) // Henüz bu badge alınmamışsa
                {
                    // Burada örnek: dış servisten cevap sayısı alınmalı, şimdilik otomatik verelim
                    var submittedCount = 1;

                    if (submittedCount >= config.Count)
                    {
                        await _context.BadgeEarned.AddAsync(new BadgeEarned
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            BadgeDefinitionId = def.Id,
                            EarnedDate = DateTime.UtcNow
                        });

                        await _context.SaveChangesAsync();

                        await _hub.Clients.User(clientId).SendAsync("BadgeEarned", new
                        {
                            BadgeName = def.Name,
                            Description = def.Description,
                            IconUrl = def.IconUrl
                        });

                        // await _hub.Clients.All.SendAsync("BadgeEarned", new
                        // {
                        //     BadgeName = def.Name,
                        //     Description = def.Description,
                        //     IconUrl = def.IconUrl
                        // });

                    }
                }
            }

            // Diğer RuleType'lar burada genişletilebilir
        }
    }

    private class AnswerCountRuleConfig
    {
        public int Count { get; set; }
    }
}

