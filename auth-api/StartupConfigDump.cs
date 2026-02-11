using System;
using System.Linq;
using Microsoft.Extensions.Configuration;

internal static class StartupConfigDump
{
    public static void Print(IConfiguration config, string environmentName, int? kestrelPort = null)
    {
        Console.WriteLine("========================================");
        Console.WriteLine($"🚀 Effective configuration ({environmentName})");
        if (kestrelPort.HasValue)
        {
            Console.WriteLine($"Kestrel:Port = {kestrelPort.Value}");
        }

        PrintSection(config, "Keycloak");
        PrintSection(config, "ConnectionStrings");
        PrintSection(config, "Redis");
        PrintSection(config, "RabbitMQ");
        PrintSection(config, "MinioConfig");
        PrintSection(config, "Server");
        PrintSection(config, "Cors");

        Console.WriteLine("========================================");
    }

    private static void PrintSection(IConfiguration config, string rootKey)
    {
        var section = config.GetSection(rootKey);
        if (!section.GetChildren().Any())
        {
            return;
        }

        foreach (var pair in section.AsEnumerable(makePathsRelative: true))
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            var fullKey = $"{rootKey}:{pair.Key}";
            Console.WriteLine($"{fullKey} = {Sanitize(fullKey, pair.Value)}");
        }
    }

    private static string Sanitize(string key, string value)
    {
        var lowerKey = key.ToLowerInvariant();
        if (lowerKey.Contains("password") ||
            lowerKey.Contains("secret") ||
            lowerKey.EndsWith(":key") ||
            lowerKey.Contains("token") ||
            lowerKey.Contains("apikey"))
        {
            return "***";
        }

        if (lowerKey.StartsWith("connectionstrings:"))
        {
            return RedactSemicolonKvp(value, new[] { "password", "pwd" });
        }

        if (lowerKey.Contains("redis:configuration"))
        {
            return RedactSemicolonKvp(value, new[] { "password" });
        }

        if (value.Length > 400)
        {
            return value.Substring(0, 400) + "…";
        }

        return value;
    }

    private static string RedactSemicolonKvp(string input, string[] sensitiveKeys)
    {
        var parts = input.Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var k = part.Substring(0, eq).Trim();
            var v = part.Substring(eq + 1);
            if (sensitiveKeys.Any(sk => string.Equals(sk, k, StringComparison.OrdinalIgnoreCase)))
            {
                parts[i] = k + "=***";
            }
            else
            {
                parts[i] = k + "=" + v;
            }
        }

        return string.Join(';', parts);
    }
}
