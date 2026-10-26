using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #572 (review M1): bir öğrencinin etkinlik tamamlamaları öğrenci anahtarında transaction-kapsamlı PostgreSQL advisory
/// lock'u (<c>pg_advisory_xact_lock</c>) altında sıralanır. Etkin süre penceresi "öğrencinin bu oturum başladıktan sonra biten
/// DİĞER tamamlanmış etkinliklerinin en geç EndTime'ı"na göre kırpılır; farklı etkinliklerin EndTest'leri farklı satırları
/// kilitlediği için satır kilidi onları sıralamaz — kilitsiz iki eşzamanlı çağrı birbirinin commit edilmemiş EndTime'ını
/// göremez ve aynı pencereyi iki kez sayardı. Kilit koşullu Completed UPDATE'inden SONRA alınır (yalnız gerçekten tamamlayan
/// çağrı bekler); kilit alındıktan sonraki sorgu READ COMMITTED'da yeni snapshot alır ve öncekinin commit'ini görür. Sıra her
/// zaman satır → öğrenci kilidi (başka yol öğrenci kilidini alıp oturum satırı beklemez → kilitlenme yok). Commit/rollback'te
/// bırakılır. Postgres dışı sağlayıcılarda (SQLite birim testleri, tek bağlantı = zaten seri) no-op.
/// </summary>
public static class ActivityCompletionLock
{
    /// <summary><c>pg_advisory_xact_lock(int4, int4)</c> sınıf anahtarı (ikinci anahtar Students.Id).</summary>
    internal const int LockClass = 572_001;

    public static async Task AcquireActivityCompletionLockAsync(this DatabaseFacade database, int studentId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Activity completion lock must be acquired inside a transaction (pg_advisory_xact_lock is transaction-scoped).");

        if (!database.IsNpgsql())
            return;

        await database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockClass}, {studentId})", ct);
    }
}
