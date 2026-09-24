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
/// Ortam guard'ı (issue #243 öncesi): yalnızca Development/Staging (seed komutlarıyla aynı kural).
/// Production'da hiç çalışmıyordu — hiç soru çözmemiş eski öğrenciler bir sonraki doğru cevaba kadar
/// 0 XP görünüyordu (issue #243, backfill kararı).
///
/// Issue #243 ile Production'da da çalışabilir, ama iki katmanlı bilinçli-onay tasarımıyla:
/// <list type="number">
/// <item><description><c>--allow-production</c> olmadan Production'da komut TAMAMEN reddedilir
/// (önceki davranışla aynı exit code, <see cref="ExitEnvironmentRefused"/>).</description></item>
/// <item><description><c>--allow-production</c> verilmiş olsa BİLE, Production'da <c>--confirm</c>
/// olmadan komut her zaman dry-run'a ZORLANIR (<see cref="ResolveEffectiveDryRun"/>) — operatör
/// <c>--dry-run</c> yazmayı unutsa da Production'da yanlışlıkla gerçek yazım olamaz. Gerçek yazım
/// için <c>--allow-production --confirm</c> ikisi birden gerekir. Açık <c>--dry-run</c> her zaman
/// kazanır (aynı çağrıda hem <c>--confirm</c> hem <c>--dry-run</c> verilirse yazım yapılmaz) — dry-run
/// niyeti asla gerçek yazıma yükseltilmez.</description></item>
/// </list>
/// Development/Staging'de davranış değişmedi: yalnızca <c>--dry-run</c> etkilidir, <c>--allow-production</c>
/// ve <c>--confirm</c> orada anlamsızdır (verilirse yok sayılır).
///
/// Güvenli tekrar (issue #225'ten korunur): event'in versiyonu aggregate'in <c>LastUpdatedUtc</c>'i
/// (backfill anı DEĞİL) — komutu ikinci kez ya da Production'da çalıştırmak canlı güncellemelerle
/// çakışmaz, exam API eşit/eski versiyonu no-op sayar.
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

    /// <summary>Komut satırı argümanlarının çözülmüş hali.</summary>
    public readonly record struct ParsedArgs(bool DryRun, bool AllowProduction, bool Confirm);

    private const string Usage =
        "Kullanım: dotnet run -- " + CommandName + " [--dry-run] [--allow-production] [--confirm]";

    /// <summary>Komut argümanlarını çözer. Bilinmeyen argüman → <see cref="ArgumentException"/>.</summary>
    public static ParsedArgs ParseArgs(string[] args)
    {
        var dryRun = false;
        var allowProduction = false;
        var confirm = false;
        foreach (var arg in args.Skip(1))
        {
            if (string.Equals(arg, "--dry-run", StringComparison.Ordinal))
                dryRun = true;
            else if (string.Equals(arg, "--allow-production", StringComparison.Ordinal))
                allowProduction = true;
            else if (string.Equals(arg, "--confirm", StringComparison.Ordinal))
                confirm = true;
            else
                throw new ArgumentException($"Bilinmeyen argüman: '{arg}'. {Usage}");
        }
        return new ParsedArgs(dryRun, allowProduction, confirm);
    }

    /// <summary>
    /// Development/Staging her zaman izinli. Production yalnızca <paramref name="allowProduction"/>
    /// (<c>--allow-production</c>) verilmişse izinlidir.
    /// </summary>
    public static bool IsAllowedEnvironment(IHostEnvironment environment, bool allowProduction)
        => environment.IsDevelopment() || environment.IsStaging()
           || (environment.IsProduction() && allowProduction);

    /// <summary>
    /// Gerçekten yazım yapılıp yapılmayacağına karar verir. Production'da <c>--confirm</c> yoksa
    /// (veya açıkça <c>--dry-run</c> istenmişse) her zaman <c>true</c> (dry-run) döner — Production'da
    /// yanlışlıkla gerçek yazım imkansızdır. Development/Staging'de yalnızca istenen değeri döner.
    /// </summary>
    public static bool ResolveEffectiveDryRun(IHostEnvironment environment, bool dryRunRequested, bool confirm)
    {
        if (!environment.IsProduction())
            return dryRunRequested;

        // Production: açık --dry-run her zaman kazanır; --confirm yoksa yine dry-run'a zorlanır.
        return dryRunRequested || !confirm;
    }

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
