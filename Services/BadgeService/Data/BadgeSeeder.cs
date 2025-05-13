using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Data;

public class BadgeSeeder
{
    public static async Task SeedAsync(BadgeDbContext context)
    {
        if (await context.BadgeDefinitions.AnyAsync())
            return;

        var badges = new List<BadgeDefinition>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "İlk Cevap",
                Description = "İlk cevabını verdin!",
                Category = "Çözüm",
                RuleType = "AnswerCount",
                RuleConfigJson = JsonSerializer.Serialize(new { count = 1 })
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "5 Doğru Üst Üste",
                Description = "5 doğru cevap arka arkaya verdin.",
                Category = "Performans",
                RuleType = "CorrectStreak",
                RuleConfigJson = JsonSerializer.Serialize(new { streak = 5 })
            }
        };

        await context.BadgeDefinitions.AddRangeAsync(badges);
        await context.SaveChangesAsync();
    }
}
