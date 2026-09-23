using System.Data.Common;
using ExamApp.Api.Data;
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
/// canlandırır (IsDeleted=false, Level=0 — reset sonrası seviye taşınmaz).
///
/// Level: BadgeService'te seviye formülü yok (kapsam dışı) → event taşımaz; mevcut Level korunur, yeni satır 0.
/// </summary>
public sealed class StudentPointsSyncService : IStudentPointsSyncService
{
    /// <summary>Saat kayması toleransı: bundan daha ileri tarihli versiyon şimdiki zamana kırpılır.</summary>
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly ILogger<StudentPointsSyncService> _logger;

    public StudentPointsSyncService(AppDbContext db, ILogger<StudentPointsSyncService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<StudentPointsSyncResult> ApplyAsync(StudentPointsChangedEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var studentId = await _db.Students
            .AsNoTracking()
            .Where(s => s.UserId == evt.UserId)
            .OrderBy(s => s.Id)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);

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
            var now = DateTime.UtcNow;
            var updated = await _db.StudentPoints
                .IgnoreQueryFilters()
                .Where(p => p.StudentId == studentId.Value
                    && (p.SourceUpdatedAtUtc == null || p.SourceUpdatedAtUtc < version))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Level, p => p.IsDeleted ? 0 : p.Level)
                    .SetProperty(p => p.XP, xp)
                    .SetProperty(p => p.SourceUpdatedAtUtc, (DateTime?)version)
                    .SetProperty(p => p.LastUpdated, now)
                    .SetProperty(p => p.UpdateTime, (DateTime?)now)
                    .SetProperty(p => p.IsDeleted, false)
                    .SetProperty(p => p.DeleteTime, (DateTime?)null)
                    .SetProperty(p => p.DeleteUserId, (int?)null), ct);

            if (updated > 0)
            {
                _logger.LogInformation(
                    "[StudentPointsSync] StudentId={StudentId} XP={Xp} güncellendi (UserId={UserId}, Version={Version:o}).",
                    studentId.Value, xp, evt.UserId, version);
                return StudentPointsSyncResult.Applied;
            }

            var exists = await _db.StudentPoints
                .IgnoreQueryFilters()
                .AnyAsync(p => p.StudentId == studentId.Value, ct);

            if (exists)
            {
                _logger.LogInformation(
                    "[StudentPointsSync] StudentId={StudentId} için eşit/eski versiyon (Version={Version:o}); no-op.",
                    studentId.Value, version);
                return StudentPointsSyncResult.Stale;
            }

            _db.StudentPoints.Add(new StudentPoint
            {
                StudentId = studentId.Value,
                XP = xp,
                Level = 0,
                LastUpdated = now,
                SourceUpdatedAtUtc = version,
            });

            try
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "[StudentPointsSync] StudentId={StudentId} için StudentPoints oluşturuldu XP={Xp} (UserId={UserId}).",
                    studentId.Value, xp, evt.UserId);
                return StudentPointsSyncResult.Applied;
            }
            catch (DbUpdateException ex) when (attempt == 1 && IsUniqueViolation(ex))
            {
                // Eşzamanlı teslim aynı öğrenci için satırı az önce ekledi (UNIQUE StudentId) — bir kez
                // baştan dene; bu kez koşullu UPDATE yolu versiyonu karşılaştırır.
                _db.ChangeTracker.Clear();
            }
        }
    }

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
