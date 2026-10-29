using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BadgeService.Commands;

/// <summary>
/// Tek seferlik veli paneli rozet backfill'i (issue #501):
/// <c>dotnet run -- backfill-student-badges [--dry-run] [--allow-production] [--confirm]</c>.
///
/// Mevcut her <c>BadgeEarned</c> satırı için outbox'a bir <see cref="ExamApp.Foundation.Contracts.StudentBadgeEarnedEvent"/>
/// yazar (canlı akışla aynı <see cref="StudentBadgeOutbox.Enqueue"/>); exam API consumer'ı bunları <c>StudentBadgeProjections</c>'a
/// taşır. #422'den önce kazanılmış rozetler böylece veli panelinde görünür.
///
/// Güvenli tekrar: consumer (öğrenci, rozet) UNIQUE ile idempotent — ikinci çalıştırma çift kayıt üretmez; kazanma anı
/// <c>EarnedDate</c>'ten gelir (backfill anı değil), öğrencinin sıfırlamasından önceki kazanımlar zaten silinmiştir.
/// Argümanlar ve ortam guard'ı <see cref="StudentPointsBackfillCommand"/> ile aynı (#243).
/// </summary>
public static class StudentBadgeBackfillCommand
{
    public const string CommandName = "backfill-student-badges";

    private const int BatchSize = 500;

    public static bool IsRequested(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    /// <summary>Tüm kazanılmış rozetler için outbox satırı yazar (batch başına bir SaveChanges); yazılan (dry-run'da yazılacak) sayı.</summary>
    public static async Task<int> RunAsync(BadgeDbContext db, ILogger logger, bool dryRun, CancellationToken ct = default)
    {
        var total = 0;

        while (true)
        {
            // Komut boyunca BadgeEarned'e yazan yok sayılır (canlı kazanım kendi event'ini zaten yazar; çakışmayı consumer yutar).
            var batch = await db.BadgeEarned
                .AsNoTracking()
                .Include(e => e.BadgeDefinition)
                .OrderBy(e => e.Id)
                .Skip(total)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            if (!dryRun)
            {
                foreach (var e in batch)
                    StudentBadgeOutbox.Enqueue(db, e.UserId, e.BadgeDefinition, e.EarnedDate);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            total += batch.Count;
            logger.LogInformation("[{Command}] {Count} rozet işlendi (toplam {Total}){DryRun}.",
                CommandName, batch.Count, total, dryRun ? " [dry-run]" : string.Empty);
        }

        logger.LogInformation("[{Command}] Tamamlandı: {Total} StudentBadgeEarnedEvent {Verb}.",
            CommandName, total, dryRun ? "yazılacaktı (dry-run)" : "outbox'a yazıldı");
        return total;
    }
}
