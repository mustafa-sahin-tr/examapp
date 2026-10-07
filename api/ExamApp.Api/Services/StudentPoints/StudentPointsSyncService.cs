using System.Data.Common;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExamApp.Api.Services.StudentPoints;

public enum StudentPointsSyncResult
{
    /// <summary>Satır oluşturuldu ya da daha yeni değerle güncellendi.</summary>
    Applied,
    /// <summary>Kayıttaki versiyon event'inkine eşit/daha yeni — tekrar teslim ya da sırasız eski event; no-op.</summary>
    Stale,
    /// <summary>UserId'ye karşılık gelen (silinmemiş) öğrenci kaydı yok; no-op (loglanır).</summary>
    StudentNotFound,
}

/// <summary>"StudentPoints:Sync" config bölümü (issue #225).</summary>
public sealed class StudentPointsSyncOptions
{
    public const string SectionName = "StudentPoints:Sync";

    /// <summary>
    /// Kabul edilen en yüksek mutlak toplam puan. Varsayılan 10.000.000: soru puanları tek/çift haneli
    /// (<c>Question.Point</c> 1, 5, 10 …) olduğundan bu, bir öğrencinin ~1 milyon doğru cevabına karşılık gelir —
    /// gerçekçi kullanımın çok üstünde, ama <c>int</c> taşmasından da uzak. Üstündeki değer bozuk/kötü niyetli
    /// bir mesaj sayılır; kabul edilseydi öğrenciyi liderliğin tepesine kilitlerdi. Mesaj atılır, Warning loglanır.
    /// </summary>
    public int MaxTotalPoints { get; set; } = 10_000_000;
}

public interface IStudentPointsSyncService
{
    Task<StudentPointsSyncResult> ApplyAsync(StudentPointsChangedEvent evt, CancellationToken ct = default);
}

/// <summary>
/// issue #225: BadgeService <see cref="StudentPointsChangedEvent"/>'ini <c>StudentPoints</c>'e taşır.
///
/// Idempotency (versiyonlu mutlak-değer upsert):
///  - Event mutlak puan taşır; aynı event iki kez uygulansa bile değer aynı kalır.
///  - Yazma KOŞULLU tek UPDATE'tir: <c>WHERE StudentId = @id AND (SourceUpdatedAtUtc IS NULL OR SourceUpdatedAtUtc &lt; @v)</c>.
///    Eşit/eski versiyon 0 satır etkiler → no-op; eşzamanlı iki teslimde de eski olan yeniyi ezemez
///    (kontrol ve yazma aynı atomik ifadede).
///  - Satır yoksa INSERT; eşzamanlı ikinci INSERT <c>IX_StudentPoints_StudentId</c> (UNIQUE) ihlaline düşer →
///    bir kez baştan denenir ve koşullu UPDATE yoluna girer.
///
/// Soft-delete: <c>StudentResetJob</c> satırı siler (soft). UNIQUE index filtresiz olduğundan yeni satır
/// eklenemez; bu yüzden sorgular <c>IgnoreQueryFilters</c> ile çalışır ve daha yeni bir event satırı
/// canlandırır (IsDeleted=false).
///
/// Günlük puan defteri (issue #422): event yalnız MUTLAK toplam taşır; uygulanan her daha yeni değerin önceki toplamla farkı,
/// aynı transaction'da event anının yerel (Europe/Istanbul) gününe <c>StudentDailyXps</c>'e eklenir (veli paneli "bu hafta").
/// Sırasız gelen eski event uygulanmadığından farkı bir sonraki uygulanan event'e (ve onun gününe) dahil olur.
///
/// Level (issue #279 item 2): <c>StudentPoints.Level</c> kolonu düşürüldü — artık okunmuyor, seviye okuma
/// anında <c>StudentLevel.FromXp(XP)</c> ile hesaplanır (issue #243).
/// </summary>
public sealed class StudentPointsSyncService : IStudentPointsSyncService
{
    /// <summary>Saat kayması toleransı: bundan daha ileri tarihli versiyon şimdiki zamana kırpılır.</summary>
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly ILogger<StudentPointsSyncService> _logger;
    private readonly ILocalDayCalendar _calendar;

    public StudentPointsSyncService(AppDbContext db, ILogger<StudentPointsSyncService> logger, ILocalDayCalendar? calendar = null)
    {
        _db = db;
        _logger = logger;
        _calendar = calendar ?? LocalDayCalendar.Default;
    }

    public async Task<StudentPointsSyncResult> ApplyAsync(StudentPointsChangedEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.UserId == evt.UserId)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.CreateTime })
            .FirstOrDefaultAsync(ct);
        var studentId = student?.Id;

        if (studentId is null)
        {
            // Karar: retry yok. AnswerSubmitted yalnızca Student kaydı olan kullanıcıdan üretilir; kayıt yoksa
            // (silinmiş öğrenci, veri tutarsızlığı) tekrar denemek düzeltmez. Öğrenci sonradan oluşursa bir
            // sonraki puan değişimi ya da backfill mutlak değeri zaten taşır.
            _logger.LogWarning(
                "[StudentPointsSync] UserId={UserId} için öğrenci kaydı yok; event atlandı (TotalPoints={TotalPoints}, UpdatedAtUtc={UpdatedAtUtc:o}).",
                evt.UserId, evt.TotalPoints, evt.UpdatedAtUtc);
            return StudentPointsSyncResult.StudentNotFound;
        }

        var version = NormalizeVersion(evt.UpdatedAtUtc, evt.UserId);
        var xp = Math.Max(0, evt.TotalPoints);

        for (var attempt = 1; ; attempt++)
        {
            var outcome = await ApplyOnceAsync(studentId.Value, student!.CreateTime, xp, version, evt.UserId, ct);
            if (outcome.HasValue)
                return outcome.Value;
            if (attempt >= MaxAttempts)
                throw new InvalidOperationException(
                    $"StudentPoints senkronu {MaxAttempts} denemede tamamlanamadı (StudentId={studentId.Value}); MassTransit retry.");
        }
    }

    /// <summary>Yarış (eşzamanlı teslim) durumunda baştan deneme sayısı.</summary>
    private const int MaxAttempts = 5;

    /// <summary>
    /// Tek deneme, tek transaction: önceki satır okunur → koşullu UPDATE (versiyon + okunan değerler hâlâ aynıysa) ya da
    /// INSERT → issue #422 günlük puan defteri (<see cref="StudentDailyXp"/>) aynı transaction'da güncellenir.
    /// null = yarış (satır bu arada değişti / eşzamanlı INSERT) → çağıran baştan dener.
    /// </summary>
    private async Task<StudentPointsSyncResult?> ApplyOnceAsync(
        int studentId, DateTime studentCreatedAt, int xp, DateTime version, int userId, CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;

            var old = await _db.StudentPoints
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.StudentId == studentId)
                .Select(p => new { p.XP, p.SourceUpdatedAtUtc, p.IsDeleted })
                .FirstOrDefaultAsync(ct);

            if (old != null)
            {
                if (old.SourceUpdatedAtUtc != null && old.SourceUpdatedAtUtc >= version)
                {
                    _logger.LogInformation(
                        "[StudentPointsSync] StudentId={StudentId} için eşit/eski versiyon (Version={Version:o}); no-op.",
                        studentId, version);
                    return (StudentPointsSyncResult?)StudentPointsSyncResult.Stale;
                }

                // Koşullu yazım: versiyon kuralı + okuduğumuz değerler hâlâ geçerli (aksi halde eşzamanlı teslim araya girdi →
                // 0 satır → baştan). Postgres'te UPDATE satır kilidi aynı öğrencinin teslimlerini commit'e kadar sıralar.
                var updated = await _db.StudentPoints
                    .IgnoreQueryFilters()
                    .Where(p => p.StudentId == studentId
                        && (p.SourceUpdatedAtUtc == null || p.SourceUpdatedAtUtc < version)
                        && p.XP == old.XP
                        && p.SourceUpdatedAtUtc == old.SourceUpdatedAtUtc
                        && p.IsDeleted == old.IsDeleted)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.XP, xp)
                        .SetProperty(p => p.SourceUpdatedAtUtc, (DateTime?)version)
                        .SetProperty(p => p.LastUpdated, now)
                        .SetProperty(p => p.UpdateTime, (DateTime?)now)
                        .SetProperty(p => p.IsDeleted, false)
                        .SetProperty(p => p.DeleteTime, (DateTime?)null)
                        .SetProperty(p => p.DeleteUserId, (int?)null), ct);
                if (updated == 0)
                    return null;

                // issue #422: fark → event anının YEREL günü. Hiç senkronlanmamış satır (seed/eski veri) geçmiş toplamı
                // taşır → taban çizgisi, deftere yazılmaz. Sıfırlanmış (soft-delete) satırdan sonra toplam 0'dan başlar.
                // Review: toplam 0'a inerse (BadgeService sıfırlaması) eksi satır yazılmaz, defter temizlenir.
                var baseline = old.IsDeleted ? 0 : old.XP;
                if (xp == 0 && baseline > 0)
                    await _db.StudentDailyXps.Where(d => d.StudentId == studentId).ExecuteDeleteAsync(ct);
                else if (old.SourceUpdatedAtUtc != null)
                    await AddToLedgerAsync(studentId, version, (long)xp - baseline, ct);

                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "[StudentPointsSync] StudentId={StudentId} XP={Xp} güncellendi (UserId={UserId}, Version={Version:o}).",
                    studentId, xp, userId, version);
                return StudentPointsSyncResult.Applied;
            }

            _db.StudentPoints.Add(new StudentPoint
            {
                StudentId = studentId,
                XP = xp,
                LastUpdated = now,
                SourceUpdatedAtUtc = version,
            });

            try
            {
                await _db.SaveChangesAsync(ct);
                // issue #422 (review): ilk senkron normalde taban çizgisidir (geçmiş toplam). Öğrenci kaydı event'in yerel
                // haftası içinde açıldıysa tüm puanı bu haftaya aittir → deftere yazılır (yeni öğrencinin ilk puanları görünsün).
                if (studentCreatedAt >= WeekStartUtc(version))
                    await AddToLedgerAsync(studentId, version, xp, ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Eşzamanlı teslim aynı öğrenci için satırı az önce ekledi (UNIQUE StudentId) — baştan dene; bu kez
                // koşullu UPDATE yolu versiyonu karşılaştırır.
                await tx.RollbackAsync(CancellationToken.None);
                return null;
            }

            await tx.CommitAsync(ct);
            _logger.LogInformation(
                "[StudentPointsSync] StudentId={StudentId} için StudentPoints oluşturuldu XP={Xp} (UserId={UserId}).",
                studentId, xp, userId);
            return StudentPointsSyncResult.Applied;
        });
    }

    /// <summary>
    /// issue #422: <see cref="StudentDailyXp"/>'ye (öğrenci, yerel gün) farkı ekler. Aynı öğrencinin yazımları StudentPoints
    /// satır kilidiyle sıralandığından (StudentId, Day) için eşzamanlı INSERT yarışı oluşmaz.
    /// </summary>
    private async Task AddToLedgerAsync(int studentId, DateTime versionUtc, long rawDelta, CancellationToken ct)
    {
        // Security review: int taşması yok — fark int aralığına kırpılır, toplama da doygun (saturating) yapılır.
        var delta = (int)Math.Clamp(rawDelta, int.MinValue, int.MaxValue);
        if (delta == 0)
            return;

        var day = LocalDay(versionUtc);
        var query = _db.StudentDailyXps.Where(d => d.StudentId == studentId && d.Day == day);
        var updated = delta > 0
            ? await query.ExecuteUpdateAsync(s => s.SetProperty(d => d.Xp,
                d => d.Xp > int.MaxValue - delta ? int.MaxValue : d.Xp + delta), ct)
            : await query.ExecuteUpdateAsync(s => s.SetProperty(d => d.Xp,
                d => d.Xp < int.MinValue - delta ? int.MinValue : d.Xp + delta), ct);
        if (updated > 0)
            return;

        _db.StudentDailyXps.Add(new StudentDailyXp { StudentId = studentId, Day = day, Xp = delta });
        await _db.SaveChangesAsync(ct);
    }

    private DateOnly LocalDay(DateTime utc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _calendar.TimeZone));

    /// <summary><paramref name="utc"/>'nin yerel haftasının (Pazartesi 00:00, Europe/Istanbul) UTC karşılığı.</summary>
    private DateTime WeekStartUtc(DateTime utc)
        => _calendar.StartOfDayUtc(ExamApp.Api.Services.Parents.ParentDashboardService.StartOfWeek(LocalDay(utc)));

    private DateTime NormalizeVersion(DateTime value, int userId)
    {
        // Üreticiyle (BadgeService) aynı normalizasyon: UTC + mikrosaniye (Foundation'daki tek yardımcı).
        var utc = EventVersion.Normalize(value);

        var now = DateTime.UtcNow;
        if (utc > now + MaxClockSkew)
        {
            _logger.LogWarning(
                "[StudentPointsSync] İleri tarihli versiyon (UserId={UserId}, Version={Version:o}); şimdiki zamana kırpıldı ki sonraki güncellemeleri kilitlemesin.",
                userId, utc);
            utc = EventVersion.Normalize(now);
        }

        return utc;
    }

    /// <summary>
    /// Yalnızca UNIQUE ihlali (eşzamanlı INSERT yarışı) yeniden denenir; FK/bağlantı gibi diğer hatalar yukarı
    /// fırlar (MassTransit retry/dead-letter). Postgres: SqlState 23505. SQLite (yalnız test sağlayıcısı; Api
    /// projesi paketi referanslamaz): SqliteErrorCode 19 = SQLITE_CONSTRAINT ve mesajda "UNIQUE constraint failed".
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => true,
        DbException db when db.GetType().Name == "SqliteException"
            && db.GetType().GetProperty("SqliteErrorCode")?.GetValue(db) is 19
            && db.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal) => true,
        _ => false,
    };
}
