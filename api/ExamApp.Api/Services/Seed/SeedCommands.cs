using System;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Services.Seed;

/// <summary>Komut modu ortak sabitleri ve çözümleyici.</summary>
public static class SeedCommands
{
    public const int ExitOk = 0;
    public const int ExitUsage = 1;
    public const int ExitEnvironmentRefused = 2;
    public const int ExitFailed = 3;

    /// <summary>Tüm seed komutları yalnızca Development/Staging'de çalışır.</summary>
    public static bool IsAllowedEnvironment(IHostEnvironment environment)
        => environment.IsDevelopment() || environment.IsStaging();

    /// <summary>
    /// İlk argüman bilinen bir komutsa çözer, değilse null. Hatalı kullanımda <see cref="ArgumentException"/>.
    /// </summary>
    public static ISeedCommand? TryParse(string[] args)
    {
        if (Schools.Seed.SchoolSeedCommand.IsRequested(args))
            return Schools.Seed.SchoolSeedCommand.Parse(args);
        if (Teachers.Seed.TeacherSeedCommand.IsRequested(args))
            return Teachers.Seed.TeacherSeedCommand.Parse(args);
        if (Teachers.Seed.TutorSeedCommand.IsRequested(args))
            return Teachers.Seed.TutorSeedCommand.Parse(args);
        if (Cleanup.SeedCleanupCommand.IsRequested(args))
            return Cleanup.SeedCleanupCommand.Parse(args);
        return null;
    }

    public static string RefusalMessage(string commandName, IHostEnvironment environment)
        => $"{commandName} yalnızca Development/Staging ortamında çalışır; mevcut ortam: {environment.EnvironmentName}.";
}
