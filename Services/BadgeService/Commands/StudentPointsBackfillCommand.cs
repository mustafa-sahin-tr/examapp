using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BadgeService.Commands;

/// <summary>
/// Tek seferlik liderlik puanı backfill'i (issue #225):
/// <c>dotnet run -- backfill-student-points [--dry-run]</c>.
///
/// Mevcut her <c>StudentQuestionAggregate</c> için BadgeService outbox'ına bir
/// <see cref="ExamApp.Foundation.Contracts.StudentPointsChangedEvent"/> yazar; <c>badge-outbox-publisher</c>
/// bunları RabbitMQ'ya, exam API consumer'ı da <c>StudentPoints</c>'e taşır. Servisler arası DB
/// paylaşımı ya da HTTP yok — canlı akışla aynı yol.
///
/// Güvenli tekrar: event'in versiyonu aggregate'in <c>LastUpdatedUtc</c>'i (backfill anı DEĞİL). Bu yüzden
/// komutu ikinci kez çalıştırmak ya da backfill sırasında gelen canlı güncellemeler çakışmaz — exam API
/// eşit/eski versiyonu no-op sayar.
///
/// Ortam guard'ı: yalnızca Development/Staging (seed komutlarıyla aynı kural). Üretimde çalıştırmak
/// bilinçli bir karar gerektirir; guard'ı kaldırmadan önce issue'da onay alınmalı.
/// </summary>
public static class StudentPointsBackfillCommand
{
    public const string CommandName = "backfill-student-points";
    public const int ExitOk = 0;
    public const int ExitUsage = 1;
    public const int ExitEnvironmentRefused = 2;
    public const int ExitFailed = 3;

    private const int BatchSize = 500;

    public static bool IsRequested(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    /// <summary>Komut argümanlarını çözer. Bilinmeyen argüman → <see cref="ArgumentException"/>.</summary>
    public static bool ParseDryRun(string[] args)
    {
        var dryRun = false;
        foreach (var arg in args.Skip(1))
        {
            if (string.Equals(arg, "--dry-run", StringComparison.Ordinal))
                dryRun = true;
            else
                throw new ArgumentException(
                    $"Bilinmeyen argüman: '{arg}'. Kullanım: dotnet run -- {CommandName} [--dry-run]");
        }
        return dryRun;
    }

    public static bool IsAllowedEnvironment(IHostEnvironment environment)
        => environment.IsDevelopment() || environment.IsStaging();

    /// <summary>
    /// Tüm aggregate'lar için outbox satırı yazar (batch başına bir SaveChanges). Yazılan (dry-run'da
    /// yazılacak) event sayısını döner.
    /// </summary>
    public static async Task<int> RunAsync(BadgeDbContext db, ILogger logger, bool dryRun, CancellationToken ct = default)
    {
        var total = 0;
        var lastUserId = int.MinValue;

        while (true)
        {
            var batch = await db.StudentQuestionAggregates
                .AsNoTracking()
                .Where(a => a.UserId > lastUserId)
                .OrderBy(a => a.UserId)
                .Take(BatchSize)
                .Select(a => new { a.UserId, a.TotalPoints, a.LastUpdatedUtc })
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            foreach (var a in batch)
            {
                if (!dryRun)
                    StudentPointsOutbox.Enqueue(db, a.UserId, a.TotalPoints, a.LastUpdatedUtc);
            }

            if (!dryRun)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            total += batch.Count;
            lastUserId = batch[^1].UserId;
            logger.LogInformation("[{Command}] {Count} aggregate işlendi (toplam {Total}){DryRun}.",
                CommandName, batch.Count, total, dryRun ? " [dry-run]" : string.Empty);
        }

        logger.LogInformation("[{Command}] Tamamlandı: {Total} StudentPointsChangedEvent {Verb}.",
            CommandName, total, dryRun ? "yazılacaktı (dry-run)" : "outbox'a yazıldı");
        return total;
    }
}
