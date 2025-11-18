using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BadgeService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Data;

public class BadgeSeeder
{
    public static async Task SeedAsync(BadgeDbContext context)
    {
        var existingBadges = await context.BadgeDefinitions
            .ToDictionaryAsync(x => x.Name, StringComparer.OrdinalIgnoreCase);

        var desiredBadges = new List<BadgeSeed>
        {
            CreateBadge(
                name: "İlk Cevap",
                description: "İlk cevabını verdin!",
                category: "Çözüm",
                ruleType: "AnswerCount",
                config: new { count = 1 },
                iconUrl: "achievements/disabled-dark.0085b3.svg"),
            CreateBadge(
                name: "5 Doğru Üst Üste",
                description: "5 doğru cevap arka arkaya verdin.",
                category: "Performans",
                ruleType: "CorrectStreak",
                config: new { streak = 5 },
                iconUrl: "achievements/disabled-dark.041736.svg")
        };

        var questionMilestones = new (string Name, int Target, string Icon)[]
        {
            ("Soru Avcısı I", 10, "achievements/disabled-dark.0b4480.svg"),
            ("Soru Avcısı II", 50, "achievements/disabled-dark.148553.svg"),
            ("Soru Avcısı III", 100, "achievements/disabled-dark.16380c.svg"),
            ("Soru Avcısı IV", 250, "achievements/disabled-dark.1679e1.svg"),
            ("Soru Avcısı V", 500, "achievements/disabled-dark.1e1b53.svg")
        };

        for (var i = 0; i < questionMilestones.Length; i++)
        {
            var milestone = questionMilestones[i];
            desiredBadges.Add(CreateBadge(
                name: milestone.Name,
                description: $"Toplam {milestone.Target} soru çözdün.",
                category: "Çözüm",
                ruleType: "AnswerCount",
                config: new { target = milestone.Target },
                iconUrl: milestone.Icon,
                pathKey: "question-hunter",
                pathName: "Soru Avcısı Yolu",
                pathOrder: i + 1));
        }

        var correctMilestones = new (string Name, int Target, string Icon)[]
        {
            ("Doğru Yolu Bul I", 10, "achievements/disabled-dark.21b1cf.svg"),
            ("Doğru Yolu Bul II", 50, "achievements/disabled-dark.0085b3.svg"),
            ("Doğru Yolu Bul III", 120, "achievements/disabled-dark.041736.svg")
        };

        for (var i = 0; i < correctMilestones.Length; i++)
        {
            var milestone = correctMilestones[i];
            desiredBadges.Add(CreateBadge(
                name: milestone.Name,
                description: $"Toplam {milestone.Target} doğru cevap verdin.",
                category: "Performans",
                ruleType: "TotalCorrectAnswers",
                config: new { target = milestone.Target },
                iconUrl: milestone.Icon,
                pathKey: "accuracy-journey",
                pathName: "Doğruluk Yolu",
                pathOrder: i + 1));
        }

        var studyTimeMilestones = new (string Name, int Minutes, string Icon, string Description)[]
        {
            ("Hızlı Başlangıç", 5, "achievements/disabled-dark.0085b3.svg", "İlk 5 dakikalık çalışma tamamlandı."),
            ("Show Time", 30, "achievements/disabled-dark.041736.svg", "Toplam 30 dakika çalıştın."),
            ("Bilgi Avcısı", 120, "achievements/disabled-dark.0b4480.svg", "Toplam 2 saat çalıştın."),
            ("Prime Time", 300, "achievements/disabled-dark.148553.svg", "Toplam 5 saat çalıştın."),
            ("Bilge İzleyici", 600, "achievements/disabled-dark.16380c.svg", "Toplam 10 saat çalıştın."),
            ("Zaman Yolcusu", 900, "achievements/disabled-dark.1679e1.svg", "Toplam 15 saat çalıştın."),
            ("Akademik Yolculuk", 1500, "achievements/disabled-dark.1e1b53.svg", "Toplam 25 saat çalıştın."),
            ("Elit Çalışkan", 3000, "achievements/disabled-dark.21b1cf.svg", "Toplam 50 saat çalıştın.")
        };

        for (var i = 0; i < studyTimeMilestones.Length; i++)
        {
            var milestone = studyTimeMilestones[i];
            desiredBadges.Add(CreateBadge(
                name: milestone.Name,
                description: milestone.Description,
                category: "Çalışma Süresi",
                ruleType: "TotalStudyTimeMinutes",
                config: new { targetMinutes = milestone.Minutes },
                iconUrl: milestone.Icon,
                pathKey: "study-time",
                pathName: "Çalışma Süresi Yolu",
                pathOrder: i + 1));
        }

        var subjects = new[] { "Türkçe", "Matematik", "Fen Bilimleri", "Sosyal Bilgiler" };

        foreach (var subject in subjects)
        {
            desiredBadges.Add(CreateBadge(
                name: $"{subject} Ustası",
                description: $"{subject} dersinde 100 soru çözdün.",
                category: "Ders Bazlı",
                ruleType: "SubjectAnswerCount",
                config: new { subjectName = subject, target = 100 },
                iconUrl: "achievements/disabled-dark.0085b3.svg",
                pathKey: $"subject-{NormalizeKey(subject)}-answers",
                pathName: $"{subject} Yolculuğu",
                pathOrder: 1));

            desiredBadges.Add(CreateBadge(
                name: $"{subject} Uzmanı",
                description: $"{subject} dersinde 60 doğru cevap verdin.",
                category: "Ders Bazlı",
                ruleType: "SubjectCorrectCount",
                config: new { subjectName = subject, target = 60 },
                iconUrl: "achievements/disabled-dark.041736.svg",
                pathKey: $"subject-{NormalizeKey(subject)}-answers",
                pathName: $"{subject} Yolculuğu",
                pathOrder: 2));

            desiredBadges.Add(CreateBadge(
                name: $"{subject} Zaman Ustası",
                description: $"{subject} dersinde 5 saat çalıştın.",
                category: "Ders Bazlı",
                ruleType: "SubjectStudyTimeMinutes",
                config: new { subjectName = subject, targetMinutes = 300 },
                iconUrl: "achievements/disabled-dark.0b4480.svg",
                pathKey: $"subject-{NormalizeKey(subject)}-answers",
                pathName: $"{subject} Yolculuğu",
                pathOrder: 3));
        }

        var streakMilestones = new (string Name, int Days, string Icon)[]
        {
            ("İstikrarlı Öğrenci I", 5, "achievements/disabled-dark.148553.svg"),
            ("İstikrarlı Öğrenci II", 10, "achievements/disabled-dark.16380c.svg"),
            ("İstikrarlı Öğrenci III", 15, "achievements/disabled-dark.1679e1.svg"),
            ("İstikrarlı Öğrenci IV", 30, "achievements/disabled-dark.1e1b53.svg")
        };

        for (var i = 0; i < streakMilestones.Length; i++)
        {
            var milestone = streakMilestones[i];
            desiredBadges.Add(CreateBadge(
                name: milestone.Name,
                description: $"{milestone.Days} gün üst üste çalıştın.",
                category: "Streak",
                ruleType: "DailyStreak",
                config: new { days = milestone.Days },
                iconUrl: milestone.Icon,
                pathKey: "streak-path",
                pathName: "İstikrar Yolu",
                pathOrder: i + 1));
        }

        var activeDayMilestones = new (string Name, int Days, string Icon)[]
        {
            ("Yeni Alışkanlıklar", 7, "achievements/disabled-dark.21b1cf.svg"),
            ("Alışkanlık Sahibi", 21, "achievements/disabled-dark.0085b3.svg"),
            ("Sürekli Öğrenen", 50, "achievements/disabled-dark.041736.svg")
        };

        for (var i = 0; i < activeDayMilestones.Length; i++)
        {
            var milestone = activeDayMilestones[i];
            desiredBadges.Add(CreateBadge(
                name: milestone.Name,
                description: $"Toplam {milestone.Days} günde aktif oldun.",
                category: "Aktivite",
                ruleType: "ActiveDays",
                config: new { days = milestone.Days },
                iconUrl: milestone.Icon,
                pathKey: "activity-journey",
                pathName: "Aktivite Yolu",
                pathOrder: i + 1));
        }

        foreach (var badge in desiredBadges)
        {
            if (existingBadges.TryGetValue(badge.Name, out var existing))
            {
                if (!string.Equals(existing.Description, badge.Description, StringComparison.Ordinal))
                {
                    existing.Description = badge.Description;
                }

                if (!string.Equals(existing.Category, badge.Category, StringComparison.Ordinal))
                {
                    existing.Category = badge.Category;
                }

                if (!string.Equals(existing.RuleType, badge.RuleType, StringComparison.Ordinal))
                {
                    existing.RuleType = badge.RuleType;
                }

                if (!string.Equals(existing.RuleConfigJson, badge.RuleConfigJson, StringComparison.Ordinal))
                {
                    existing.RuleConfigJson = badge.RuleConfigJson;
                }

                if (!string.Equals(existing.IconUrl, badge.IconUrl, StringComparison.Ordinal))
                {
                    existing.IconUrl = badge.IconUrl;
                }

                if (!string.Equals(existing.PathKey, badge.PathKey, StringComparison.Ordinal))
                {
                    existing.PathKey = badge.PathKey;
                }

                if (!string.Equals(existing.PathName, badge.PathName, StringComparison.Ordinal))
                {
                    existing.PathName = badge.PathName;
                }

                if (existing.PathOrder != badge.PathOrder)
                {
                    existing.PathOrder = badge.PathOrder;
                }
            }
            else
            {
                context.BadgeDefinitions.Add(new BadgeDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = badge.Name,
                    Description = badge.Description,
                    Category = badge.Category,
                    RuleType = badge.RuleType,
                    RuleConfigJson = badge.RuleConfigJson,
                    IconUrl = badge.IconUrl,
                    PathKey = badge.PathKey,
                    PathName = badge.PathName,
                    PathOrder = badge.PathOrder
                });
            }
        }

        await context.SaveChangesAsync();
    }

    private static BadgeSeed CreateBadge(
        string name,
        string description,
        string category,
        string ruleType,
        object config,
        string? iconUrl,
        string? pathKey = null,
        string? pathName = null,
        int? pathOrder = null)
    {
        return new BadgeSeed(
            Name: name,
            Description: description,
            Category: category,
            RuleType: ruleType,
            RuleConfigJson: JsonSerializer.Serialize(config),
            IconUrl: iconUrl,
            PathKey: pathKey,
            PathName: pathName,
            PathOrder: pathOrder);
    }

    private sealed record BadgeSeed(
        string Name,
        string Description,
        string Category,
        string RuleType,
        string RuleConfigJson,
        string? IconUrl,
        string? PathKey,
        string? PathName,
        int? PathOrder);

    private static string NormalizeKey(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c);
        }

        var cleaned = builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant();

        var slugBuilder = new StringBuilder(cleaned.Length);

        foreach (var c in cleaned)
        {
            if (char.IsLetterOrDigit(c))
            {
                slugBuilder.Append(c);
            }
            else if (char.IsWhiteSpace(c) || c == '-' || c == '_')
            {
                if (slugBuilder.Length > 0 && slugBuilder[^1] != '-')
                {
                    slugBuilder.Append('-');
                }
            }
        }

        return slugBuilder.ToString().Trim('-');
    }
}
